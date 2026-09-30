using System.Text.Json.Serialization;
using ReelDiscovery.Models;

namespace ReelDiscovery.Services;

public class CharacterGenerator
{
    private readonly OpenAIService _openAI;

    // Available TTS voices with characteristics
    private static readonly string[] MaleVoices = ["echo", "onyx", "fable"];
    private static readonly string[] FemaleVoices = ["nova", "shimmer", "alloy"];

    public CharacterGenerator(OpenAIService openAI)
    {
        _openAI = openAI;
    }

    private const int MaxCharactersPerBatch = 25;

    /// <summary>
    /// Assigns a TTS voice based on character gender (from AI) or inferred from name/personality.
    /// </summary>
    private string AssignVoice(string gender, string firstName, string personalityNotes)
    {
        // First, check if AI provided explicit gender
        var lowerGender = gender?.ToLowerInvariant() ?? "";
        if (lowerGender == "female" || lowerGender == "f")
        {
            return FemaleVoices[Random.Shared.Next(FemaleVoices.Length)];
        }
        if (lowerGender == "male" || lowerGender == "m")
        {
            return MaleVoices[Random.Shared.Next(MaleVoices.Length)];
        }

        // Fallback: use heuristics based on name and personality notes
        var lowerName = firstName.ToLowerInvariant();
        var lowerNotes = personalityNotes?.ToLowerInvariant() ?? "";

        // Check personality notes for gender hints
        bool likelyFemale = lowerNotes.Contains("she ") || lowerNotes.Contains("her ") ||
                           lowerNotes.Contains("woman") || lowerNotes.Contains("female");
        bool likelyMale = lowerNotes.Contains("he ") || lowerNotes.Contains("his ") ||
                         lowerNotes.Contains(" man") || lowerNotes.Contains("male");

        // Common female name endings/patterns
        var femalePatterns = new[] { "a", "ie", "y", "elle", "ine", "ette", "lyn", "een", "is" };
        var malePatterns = new[] { "ew", "ck", "on", "er", "rd", "ld", "ke" };

        if (!likelyMale && !likelyFemale)
        {
            // Guess based on name patterns
            likelyFemale = femalePatterns.Any(p => lowerName.EndsWith(p));
            likelyMale = malePatterns.Any(p => lowerName.EndsWith(p));
        }

        // Pick a random voice from the appropriate set
        if (likelyFemale && !likelyMale)
        {
            return FemaleVoices[Random.Shared.Next(FemaleVoices.Length)];
        }
        else if (likelyMale && !likelyFemale)
        {
            return MaleVoices[Random.Shared.Next(MaleVoices.Length)];
        }
        else
        {
            // Unknown or ambiguous - pick randomly from all
            var allVoices = MaleVoices.Concat(FemaleVoices).ToArray();
            return allVoices[Random.Shared.Next(allVoices.Length)];
        }
    }

    public async Task<CharacterGenerationResult> GenerateCharactersAsync(
        string topic,
        List<Storyline> storylines,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        // Calculate character count based on storylines (2 per storyline, minimum 10)
        var targetCharacterCount = Math.Max(storylines.Count * 2, 10);
        var internalCount = (int)Math.Ceiling(targetCharacterCount * 0.65); // ~65% internal
        var externalCount = targetCharacterCount - internalCount; // ~35% external

        // For large character counts, generate in batches to avoid API timeouts
        if (targetCharacterCount > MaxCharactersPerBatch)
        {
            return await GenerateCharactersInBatchesAsync(topic, storylines, targetCharacterCount, internalCount, externalCount, progress, ct);
        }

        progress?.Report($"Generating {targetCharacterCount} characters...");
        return await GenerateSingleBatchAsync(topic, storylines, targetCharacterCount, internalCount, externalCount, null, null, null, null, ct);
    }

