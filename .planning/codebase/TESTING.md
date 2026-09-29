# Testing Patterns

**Analysis Date:** 2026-09-29

## Test Framework

**Runner:**
- Not detected. The repository contains no test project, no `*Tests.cs` files, no `.sln`, and no CI workflow (`.github/` absent).
- The only verification tooling in use is `dotnet build` (allowed in `.claude/settings.local.json`) and manual runs of the WinForms wizard.
- Config: none.

**Assertion Library:**
- None.

**Recommended setup (when adding tests):**
- xUnit + `Microsoft.NET.Test.Sdk` + `xunit.runner.visualstudio`, targeting `net8.0-windows` with `<UseWindowsForms>true</UseWindowsForms>` so it can reference the WinExe project.
- The git root is the project folder itself (`ReelDiscovery.csproj` sits at repo root) and SDK-style default globbing compiles every `**/*.cs`. A test project placed inside the repo MUST be excluded from the app project, otherwise the app build will try to compile the tests:
  ```xml
  <!-- ReelDiscovery.csproj -->
  <ItemGroup>
    <Compile Remove="ReelDiscovery.Tests\**" />
    <EmbeddedResource Remove="ReelDiscovery.Tests\**" />
    <None Remove="ReelDiscovery.Tests\**" />
  </ItemGroup>
  ```
- Test project reference: `<ProjectReference Include="..\ReelDiscovery.csproj" />` from `ReelDiscovery.Tests/ReelDiscovery.Tests.csproj`.
- Note: `ReelDiscovery.csproj` sets `PublishSingleFile`, `SelfContained`, and `RuntimeIdentifier=win-x64` unconditionally; if referencing it from a test project causes RID/self-contained build errors, move those properties into a `Condition="'$(Configuration)' == 'Release'"` group or a publish profile.

**Run Commands:**
```bash
dotnet build                                   # Only current verification step
dotnet test ReelDiscovery.Tests                # Run all tests (once project exists)
dotnet test ReelDiscovery.Tests --collect:"XPlat Code Coverage"   # Coverage (requires coverlet.collector)
```

## Test File Organization

**Location:**
- Not applicable yet. Use a separate project `ReelDiscovery.Tests/` at repo root, mirroring source folders.

**Naming:**
- `{TypeUnderTest}Tests.cs`, test methods `MethodName_Scenario_ExpectedResult`.

**Structure:**
```
ReelDiscovery.Tests/
├── ReelDiscovery.Tests.csproj
├── Helpers/
│   ├── ThreadingHelperTests.cs
│   ├── FileNameHelperTests.cs
│   ├── DateHelperTests.cs
│   └── HtmlEmailFormatterTests.cs
├── Models/
│   ├── AttachmentTests.cs
│   ├── CharacterTests.cs
│   └── TokenUsageTrackerTests.cs
└── Services/
    ├── EmlFileServiceTests.cs
    ├── OfficeDocumentServiceTests.cs
    └── CalendarServiceTests.cs
```

## Test Structure

**Suite Organization (prescribed, xUnit):**
```csharp
using ReelDiscovery.Helpers;
using Xunit;

namespace ReelDiscovery.Tests.Helpers;

public class ThreadingHelperTests
{
    [Theory]
    [InlineData("Budget", "RE: Budget")]
    [InlineData("RE: Budget", "RE: Budget")]
    [InlineData("Re: Budget", "Re: Budget")]
    public void AddReplyPrefix_AddsPrefixOnlyOnce(string input, string expected)
    {
        Assert.Equal(expected, ThreadingHelper.AddReplyPrefix(input));
    }

    [Fact]
    public void GetCleanSubject_StripsNestedPrefixes()
    {
        Assert.Equal("Budget", ThreadingHelper.GetCleanSubject("RE: FW: Fwd: Budget"));
    }
}
```

**Patterns:**
- Setup: construct models directly with object initializers (all models in `Models/` are mutable POCOs with parameterless constructors and default values).
- Teardown: for file-writing services, create a unique temp dir per test (`Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))`) and delete it in `Dispose()` (implement `IDisposable` on the test class).
- Assertion: `Assert.Equal`/`Assert.True`/`Assert.Contains`; for EML output, re-parse with `MimeKit.MimeMessage.Load(path)` and assert on headers.

## Mocking

**Framework:** None in use. No interfaces exist over external dependencies.

**Constraints that shape mocking:**
- `Services/OpenAIService.cs` is a concrete class with non-virtual methods and constructs `OpenAIClient` internally; it cannot be mocked without refactoring.
- Generators (`Services/EmailGenerator.cs`, `Services/CharacterGenerator.cs`, `Services/StorylineGenerator.cs`, `Services/ThemeGenerator.cs`) take `OpenAIService` in the constructor, so they are only testable against the live API today.
- `Services/TelemetryService.cs` is static with a hard-coded webhook and static `HttpClient`, and writes to `%LOCALAPPDATA%\ReelDiscovery\settings.json`.
- `Helpers/DateHelper.cs`, `Services/EmailGenerator.cs`, and `Services/CharacterGenerator.cs` use private `System.Random` instances (non-deterministic, not injectable).

