using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using ReelDiscovery.Models;
using ReelDiscovery.Helpers;

namespace ReelDiscovery.Services;

public class EmailGenerator
{
    private readonly OpenAIService _openAI;
    private readonly OfficeDocumentService _officeService;
    private readonly CalendarService _calendarService;

    // Track document chains across threads for versioning
    private readonly ConcurrentDictionary<string, DocumentChainState> _documentChains = new();

    // Maximum emails per API call to avoid token limits and timeouts
    private const int MaxEmailsPerBatch = 15;

    public EmailGenerator(OpenAIService openAI)
    {
        _openAI = openAI;
        _officeService = new OfficeDocumentService();
        _calendarService = new CalendarService();
    }

    internal class DocumentChainState
    {
        private readonly object _versionLock = new();

        public string ChainId { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string BaseTitle { get; set; } = "";
        public AttachmentType Type { get; set; }
        public int VersionNumber { get; set; } = 1;
        public string LastContent { get; set; } = "";

        /// <summary>
        /// Atomically claim the next version number for a revision of this chain.
        /// </summary>
        public int ReserveNextVersion()
        {
            lock (_versionLock)
            {
                VersionNumber++;
                return VersionNumber;
            }
        }
    }

    public async Task<GenerationResult> GenerateEmailsAsync(
        WizardState state,
        IProgress<GenerationProgress> progress,
        CancellationToken ct = default)
    {
        var result = new GenerationResult
        {
            OutputFolder = state.Config.OutputFolder
        };

        var startTime = DateTime.Now;
        var progressData = new GenerationProgress
        {
            TotalEmails = state.Config.TotalEmailCount,
            CurrentOperation = "Initializing..."
        };
        var progressLock = new object();

        // Mutate the shared progress under the lock, then report an immutable snapshot
        void Report(Action<GenerationProgress>? mutate = null)
        {
            GenerationProgress snapshot;
            lock (progressLock)
            {
                mutate?.Invoke(progressData);
                snapshot = progressData.Clone();
            }
            progress.Report(snapshot);
        }

        // Linked run token: plan 01-11 uses runCts to stop the run on fatal provider errors
        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var runToken = runCts.Token;

        // Hoisted so completed threads can still be saved if the run is cancelled
        var threads = new ConcurrentBag<EmailThread>();
        var totalAttachments = 0;

        try
        {
            // Distribute emails across storylines
            var emailDistribution = DistributeEmails(state.Storylines, state.Config.TotalEmailCount);

            // Calculate date range
            var startDate = state.Config.LetAISuggestDates && state.AISuggestedStartDate.HasValue
                ? state.AISuggestedStartDate.Value
                : state.Config.StartDate;
            var endDate = state.Config.LetAISuggestDates && state.AISuggestedEndDate.HasValue
                ? state.AISuggestedEndDate.Value
                : state.Config.EndDate;

            var completedEmails = new int[1]; // Use array to allow ref-like access in closure

            // Generate threads for each storyline in parallel
            var parallelism = Math.Max(1, state.Config.ParallelThreads);
            using var semaphore = new SemaphoreSlim(parallelism, parallelism);

            Directory.CreateDirectory(state.Config.OutputFolder);

            // Prepare all storyline tasks with their parameters
            var storylineTasks = state.Storylines.Select(async (storyline, i) =>
            {
                var emailCount = emailDistribution[i];
                var (storyStart, storyEnd) = DateHelper.AllocateDateWindow(
                    startDate, endDate, i, state.Storylines.Count);

                await semaphore.WaitAsync(runToken);
                try
                {
                    runToken.ThrowIfCancellationRequested();

                    // Report that we're starting this storyline
                    Report(p =>
                    {
                        p.CurrentStoryline = storyline.Title;
                        p.CurrentOperation = $"Processing storyline: {storyline.Title}";
                    });

                    // Generate thread(s) for this storyline
                    var storylineThreads = await GenerateThreadsForStorylineAsync(
                        storyline, state.Characters, state.CompanyDomain,
                        emailCount, storyStart, storyEnd,
                        state.Config, state.DomainThemes, runToken);

                    // Add to concurrent collection
                    foreach (var thread in storylineThreads)
                    {
                        threads.Add(thread);
                    }

                    // Update progress atomically
                    var newCompleted = Interlocked.Add(ref completedEmails[0], emailCount);
                    Report(p => p.CompletedEmails = newCompleted);
                }
                catch (OperationCanceledException)
                {
                    throw; // Let cancellation propagate
                }
                catch (Exception ex)
                {
                    // Per-storyline error handling: log and continue with others
                    result.AddError($"Storyline '{storyline.Title}' failed: {ex.Message}");

                    // Still count these emails as "completed" for progress tracking
                    var newCompleted = Interlocked.Add(ref completedEmails[0], emailCount);
                    Report(p => p.CompletedEmails = newCompleted);
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            // Wait for all storylines to complete
            await Task.WhenAll(storylineTasks);

            var allEmails = threads.SelectMany(t => t.Messages).ToList();
            var emailsWithPlannedDocuments = allEmails.Where(e => e.PlannedHasDocument).ToList();

            // Update final progress and start the attachment pass
            Report(p =>
            {
                p.CompletedEmails = completedEmails[0];
                p.CurrentOperation = "Adding attachments...";
                p.TotalAttachments = emailsWithPlannedDocuments.Count;
            });

            // Generate document attachments in parallel; each item is isolated
            var attachmentTasks = emailsWithPlannedDocuments.Select(async email =>
            {
                await semaphore.WaitAsync(runToken);
                try
                {
                    return await RunIsolatedAsync(async () =>
                    {
                        runToken.ThrowIfCancellationRequested();

                        Report(p => p.CurrentOperation = $"Creating attachment for: {email.Subject}");

                        await GeneratePlannedDocumentAsync(email, state, runToken);

                        var attachment = email.Attachments.FirstOrDefault(a =>
                            a.Type == AttachmentType.Word ||
                            a.Type == AttachmentType.Excel ||
                            a.Type == AttachmentType.PowerPoint);

                        GenerationProgress snapshot;
                        lock (progressLock)
                        {
                            progressData.CompletedAttachments++;

                            if (attachment != null)
                            {
                                switch (attachment.Type)
                                {
                                    case AttachmentType.Word:
                                        result.WordDocumentsGenerated++;
                                        break;
                                    case AttachmentType.Excel:
                                        result.ExcelDocumentsGenerated++;
                                        break;
                                    case AttachmentType.PowerPoint:
                                        result.PowerPointDocumentsGenerated++;
                                        break;
                                }
                            }

                            snapshot = progressData.Clone();
                        }

                        progress.Report(snapshot);
                        return attachment != null ? 1 : 0;
                    }, result, $"Attachment for '{email.Subject}'", runCts);
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            var attachmentResults = await Task.WhenAll(attachmentTasks);
            totalAttachments = attachmentResults.Sum();

            // Generate images for emails that were planned to have them
            if (state.Config.IncludeImages)
            {
                var emailsWithPlannedImages = allEmails.Where(e => e.PlannedHasImage).ToList();

                if (emailsWithPlannedImages.Count > 0)
                {
                    Report(p =>
                    {
                        p.CurrentOperation = "Generating images...";
                        p.TotalImages = emailsWithPlannedImages.Count;
                    });

                    var imageTasks = emailsWithPlannedImages.Select(async email =>
                    {
                        await semaphore.WaitAsync(runToken);
                        try
                        {
                            return await RunIsolatedAsync(async () =>
                            {
                                runToken.ThrowIfCancellationRequested();

                                Report(p => p.CurrentOperation = $"Generating image for: {email.Subject}");

                                await GeneratePlannedImageAsync(email, state, runToken);

                                var hasImage = email.Attachments.Any(a => a.Type == AttachmentType.Image);

                                GenerationProgress snapshot;
                                lock (progressLock)
                                {
                                    progressData.CompletedImages++;
                                    if (hasImage)
                                    {
                                        result.ImagesGenerated++;
                                    }

                                    snapshot = progressData.Clone();
                                }

                                progress.Report(snapshot);
                                return hasImage ? 1 : 0;
                            }, result, $"Image for '{email.Subject}'", runCts);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }).ToList();

                    await Task.WhenAll(imageTasks);
                }
            }

            // Detect and add calendar invites (if enabled)
            if (state.Config.IncludeCalendarInvites && state.Config.CalendarInvitePercentage > 0)
            {
                Report(p => p.CurrentOperation = "Detecting meetings and adding calendar invites...");

                // Only check a limited number of emails for calendar invites based on percentage
                var maxCalendarEmails = Math.Max(1, (int)Math.Round(allEmails.Count * state.Config.CalendarInvitePercentage / 100.0));
                var emailsToCheckForCalendar = allEmails
                    .OrderBy(_ => Random.Shared.Next()) // Randomize which emails to check
                    .Take(maxCalendarEmails)
                    .ToList();

                // Check selected emails for meeting references (run in parallel)
                var calendarTasks = emailsToCheckForCalendar.Select(async email =>
                {
                    await semaphore.WaitAsync(runToken);
                    try
                    {
                        return await RunIsolatedAsync(async () =>
                        {
                            runToken.ThrowIfCancellationRequested();
                            await DetectAndAddCalendarInviteAsync(email, state.Characters, runToken);
                            return email.Attachments.Any(a => a.Type == AttachmentType.CalendarInvite) ? 1 : 0;
                        }, result, $"Calendar invite for '{email.Subject}'", runCts);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }).ToList();

                var calendarResults = await Task.WhenAll(calendarTasks);
                result.CalendarInvitesGenerated = calendarResults.Sum();
            }

            // Generate voicemails for emails that were planned to have them
            if (state.Config.IncludeVoicemails)
            {
                var emailsWithPlannedVoicemails = allEmails.Where(e => e.PlannedHasVoicemail).ToList();

                if (emailsWithPlannedVoicemails.Count > 0)
                {
                    Report(p => p.CurrentOperation = "Generating voicemails...");

                    var voicemailTasks = emailsWithPlannedVoicemails.Select(async email =>
                    {
                        await semaphore.WaitAsync(runToken);
                        try
                        {
                            return await RunIsolatedAsync(async () =>
                            {
                                runToken.ThrowIfCancellationRequested();

                                Report(p => p.CurrentOperation = $"Generating voicemail for: {email.From.FullName}");

                                await GeneratePlannedVoicemailAsync(email, state, runToken);

                                var hasVoicemail = email.Attachments.Any(a => a.Type == AttachmentType.Voicemail);

                                lock (progressLock)
                                {
                                    if (hasVoicemail)
                                    {
                                        result.VoicemailsGenerated++;
                                    }
                                }

                                return hasVoicemail ? 1 : 0;
                            }, result, $"Voicemail for '{email.Subject}'", runCts);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }).ToList();

                    await Task.WhenAll(voicemailTasks);
                }
            }

            await SaveAndFinalizeAsync(threads.ToList(), allEmails, totalAttachments, state, result, startTime,
                op => Report(p => p.CurrentOperation = op));

            Report(p => p.CurrentOperation = "Complete!");

            return result;
        }
        catch (OperationCanceledException)
        {
            result.WasCancelled = true;

            // Save the threads completed so far so a cancelled run still leaves usable output
            if (!threads.IsEmpty)
            {
                var threadsList = threads.ToList();
                var allEmails = threadsList.SelectMany(t => t.Messages).ToList();
                var attachmentsSoFar = result.WordDocumentsGenerated + result.ExcelDocumentsGenerated + result.PowerPointDocumentsGenerated;
                await SaveAndFinalizeAsync(threadsList, allEmails, attachmentsSoFar, state, result, startTime,
                    op => Report(p => p.CurrentOperation = op));
            }

            result.ElapsedTime = DateTime.Now - startTime;
            return result;
        }
        catch (Exception ex)
        {
            result.AddError(ex.Message);
            result.ElapsedTime = DateTime.Now - startTime;
            return result;
        }
    }

    /// <summary>
    /// Run one enrichment item (attachment, image, calendar invite, voicemail) so that a failure
    /// is recorded and skipped instead of faulting the whole pass. Cancellation still propagates.
    /// </summary>
    /// <param name="runCts">Linked run token source; used by plan 01-11 to stop the run on fatal provider errors.</param>
    internal static async Task<int> RunIsolatedAsync(
        Func<Task<int>> work, GenerationResult result, string failureLabel, CancellationTokenSource runCts)
    {
        try
        {
            return await work();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result.AddError($"{failureLabel} failed: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Save all threads to EML and fill in the final result counters. Always saves with
    /// CancellationToken.None so a cancelled run still writes the threads it completed.
    /// </summary>
    private async Task SaveAndFinalizeAsync(
        List<EmailThread> threadsList,
        List<EmailMessage> allEmails,
        int totalAttachments,
        WizardState state,
        GenerationResult result,
        DateTime startTime,
        Action<string> reportOperation)
    {
        reportOperation("Saving EML files...");

        try
        {
            var emlService = new EmlFileService();
            var emlProgress = new Progress<(int completed, int total, string currentFile)>(p =>
                reportOperation($"Saving: {p.currentFile}"));

            await emlService.SaveAllEmailsAsync(threadsList, state.Config.OutputFolder, state.Config.OrganizeBySender, emlProgress, CancellationToken.None);

            // Release attachment byte arrays to free memory after saving
            foreach (var thread in threadsList)
            {
                foreach (var email in thread.Messages)
                {
                    foreach (var attachment in email.Attachments)
                    {
                        attachment.Content = null;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            result.AddError($"Failed to save EML files: {ex.Message}");
            if (ex.InnerException != null)
            {
                result.AddError($"  Inner error: {ex.InnerException.Message}");
            }
        }

        // Finalize results
        result.TotalEmailsGenerated = allEmails.Count;
        result.TotalThreadsGenerated = threadsList.Count;
        result.TotalAttachmentsGenerated = totalAttachments;
        result.ElapsedTime = DateTime.Now - startTime;

        state.GeneratedThreads = threadsList;
        state.Result = result;
    }

    private static string IndentSignature(string signature)
    {
        if (string.IsNullOrEmpty(signature)) return "    (no signature)";
        var lines = signature.Replace("\\n", "\n").Split('\n');
        return string.Join("\n", lines.Select(l => $"    {l}"));
    }

    private List<int> DistributeEmails(List<Storyline> storylines, int totalEmails)
    {
        var distribution = new List<int>();
        var totalSuggested = storylines.Sum(s => s.SuggestedEmailCount);

        if (totalSuggested == 0)
        {
            // Equal distribution
            var perStoryline = totalEmails / storylines.Count;
            var remainder = totalEmails % storylines.Count;

            for (int i = 0; i < storylines.Count; i++)
            {
                distribution.Add(perStoryline + (i < remainder ? 1 : 0));
            }
        }
        else
        {
            // Proportional distribution based on suggested counts
            foreach (var storyline in storylines)
            {
                var proportion = (double)storyline.SuggestedEmailCount / totalSuggested;
                distribution.Add(Math.Max(1, (int)Math.Round(totalEmails * proportion)));
            }

            // Adjust to match total
            var diff = totalEmails - distribution.Sum();
            if (diff != 0)
            {
                distribution[0] += diff;
            }
        }

        return distribution;
    }

    private async Task<List<EmailThread>> GenerateThreadsForStorylineAsync(
        Storyline storyline,
        List<Character> characters,
        string domain,
        int emailCount,
        DateTime startDate,
        DateTime endDate,
        GenerationConfig config,
        Dictionary<string, OrganizationTheme> domainThemes,
        CancellationToken ct)
    {
        var systemPrompt = BuildEmailSystemPrompt();

        var characterList = string.Join("\n\n", characters.Select(c =>
            $"- {c.FullName} ({c.Email})\n  Role: {c.Role}, {c.Department} @ {c.Organization}\n  Style: {c.PersonalityNotes}\n  Signature:\n{IndentSignature(c.SignatureBlock)}"));

        var thread = new EmailThread
        {
            StorylineId = storyline.Id
        };

        // Break into batches to avoid token limits and timeouts
        var totalBatches = (int)Math.Ceiling((double)emailCount / MaxEmailsPerBatch);
        var emailsGenerated = 0;
        var sequence = 0;

        // Distribute attachments across batches proportionally
        var totalDocAttachments = config.AttachmentPercentage > 0 && config.EnabledAttachmentTypes.Count > 0
            ? Math.Max(0, (int)Math.Round(emailCount * config.AttachmentPercentage / 100.0))
            : 0;
        var totalImageAttachments = config.IncludeImages && config.ImagePercentage > 0
            ? Math.Max(1, (int)Math.Round(emailCount * config.ImagePercentage / 100.0))
            : 0;
        var totalVoicemailAttachments = config.IncludeVoicemails && config.VoicemailPercentage > 0
            ? Math.Max(0, (int)Math.Round(emailCount * config.VoicemailPercentage / 100.0))
            : 0;

        var docsAssigned = 0;
        var imagesAssigned = 0;
        var voicemailsAssigned = 0;

        for (int batch = 0; batch < totalBatches; batch++)
        {
            ct.ThrowIfCancellationRequested();

            var batchSize = Math.Min(MaxEmailsPerBatch, emailCount - emailsGenerated);
            var isFirstBatch = batch == 0;
            var isLastBatch = batch == totalBatches - 1;

            // Calculate date window for this batch
            var batchStartDate = DateHelper.InterpolateDateInRange(startDate, endDate, (double)emailsGenerated / emailCount);
            var batchEndDate = DateHelper.InterpolateDateInRange(startDate, endDate, (double)(emailsGenerated + batchSize) / emailCount);

            // Distribute attachments for this batch proportionally
            var batchDocs = DistributeAttachmentsForBatch(totalDocAttachments, ref docsAssigned, batchSize, emailCount - emailsGenerated, isLastBatch);
            var batchImages = DistributeAttachmentsForBatch(totalImageAttachments, ref imagesAssigned, batchSize, emailCount - emailsGenerated, isLastBatch);
            var batchVoicemails = DistributeAttachmentsForBatch(totalVoicemailAttachments, ref voicemailsAssigned, batchSize, emailCount - emailsGenerated, isLastBatch);

            // Build batch-specific attachment instructions
            var batchAttachmentInstructions = BuildBatchAttachmentInstructions(config, batchDocs, batchImages, batchVoicemails);

            // Build narrative context from previous batches
            var narrativeContext = "";
            if (!isFirstBatch && thread.Messages.Count > 0)
            {
                narrativeContext = BuildNarrativeContext(thread.Messages);
            }

            // Determine narrative phase
            var narrativePhase = isFirstBatch ? "BEGINNING - Introduce the conflict and set up the storyline."
                : isLastBatch ? "CONCLUSION - Bring the storyline to a resolution or climax."
                : $"MIDDLE (Part {batch + 1} of {totalBatches}) - Escalate tensions and develop the conflict.";

            var userPrompt = BuildBatchUserPrompt(
                storyline, characterList, batchStartDate, batchEndDate,
                batchSize, batchAttachmentInstructions, narrativeContext,
                narrativePhase, isFirstBatch, thread.Subject);

            var response = await _openAI.GetJsonCompletionAsync<ThreadApiResponse>(systemPrompt, userPrompt, $"Email Thread Generation (batch {batch + 1}/{totalBatches})", ct);

            if (response == null)
                throw new InvalidOperationException($"Failed to generate batch {batch + 1} for storyline: {storyline.Title}");

            // Capture the subject from the first batch
            if (isFirstBatch)
            {
                thread.Subject = response.Subject;
            }

            // Trim to the requested batch size - never trust the model's count
            var batchEmails = response.Emails.Take(batchSize).ToList();
            if (batchEmails.Count == 0)
            {
                // An empty batch would never advance the loop's progress
                break;
            }

            // replyToIndex values are relative to this batch
            var batchOffset = thread.Messages.Count;

            // Process emails from this batch
            foreach (var e in batchEmails)
            {
                var emailMessage = ConvertDtoToEmailMessage(e, thread, batchOffset, characters, domainThemes, startDate, endDate, ref sequence);
                thread.Messages.Add(emailMessage);
            }

            emailsGenerated += batchEmails.Count;
        }

        // Setup threading headers
        ThreadingHelper.SetupThreading(thread, domain);

        return new List<EmailThread> { thread };
    }

    /// <summary>
    /// Build the system prompt for email generation (shared across batches)
    /// </summary>
    private static string BuildEmailSystemPrompt()
    {
        return @"You are generating immersive email/message threads set WITHIN a fictional universe for an e-discovery dataset.
These should read like authentic communications between characters in that world - capturing their voices, the stakes, and the drama of the source material.

CRITICAL - EMOTIONAL AUTHENTICITY:
Characters who dislike each other should NOT be cordial and professional. Real emails between people in conflict show:
- Passive-aggressive jabs and subtle insults
- Curt, cold responses that barely hide contempt
- Accusations (direct or implied)
- Defensive reactions and blame-shifting
- CC'ing others to 'witness' or build alliances
- Barely contained anger ('I don't appreciate your tone', 'Per my LAST email...')
- Sarcasm and mockery
- Threats (veiled or explicit)

EXAMPLES OF EMOTIONALLY AUTHENTIC EMAILS:

Between RIVALS/ENEMIES:
'Michael, I've now explained this THREE times. Perhaps if you spent less time on your improv classes and more time reading the reports I send you, we wouldn't keep having this conversation. The numbers don't lie - your branch is underperforming. Again. - Jan'

'Dwight - I don't know why you thought it was appropriate to CC the entire office on your 'concerns' about my sales numbers, but I'll be discussing this with Michael. If you have a problem with me, say it to my face. - Jim'

Between ALLIES sharing frustration:
'Can you BELIEVE what she said in that meeting?? I'm still shaking. She basically accused me of sabotaging the whole project. I need to vent - lunch today? Don't reply all on this one obviously lol'

Passive-aggressive 'professional' hostility:
'As I mentioned in my previous email (attached again for your convenience, since you seem to have missed it), the deadline was Friday. I'm happy to discuss your challenges with time management at your earliest convenience.'

DO NOT write emails like this (too bland):
'Hi Michael, Just following up on the report. Please let me know if you have any questions. Thanks, Dwight'

WRITING STYLE:
1. VARY EMAIL LENGTH REALISTICALLY:
   - Quick hostile replies: 'Fine.' or 'Whatever you say.' or 'Unbelievable.'
   - Venting messages: Long, emotional paragraphs when someone is upset
   - Cold professional responses: Short, clipped sentences when barely containing anger
   - Mix these based on the emotional state of the character

2. USE VARIED FORMATTING - NOT JUST PLAIN PARAGRAPHS:
   - Bullet points for lists: '- Item one' or '• Item one'
   - Numbered lists for steps or priorities: '1. First step' '2. Second step'
   - Action items: '[ ] Task to complete' or 'ACTION REQUIRED: ...'
   - ALL CAPS for emphasis when frustrated or angry
   - Bold key points with *asterisks* for emphasis
   - Short, punchy sentences mixed with longer explanatory ones

3. CAPTURE CHARACTER RELATIONSHIPS:
   - Write how these characters would ACTUALLY communicate based on their relationship
   - If they hate each other, SHOW IT in the email
   - If they're allies, show warmth and inside jokes
   - Use the personality notes to inform how characters interact
   - Tone should shift dramatically based on recipient (warm to friends, hostile to rivals)

4. INCLUDE DRAMA AND STAKES:
   - Reference specific plot events and their consequences
   - Show emotions: fear, excitement, anger, concern, desperation, contempt, jealousy
   - Include subtext and things left unsaid
   - Let tensions EXPLODE sometimes, not just simmer
   - Include emails people would regret sending

5. MAKE IT FEEL AUTHENTIC TO THE WORLD:
   - Use in-universe terminology, locations, and references
   - Reference shared history and grudges between characters
   - Include world-appropriate concerns and priorities

ATTACHMENTS - INTEGRATE NATURALLY INTO EMAIL CONTENT:
When an email has an attachment, the email body MUST reference it naturally:
- Documents: 'I've attached the report on...' or 'See the attached spreadsheet for...' or 'Here's the presentation we discussed'
- Images: 'Here's a picture from the feast!' or 'I managed to capture this - look at the expression on their face!' or 'Attached: photo evidence of...'
- For INLINE images, describe what's being shared: 'Check out this image from the ceremony:' (the image will appear in the email body)

The attachment fields you fill out will drive actual document/image generation, so be specific:
- documentDescription: What the document contains (e.g., 'Budget analysis for the dragon sanctuary expansion')
- imageDescription: What the image shows (e.g., 'A photo of the council meeting with the dragon visible through the window')

TECHNICAL RULES:
1. Each email must logically follow the previous one
2. Reference previous emails naturally in replies (the system will automatically add quoted text)
3. ALWAYS include the sender's signature block at the end of each email body
4. Signature blocks must be used EXACTLY as provided - copy them character for character
5. DO NOT include quoted previous emails in bodyPlain - the system adds those automatically
6. IDENTITY RULE: The person in fromEmail IS the person writing the email. The greeting, body text, and signature MUST all be written AS that person. If fromEmail is alice@example.com, then Alice is writing, Alice's perspective is used, and Alice's signature block goes at the end. NEVER write an email as one character but put a different character in fromEmail.

THREAD STRUCTURE - CREATE REALISTIC COMPLEXITY:
For longer threads (5+ emails), create realistic branching and side conversations:
- Side conversations: Someone forwards to an ally privately asking for input
- Breakout threads: A reply goes to just ONE person instead of reply-all
- Forwards: Someone forwards the thread to bring in a new person with 'FYI' or 'See below'
- Loop-backs: After a side conversation, someone rejoins the main thread with new info

Use the 'replyToIndex' field to specify which email is being replied to or forwarded:
- Index 0 = first email, 1 = second email, etc.
- This allows branching: email 5 might reply to email 2 (a side conversation), not email 4

Respond with valid JSON only.";
    }

    /// <summary>
    /// Build a narrative context summary from previous emails for batch continuity
    /// </summary>
    private static string BuildNarrativeContext(List<EmailMessage> previousMessages)
    {
        // Summarize the last few emails to give the AI context
        var recentMessages = previousMessages.TakeLast(5).ToList();
        var summaries = recentMessages.Select(m =>
        {
            // Take first 150 chars of body (before any quoted content)
            var bodyPreview = m.BodyPlain;
            var quoteIndex = bodyPreview.IndexOf("\n> ", StringComparison.Ordinal);
            if (quoteIndex > 0) bodyPreview = bodyPreview[..quoteIndex];
            var forwardIndex = bodyPreview.IndexOf("---------- Forwarded", StringComparison.Ordinal);
            if (forwardIndex > 0) bodyPreview = bodyPreview[..forwardIndex];
            if (bodyPreview.Length > 150) bodyPreview = bodyPreview[..150] + "...";

            return $"  - {m.From.FullName} → {string.Join(", ", m.To.Select(t => t.FirstName))}: {bodyPreview.Replace("\n", " ").Trim()}";
        });

        return $@"PREVIOUS EMAILS IN THIS THREAD (continue from here - use replyToIndex relative to THIS batch, starting at 0):
The thread subject is already established. Here's what happened so far ({previousMessages.Count} emails total):
{string.Join("\n", summaries)}

IMPORTANT: Continue the narrative naturally from where it left off. Reference events from the previous emails.
Your replyToIndex values should be 0-based within THIS batch (0 = first email in this batch).
The first email in this batch should be a reply or forward continuing the conversation.";
    }

    /// <summary>
    /// Distribute attachment counts for a batch proportionally
    /// </summary>
    private static int DistributeAttachmentsForBatch(int totalAttachments, ref int assigned, int batchSize, int remaining, bool isLastBatch)
    {
        if (totalAttachments <= 0 || assigned >= totalAttachments)
            return 0;

        if (isLastBatch)
        {
            // Last batch gets whatever is left
            var left = totalAttachments - assigned;
            assigned += left;
            return left;
        }

        // Proportional distribution
        var batchShare = (int)Math.Round((double)(totalAttachments - assigned) * batchSize / remaining);
        batchShare = Math.Min(batchShare, totalAttachments - assigned);
        batchShare = Math.Min(batchShare, batchSize); // Can't have more attachments than emails
        assigned += batchShare;
        return batchShare;
    }

    /// <summary>
    /// Build attachment instructions for a specific batch
    /// </summary>
    private static string BuildBatchAttachmentInstructions(GenerationConfig config, int docCount, int imageCount, int voicemailCount)
    {
        var instructions = new List<string>();
        var limits = new List<string>();

        if (docCount > 0 && config.EnabledAttachmentTypes.Count > 0)
        {
            var types = string.Join(", ", config.EnabledAttachmentTypes.Select(t => t.ToString().ToLower()));
            instructions.Add($@"DOCUMENT ATTACHMENTS: Include EXACTLY {docCount} email(s) with document attachments (no more, no less).
  - Available types: {types}
  - Make documents relevant to the storyline (reports, spreadsheets, presentations)
  - The email body MUST reference the attachment naturally
  - Provide a detailed documentDescription that explains what the document contains");
            limits.Add($"documents: {docCount}");
        }

        if (imageCount > 0)
        {
            instructions.Add($@"IMAGE ATTACHMENTS - MANDATORY: You MUST set hasImage: true for EXACTLY {imageCount} email(s).
  - This is REQUIRED, not optional. Set hasImage: true for {imageCount} emails.
  - Images should be photos, screenshots, or visual evidence relevant to the plot
  - MAKE MOST IMAGES INLINE (isImageInline: true) - the reader sees the image embedded in the email
  - Provide a VIVID, DETAILED imageDescription that can be used to generate the image with DALL-E
  - Example: 'A candid photo taken at the office party showing Michael wearing a ridiculous costume while Jim looks on with an exasperated expression'");
            limits.Add($"images: {imageCount}");
        }

        if (voicemailCount > 0)
        {
            instructions.Add($@"VOICEMAIL ATTACHMENTS: Include EXACTLY {voicemailCount} email(s) with voicemail attachments (no more, no less).
  - Voicemails are audio messages that complement or precede the email
  - Great for urgent situations, follow-ups
  - Provide voicemailContext describing what the voice message is about");
            limits.Add($"voicemails: {voicemailCount}");
        }

        if (instructions.Count == 0)
        {
            return "ATTACHMENTS: Do NOT include any attachments in this batch. Set all hasDocument, hasImage, hasVoicemail to false.";
        }

        var limitsStr = string.Join(", ", limits);
        return $"ATTACHMENT LIMITS - STRICT COUNT: {limitsStr}\n" +
               "DO NOT exceed these counts. Most emails should have NO attachments.\n\n" +
               string.Join("\n\n", instructions);
    }

    /// <summary>
    /// Build the user prompt for a specific batch
    /// </summary>
    private string BuildBatchUserPrompt(
        Storyline storyline, string characterList,
        DateTime batchStartDate, DateTime batchEndDate,
        int batchSize, string attachmentInstructions,
        string narrativeContext, string narrativePhase,
        bool isFirstBatch, string? existingSubject)
    {
        var subjectInstruction = isFirstBatch
            ? @"""subject"": ""string (original email subject, not including RE: or FW:)"""
            : $@"""subject"": ""{existingSubject}"" (MUST use this exact subject)";

        var firstEmailNote = isFirstBatch
            ? "- First email should NOT be a reply (replyToIndex: -1 or omit)"
            : "- First email in this batch should continue the thread (reply or forward)";

        return $@"Storyline: {storyline.Title}
Description: {storyline.Description}

NARRATIVE PHASE: {narrativePhase}

Available Characters:
{characterList}

Date Range: {batchStartDate:yyyy-MM-dd} to {batchEndDate:yyyy-MM-dd}

{narrativeContext}

Generate EXACTLY {batchSize} emails for this storyline.
The emails should stay TRUE to the fictional universe.

CRITICAL - EMOTIONAL AUTHENTICITY:
- Look at each character's personality notes - if they DISLIKE someone, their emails should SHOW IT
- Rivals and enemies should be hostile, passive-aggressive, or coldly professional - NOT friendly
- Allies should be warm, share frustrations about mutual enemies, use inside jokes
- Include at least ONE email where someone is clearly angry, upset, or hostile
- Let tensions escalate - don't keep everything polite and professional
- Include emails people might regret sending (too angry, too honest, cc'd wrong people)

MAKE THE EMAILS VARIED AND REALISTIC:
- Vary lengths: curt angry replies ('Fine.'), venting rants, cold professional responses
- Use formatting: bullet points, numbered lists, *emphasis*, ALL CAPS when frustrated
- Show the RELATIONSHIP in every email - enemies don't write friendly emails
- Include the drama, tension, and stakes from the source material
- Show real emotions: contempt, anger, fear, jealousy, betrayal, desperation
- Reference the grudges and conflicts between these specific characters
- Let conflicts EXPLODE at least once, not just simmer politely

{attachmentInstructions}

For threads with 5+ emails, include at least ONE of these realistic patterns:
- A private side conversation (someone forwards to an ally asking for their take)
- Someone brings in another character via forward
- A reply that only goes to one person instead of the whole group
- Someone who was CC'd jumps into the conversation

Respond with JSON in this exact format:
{{
  {subjectInstruction},
  ""emails"": [
    {{
      ""fromEmail"": ""string (must be one of the character emails)"",
      ""toEmails"": [""string""],
      ""ccEmails"": [""string""] (optional, can be empty array),
      ""sentDateTime"": ""ISO 8601 format"",
      ""bodyPlain"": ""string (full email body including greeting and signature)"",
      ""isReply"": boolean,
      ""isForward"": boolean,
      ""replyToIndex"": number (0-based index WITHIN THIS BATCH of which email this replies to/forwards; use -1 for first email),
      ""hasDocument"": boolean (true if this email has a document attachment),
      ""documentType"": ""word"" | ""excel"" | ""powerpoint"" (only if hasDocument is true),
      ""documentDescription"": ""string describing document content (only if hasDocument is true)"",
      ""hasImage"": boolean (true if this email includes an image),
      ""imageDescription"": ""string describing what the image shows (only if hasImage is true)"",
      ""isImageInline"": boolean (true = image embedded in email body, false = regular attachment),
      ""hasVoicemail"": boolean (true if a voicemail accompanies this email),
      ""voicemailContext"": ""string describing voicemail context (only if hasVoicemail is true)""
    }}
  ]
}}

CRITICAL RULES:
- Generate EXACTLY {batchSize} emails
- All email addresses must match exactly one of the characters listed above
- Dates must be within the specified range and in chronological order
{firstEmailNote}
- Use replyToIndex to create branching within this batch
- Forwards bring new people into the conversation
- DO NOT include '> ' quoted text or 'On [date] wrote:' sections - the system adds those automatically
- Write only the NEW content the sender is adding (their message + signature)
- When an email has an attachment, the body MUST reference it naturally

EMAIL BODY FORMAT - IMPORTANT:
- The bodyPlain field should contain ONLY the email message content
- NEVER start bodyPlain with 'Subject:', 'RE:', 'FW:', or any header-like text
- The subject is a SEPARATE field - do not repeat it in the body
- bodyPlain should start with a greeting or jump straight into the message

SIGNATURE BLOCK RULES - EXTREMELY IMPORTANT:
- The signature at the end of bodyPlain MUST belong to the person in fromEmail
- NEVER put one character's signature on another character's email
- Copy the EXACT signature block for that character from the list above - character by character
- This is a DATA INTEGRITY issue - wrong signatures make the emails invalid

ATTACHMENT REMINDER:
- If the instructions say to include N images, you MUST set hasImage: true for exactly N emails
- If the instructions say to include N documents, you MUST set hasDocument: true for exactly N emails
- If the instructions say to include N voicemails, you MUST set hasVoicemail: true for exactly N emails
- Do NOT skip attachments - they are REQUIRED if specified in the instructions above";
    }

    /// <summary>
    /// Convert an EmailDto from the API response into an EmailMessage, handling threading and quoted content
    /// </summary>
    /// <summary>
    /// Resolve a model-supplied replyToIndex, which is relative to the current batch
    /// (the batch starts at <paramref name="batchOffset"/> in the thread). Out-of-range
    /// indexes fall back to the last message; an empty thread returns null.
    /// </summary>
    internal static EmailMessage? ResolveReferencedEmail(IReadOnlyList<EmailMessage> messages, int batchOffset, int replyToIndex)
    {
        if (replyToIndex >= 0 && batchOffset + replyToIndex < messages.Count)
        {
            return messages[batchOffset + replyToIndex];
        }

        return messages.Count > 0 ? messages[^1] : null;
    }

    /// <summary>
    /// Build a meeting start time, clamping model-supplied hour/minute to valid ranges.
    /// </summary>
    internal static DateTime BuildMeetingStart(DateTime date, int hour, int minute) =>
        new(date.Year, date.Month, date.Day, Math.Clamp(hour, 0, 23), Math.Clamp(minute, 0, 59), 0);

    private EmailMessage ConvertDtoToEmailMessage(
        EmailDto e, EmailThread thread, int batchOffset, List<Character> characters,
        Dictionary<string, OrganizationTheme> domainThemes,
        DateTime startDate, DateTime endDate, ref int sequence)
    {
        var fromChar = characters.FirstOrDefault(c =>
            c.Email.Equals(e.FromEmail, StringComparison.OrdinalIgnoreCase))
            ?? characters[Random.Shared.Next(characters.Count)];

        var toChars = e.ToEmails
            .Select(email => characters.FirstOrDefault(c =>
                c.Email.Equals(email, StringComparison.OrdinalIgnoreCase)))
            .Where(c => c != null)
            .Cast<Character>()
            .ToList();

        if (toChars.Count == 0)
        {
            // Fall back to the sender when the cast has only one character
            toChars.Add(characters.FirstOrDefault(c => c.Id != fromChar.Id) ?? fromChar);
        }

        var ccChars = (e.CcEmails ?? new List<string>())
            .Select(email => characters.FirstOrDefault(c =>
                c.Email.Equals(email, StringComparison.OrdinalIgnoreCase)))
            .Where(c => c != null)
            .Cast<Character>()
            .ToList();

        var subject = thread.Subject;
        if (e.IsForward)
            subject = ThreadingHelper.AddForwardPrefix(subject);
        else if (e.IsReply && sequence > 0)
            subject = ThreadingHelper.AddReplyPrefix(subject);

        DateTime sentDate;
        if (!DateTime.TryParse(e.SentDateTime, out sentDate))
        {
            sentDate = DateHelper.RandomDateInRange(startDate, endDate);
        }

        // Fix sender/signature mismatch: ensure the body's signature matches the fromEmail character
        var correctedBody = CorrectSignatureBlock(e.BodyPlain, fromChar, characters);

        // Build the full email body with quoted content for replies/forwards
        var fullBody = correctedBody;

        // Get the email being replied to/forwarded based on the batch-relative replyToIndex
        var referencedEmail = ResolveReferencedEmail(thread.Messages, batchOffset, e.ReplyToIndex);

        var shouldQuoteAsReply = e.IsReply;
        if (!shouldQuoteAsReply && !e.IsForward && sequence > 0 && referencedEmail != null)
        {
            shouldQuoteAsReply = true;
        }

        if (shouldQuoteAsReply && referencedEmail != null)
        {
            fullBody += ThreadingHelper.FormatQuotedReply(referencedEmail);
        }
        else if (e.IsForward && referencedEmail != null)
        {
            fullBody += ThreadingHelper.FormatForwardedContent(referencedEmail);
        }

        var senderDomain = fromChar.Domain;
        domainThemes.TryGetValue(senderDomain, out var senderTheme);

        var email = new EmailMessage
        {
            ThreadId = thread.Id,
            From = fromChar,
            To = toChars,
            Cc = ccChars,
            Subject = subject,
            BodyPlain = fullBody,
            BodyHtml = HtmlEmailFormatter.ConvertToHtml(fullBody, senderTheme),
            SentDate = sentDate,
            SequenceInThread = sequence++,
            PlannedHasDocument = e.HasDocument,
            PlannedDocumentType = e.DocumentType,
            PlannedDocumentDescription = e.DocumentDescription,
            PlannedHasImage = e.HasImage,
            PlannedImageDescription = e.ImageDescription,
            PlannedIsImageInline = e.IsImageInline,
            PlannedHasVoicemail = e.HasVoicemail,
            PlannedVoicemailContext = e.VoicemailContext
        };

        return email;
    }

    /// <summary>
    /// Programmatically correct the signature block in an email body to match the actual sender.
    /// The AI sometimes puts the wrong character's signature on an email.
    /// </summary>
    private static string CorrectSignatureBlock(string body, Character fromChar, List<Character> allCharacters)
    {
        if (string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(fromChar.SignatureBlock))
            return body;

        var correctSig = fromChar.SignatureBlock.Trim();

        // Check if the correct signature is already present
        if (body.Contains(correctSig, StringComparison.OrdinalIgnoreCase))
            return body;

        // Check if any OTHER character's signature is in the body
        foreach (var otherChar in allCharacters)
        {
            if (otherChar.Id == fromChar.Id || string.IsNullOrWhiteSpace(otherChar.SignatureBlock))
                continue;

            var wrongSig = otherChar.SignatureBlock.Trim();
            if (string.IsNullOrWhiteSpace(wrongSig))
                continue;

            var sigIndex = body.IndexOf(wrongSig, StringComparison.OrdinalIgnoreCase);
            if (sigIndex >= 0)
            {
                // Replace the wrong signature with the correct one
                body = body[..sigIndex] + correctSig + body[(sigIndex + wrongSig.Length)..];
                return body;
            }
        }

        // No exact signature block match found - check for wrong character names in common signature patterns
        // Look for patterns like "Best,\nWrong Name" or "Thanks,\nWrong Name" or "Regards,\nWrong Name"
        foreach (var otherChar in allCharacters)
        {
            if (otherChar.Id == fromChar.Id)
                continue;

            // Check for the other character's full name near the end of the email (last 30% of body)
            var searchRegion = body.Length > 100
                ? body[(int)(body.Length * 0.7)..]
                : body;

            var nameIndex = searchRegion.IndexOf(otherChar.FullName, StringComparison.OrdinalIgnoreCase);
            if (nameIndex >= 0)
            {
                // Found another character's name in the signature area
                // Look backwards for a common sign-off pattern to find where the signature starts
                var absoluteIndex = body.Length - searchRegion.Length + nameIndex;
                var beforeName = body[..absoluteIndex];

                // Find the last sign-off line before this name
                var signOffPatterns = new[] { "Best,", "Best regards,", "Regards,", "Thanks,", "Thank you,",
                    "Sincerely,", "Cheers,", "Kind regards,", "Warm regards,", "All the best,",
                    "Best wishes,", "Thanks!", "Thank you!", "Respectfully,", "Cordially," };

                int signOffStart = -1;
                foreach (var pattern in signOffPatterns)
                {
                    var idx = beforeName.LastIndexOf(pattern, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0 && idx > signOffStart)
                    {
                        signOffStart = idx;
                    }
                }

                if (signOffStart >= 0)
                {
                    // Replace everything from the sign-off to the end with the correct signature
                    body = body[..signOffStart].TrimEnd() + "\n\n" + correctSig;
                    return body;
                }
                else
                {
                    // No sign-off found, just replace from the wrong name onwards
                    body = body[..absoluteIndex].TrimEnd() + "\n\n" + correctSig;
                    return body;
                }
            }
        }

        // Also check if fromChar's own name is missing entirely from the signature area
        // If so, the AI may have written a signature with a completely different format
        var tailRegion = body.Length > 100 ? body[(int)(body.Length * 0.7)..] : body;
        if (!tailRegion.Contains(fromChar.FullName, StringComparison.OrdinalIgnoreCase) &&
            !tailRegion.Contains(fromChar.FirstName, StringComparison.OrdinalIgnoreCase))
        {
            // The correct sender's name doesn't appear at all in the signature area
            // Try to find and replace any sign-off + name pattern at the end
            var signOffPatterns = new[] { "Best,", "Best regards,", "Regards,", "Thanks,", "Thank you,",
                "Sincerely,", "Cheers,", "Kind regards,", "Warm regards,", "All the best,",
                "Best wishes,", "Thanks!", "Thank you!", "Respectfully,", "Cordially," };

            int lastSignOff = -1;
            foreach (var pattern in signOffPatterns)
            {
                var tailStart = (int)(body.Length * 0.7);
                var idx = body.IndexOf(pattern, tailStart, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0 && (lastSignOff == -1 || idx < lastSignOff))
                {
                    lastSignOff = idx;
                }
            }

            if (lastSignOff >= 0)
            {
                // Replace from the sign-off to end with correct signature
                body = body[..lastSignOff].TrimEnd() + "\n\n" + correctSig;
                return body;
            }
        }

        return body;
    }

    private List<EmailMessage> SelectEmailsForAttachments(List<EmailMessage> emails, int percentage)
    {
        if (percentage <= 0) return new List<EmailMessage>();

        var count = Math.Max(1, (int)Math.Round(emails.Count * percentage / 100.0));
        return emails.OrderBy(_ => Random.Shared.Next()).Take(count).ToList();
    }

    /// <summary>
    /// Generate a document attachment based on the AI-planned description
    /// </summary>
    private async Task GeneratePlannedDocumentAsync(EmailMessage email, WizardState state, CancellationToken ct)
    {
        if (!email.PlannedHasDocument || string.IsNullOrEmpty(email.PlannedDocumentType))
            return;

        var attachmentType = email.PlannedDocumentType.ToLowerInvariant() switch
        {
            "word" => AttachmentType.Word,
            "excel" => AttachmentType.Excel,
            "powerpoint" => AttachmentType.PowerPoint,
            _ => AttachmentType.Word
        };

        // Check if this type is enabled in config
        if (!state.Config.EnabledAttachmentTypes.Contains(attachmentType))
        {
            // Fall back to first enabled type
            if (state.Config.EnabledAttachmentTypes.Count == 0) return;
            attachmentType = state.Config.EnabledAttachmentTypes[0];
        }

        var isDetailed = state.Config.AttachmentComplexity == AttachmentComplexity.Detailed;

        // Use the planned description as context
        var context = $@"Email subject: {email.Subject}
Document purpose (from email): {email.PlannedDocumentDescription ?? "Supporting document for this email"}
Email body preview: {email.BodyPlain[..Math.Min(300, email.BodyPlain.Length)]}...";

        // Check if we should continue an existing document chain
        DocumentChainState? chainState = null;
        int? reservedVersion = null;
        if (state.Config.EnableAttachmentChains && _documentChains.Count > 0 && Random.Shared.Next(100) < 30)
        {
            var matchingChains = _documentChains.Values
                .Where(c => c.Type == attachmentType)
                .ToList();

            if (matchingChains.Count > 0)
            {
                chainState = matchingChains[Random.Shared.Next(matchingChains.Count)];
                // Reserve the version up front so parallel revisions never share a number
                reservedVersion = chainState.ReserveNextVersion();
                context += $"\n\nIMPORTANT: This is a REVISION of a document titled '{chainState.BaseTitle}'. ";
                context += $"This is version {reservedVersion}. ";
                context += "Make changes/updates to reflect edits, feedback, or revisions.";
            }
        }

        Attachment attachment;
        switch (attachmentType)
        {
            case AttachmentType.Word:
                attachment = await GenerateWordAttachmentAsync(context, email, isDetailed, state, ct, chainState, reservedVersion);
                break;
            case AttachmentType.Excel:
                attachment = await GenerateExcelAttachmentAsync(context, email, isDetailed, ct);
                break;
            case AttachmentType.PowerPoint:
                attachment = await GeneratePowerPointAttachmentAsync(context, email, isDetailed, state, ct);
                break;
            default:
                return;
        }

        // Handle document chain versioning
        if (chainState != null && reservedVersion.HasValue && attachment.Content != null)
        {
            attachment.DocumentChainId = chainState.ChainId;
            attachment.VersionLabel = GetVersionLabel(reservedVersion.Value);
            var baseName = chainState.BaseTitle.Replace(" ", "_");
            attachment.FileName = $"{baseName}_{attachment.VersionLabel}{attachment.Extension}";
        }
        else if (state.Config.EnableAttachmentChains && attachment.Type == AttachmentType.Word && Random.Shared.Next(100) < 50)
        {
            // Start a new chain for Word documents
            var newChain = new DocumentChainState
            {
                BaseTitle = attachment.ContentDescription,
                Type = attachment.Type,
                VersionNumber = 1
            };
            _documentChains.TryAdd(newChain.ChainId, newChain);
            attachment.DocumentChainId = newChain.ChainId;
            attachment.VersionLabel = "v1";
        }

        if (string.IsNullOrEmpty(attachment.FileName))
        {
            attachment.FileName = FileNameHelper.GenerateAttachmentFileName(attachment, email);
        }

        email.Attachments.Add(attachment);
    }

    /// <summary>
    /// Generate an image based on the AI-planned description
    /// </summary>
    private async Task GeneratePlannedImageAsync(EmailMessage email, WizardState state, CancellationToken ct)
    {
        if (!email.PlannedHasImage || string.IsNullOrEmpty(email.PlannedImageDescription))
            return;

        // Generate the image using DALL-E with the planned description
        var imagePrompt = $"A vivid, realistic image in the style/universe of {state.Topic}: {email.PlannedImageDescription}. High quality, detailed.";

        var imageBytes = await _openAI.GenerateImageAsync(imagePrompt, "Image Generation", ct);

        if (imageBytes == null || imageBytes.Length == 0)
            return;

        var contentId = $"img_{Guid.NewGuid():N}";
        var attachment = new Attachment
        {
            Type = AttachmentType.Image,
            Content = imageBytes,
            ContentDescription = email.PlannedImageDescription,
            IsInline = email.PlannedIsImageInline,
            ContentId = contentId,
            FileName = $"image_{email.SentDate:yyyyMMdd}_{Guid.NewGuid().ToString("N")[..6]}.png"
        };

        email.Attachments.Add(attachment);

        // If inline, update the HTML body to include the image in the main content (before quoted text)
        if (email.PlannedIsImageInline && !string.IsNullOrEmpty(email.BodyHtml))
        {
            var caption = email.PlannedImageDescription.Length > 100
                ? email.PlannedImageDescription[..100] + "..."
                : email.PlannedImageDescription;

            var imageHtml = $@"<div style=""text-align: center; margin: 15px 0;""><img src=""cid:{contentId}"" alt=""{System.Net.WebUtility.HtmlEncode(caption)}"" style=""max-width: 600px; height: auto; border-radius: 4px;"" /></div>";

            // Try to insert the image BEFORE the quoted content (reply or forward sections)
            // This ensures the image appears in the new email content, not after the quoted text
            var insertionPoints = new[]
            {
                "<div class=\"quoted-content\">",  // Reply quoted content
                "<div class=\"forward-header\">",   // Forwarded message header
                "<div class=\"signature\">"         // Before signature if no quoted content
            };

            bool inserted = false;
            foreach (var marker in insertionPoints)
            {
                var markerIndex = email.BodyHtml.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (markerIndex > 0)
                {
                    email.BodyHtml = email.BodyHtml.Insert(markerIndex, imageHtml);
                    inserted = true;
                    break;
                }
            }

            // If no insertion point found, insert before the disclaimer banner or closing div
            if (!inserted)
            {
                // Look for the disclaimer banner image
                var disclaimerIndex = email.BodyHtml.IndexOf("<img src=\"data:image/png;base64,", StringComparison.OrdinalIgnoreCase);
                if (disclaimerIndex > 0)
                {
                    email.BodyHtml = email.BodyHtml.Insert(disclaimerIndex, imageHtml);
                }
                else if (email.BodyHtml.Contains("</div>\n</body>"))
                {
                    // Insert before closing email-body div
                    email.BodyHtml = email.BodyHtml.Replace("</div>\n</body>", imageHtml + "</div>\n</body>");
                }
                else if (email.BodyHtml.Contains("</body>"))
                {
                    email.BodyHtml = email.BodyHtml.Replace("</body>", imageHtml + "</body>");
                }
                else
                {
                    email.BodyHtml += imageHtml;
                }
            }
        }
    }

    /// <summary>
    /// Generate a voicemail based on the AI-planned context
    /// </summary>
    private async Task GeneratePlannedVoicemailAsync(EmailMessage email, WizardState state, CancellationToken ct)
    {
        if (!email.PlannedHasVoicemail)
            return;

        // Generate a voicemail script using the planned context
        var systemPrompt = @"You are creating a voicemail message that relates to an email.
The voicemail should sound natural and conversational, as if someone called and left a message.
Keep the voicemail BRIEF - 15-30 seconds when spoken (about 40-80 words).

Respond with JSON only.";

        var context = $@"Email subject: {email.Subject}
Sender: {email.From.FullName}
Voicemail context: {email.PlannedVoicemailContext ?? "A follow-up or urgent message related to the email"}
Topic/Universe: {state.Topic}";

        var userPrompt = $@"{context}

Create a voicemail that {email.From.FirstName} might leave related to this email.
The voicemail should:
- Sound natural and conversational (include 'um', 'uh', pauses indicated by '...')
- Reference the email topic with appropriate urgency
- Start with a greeting ('Hey, it's [name]...' or 'Hi, this is [name] calling about...')
- End naturally ('...call me back when you get this' or 'talk soon')
- Be 40-80 words total

Respond with JSON:
{{
  ""voicemailScript"": ""string (the voicemail transcript)""
}}";

        var response = await _openAI.GetJsonCompletionAsync<VoicemailScriptResponse>(systemPrompt, userPrompt, "Voicemail Script", ct);

        if (response == null || string.IsNullOrEmpty(response.VoicemailScript))
            return;

        // Generate the audio using TTS
        var audioBytes = await _openAI.GenerateSpeechAsync(
            response.VoicemailScript,
            email.From.VoiceId,
            "Voicemail TTS",
            ct);

        if (audioBytes == null || audioBytes.Length == 0)
            return;

        var attachment = new Attachment
        {
            Type = AttachmentType.Voicemail,
            Content = audioBytes,
            ContentDescription = $"Voicemail from {email.From.FullName}",
            VoiceId = email.From.VoiceId,
            FileName = $"voicemail_{email.From.LastName}_{email.SentDate:yyyyMMdd_HHmm}.mp3"
        };

        email.Attachments.Add(attachment);
    }

    private async Task GenerateAttachmentAsync(EmailMessage email, WizardState state, CancellationToken ct)
    {
        var enabledTypes = state.Config.EnabledAttachmentTypes;
        if (enabledTypes.Count == 0) return;

        var attachmentType = enabledTypes[Random.Shared.Next(enabledTypes.Count)];
        var isDetailed = state.Config.AttachmentComplexity == AttachmentComplexity.Detailed;

        // Check if we should continue an existing document chain (30% chance if enabled)
        DocumentChainState? chainState = null;
        int? reservedVersion = null;
        if (state.Config.EnableAttachmentChains && _documentChains.Count > 0 && Random.Shared.Next(100) < 30)
        {
            // Pick a random existing chain of the same type
            var matchingChains = _documentChains.Values
                .Where(c => c.Type == attachmentType)
                .ToList();

            if (matchingChains.Count > 0)
            {
                chainState = matchingChains[Random.Shared.Next(matchingChains.Count)];
                // Reserve the version up front so parallel revisions never share a number
                reservedVersion = chainState.ReserveNextVersion();
            }
        }

        var attachment = new Attachment
        {
            Type = attachmentType
        };

        var context = $"Email subject: {email.Subject}\nEmail body preview: {email.BodyPlain[..Math.Min(200, email.BodyPlain.Length)]}...";

        // Add version context if continuing a chain
        if (chainState != null)
        {
            context += $"\n\nIMPORTANT: This is a REVISION of a document titled '{chainState.BaseTitle}'. ";
            context += $"This is version {reservedVersion}. ";
            context += "Make changes/updates to reflect edits, feedback, or revisions - don't create something completely new.";
        }

        switch (attachmentType)
        {
            case AttachmentType.Word:
                attachment = await GenerateWordAttachmentAsync(context, email, isDetailed, state, ct, chainState, reservedVersion);
                break;
            case AttachmentType.Excel:
                attachment = await GenerateExcelAttachmentAsync(context, email, isDetailed, ct);
                break;
            case AttachmentType.PowerPoint:
                attachment = await GeneratePowerPointAttachmentAsync(context, email, isDetailed, state, ct);
                break;
        }

        // Handle document chain versioning
        if (chainState != null && reservedVersion.HasValue && attachment.Content != null)
        {
            attachment.DocumentChainId = chainState.ChainId;
            attachment.VersionLabel = GetVersionLabel(reservedVersion.Value);

            // Update filename to include version
            var baseName = chainState.BaseTitle.Replace(" ", "_");
            attachment.FileName = $"{baseName}_{attachment.VersionLabel}{attachment.Extension}";
        }
        else if (state.Config.EnableAttachmentChains && attachment.Type == AttachmentType.Word)
        {
            // Start a new chain for Word documents (50% chance)
            if (Random.Shared.Next(100) < 50)
            {
                var newChain = new DocumentChainState
                {
                    BaseTitle = attachment.ContentDescription,
                    Type = attachment.Type,
                    VersionNumber = 1
                };
                _documentChains.TryAdd(newChain.ChainId, newChain);
                attachment.DocumentChainId = newChain.ChainId;
                attachment.VersionLabel = "v1";
            }
        }

        if (string.IsNullOrEmpty(attachment.FileName))
        {
            attachment.FileName = FileNameHelper.GenerateAttachmentFileName(attachment, email);
        }
        email.Attachments.Add(attachment);
    }

    private static string GetVersionLabel(int version)
    {
        // Fun realistic version labels
        return version switch
        {
            1 => "v1",
            2 => "v2",
            3 => "v3_revised",
            4 => "v4_final",
            5 => "v5_FINAL",
            6 => "v6_FINAL_v2",
            7 => "v7_FINAL_FINAL",
            8 => "v8_USE_THIS_ONE",
            _ => $"v{version}_latest"
        };
    }

    private async Task<Attachment> GenerateWordAttachmentAsync(
        string context, EmailMessage email, bool detailed, WizardState state, CancellationToken ct, DocumentChainState? chainState = null, int? reservedVersion = null)
    {
        var systemPrompt = @"Generate content for a Word document attachment.
The content should be realistic and related to the email context.
Respond with valid JSON only.";

        var detailLevel = detailed
            ? "Generate a detailed document with 4-6 paragraphs, including an introduction, main content with 2-3 key points, and a conclusion."
            : "Generate a brief document with 2-3 paragraphs of relevant content.";

        // Add revision instructions if this is a versioned document
        var versionNote = "";
        if (chainState != null)
        {
            versionNote = $@"

IMPORTANT: This is VERSION {reservedVersion ?? chainState.VersionNumber} of '{chainState.BaseTitle}'.
Make realistic revisions:
- Keep the same overall topic/title
- Add or modify some sections
- Include tracked-changes style notes like '[Updated per feedback]' or '[Revised figures]'
- Maybe add a 'Revision History' section at the end";
        }

        var userPrompt = $@"Context:
{context}
{versionNote}
{detailLevel}

Respond with JSON:
{{
  ""title"": ""string (document title)"",
  ""content"": ""string (full document content, paragraphs separated by double newlines)""
}}";

        var response = await _openAI.GetJsonCompletionAsync<WordDocResponse>(systemPrompt, userPrompt, "Word Attachment", ct);

        var title = response?.Title ?? "Document";

        // If continuing a chain, keep the original title
        if (chainState != null)
        {
            title = chainState.BaseTitle;
        }

        // Get the organization theme for the sender's domain
        OrganizationTheme? theme = null;
        var senderDomain = email.From?.Domain;
        if (!string.IsNullOrEmpty(senderDomain) && state.DomainThemes.TryGetValue(senderDomain, out var domainTheme))
        {
            theme = domainTheme;
        }

        var content = _officeService.CreateWordDocument(
            title,
            response?.Content ?? "Content unavailable.",
            theme);

        return new Attachment
        {
            Type = AttachmentType.Word,
            ContentDescription = title,
            Content = content
        };
    }

    private async Task<Attachment> GenerateExcelAttachmentAsync(
        string context, EmailMessage email, bool detailed, CancellationToken ct)
    {
        var systemPrompt = @"Generate data for an Excel spreadsheet attachment.
The data should be realistic and related to the email context.
IMPORTANT: All values in the rows array MUST be strings, even if they represent numbers.
For example: use ""1234"" instead of 1234, use ""$5,000"" instead of 5000.
Respond with valid JSON only.";

        var rowCount = detailed ? "10-15" : "5-8";

        var userPrompt = $@"Context:
{context}

Generate spreadsheet data with:
- Appropriate column headers (3-6 columns)
- {rowCount} rows of realistic data
- Format numeric values as strings (e.g., ""$1,234"", ""500"", ""12.5%"")

Respond with JSON:
{{
  ""title"": ""string (spreadsheet title)"",
  ""headers"": [""string""],
  ""rows"": [[""string (ALL values must be strings, even numbers)""]]
}}

CRITICAL: Every cell value in rows must be a JSON string, not a number. Use quotes around all values.";

        var response = await _openAI.GetJsonCompletionAsync<ExcelDocResponseRaw>(systemPrompt, userPrompt, "Excel Attachment", ct);

        // Convert JsonElement rows to strings (handles both string and number values)
        var rows = new List<List<string>>();
        if (response?.Rows != null)
        {
            foreach (var row in response.Rows)
            {
                var stringRow = new List<string>();
                foreach (var cell in row)
                {
                    // Handle both string and numeric JSON values
                    stringRow.Add(cell.ValueKind == System.Text.Json.JsonValueKind.String
                        ? cell.GetString() ?? ""
                        : cell.ToString());
                }
                rows.Add(stringRow);
            }
        }

        if (rows.Count == 0)
        {
            rows.Add(new List<string> { "Data1", "Data2" });
        }

        var content = _officeService.CreateExcelDocument(
            response?.Title ?? "Spreadsheet",
            response?.Headers ?? new List<string> { "Column1", "Column2" },
            rows);

        return new Attachment
        {
            Type = AttachmentType.Excel,
            ContentDescription = response?.Title ?? "Spreadsheet",
            Content = content
        };
    }

    private async Task<Attachment> GeneratePowerPointAttachmentAsync(
        string context, EmailMessage email, bool detailed, WizardState state, CancellationToken ct)
    {
        var systemPrompt = @"Generate content for a PowerPoint presentation attachment.
The content should be realistic and related to the email context.
Respond with valid JSON only.";

        var slideCount = detailed ? "5-8" : "3-4";

        var userPrompt = $@"Context:
{context}

Generate presentation content with:
- A main title
- {slideCount} content slides
- Each slide should have a title and bullet points or brief content

Respond with JSON:
{{
  ""title"": ""string (presentation title)"",
  ""slides"": [
    {{
      ""slideTitle"": ""string"",
      ""content"": ""string (bullet points or paragraph)""
    }}
  ]
}}";

        var response = await _openAI.GetJsonCompletionAsync<PowerPointDocResponse>(systemPrompt, userPrompt, "PowerPoint Attachment", ct);

        var slides = response?.Slides?
            .Select(s => (s.SlideTitle, s.Content))
            .ToList() ?? new List<(string, string)> { ("Slide 1", "Content") };

        // Get the organization theme for the sender's domain
        OrganizationTheme? theme = null;
        var senderDomain = email.From?.Domain;
        if (!string.IsNullOrEmpty(senderDomain) && state.DomainThemes.TryGetValue(senderDomain, out var domainTheme))
        {
            theme = domainTheme;
        }

        var content = _officeService.CreatePowerPointDocument(
            response?.Title ?? "Presentation",
            slides,
            theme);

        return new Attachment
        {
            Type = AttachmentType.PowerPoint,
            ContentDescription = response?.Title ?? "Presentation",
            Content = content
        };
    }

    private async Task GenerateImageForEmailAsync(EmailMessage email, WizardState state, CancellationToken ct)
    {
        // First, get AI to describe what image would be appropriate for this email
        var systemPrompt = @"You are helping generate an image for an email in a fictional universe.
Based on the email content, suggest a single image that someone might attach or embed in this email.
The image should feel authentic to the fictional world and relevant to the email's content.

Respond with JSON only.";

        var context = $@"Email subject: {email.Subject}
Email body: {email.BodyPlain[..Math.Min(500, email.BodyPlain.Length)]}
Topic/Universe: {state.Topic}";

        var userPrompt = $@"{context}

Suggest ONE image that would be realistic to include with this email. Consider:
- Photos someone might share ('Here's a picture from the event')
- Screenshots or diagrams being discussed
- Images that add context to the storyline

Respond with JSON:
{{
  ""shouldIncludeImage"": boolean (false if no image makes sense for this email),
  ""imageDescription"": ""string (detailed description for image generation, 2-3 sentences)"",
  ""imageContext"": ""string (brief caption or how it's referenced in email, e.g., 'Attached: Photo from the banquet')"",
  ""isInline"": boolean (true if image should display in email body, false for attachment)
}}";

        var response = await _openAI.GetJsonCompletionAsync<ImageSuggestionResponse>(systemPrompt, userPrompt, "Image Suggestion", ct);

        if (response == null || !response.ShouldIncludeImage || string.IsNullOrEmpty(response.ImageDescription))
            return;

        // Generate the image using DALL-E
        // Craft a safe, descriptive prompt
        var imagePrompt = $"A realistic image in the style of {state.Topic}: {response.ImageDescription}. High quality, photorealistic where appropriate.";

        var imageBytes = await _openAI.GenerateImageAsync(imagePrompt, "Image Generation", ct);

        if (imageBytes == null || imageBytes.Length == 0)
            return;

        var contentId = $"img_{Guid.NewGuid():N}";
        var attachment = new Attachment
        {
            Type = AttachmentType.Image,
            Content = imageBytes,
            ContentDescription = response.ImageContext ?? "Attached image",
            IsInline = response.IsInline,
            ContentId = contentId,
            FileName = $"image_{email.SentDate:yyyyMMdd}_{Guid.NewGuid().ToString("N")[..6]}.png"
        };

        email.Attachments.Add(attachment);

        // If inline, update the HTML body to include the image in the main content (before quoted text)
        if (response.IsInline && !string.IsNullOrEmpty(email.BodyHtml))
        {
            var safeContext = System.Net.WebUtility.HtmlEncode(response.ImageContext ?? "Attached image");
            var imageHtml = $@"<div style=""margin: 15px 0;""><p><em>{safeContext}</em></p><div style=""text-align: center;""><img src=""cid:{contentId}"" alt=""{safeContext}"" style=""max-width: 600px; height: auto; border-radius: 4px;"" /></div></div>";

            // Try to insert the image BEFORE the quoted content (reply or forward sections)
            var insertionPoints = new[]
            {
                "<div class=\"quoted-content\">",
                "<div class=\"forward-header\">",
                "<div class=\"signature\">"
            };

            bool inserted = false;
            foreach (var marker in insertionPoints)
            {
                var markerIndex = email.BodyHtml.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (markerIndex > 0)
                {
                    email.BodyHtml = email.BodyHtml.Insert(markerIndex, imageHtml);
                    inserted = true;
                    break;
                }
            }

            if (!inserted)
            {
                var disclaimerIndex = email.BodyHtml.IndexOf("<img src=\"data:image/png;base64,", StringComparison.OrdinalIgnoreCase);
                if (disclaimerIndex > 0)
                {
                    email.BodyHtml = email.BodyHtml.Insert(disclaimerIndex, imageHtml);
                }
                else if (email.BodyHtml.Contains("</body>"))
                {
                    email.BodyHtml = email.BodyHtml.Replace("</body>", imageHtml + "</body>");
                }
                else
                {
                    email.BodyHtml += imageHtml;
                }
            }
        }
    }

    private class ImageSuggestionResponse
    {
        [JsonPropertyName("shouldIncludeImage")]
        public bool ShouldIncludeImage { get; set; }

        [JsonPropertyName("imageDescription")]
        public string ImageDescription { get; set; } = string.Empty;

        [JsonPropertyName("imageContext")]
        public string? ImageContext { get; set; }

        [JsonPropertyName("isInline")]
        public bool IsInline { get; set; }
    }

    private async Task DetectAndAddCalendarInviteAsync(EmailMessage email, List<Character> characters, CancellationToken ct)
    {
        // Ask AI if this email mentions a meeting that should have a calendar invite
        var systemPrompt = @"You analyze emails to detect if they are scheduling or confirming a meeting/event that should have a calendar invite attached.

Look for:
- Specific dates and times mentioned ('tomorrow at 3pm', 'Friday at noon', 'next week Monday')
- Meeting requests or confirmations
- Event invitations
- Scheduled calls or gatherings

Respond with JSON only.";

        var userPrompt = $@"Email subject: {email.Subject}
Email body: {email.BodyPlain[..Math.Min(800, email.BodyPlain.Length)]}
Email sent date: {email.SentDate:yyyy-MM-dd}

Does this email mention a specific meeting, event, or call that should have a calendar invite?

Respond with JSON:
{{
  ""hasMeeting"": boolean,
  ""meetingTitle"": ""string (title for the calendar invite)"",
  ""meetingDescription"": ""string (brief description)"",
  ""location"": ""string (meeting location or 'Virtual' or 'TBD')"",
  ""suggestedDate"": ""YYYY-MM-DD (the date of the meeting, based on context)"",
  ""suggestedStartTime"": ""HH:MM (24-hour format)"",
  ""durationMinutes"": number (30, 60, 90, 120, etc.)
}}";

        var response = await _openAI.GetJsonCompletionAsync<MeetingDetectionResponse>(systemPrompt, userPrompt, "Meeting Detection", ct);

        if (response == null || !response.HasMeeting)
            return;

        // Parse the meeting date and time
        if (!DateTime.TryParse(response.SuggestedDate, out var meetingDate))
        {
            meetingDate = email.SentDate.AddDays(1); // Default to next day
        }

        var timeParts = (response.SuggestedStartTime ?? "10:00").Split(':');
        var hour = int.TryParse(timeParts[0], out var h) ? h : 10;
        var minute = timeParts.Length > 1 && int.TryParse(timeParts[1], out var m) ? m : 0;

        var startTime = BuildMeetingStart(meetingDate, hour, minute);
        var endTime = startTime.AddMinutes(response.DurationMinutes > 0 ? response.DurationMinutes : 60);

        // Get attendees from the email recipients
        var attendees = email.To
            .Concat(email.Cc)
            .Where(c => c.Email != email.From.Email)
            .Select(c => (c.FullName, c.Email))
            .ToList();

        var icsContent = _calendarService.CreateCalendarInvite(
            response.MeetingTitle ?? email.Subject,
            response.MeetingDescription ?? "",
            startTime,
            endTime,
            response.Location ?? "TBD",
            email.From.FullName,
            email.From.Email,
            attendees);

        var attachment = new Attachment
        {
            Type = AttachmentType.CalendarInvite,
            Content = icsContent,
            ContentDescription = response.MeetingTitle ?? "Meeting Invite",
            FileName = $"invite_{startTime:yyyyMMdd}_{Guid.NewGuid().ToString("N")[..6]}.ics"
        };

        email.Attachments.Add(attachment);
    }

    private class MeetingDetectionResponse
    {
        [JsonPropertyName("hasMeeting")]
        public bool HasMeeting { get; set; }

        [JsonPropertyName("meetingTitle")]
        public string? MeetingTitle { get; set; }

        [JsonPropertyName("meetingDescription")]
        public string? MeetingDescription { get; set; }

        [JsonPropertyName("location")]
        public string? Location { get; set; }

        [JsonPropertyName("suggestedDate")]
        public string? SuggestedDate { get; set; }

        [JsonPropertyName("suggestedStartTime")]
        public string? SuggestedStartTime { get; set; }

        [JsonPropertyName("durationMinutes")]
        public int DurationMinutes { get; set; }
    }

    private async Task GenerateVoicemailForEmailAsync(EmailMessage email, WizardState state, CancellationToken ct)
    {
        // Ask AI to create a voicemail script related to this email thread
        var systemPrompt = @"You are creating a voicemail message that relates to an email thread.
The voicemail should sound natural and conversational, as if someone called and left a message.
It should relate to the email content but not simply read the email aloud.

Keep the voicemail BRIEF - 15-30 seconds when spoken (about 40-80 words).

Respond with JSON only.";

        var context = $@"Email subject: {email.Subject}
Email body preview: {email.BodyPlain[..Math.Min(400, email.BodyPlain.Length)]}
Sender: {email.From.FullName}
Topic/Universe: {state.Topic}";

        var userPrompt = $@"{context}

Create a voicemail that {email.From.FirstName} might leave that relates to this email thread.
The voicemail should:
- Sound natural and conversational (include 'um', 'uh', pauses indicated by '...')
- Reference the email topic but add urgency or context
- Start with a greeting ('Hey, it's [name]...' or 'Hi, this is [name] calling about...')
- End naturally ('...call me back when you get this' or 'talk soon')
- Be 40-80 words total

Respond with JSON:
{{
  ""shouldCreateVoicemail"": boolean (false if voicemail doesn't make sense),
  ""voicemailScript"": ""string (the voicemail transcript)"",
  ""recipientName"": ""string (who the voicemail is for)""
}}";

        var response = await _openAI.GetJsonCompletionAsync<VoicemailScriptResponse>(systemPrompt, userPrompt, "Voicemail Script", ct);

        if (response == null || !response.ShouldCreateVoicemail || string.IsNullOrEmpty(response.VoicemailScript))
            return;

        // Generate the audio using TTS
        var audioBytes = await _openAI.GenerateSpeechAsync(
            response.VoicemailScript,
            email.From.VoiceId,
            "Voicemail TTS",
            ct);

        if (audioBytes == null || audioBytes.Length == 0)
            return;

        var attachment = new Attachment
        {
            Type = AttachmentType.Voicemail,
            Content = audioBytes,
            ContentDescription = $"Voicemail from {email.From.FullName}",
            VoiceId = email.From.VoiceId,
            FileName = $"voicemail_{email.From.LastName}_{email.SentDate:yyyyMMdd_HHmm}.mp3"
        };

        email.Attachments.Add(attachment);
    }

    private class VoicemailScriptResponse
    {
        [JsonPropertyName("shouldCreateVoicemail")]
        public bool ShouldCreateVoicemail { get; set; }

        [JsonPropertyName("voicemailScript")]
        public string VoicemailScript { get; set; } = string.Empty;

        [JsonPropertyName("recipientName")]
        public string? RecipientName { get; set; }
    }

    // Response DTOs
    private class ThreadApiResponse
    {
        [JsonPropertyName("subject")]
        public string Subject { get; set; } = string.Empty;

        [JsonPropertyName("emails")]
        public List<EmailDto> Emails { get; set; } = new();
    }

    private class EmailDto
    {
        [JsonPropertyName("fromEmail")]
        public string FromEmail { get; set; } = string.Empty;

        [JsonPropertyName("toEmails")]
        public List<string> ToEmails { get; set; } = new();

        [JsonPropertyName("ccEmails")]
        public List<string>? CcEmails { get; set; }

        [JsonPropertyName("sentDateTime")]
        public string SentDateTime { get; set; } = string.Empty;

        [JsonPropertyName("bodyPlain")]
        public string BodyPlain { get; set; } = string.Empty;

        [JsonPropertyName("isReply")]
        public bool IsReply { get; set; }

        [JsonPropertyName("isForward")]
        public bool IsForward { get; set; }

        [JsonPropertyName("replyToIndex")]
        public int ReplyToIndex { get; set; } = -1;

        // Attachment planning fields - AI decides which emails get attachments
        [JsonPropertyName("hasDocument")]
        public bool HasDocument { get; set; }

        [JsonPropertyName("documentType")]
        public string? DocumentType { get; set; } // "word", "excel", "powerpoint"

        [JsonPropertyName("documentDescription")]
        public string? DocumentDescription { get; set; } // What the document is about

        [JsonPropertyName("hasImage")]
        public bool HasImage { get; set; }

        [JsonPropertyName("imageDescription")]
        public string? ImageDescription { get; set; } // What the image shows

        [JsonPropertyName("isImageInline")]
        public bool IsImageInline { get; set; } // true = inline in body, false = attachment

        [JsonPropertyName("hasVoicemail")]
        public bool HasVoicemail { get; set; }

        [JsonPropertyName("voicemailContext")]
        public string? VoicemailContext { get; set; } // Context for the voicemail
    }

    private class WordDocResponse
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    private class ExcelDocResponse
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("headers")]
        public List<string> Headers { get; set; } = new();

        [JsonPropertyName("rows")]
        public List<List<string>> Rows { get; set; } = new();
    }

    // Raw response that handles mixed types (strings and numbers) in Excel rows
    private class ExcelDocResponseRaw
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("headers")]
        public List<string> Headers { get; set; } = new();

        [JsonPropertyName("rows")]
        public List<List<System.Text.Json.JsonElement>> Rows { get; set; } = new();
    }

    private class PowerPointDocResponse
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("slides")]
        public List<SlideDto> Slides { get; set; } = new();
    }

    private class SlideDto
    {
        [JsonPropertyName("slideTitle")]
        public string SlideTitle { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }
}