    private async Task<CharacterGenerationResult> GenerateCharactersInBatchesAsync(
        string topic,
        List<Storyline> storylines,
        int totalCount,
        int totalInternal,
        int totalExternal,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var result = new CharacterGenerationResult();
        var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var remainingInternal = totalInternal;
        var remainingExternal = totalExternal;

        // Calculate number of batches needed
        var batchCount = (int)Math.Ceiling((double)totalCount / MaxCharactersPerBatch);

        for (int batch = 0; batch < batchCount; batch++)
        {
            ct.ThrowIfCancellationRequested();

            // Calculate how many characters for this batch
            var remainingTotal = remainingInternal + remainingExternal;
            var batchSize = Math.Min(MaxCharactersPerBatch, remainingTotal);
            var batchInternal = (int)Math.Ceiling(batchSize * 0.65);
            batchInternal = Math.Min(batchInternal, remainingInternal);
            var batchExternal = Math.Min(batchSize - batchInternal, remainingExternal);

            // Determine which storylines this batch should cover
            var storylinesPerBatch = (int)Math.Ceiling((double)storylines.Count / batchCount);
            var startIdx = batch * storylinesPerBatch;
            var endIdx = Math.Min(startIdx + storylinesPerBatch, storylines.Count);
            var batchStorylineIndices = Enumerable.Range(startIdx, endIdx - startIdx).ToList();

            // Report progress
            progress?.Report($"Generating characters (batch {batch + 1} of {batchCount})...");

            var batchResult = await GenerateSingleBatchAsync(
                topic,
                storylines,
                batchInternal + batchExternal,
                batchInternal,
                batchExternal,
                existingNames,
                batchStorylineIndices,
                batch == 0 ? null : result.CompanyDomain, // Pass company domain from first batch
                batch == 0 ? null : result.CompanyName,   // Pass company name from first batch
                ct);

            // First batch sets company info
            if (batch == 0)
            {
                result.CompanyDomain = batchResult.CompanyDomain;
                result.CompanyName = batchResult.CompanyName;
            }

            // Add characters and track names to avoid duplicates
            foreach (var character in batchResult.Characters)
            {
                var fullName = $"{character.FirstName} {character.LastName}";
                // Skip if we already have this character (extra safety check)
                if (existingNames.Contains(fullName))
                    continue;

                result.Characters.Add(character);
                existingNames.Add(fullName);
            }

            // Report completion of batch
            progress?.Report($"Generated {result.Characters.Count} of {totalCount} characters...");

            remainingInternal -= batchInternal;
            remainingExternal -= batchExternal;
        }

        return result;
    }

