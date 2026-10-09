using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Discovery;
using ReelDiscovery.Tests.Case;
using ReelDiscovery.Tests.Evidence;
using ReelDiscovery.Tests.Fakes;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Discovery;

/// <summary>
/// Discovery Documents run wiring (08-17): Generate, Cancel, progress and outcome lines, Regenerate..., Save Again and the
/// open actions, driven through the step's seams with a fake provider (MaxParallel 1, responses routed by schema and
/// prompt, never by queue order). Everything runs on one STA thread with a WinForms context; the step is never shown.
/// </summary>
public partial class StepDiscoveryDocumentsRunTests
{
    private const string Witness = "CH-003";
    private const string RfpKey = "RFP";
    private const string QuestionText = "Did you review that schedule with the project team on site?";
    private const string AnswerText = "Yes, I reviewed it with the team that week.";
    private const string RfpText = "All Documents and Communications Concerning the revised project schedule.";

    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new JsonStringEnumConverter() } };
    private static readonly string DepositionSchema = JsonSchemaRegistry.GetSchemaName(typeof(DiscoveryPrompts.DepositionChunkDto));

    [GeneratedRegex(@"^- Slot (\d+) \|", RegexOptions.Multiline)]
    private static partial Regex SlotLineRegex();

    private static string DepKey => DiscoveryWorklist.DepositionKey(Witness);

    private sealed class Fixture : IDisposable
    {
        private readonly string _root;

        public Fixture(bool withRfp = true)
        {
            _root = Path.Combine(Path.GetTempPath(), "rd-discovery-run-" + Guid.NewGuid().ToString("N"));
            var store = new CaseStore(Path.Combine(_root, "Cases"), Path.Combine(_root, "AppData"));
            Draft = DiscoveryTestData.LitigationDraft();
            Draft.Discovery = new DiscoverySettings
            {
                WitnessIds = new List<string> { Witness },
                PagesPerExcerpt = 10,
                RfpEnabled = withRfp,
                RfpCount = 5,
                RogEnabled = false,
                RfaEnabled = false,
                UserTouched = true
            };
            store.EnsureFolder(Draft);
            State = new WizardState { Case = Draft, CaseStore = store, OpenAIApiKey = "sk-step-test", OpenAIKeyTested = true };
            Fake = new FakeLlmProvider(LlmProviderKind.Anthropic);
            Gateway = new LlmGateway(Fake, CaseTestData.StoryModel(), CaseTestData.VolumeModel());
        }

        public WizardState State { get; }
        public CaseDraft Draft { get; }
        public FakeLlmProvider Fake { get; }
        public LlmGateway Gateway { get; }
        public readonly List<string> Confirms = new();
        public bool ConfirmAnswer = true;
        public readonly List<string> Opened = new();
        public readonly List<string> OpenedFolders = new();
        public DepositionPlan? Plan;

        public string Folder => Draft.FolderPath;

        public StepDiscoveryDocuments CreateStep()
        {
            var step = new StepDiscoveryDocuments();
            step.BindState(State);
            step.KeyReady = () => true;
            step.SaveCase = () => Task.FromResult(true);
            step.Confirm = (title, text, icon) =>
            {
                Confirms.Add(title);
                return ConfirmAnswer;
            };
            step.GatewayFactory = () => Gateway;
            step.GeneratorFactory = llm => new DiscoveryGenerator(llm) { MaxParallel = 1 };
            step.OpenFile = path => Opened.Add(path);
            step.OpenFolder = path => OpenedFolders.Add(path);
            return step;
        }

        public DiscoveryDocumentSet LoadSet()
        {
            var load = DiscoveryStore.Load(Folder, Draft.CaseId);
            Assert.True(load.Succeeded, load.Reason);
            return load.Set!;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // A file handle may linger briefly; the temp folder is harmless
            }
        }
    }

    private static void Run(Action<Fixture, StepDiscoveryDocuments> body, bool withRfp = true)
    {
        using var fixture = new Fixture(withRfp);
        GenerateCorpusStepTests.RunSta(() =>
        {
            using var step = fixture.CreateStep();
            body(fixture, step);
        });
    }

    private static void Enter(Fixture f, StepDiscoveryDocuments step)
    {
        GenerateCorpusStepTests.Pump(step.OnEnterStepAsync());
        f.Plan = step.Model.ToWrite.FirstOrDefault(i => i.Deposition != null)?.Deposition ?? f.Plan;
    }

    private static void Pump(Task task, Action? onTurn = null) => GenerateCorpusStepTests.Pump(task, onTurn);

    private static Dictionary<string, string> Statuses(StepDiscoveryDocuments step) =>
        step.DocumentsGrid.Rows.Cast<DataGridViewRow>()
            .ToDictionary(r => ((DiscoveryRowInfo)r.Tag!).Key, r => (string)r.Cells["Status"].Value!, StringComparer.Ordinal);

    private static string Title(StepDiscoveryDocuments step, string key) => step.Model.Rows.Single(r => r.Key == key).ShortTitle;

    private static bool IsDeposition(LlmProviderRequest request) => request.SchemaName == DepositionSchema;

    /// <summary>
    /// Routes each request by schema: deposition chunks to <paramref name="deposition"/> (chunk found from the prompt's
    /// topic and part), written sets to <paramref name="written"/> (slot numbers parsed from the prompt).
    /// </summary>
    private static void Script(Fixture f, Func<DepositionChunk, LlmProviderResponse>? deposition = null,
        Func<List<int>, LlmProviderResponse>? written = null)
    {
        deposition ??= chunk => Ok(CleanDeposition(f, chunk));
        written ??= slots => Ok(Rfp(slots));
        f.Fake.Respond = request =>
        {
            if (IsDeposition(request))
                return deposition(ChunkFor(f, request.UserPrompt));
            var slots = SlotLineRegex().Matches(request.UserPrompt)
                .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
            return written(slots);
        };
    }

    private static DepositionChunk ChunkFor(Fixture f, string prompt)
    {
        foreach (var chunk in f.Plan!.Chunks)
        {
            if (prompt.Contains("- Topic: " + chunk.TopicTitle + "\n", StringComparison.Ordinal)
                && prompt.Contains($"This is part {chunk.Part} of {chunk.PartCount} of this topic.", StringComparison.Ordinal))
                return chunk;
        }

        throw new InvalidOperationException("Prompt matches no chunk.");
    }

    private static DiscoveryPrompts.DepositionChunkDto CleanDeposition(Fixture f, DepositionChunk chunk)
    {
        var examining = f.Plan!.Speakers.First(s => s.Role == SpeakerRole.Examining);
        var witness = f.Plan.Speakers.First(s => s.Role == SpeakerRole.Witness);
        var dto = new DiscoveryPrompts.DepositionChunkDto();
        for (var i = 0; i < chunk.TargetTurns; i++)
        {
            var question = i % 2 == 0;
            var exhibitSlot = i / 2;
            dto.Turns.Add(new DiscoveryPrompts.TurnDto
            {
                Kind = question ? TurnKind.Q : TurnKind.A,
                SpeakerId = question ? examining.SpeakerId : witness.SpeakerId,
                Text = question ? QuestionText : AnswerText,
                ExhibitNumber = question && exhibitSlot < chunk.ExhibitNumbers.Count ? chunk.ExhibitNumbers[exhibitSlot] : 0
            });
        }

        return dto;
    }

    private static DiscoveryPrompts.RequestSetDto Rfp(IEnumerable<int> slots)
    {
        var dto = new DiscoveryPrompts.RequestSetDto();
        foreach (var slot in slots)
            dto.Requests.Add(new DiscoveryPrompts.RequestDto { Slot = slot, Text = RfpText });
        return dto;
    }

    private static LlmProviderResponse Ok(DiscoveryPrompts.DepositionChunkDto dto) =>
        FakeLlmProvider.Ok(JsonSerializer.Serialize(dto, JsonOptions));

    private static LlmProviderResponse Ok(DiscoveryPrompts.RequestSetDto dto) => FakeLlmProvider.Ok(JsonSerializer.Serialize(dto));

    // ----- Generate -----

    [Fact]
    public void Generate_Clean_WritesEveryRow_DoneStatusAndPrimaryButton()
    {
        Run((f, step) =>
        {
            Enter(f, step);
            Assert.Equal(new[] { DepKey, RfpKey }, step.Model.ToWrite.Select(i => i.Key));
            var depTitle = Title(step, DepKey);

            using var seen = new ManualResetEventSlim(false);
            var sawGenerating = false;
            var sawWriting = false;
            var first = true;
            Script(f, deposition: chunk =>
            {
                if (first)
                {
                    first = false;
                    sawGenerating = step.IsGenerating && !step.CanMoveNext && !step.CanMoveBack;
                    // Hold the first call until the UI thread has painted the Writing row
                    seen.Wait(TimeSpan.FromSeconds(10));
                }
                return Ok(CleanDeposition(f, chunk));
            });

            Pump(step.RunClickAsync(), () =>
            {
                if (seen.IsSet)
                    return;
                if (Statuses(step).TryGetValue(DepKey, out var s) && s == DiscoveryUiText.RowStatusCell(DiscoveryRowState.Writing)
                    && step.StatusLabel.Text.StartsWith($"Writing 1 of 2 documents: {depTitle}", StringComparison.Ordinal)
                    && step.StatusLabel.ForeColor == Theme.Accent)
                {
                    sawWriting = true;
                    seen.Set();
                }
            });

            Assert.True(sawGenerating);
            Assert.True(sawWriting);
            Assert.False(step.IsGenerating);
            Assert.True(step.CanMoveNext);
            var statuses = Statuses(step);
            Assert.Equal("Generated", statuses[DepKey]);
            Assert.Equal("Generated", statuses[RfpKey]);
            foreach (var doc in f.LoadSet().Documents)
                Assert.True(File.Exists(DiscoveryStore.DocxPath(f.Folder, doc.FileName)));
            Assert.Equal(DiscoveryUiText.StatusDone(2), step.StatusLabel.Text);
            Assert.Equal(Theme.Success, step.StatusLabel.ForeColor);
            Assert.Equal(DiscoveryUiText.GenerateButton, step.RunButton.Text);
            Assert.Empty(f.Confirms);
        });
    }

    [Fact]
    public void Generate_WithWarnings_DoneWithProblemsInWarningTone()
    {
        Run((f, step) =>
        {
            Enter(f, step);
            // Slot 1 is never returned: the RFP is saved with a missing-request warning
            Script(f, written: slots => Ok(Rfp(slots.Where(s => s != 1))));

            Pump(step.RunClickAsync());

            Assert.Equal(DiscoveryUiText.StatusDoneWithProblems(1, 0, 0), step.StatusLabel.Text);
            Assert.Equal(Theme.Warning, step.StatusLabel.ForeColor);
            Assert.Equal(DiscoveryUiText.RowStatusCell(DiscoveryRowState.Warnings), Statuses(step)[RfpKey]);
        });
    }

    [Fact]
    public void Generate_Refused_FailedCountedInProblems()
    {
        Run((f, step) =>
        {
            Enter(f, step);
            Script(f, written: _ => FakeLlmProvider.Refused("Not comfortable"));

            Pump(step.RunClickAsync());

            Assert.Equal(DiscoveryUiText.StatusDoneWithProblems(0, 1, 0), step.StatusLabel.Text);
            Assert.Equal(Theme.Warning, step.StatusLabel.ForeColor);
            Assert.Equal(DiscoveryUiText.RowStatusCell(DiscoveryRowState.Failed), Statuses(step)[RfpKey]);
            Assert.Equal("Generated", Statuses(step)[DepKey]);
        });
    }

    [Fact]
    public void Generate_FatalAuth_DangerStatus_FinishedDocumentKept()
    {
        Run((f, step) =>
        {
            Enter(f, step);
            Script(f, written: _ => FakeLlmProvider.Failed(LlmFailureKind.Auth, "The API key was rejected."));

            Pump(step.RunClickAsync());

            Assert.StartsWith("Generation stopped: ", step.StatusLabel.Text, StringComparison.Ordinal);
            Assert.Equal(Theme.Danger, step.StatusLabel.ForeColor);
            var statuses = Statuses(step);
            Assert.Equal("Generated", statuses[DepKey]);
            Assert.Equal(DiscoveryUiText.RowStatusCell(DiscoveryRowState.NotGenerated), statuses[RfpKey]);
            Assert.False(step.IsGenerating);
        });
    }

    // ----- Cancel -----

    [Fact]
    public void Cancel_ConfirmNo_KeepsRunning_ConfirmYes_CancelsAndKeepsFinished()
    {
        Run((f, step) =>
        {
            Enter(f, step);
            Script(f);
            var depositionCalls = f.Plan!.Chunks.Count;
            using var reached = new ManualResetEventSlim(false);
            using var release = new ManualResetEventSlim(false);
            // The first RFP call waits until the UI thread has answered the cancel question
            f.Fake.OnSend = index =>
            {
                if (index != depositionCalls)
                    return;
                reached.Set();
                release.Wait(TimeSpan.FromSeconds(10));
            };

            var asked = 0;
            Pump(step.RunClickAsync(), () =>
            {
                if (!reached.IsSet || release.IsSet)
                    return;
                if (asked == 0)
                {
                    // No: the run keeps going
                    f.ConfirmAnswer = false;
                    Pump(step.RunClickAsync());
                    Assert.True(step.IsGenerating);
                    Assert.Single(f.Confirms);
                    asked = 1;
                    return;
                }

                f.ConfirmAnswer = true;
                Pump(step.RunClickAsync());
                Assert.Equal(DiscoveryUiText.StatusStopping, step.StatusLabel.Text);
                asked = 2;
                release.Set();
            });

            Assert.Equal(2, asked);
            Assert.Equal(new[] { DiscoveryUiText.CD1CancelGeneration().Title, DiscoveryUiText.CD1CancelGeneration().Title },
                f.Confirms);
            Assert.Equal(DiscoveryUiText.StatusCancelled(1, 2), step.StatusLabel.Text);
            Assert.Equal(Theme.Warning, step.StatusLabel.ForeColor);
            var statuses = Statuses(step);
            Assert.Equal("Generated", statuses[DepKey]);
            Assert.Equal(DiscoveryUiText.RowStatusCell(DiscoveryRowState.NotGenerated), statuses[RfpKey]);
            Assert.False(step.IsGenerating);
            Assert.Equal(DiscoveryUiText.GenerateButton, step.RunButton.Text);
        });
    }

    // ----- FileLockDetector.IsOpenElsewhere -----

    [Fact]
    public void IsOpenElsewhere_MissingLockedReleased()
    {
        var path = Path.Combine(Path.GetTempPath(), "rd-lock-" + Guid.NewGuid().ToString("N") + ".docx");
        try
        {
            Assert.False(FileLockDetector.IsOpenElsewhere(path));
            Assert.False(FileLockDetector.IsOpenElsewhere(string.Empty));
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Assert.True(FileLockDetector.IsOpenElsewhere(path));
            Assert.False(FileLockDetector.IsOpenElsewhere(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