**Patterns (prescribed when introducing seams):**
```csharp
// Extract an interface from OpenAIService and inject it into generators
public interface IOpenAIService
{
    Task<string> GetCompletionAsync(string systemPrompt, string userPrompt, string? operationName = null, CancellationToken ct = default);
    Task<T?> GetJsonCompletionAsync<T>(string systemPrompt, string userPrompt, string? operationName = null, CancellationToken ct = default) where T : class;
    Task<byte[]?> GenerateImageAsync(string prompt, string? operationName = null, CancellationToken ct = default);
    Task<byte[]?> GenerateSpeechAsync(string text, string voice = "alloy", string? operationName = null, CancellationToken ct = default);
}

// Hand-written fake returning canned JSON (preferred over a mocking library for this codebase)
internal sealed class FakeOpenAIService : IOpenAIService { /* return fixture DTOs */ }
```

**What to Mock:**
- OpenAI calls (cost, latency, nondeterminism).
- Randomness (accept a `Random` seed or instance via constructor/optional parameter).
- Telemetry HTTP and settings file paths.

**What NOT to Mock:**
- `MimeKit` and `DocumentFormat.OpenXml` — run them for real against temp files; they are deterministic and the output format is the product.
- Pure helpers in `Helpers/` and computed properties in `Models/`.

## Fixtures and Factories

**Test Data (prescribed):**
```csharp
internal static class TestData
{
    public static Character Character(string first = "Jim", string last = "Halpert", string domain = "dundermifflin.com") => new()
    {
        FirstName = first,
        LastName = last,
        Email = $"{first.ToLower()}.{last.ToLower()}@{domain}",
        Role = "Sales Rep",
        Organization = "Dunder Mifflin"
    };

    public static EmailMessage Email(Character from, params Character[] to) => new()
    {
        From = from,
        To = to.ToList(),
        Subject = "Quarterly numbers",
        BodyPlain = "Line one\nLine two",
        SentDate = new DateTime(2026, 1, 15, 10, 30, 0)
    };
}
```
- Verify property names against `Models/EmailMessage.cs` before use.

**Location:**
- `ReelDiscovery.Tests/TestData.cs` for factories; `ReelDiscovery.Tests/Fixtures/*.json` for canned AI responses (copy real JSON shapes from the prompts in `Services/CharacterGenerator.cs` and `Services/EmailGenerator.cs`).

## Coverage

**Requirements:** None enforced. Current coverage is 0%.

**View Coverage:**
```bash
dotnet test ReelDiscovery.Tests --collect:"XPlat Code Coverage"
```

## Test Types

**Unit Tests:**
- Not present. Highest-value, zero-refactor targets:
  - `Helpers/ThreadingHelper.cs` — `AddReplyPrefix`, `AddForwardPrefix`, `GetCleanSubject`, `QuoteText`, `SetupThreading` (Message-ID / In-Reply-To / References chain).
  - `Helpers/FileNameHelper.cs` — `SanitizeForFileName` (invalid chars, empty -> `"unnamed"`), `GenerateEmlFileName`.
  - `Helpers/DateHelper.cs` — `DistributeDatesForThread` count/ordering/bounds, `AllocateDateWindow`.
  - `Models/Attachment.cs` — `MimeType`/`Extension` per `AttachmentType`.
  - `Models/Character.cs` — `FullName`, `DisplayName`, `Domain`.
  - `Models/TokenUsageTracker.cs` — totals, `Reset`, concurrent `RecordUsage`.
  - `Models/AIModelConfig.cs` — `CalculateTotalCost`.
  - `Services/EmailGenerator.cs` private `CorrectSignatureBlock` and `DistributeEmails` (make `internal` + `[assembly: InternalsVisibleTo("ReelDiscovery.Tests")]` to test).

**Integration Tests:**
- Not present. Targets: `Services/EmlFileService.cs` (write + re-parse with MimeKit), `Services/OfficeDocumentService.cs` (open generated .docx/.xlsx/.pptx with OpenXml SDK and validate), `Services/CalendarService.cs` (.ics content).
- Live OpenAI tests, if added, must be opt-in (skip unless an `OPENAI_API_KEY` env var is present) and never run by default.

**E2E Tests:**
- Not used. The WinForms wizard (`Forms/WizardForm.cs`, `UserControls/Step*.cs`) is verified manually.

## Common Patterns

**Async Testing:**
```csharp
[Fact]
public async Task SaveAllEmailsAsync_WritesOneEmlPerMessage()
{
    var service = new EmlFileService();
    await service.SaveAllEmailsAsync(threads, _tempDir, organizeBySender: false, progress: null, ct: CancellationToken.None);
    Assert.Equal(expectedCount, Directory.GetFiles(_tempDir, "*.eml", SearchOption.AllDirectories).Length);
}
```
(Confirm exact parameter names in `Services/EmlFileService.cs`.)

**Error Testing:**
```csharp
[Fact]
public async Task GenerateCharactersAsync_NullResponse_Throws()
{
    var generator = new CharacterGenerator(new FakeOpenAIService(returnsNull: true)); // requires IOpenAIService seam
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => generator.GenerateCharactersAsync("The Office", storylines));
}

[Fact]
public async Task GenerateEmailsAsync_Cancelled_SetsWasCancelled()
{
    using var cts = new CancellationTokenSource();
    cts.Cancel();
    var result = await generator.GenerateEmailsAsync(state, new Progress<GenerationProgress>(), cts.Token);
    Assert.True(result.WasCancelled);
}
```
- `EmailGenerator.GenerateEmailsAsync` swallows exceptions into `GenerationResult.Errors` / `WasCancelled` rather than throwing — assert on the result object, not on thrown exceptions.

---

*Testing analysis: 2026-09-29*
