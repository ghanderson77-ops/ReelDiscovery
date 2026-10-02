using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Tests.Corpus;
using ReelDiscovery.Tests.Fakes;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Render;

/// <summary>
/// Renders the Review Plan step at 144 DPI (04-UI-SPEC S2, render states 5-11) at Large and WizardMin, from plans built
/// by the real CorpusPlanner over a schema-aware fake model.
/// </summary>
[Trait("Category", "Render")]
[Collection(RenderCollection.Name)]
public partial class CorpusReviewPlanRenderTests
{
    // "T-0003 msg 2 | ... | attachments: Word, Excel" (RelevantThreadFiller chunk prompt)
    [GeneratedRegex(@"^\s*(T-\d{4}) msg (\d+) \|.*\| attachments: (.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex MessageLinePattern();

    // "S-0001 | Daily business | ..." (SubjectSeeder prompt)
    [GeneratedRegex(@"^(S-\d{4}) \| ([^|]+?) \|", RegexOptions.Multiline)]
    private static partial Regex SlotLinePattern();

    private static readonly string ThreadSchema = JsonSchemaRegistry.GetSchemaName(typeof(RelevantThreadFiller.ThreadPlanBatchDto));
    private static readonly string SeedSchema = JsonSchemaRegistry.GetSchemaName(typeof(SubjectSeeder.SubjectSeedBatchDto));
    private static readonly string ProtocolSchema = JsonSchemaRegistry.GetSchemaName(typeof(ReviewProtocolBuilder.ReviewProtocolProseDto));
    private static readonly string GuidanceSchema = JsonSchemaRegistry.GetSchemaName(typeof(CorpusPlanner.GuidanceWeightsDto));

    private const string ProseJson = """
        {
          "purpose": "This protocol guides review of documents collected for the Lakeview Tower payment dispute.",
          "background": "Brightline Glazing installed windows on Lakeview Tower and was not paid for Change Order 7.",
          "hotGuidance": "Flag documents that directly show Harmon approving or refusing Change Order 7.",
          "privilegeGuidance": "Communications with counsel are privileged unless a third party is copied.",
          "issues": [
            { "issueId": "ISS-01", "relevantExamples": ["An email approving Change Order 7."], "notRelevantExamples": ["A lunch invitation."] },
            { "issueId": "ISS-02", "relevantExamples": ["A late-payment notice."], "notRelevantExamples": ["A holiday schedule."] }
          ]
        }
        """;

    private const int BigFacts = 46;
    private const int BigCast = 42;

    private static readonly Lazy<CorpusPlan> NormalPlan = new(() => BuildPlan(NormalDraft()));
    private static readonly Lazy<CorpusPlan> BigPlan = new(() => BuildPlan(BigDraft()));

    // ----- States 5-11 -----

    [Fact]
    public void Planning()
    {
        RenderBoth("review-plan-planning", NormalDraft, plan: null, ReviewPlanRenderState.Planning, "Issue", null,
            (step, labels) =>
            {
                Assert.True(step.StateCardPanel.Visible);
                Assert.Contains("Planning the corpus...", labels);
                Assert.Contains(CorpusUiText.ProgressText(PlanStage.PlanningRelevant, 6, 17), labels);
            });
    }

    [Fact]
    public void ReadyWithWarnings_IssueTable()
    {
        var findings = new List<PlanFinding>
        {
            Finding(PlanArea.Facts, "F-003", "Fact F-003 is Hot but has only one planned email.", "Raise the corpus size or the relevant share, then click Re-plan."),
            Finding(PlanArea.Custodian, "CH-002", "Tom Becker sends 41% of relevant email.", "Click Re-plan with guidance to spread the traffic."),
            Finding(PlanArea.Thread, "T-0004", "Thread T-0004 has a reply dated before its first message.", "Click Re-plan."),
            Finding(PlanArea.Issues, "ISS-02", "Issue ISS-02 has no near-miss decoys.", "Raise the near-miss share under Advanced."),
            Finding(PlanArea.Plan, "", "The non-relevant mix is 4 points off the settings.", "Click Re-plan.")
        };
        RenderBoth("review-plan-ready-warnings", NormalDraft, NormalPlan.Value, ReviewPlanRenderState.Ready, "Issue", findings,
            (step, labels) =>
            {
                Assert.Equal(5, step.ChecksGrid.Rows.Count);
                Assert.Contains(CorpusUiText.ReadyWithWarningsText(5), labels);
                Assert.True(step.ApproveButton.Enabled);
            });
    }

    [Fact]
    public void CleanPlan_CustodianTable()
    {
        RenderBoth("review-plan-custodians", BigDraft, BigPlan.Value, ReviewPlanRenderState.Ready, "Custodian", new List<PlanFinding>(),
            (step, labels) =>
            {
                Assert.Contains(CorpusUiText.NoChecksText, labels);
                Assert.True(step.CountGrids[2].Rows.Count >= 20, $"{step.CountGrids[2].Rows.Count} custodians");
            });
    }

    [Fact]
    public void KeyFactTable_51Buckets()
    {
        RenderBoth("review-plan-facts", BigDraft, BigPlan.Value, ReviewPlanRenderState.Ready, "Key fact", new List<PlanFinding>(),
            (step, labels) =>
            {
                Assert.Equal(CorpusTestData.DatedFactCount(BigDraft()), step.CountGrids[1].Rows.Count);
                Assert.True(step.Histogram.Buckets.Count >= 49, $"{step.Histogram.Buckets.Count} buckets");
            });
    }

    [Fact]
    public void StaleGuideBanner_ApproveDisabled()
    {
        RenderBoth("review-plan-stale", NormalDraft, NormalPlan.Value, ReviewPlanRenderState.Stale, "Issue", null,
            (step, labels) =>
            {
                Assert.Contains("The plan is out of date with the case guide.", labels);
                Assert.False(step.ApproveButton.Enabled);
            },
            draft =>
            {
                draft.Corpus!.Approved = true;
                draft.Corpus.ApprovedUtc = new DateTime(2026, 9, 30, 15, 5, 0, DateTimeKind.Utc);
                draft.Guide!.Facts[0].Title += " (revised)";
                Assert.True(CorpusApproval.IsGuideStale(draft));
                Assert.True(CorpusApproval.WithdrawApprovalIfStale(draft));
            });
    }

    [Fact]
    public void ApprovedBadge_SettingsChangedBanner()
    {
        RenderBoth("review-plan-approved-settings", NormalDraft, NormalPlan.Value, ReviewPlanRenderState.ApprovedSettingsChanged, "Issue", null,
            (step, labels) =>
            {
                Assert.Contains("The corpus settings changed after this plan was approved.", labels);
                Assert.False(step.ApproveButton.Visible);
            },
            draft =>
            {
                draft.Corpus!.Approved = true;
                draft.Corpus.ApprovedUtc = new DateTime(2026, 9, 30, 15, 5, 0, DateTimeKind.Utc);
                draft.Corpus.Settings.TotalEmails = 2_500;
                Assert.True(CorpusApproval.IsSettingsChanged(draft));
            },
            expectBadge: true);
    }

    [Fact]
    public void NoPlanCard()
    {
        RenderBoth("review-plan-noplan", NormalDraft, plan: null, ReviewPlanRenderState.NoPlan, "Issue", null,
            (step, labels) =>
            {
                Assert.Contains("No corpus plan yet", labels);
                Assert.Contains("Plan Corpus", AllControls(step).OfType<Button>().Where(b => b.Visible).Select(b => b.Text));
            });
    }

    [Fact]
    public void FailedCard()
    {
        RenderBoth("review-plan-failed", NormalDraft, plan: null, ReviewPlanRenderState.Failed, "Issue", null,
            (step, labels) =>
            {
                Assert.Contains("The plan was not built", labels);
                Assert.Contains("Retry Planning", AllControls(step).OfType<Button>().Where(b => b.Visible).Select(b => b.Text));
            },
            reason: "The model returned no threads for the first chunk");
    }

    // ----- Harness -----

    private sealed class Measured
    {
        public List<string> Labels = new();
        public List<int> TileHeights = new();
        public int FirstTileLeft;
        public int LastTileRight;
        public int HistogramLeft;
        public int CountsRight;
        public bool AnyGridHorizontalScroll;
        public int SelectedGridHeight;
        public int SelectedGridHeaderHeight;
        public bool BadgeVisible;
        public int BadgeRight;
        public int ApproveRight;
        public bool ApproveVisible;
        public int CardsRight;
        public bool BodyShown;
    }

    private static void RenderBoth(string name, Func<CaseDraft> createDraft, CorpusPlan? plan, ReviewPlanRenderState render,
        string table, List<PlanFinding>? findings, Action<StepReviewPlan, List<string>> verify, Action<CaseDraft>? setup = null,
        bool expectBadge = false, string reason = "")
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.WizardMin })
            {
                var draft = createDraft();
                using var fixture = new PlanFixture(draft, plan);
                setup?.Invoke(draft);

                StepReviewPlan? step = null;
                var measured = new Measured();
                var result = RenderHarness.RenderControl(() =>
                {
                    step = new StepReviewPlan();
                    step.LoadForRender(fixture.State, render, table, findings: findings, reason: reason);
                    return step;
                }, name, size, control =>
                {
                    control.PerformLayout();
                    Application.DoEvents();
                    Measure(step!, measured, table);
                    // Visible flags and grid rows only hold while the form is shown, so state checks run here
                    verify(step!, measured.Labels);
                });

                Assert.True(File.Exists(result.PngPath), result.PngPath);
                Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
                Assert.Empty(result.Clipping);

                if (measured.BodyShown)
                {
                    // Rule 12: one tile height; the outer tiles align with the cards below (1 device px tolerance)
                    Assert.Single(measured.TileHeights.Distinct());
                    Assert.True(Math.Abs(measured.FirstTileLeft - measured.HistogramLeft) <= 1, $"tile left {measured.FirstTileLeft}, card left {measured.HistogramLeft}");
                    Assert.True(Math.Abs(measured.LastTileRight - measured.CountsRight) <= 1, $"tile right {measured.LastTileRight}, card right {measured.CountsRight}");
                    // Rule 13: the header's rightmost control ends at the cards' right edge
                    var headerRight = measured.BadgeVisible ? measured.BadgeRight : measured.ApproveRight;
                    if (measured.BadgeVisible || measured.ApproveVisible)
                        Assert.True(Math.Abs(headerRight - measured.CardsRight) <= 1, $"header right {headerRight}, cards right {measured.CardsRight}");
                    // Card headers stay whole even at WizardMin with a banner and findings
                    Assert.True(measured.SelectedGridHeight >= measured.SelectedGridHeaderHeight,
                        $"grid {measured.SelectedGridHeight} shorter than its header {measured.SelectedGridHeaderHeight}");
                    if (size == RenderHarness.Large)
                        Assert.False(measured.AnyGridHorizontalScroll);
                }

                Assert.Equal(expectBadge, measured.BadgeVisible);
            }
        });
    }

    private static void Measure(StepReviewPlan step, Measured m, string table)
    {
        // Visible only while the form is shown, so collect everything here
        m.Labels = AllControls(step).OfType<Label>().Where(l => l.Visible).Select(l => l.Text).ToList();
        m.BodyShown = step.HistogramCard.Visible;
        m.BadgeVisible = step.Badge.Visible;
        m.ApproveVisible = step.ApproveButton.Visible;
        if (!m.BodyShown)
            return;

        m.TileHeights = step.Tiles.Select(t => t.Height).ToList();
        m.FirstTileLeft = Left(step, step.Tiles[0]);
        m.LastTileRight = Left(step, step.Tiles[^1]) + step.Tiles[^1].Width;
        m.HistogramLeft = Left(step, step.HistogramCard);
        m.CountsRight = Left(step, step.CountsCard) + step.CountsCard.Width;
        m.CardsRight = m.CountsRight;
        m.BadgeRight = Left(step, step.Badge) + step.Badge.Width;
        m.ApproveRight = Left(step, step.ApproveButton) + step.ApproveButton.Width;
        m.AnyGridHorizontalScroll = step.CountGrids.Any(g => g.Controls.OfType<HScrollBar>().Any(b => b.Visible));
        var index = table switch { "Key fact" => 1, "Custodian" => 2, _ => 0 };
        m.SelectedGridHeight = step.CountGrids[index].Height;
        m.SelectedGridHeaderHeight = step.CountGrids[index].ColumnHeadersHeight;
    }

    private static int Left(Control root, Control control) => root.PointToClient(control.PointToScreen(Point.Empty)).X;

    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in AllControls(child))
                yield return nested;
        }
    }

    private static PlanFinding Finding(PlanArea area, string id, string message, string fix) =>
        new() { Area = area, EntityId = id, Message = message, FixHint = fix };

    // ----- Fixture data -----

    private static CaseDraft NormalDraft()
    {
        var draft = CorpusTestData.RichDraft();
        draft.Complexity = ComplexityLevel.Standard;
        draft.Corpus = new CorpusState { Settings = CorpusSettings.CreateDefault(draft) };
        return draft;
    }

    /// <summary>
    /// RichDraft grown to 46 dated facts over 48 months and 42 cast members (Key / Background mix), planned at 5,000
    /// emails, 20% relevant. Deterministic, so every call yields the same guide hash as the cached plan.
    /// </summary>
    private static CaseDraft BigDraft()
    {
        var draft = CorpusTestData.RichDraft();
        var guide = draft.Guide!;
        var orgs = new[] { "ORG-01", "ORG-02" };

        foreach (var member in guide.Cast)
        {
            member.ActiveFrom = "2021-01-01";
            member.ActiveTo = "2025-12-31";
        }

        var n = 0;
        while (guide.Cast.Count < BigCast)
        {
            n++;
            var id = $"CH-{100 + n:000}";
            var org = guide.Organizations.Single(o => o.Id == orgs[n % 2]);
            guide.Cast.Add(new CastMember
            {
                Id = id,
                FirstName = $"First{n}",
                LastName = $"Last{n}",
                Title = n % 3 == 0 ? "Project Engineer" : "Coordinator",
                Department = "Operations",
                OrganizationId = org.Id,
                PartyId = org.PartyId,
                Tier = n % 4 == 0 ? CastTier.Key : CastTier.Background,
                Role = CastRole.Staff,
                Email = $"first{n}.last{n}@{org.Domain}",
                ActiveFrom = "2021-01-01",
                ActiveTo = "2025-12-31",
                Involvement = n % 4 == 0 ? "Coordinated site logistics and copied the project team on change order traffic." : string.Empty
            });
        }

        // Spread the new facts evenly over the 48 months Jan 2021 - Dec 2024 (first and last month included), skipping
        // the months RichDraft already uses; the default window then adds a month before and two after (51 buckets)
        var taken = guide.Facts.Select(f => f.Date[..7]).ToHashSet();
        var castIds = guide.Cast.Select(c => c.Id).ToList();
        var next = guide.Facts.Count;
        var start = new DateTime(2021, 1, 12);
        var free = Enumerable.Range(0, 48).Select(m => start.AddMonths(m))
            .Where(d => !taken.Contains(d.ToString("yyyy-MM", CultureInfo.InvariantCulture))).ToList();
        var needed = BigFacts - guide.Facts.Count;
        var picks = Enumerable.Range(0, needed)
            .Select(i => free[(int)Math.Round(i * (free.Count - 1) / (double)(needed - 1))]).ToList();
        foreach (var date in picks)
        {
            next++;
            var actor = castIds[next % castIds.Count];
            guide.Facts.Add(new KeyFact
            {
                Id = $"F-{next:000}",
                Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Title = $"Project milestone {next} on Lakeview Tower",
                Description = $"Milestone {next}: the project team exchanged schedule and payment updates on the Lakeview Tower glazing work.",
                IssueIds = new List<string> { next % 3 == 0 ? "ISS-02" : "ISS-01" },
                ActorIds = new List<string> { actor },
                KnownByIds = new List<string> { actor, castIds[(next + 1) % castIds.Count] },
                Significance = next % 7 == 0 ? FactSignificance.Hot : next % 2 == 0 ? FactSignificance.Key : FactSignificance.Supporting,
                SourceSectionId = "facts-2"
            });
        }

        guide.IdCounters["F"] = next;
        guide.IdCounters["CH"] = 100 + n;
        draft.Complexity = ComplexityLevel.Complex;
        var settings = CorpusSettings.CreateDefault(draft);
        settings.TotalEmails = 5_000;
        settings.RelevantPercent = 20;
        draft.Corpus = new CorpusState { Settings = settings };
        return draft;
    }

    private static CorpusPlan BuildPlan(CaseDraft draft)
    {
        var (gateway, fake) = CorpusTestData.CreateGateway();
        fake.Respond = Respond;
        var result = new CorpusPlanner(gateway, new Random(7)).PlanAsync(draft, true, "", null, default).GetAwaiter().GetResult();
        Assert.True(result.Succeeded, result.FailureMessage + result.FatalError);
        return result.Plan!;
    }

    /// <summary>
    /// Schema-aware responder (from CorpusPlannerTests): echoes thread ids and slots, writes protocol prose, no guidance weights.
    /// </summary>
    private static LlmProviderResponse Respond(LlmProviderRequest request)
    {
        if (request.SchemaName == ThreadSchema)
            return EchoThreads(request);
        if (request.SchemaName == SeedSchema)
            return EchoSlots(request);
        if (request.SchemaName == ProtocolSchema)
            return FakeLlmProvider.Ok(ProseJson);
        if (request.SchemaName == GuidanceSchema)
            return FakeLlmProvider.Ok("""{ "factWeights": [] }""");
        throw new InvalidOperationException($"Unexpected schema {request.SchemaName}");
    }

    private static LlmProviderResponse EchoThreads(LlmProviderRequest request)
    {
        var threads = new List<(string Id, List<(int Seq, int Attachments)> Messages)>();
        foreach (Match match in MessageLinePattern().Matches(request.UserPrompt))
        {
            var id = match.Groups[1].Value;
            var seq = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            var kinds = match.Groups[3].Value.Trim();
            var count = kinds == "none" ? 0 : kinds.Split(',').Length;
            var index = threads.FindIndex(t => t.Id == id);
            if (index < 0)
                threads.Add((id, new List<(int, int)> { (seq, count) }));
            else
                threads[index].Messages.Add((seq, count));
        }

        var dto = threads.Select(t => new
        {
            threadId = t.Id,
            subject = $"Change order follow-up {t.Id}",
            purpose = $"Purpose of {t.Id}.",
            rationale = $"Rationale for {t.Id}.",
            linkedThreadIds = Array.Empty<string>(),
            messages = t.Messages.Select(m => new
            {
                seq = m.Seq,
                intent = $"Intent {t.Id} {m.Seq}",
                mustConvey = new[] { "Point A" },
                attachmentTitles = Enumerable.Range(1, m.Attachments).Select(k => $"Title {t.Id} m{m.Seq} a{k}").ToArray()
            }).ToArray()
        }).ToArray();
        return FakeLlmProvider.Ok(JsonSerializer.Serialize(new { threads = dto }));
    }

    private static LlmProviderResponse EchoSlots(LlmProviderRequest request)
    {
        var seeds = SlotLinePattern().Matches(request.UserPrompt).Select(m =>
        {
            var slotId = m.Groups[1].Value;
            var category = m.Groups[2].Value.Trim();
            return new
            {
                slotId,
                subject = $"Routine topic {slotId}",
                angle = category == "Near-miss" ? $"Angle {slotId}." : string.Empty,
                senderName = category is "Newsletter" or "Spam" or "Vendor" ? $"Sender {slotId}"
                    : category == "Personal" ? $"Pat Sender{slotId[2..]}" : string.Empty
            };
        }).ToArray();
        return FakeLlmProvider.Ok(JsonSerializer.Serialize(new { seeds }));
    }

    /// <summary>
    /// A saved case (and its corpus-plan.json when a plan is given) under a temp Cases root; deleted on dispose.
    /// </summary>
    private sealed class PlanFixture : IDisposable
    {
        private readonly string _root;

        public PlanFixture(CaseDraft draft, CorpusPlan? plan)
        {
            _root = Path.Combine(Path.GetTempPath(), "rd-review-plan-" + Guid.NewGuid().ToString("N"));
            var store = new CaseStore(Path.Combine(_root, "Cases"), Path.Combine(_root, "AppData"));
            draft.AdvanceStage(CaseStage.CorpusSettings);
            var folder = store.EnsureFolder(draft);
            if (plan != null)
            {
                var apply = CorpusApproval.ApplyNewPlanAsync(folder, draft, plan).GetAwaiter().GetResult();
                Assert.True(apply.Saved, apply.Error);
            }
            Assert.True(store.SaveNow(draft), store.LastSaveError);
            State = new WizardState { Case = draft, CaseStore = store, OpenAIApiKey = "sk-render-test", OpenAIKeyTested = true };
        }

        public WizardState State { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // A file handle may linger briefly after the render; the temp folder is harmless
            }
        }
    }
}