    private async Task<CharacterGenerationResult> GenerateSingleBatchAsync(
        string topic,
        List<Storyline> storylines,
        int characterCount,
        int internalCount,
        int externalCount,
        HashSet<string>? existingNames,
        List<int>? focusStorylineIndices,
        string? establishedCompanyDomain,
        string? establishedCompanyName,
        CancellationToken ct)
    {
        var systemPrompt = @"You are creating characters for a realistic email dataset that will be used for e-discovery demonstrations.
Generate characters with believable names, email addresses, and roles that fit the given topic.

CRITICAL RULE - STAY ON TOPIC:
If the topic is a specific movie, TV show, book, or other media property:
- You MUST ONLY use characters from THAT SPECIFIC property
- NEVER mix characters from different shows/movies (e.g., NO Parks and Rec characters in an Office dataset)
- Use the ACTUAL organization/company name from that source material
- Use the ACTUAL character names from that source material
- The email domain MUST match the canonical organization

Examples of CORRECT behavior:
- Topic 'The Office' → ONLY Dunder Mifflin characters (Michael Scott, Jim Halpert, Pam Beesly, Dwight Schrute, etc.) with dundermifflin.com
- Topic 'Parks and Recreation' → ONLY Pawnee Parks Dept characters (Leslie Knope, Ron Swanson, etc.) with pawnee.gov
- Topic 'Mad Men' → ONLY Sterling Cooper characters with sterlingcooper.com

Examples of WRONG behavior:
- Topic 'The Office' → Including Leslie Knope or Ron Swanson (WRONG - those are Parks and Rec characters!)
- Mixing ANY characters between different TV shows, movies, or books

For original/custom topics, create fitting fictional characters and company domain.

IMPORTANT: Create a realistic mix of internal and external contacts:
- Internal employees at the primary organization (from the source material)
- External contacts (customers, vendors, lawyers, consultants) with their OWN separate email domains
- External characters can be fictional, but internal characters MUST be from the source material

CHARACTER RELATIONSHIPS AND EMOTIONAL DYNAMICS - CRITICAL:
The personalityNotes field MUST include relationship dynamics with other characters:
- Who are their ALLIES? (friends, mentors, collaborators they genuinely like)
- Who are their RIVALS or ENEMIES? (competitors, adversaries, people they distrust or despise)
- What TENSIONS exist? (grudges, jealousy, resentment, power struggles, old wounds)
- What makes them ANGRY? What triggers emotional reactions?
- How do they TRULY feel about the people they work with?

Examples of GOOD personalityNotes (emotionally rich):
- 'Sarcastic and deadpan. Close friends with Pam - they share inside jokes. Openly mocks and pranks Dwight, viewing him as insufferable. Resents Michael's incompetence but feels guilty about it. Secretly ambitious but hides it behind irony.'
- 'Intensely loyal to Michael, sees Jim as a direct threat to everything he's worked for. Aggressive and condescending with subordinates, submissive with authority figures. Holds grudges for years. Seethes with resentment when overlooked.'
- 'Calculating and ruthless behind a polished exterior. Views colleagues as pawns to be used. Despises weakness and sentimentality. Will sabotage rivals while maintaining plausible deniability. Enjoys watching others fail.'
- 'Warm but with a temper when pushed. Protective of friends, hostile to outsiders. Still bitter about being passed over for promotion. Distrusts management. Will get passive-aggressive when feeling disrespected.'

Examples of BAD personalityNotes (too bland - DO NOT DO THIS):
- 'Professional and organized. Good communicator.'
- 'Friendly and helpful team player.'
- 'Experienced manager with strong leadership skills.'

Always respond with valid JSON matching the specified schema exactly.";

        // Build storyline summary with indices for character assignment
        var storylinesSummary = string.Join("\n", storylines.Select((s, i) =>
            $"[{i}] {s.Title}: {s.Description}"));

        var existingNamesNote = existingNames != null && existingNames.Count > 0
            ? $"\n\nCRITICAL - DO NOT DUPLICATE: The following characters have already been created. You MUST create DIFFERENT characters - do NOT use any of these names again:\n{string.Join(", ", existingNames)}\n"
            : "";

        var focusNote = focusStorylineIndices != null
            ? $"\n\nFocus primarily on storylines {string.Join(", ", focusStorylineIndices)} but characters may be involved in others too.\n"
            : "";

        var companyNote = !string.IsNullOrEmpty(establishedCompanyDomain) && !string.IsNullOrEmpty(establishedCompanyName)
            ? $"\n\nIMPORTANT - USE ESTABLISHED COMPANY: The primary company has already been established as:\n- Company Name: {establishedCompanyName}\n- Domain: {establishedCompanyDomain}\nYou MUST use this exact company name and domain for all internal characters.\n"
            : "";

        var userPrompt = $@"Topic: {topic}

Storylines that need characters (use the index numbers to assign characters):
{storylinesSummary}
{existingNamesNote}{companyNote}{focusNote}
Generate exactly {characterCount} NEW characters who would be involved in these storylines. Include:

INTERNAL CHARACTERS ({internalCount}):
- A mix of management and staff levels at the primary organization
- Different departments as appropriate
- Use the primary company domain for all internal employees

EXTERNAL CHARACTERS ({externalCount}):
- Each external character should have a UNIQUE email domain from a DIFFERENT organization
- Use diverse, realistic external domains such as:
  * Law firms: harrisonlegal.com, whitfieldlaw.com, kmattorneys.com
  * Consulting: strategypartners.com, deloitte.com, mckinsey.com
  * Vendors/suppliers: officesupplyco.com, techsolutions.net, globalshipping.com
  * Customers/clients: acmecorp.com, horizonmedia.com, nationalretail.com
  * Banks/finance: firstnational.com, capitalinvest.com
  * Government/regulatory: state.gov, compliance-board.org
  * Personal emails for informal contacts: gmail.com, yahoo.com, outlook.com
- IMPORTANT: External characters should NOT all share the same domain - each external company/person should have their own unique domain
- Create realistic company names that match the domains

For each character, specify which storyline indices (0-based) they are involved in.

CRITICAL - STAY ON TOPIC:
- If this is a known TV show, movie, book, or other media, you MUST use ONLY characters from THAT SPECIFIC source
- DO NOT mix characters from different properties (e.g., NO Parks and Rec characters in The Office dataset!)
- For 'The Office': ONLY use Dunder Mifflin characters (Michael Scott, Jim Halpert, Pam Beesly, Dwight Schrute, Angela Martin, Kevin Malone, Oscar Martinez, Stanley Hudson, Phyllis Vance, etc.)
- Use the REAL company/organization domain (e.g., for 'The Office': dundermifflin.com)
- External contacts can be fictional (customers, vendors, corporate contacts from other companies)

SIGNATURE BLOCKS:
- All characters from corporate/organizational domains MUST have a professional email signature block
- Signature blocks should be consistent for all employees of the same organization
- Include: name, title, organization, phone (fictional), and optionally address or tagline
- Personal email domains (gmail.com, yahoo.com, etc.) may have simple or no signatures

PERSONALITY NOTES - MAKE THEM EMOTIONALLY RICH:
- DO NOT write bland descriptions like 'professional and organized'
- INCLUDE who they like, who they dislike, who they resent
- INCLUDE what triggers them emotionally
- INCLUDE their real attitudes toward colleagues (not just job function)
- Think about the SOURCE MATERIAL - how do these characters ACTUALLY interact?

Respond with JSON in this exact format:
{{
  ""primaryCompanyDomain"": ""string (e.g., dundermifflin.com)"",
  ""primaryCompanyName"": ""string (e.g., Dunder Mifflin Paper Company)"",
  ""characters"": [
    {{
      ""firstName"": ""string"",
      ""lastName"": ""string"",
      ""gender"": ""male"" | ""female"" (for TTS voice selection),
      ""role"": ""string (job title)"",
      ""department"": ""string"",
      ""organization"": ""string (company name)"",
      ""domain"": ""string (email domain for this character)"",
      ""isExternal"": boolean (true if not part of primary company),
      ""personalityNotes"": ""string (2-3 sentences about personality, relationships, who they like/dislike, what triggers them)"",
      ""signatureBlock"": ""string (professional email signature with name, title, company, phone - use \\n for line breaks)"",
      ""storylineIndices"": [0, 1, 2] (array of storyline indices this character is involved in)
    }}
  ]
}}";

        var response = await _openAI.GetJsonCompletionAsync<CharacterApiResponse>(systemPrompt, userPrompt, "Character Generation", ct);

        if (response == null)
            throw new InvalidOperationException("Failed to generate characters");

        var result = new CharacterGenerationResult
        {
            CompanyDomain = response.PrimaryCompanyDomain,
            CompanyName = response.PrimaryCompanyName
        };

        foreach (var c in response.Characters)
        {
            var emailLocal = $"{c.FirstName.ToLower()}.{c.LastName.ToLower()}"
                .Replace(" ", "")
                .Replace("'", "");

            var character = new Character
            {
                FirstName = c.FirstName,
                LastName = c.LastName,
                Email = $"{emailLocal}@{c.Domain}",
                Role = c.Role,
                Department = c.Department,
                Organization = c.Organization,
                PersonalityNotes = c.PersonalityNotes,
                SignatureBlock = c.SignatureBlock ?? string.Empty,
                IsExternal = c.IsExternal,
                VoiceId = AssignVoice(c.Gender, c.FirstName, c.PersonalityNotes)
            };

            result.Characters.Add(character);

            // Assign character to storylines
            if (c.StorylineIndices != null)
            {
                foreach (var idx in c.StorylineIndices)
                {
                    if (idx >= 0 && idx < storylines.Count)
                    {
                        storylines[idx].InvolvedCharacterIds.Add(character.Id);
                    }
                }
            }
        }

        return result;
    }

