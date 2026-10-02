using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Tests.Case;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Render;

/// <summary>
/// Renders Describe the Case at the active scaling with the Recent cases card (UI-SPEC S3).
/// Cases and the recent list live under a temp root, never the user's Documents or %LOCALAPPDATA%.
/// </summary>
[Trait("Category", "Render")]
[Collection(RenderCollection.Name)]
public class CaseScenarioRenderTests
{
    private static readonly (string First, string FirstShort, string Second, string SecondShort)[] PartyPairs =
    {
        ("Brightline Glazing, LLC", "Brightline", "Harmon Builders, Inc.", "Harmon"),
        ("Northgate Steel Corp.", "Northgate", "Pemberton Development LLC", "Pemberton"),
        ("Cascade Roofing Co.", "Cascade", "Ridgeway Properties, Inc.", "Ridgeway"),
        ("Alder Mechanical, Inc.", "Alder", "Summit Tower Partners LP", "Summit"),
        ("Bluewater Concrete LLC", "Bluewater", "Kestrel Homes, Inc.", "Kestrel"),
        ("Ironside Electrical Contractors, Inc.", "Ironside", "Marlowe Hospitality Group LLC", "Marlowe"),
        ("Granite Peak Masonry", "Granite Peak", "Oakhurst Medical Center", "Oakhurst"),
        ("Silverline Elevators, Inc.", "Silverline", "Fairmont Commercial Holdings, LLC", "Fairmont")
    };

    [Fact]
    public void Scenario_NoRecent()
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.Small })
            {
                using var fixture = new ScenarioFixture();
                fixture.State.Case = new CaseDraft { Scenario = "A construction defect dispute between a window subcontractor, a general contractor and the developer." };

                var emptyVisible = false;
                var listVisible = true;
                var result = Render(fixture, "case-scenario-no-recent", size, control =>
                {
                    emptyVisible = AllControls(control).OfType<Label>().Any(l => l.Text == StepCaseScenario.RecentEmptyText && l.Visible);
                    listVisible = AllControls(control).OfType<ListBox>().Any(l => l.Visible);
                });

                AssertRendered(result);
                Assert.True(emptyVisible);
                Assert.False(listVisible);
            }
        });
    }

    [Fact]
    public void Scenario_EightRecent()
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.Small })
            {
                using var fixture = new ScenarioFixture();
                var stages = new[] { CaseStage.CoreDocument, CaseStage.CaseGuide, CaseStage.Summary };
                CaseDraft? openCase = null;
                for (var i = 0; i < PartyPairs.Length; i++)
                {
                    var draft = fixture.SaveCase(PartyPairs[i], stages[i % stages.Length]);
                    // The third-newest case is the one open now
                    if (i == PartyPairs.Length - 3)
                        openCase = draft;
                }
                fixture.State.Case = openCase!;

                var items = 0;
                var newCaseVisible = false;
                var result = Render(fixture, "case-scenario-eight-recent", size, control =>
                {
                    items = AllControls(control).OfType<ListBox>().Single().Items.Count;
                    newCaseVisible = AllControls(control).OfType<Button>().Any(b => b.Text == "New Case" && b.Visible);
                });

                AssertRendered(result);
                Assert.Equal(CaseStore.MaxRecentCases, items);
                Assert.True(newCaseVisible);
            }
        });
    }

    [Fact]
    public void Scenario_LoadError()
    {
        RenderHarness.RunSta(() =>
        {
            // Small too: the long path must wrap in the status line, not run past the page edge
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.Small })
            {
                using var fixture = new ScenarioFixture();
                var missing = fixture.SaveCase(PartyPairs[0], CaseStage.CaseGuide);
                fixture.SaveCase(PartyPairs[1], CaseStage.Summary);
                var missingPath = Path.Combine(missing.FolderPath, CaseStore.CaseFileName);
                fixture.State.Case = new CaseDraft();
    
                var status = string.Empty;
                Color? statusColor = null;
                var items = -1;
                var result = Render(fixture, "case-scenario-load-error", size, control =>
                {
                    // The folder disappears after the list was read: opening it fails and prunes the entry
                    Directory.Delete(missing.FolderPath, recursive: true);
                    ((StepCaseScenario)control).OpenCase(missingPath, "Brightline v. Harmon");
    
                    var label = AllControls(control).OfType<Label>().FirstOrDefault(l => l.Text.StartsWith("This case folder no longer exists: ", StringComparison.Ordinal));
                    status = label?.Text ?? string.Empty;
                    statusColor = label?.ForeColor;
                    items = AllControls(control).OfType<ListBox>().Single().Items.Count;
                });
    
                AssertRendered(result);
                Assert.Equal($"This case folder no longer exists: {missingPath}. It was removed from Recent cases.", status);
                Assert.Equal(Helpers.Theme.Danger, statusColor);
                Assert.Equal(1, items);
            }
        });
    }

    private static RenderResult Render(ScenarioFixture fixture, string name, Size size, Action<Control>? inspect = null)
    {
        return RenderHarness.RenderControl(() =>
        {
            var step = new StepCaseScenario();
            step.LoadForRender(fixture.State);
            return step;
        }, name, size, control => inspect?.Invoke(control));
    }

    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in AllControls(child))
                yield return nested;
        }
    }

    private static void AssertRendered(RenderResult result)
    {
        Assert.True(File.Exists(result.PngPath), result.PngPath);
        Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
        Assert.Empty(result.Clipping);
    }

    /// <summary>
    /// A CaseStore under a temp Cases root and temp app-data folder; deleted on dispose. Tests write only here.
    /// </summary>
    private sealed class ScenarioFixture : IDisposable
    {
        private readonly string _root;

        public ScenarioFixture()
        {
            _root = Path.Combine(Path.GetTempPath(), "rd-scenario-" + Guid.NewGuid().ToString("N"));
            Store = new CaseStore(Path.Combine(_root, "Cases"), Path.Combine(_root, "AppData"));
            State = new WizardState { CaseStore = Store };
        }

        public CaseStore Store { get; }

        public WizardState State { get; }

        public CaseDraft SaveCase((string First, string FirstShort, string Second, string SecondShort) parties, CaseStage stage)
        {
            var draft = CaseTestData.SampleGuidedDraft();
            var doc = draft.CoreDocument!;
            doc.Parties[0].Name = parties.First;
            doc.Parties[0].ShortName = parties.FirstShort;
            doc.Parties[1].Name = parties.Second;
            doc.Parties[1].ShortName = parties.SecondShort;
            draft.Stage = stage;
            Store.EnsureFolder(draft);
            Assert.True(Store.SaveNow(draft), Store.LastSaveError);
            // Distinct opened times keep the most-recent-first order stable
            Thread.Sleep(15);
            return draft;
        }

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