    // API Response DTOs
    private class CharacterApiResponse
    {
        [JsonPropertyName("primaryCompanyDomain")]
        public string PrimaryCompanyDomain { get; set; } = string.Empty;

        [JsonPropertyName("primaryCompanyName")]
        public string PrimaryCompanyName { get; set; } = string.Empty;

        [JsonPropertyName("characters")]
        public List<CharacterDto> Characters { get; set; } = new();
    }

    private class CharacterDto
    {
        [JsonPropertyName("firstName")]
        public string FirstName { get; set; } = string.Empty;

        [JsonPropertyName("lastName")]
        public string LastName { get; set; } = string.Empty;

        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("department")]
        public string Department { get; set; } = string.Empty;

        [JsonPropertyName("organization")]
        public string Organization { get; set; } = string.Empty;

        [JsonPropertyName("domain")]
        public string Domain { get; set; } = string.Empty;

        [JsonPropertyName("isExternal")]
        public bool IsExternal { get; set; }

        [JsonPropertyName("personalityNotes")]
        public string PersonalityNotes { get; set; } = string.Empty;

        [JsonPropertyName("signatureBlock")]
        public string SignatureBlock { get; set; } = string.Empty;

        [JsonPropertyName("storylineIndices")]
        public List<int> StorylineIndices { get; set; } = new();

        [JsonPropertyName("gender")]
        public string Gender { get; set; } = string.Empty;
    }
}

public class CharacterGenerationResult
{
    public string CompanyDomain { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public List<Character> Characters { get; set; } = new();
}
