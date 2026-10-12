using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Tests.Discovery;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Render;

/// <summary>
/// Renders the Discovery Documents step at 144 DPI (08-UI-SPEC S1 render checks 1-7) at Large and WizardMin through
/// LoadForRender. Nothing is generated, probed or saved.
/// </summary>
[Trait("Category", "Render")]
[Collection(RenderCollection.Name)]
public class DiscoveryDocumentsRenderTests
{
    // ----- Render check 1: no guide -----

    [Fact]
    public void NoGuide()
    {
        RenderBoth("discovery-no-guide", DiscoveryRenderState.NoGuide, DiscoveryTestData.NoGuideDraft, null, step =>
        {
            Assert.True(step.StateCardShown);
            Assert.True(step.StateCardPanel.Visible);
            Assert.False(step.CardsLayout.Visible);
            Assert.False(step.NoticeLabel.Visible);
            Assert.True(step.CanMoveNext);
        }, primary: 0);
    }

    // ----- Render check 2: Standard civil, no corpus -----

    [Fact]
    public void Ready()
    {
        RenderBoth("discovery-ready", DiscoveryRenderState.Ready, DiscoveryTestData.LitigationDraft, null, step =>
        {
            Assert.False(step.StateCardShown);
            Assert.True(step.NoticeLabel.Visible);
            Assert.Equal(DiscoveryUiText.Notice(memo: false, corpusUsable: false), step.NoticeLabel.Text);
            Assert.Equal(5, step.DocumentsGrid.Rows.Count);
            Assert.All(step.DocumentsGrid.Rows.Cast<DataGridViewRow>(), r => Assert.Equal("Not generated", r.Cells["Status"].Value));
            Assert.StartsWith("Estimated cost ≈$", step.EstimateLabel.Text);
            Assert.True(step.RunButton.Visible);
            Assert.True(step.RunButton.Enabled);
            Assert.Equal(DiscoveryUiText.GenerateButton, step.RunButton.Text);
            Assert.Equal(ButtonStyle.Primary, ((ThemedButton)step.RunButton).Variant);
            Assert.Same(step.RunButton, step.ActiveControl);
            // The 28-wide check cell keeps no text inset, so the box is drawn centered
            Assert.Equal(Padding.Empty, step.WitnessGrid.Rows[0].Cells["Check"].InheritedStyle.Padding);
            Assert.Equal(2, step.WitnessGrid.Rows.Cast<DataGridViewRow>().Count(r => r.Cells["Check"].Value is true));
            Assert.False(step.StatusLabel.Visible);
            Assert.False(step.ProgressBar.Visible);
            Assert.False(step.SaveAgainButton.Visible);
            Assert.True(step.RegenerateButton.Visible);
        }, primary: 1);
    }

    // ----- Render check 3: running -----

    [Fact]
    public void Running()
    {
        RenderBoth("discovery-running", DiscoveryRenderState.Running, DiscoveryTestData.LitigationDraft, RunningModel, step =>
        {
            Assert.Equal(2, step.DocumentsGrid.Rows.Cast<DataGridViewRow>().Count(r => (string)r.Cells["Status"].Value! == "Writing..."));
            Assert.True(step.StatusLabel.Visible);
            Assert.Equal(Theme.Accent, step.StatusLabel.ForeColor);
            Assert.Contains("part 2 of 3", step.StatusLabel.Text);
            Assert.True(step.ProgressBar.Visible);
            Assert.False(step.EstimateLabel.Visible);
            // Two lines, so the part counter at the end is on screen (UI-SPEC S1 Card 3 row 6, amended in 08-14)
            Assert.False(Ellipsized(step.StatusLabel), step.StatusLabel.Text);
            Assert.Equal(DiscoveryUiText.CancelGenerationButton, step.RunButton.Text);
            Assert.Equal(ButtonStyle.Danger, ((ThemedButton)step.RunButton).Variant);
            Assert.True(step.RunButton.Enabled);
            Assert.False(step.PagesText.Enabled);
            Assert.False(step.RfpCheck.Enabled);
            Assert.False(step.ServedByCombo.Enabled);
            Assert.False(step.OpenWordButton.Enabled);
            Assert.False(step.RegenerateButton.Enabled);
            Assert.True(step.WitnessGrid.Columns["Check"]!.ReadOnly);
            Assert.False(step.CanMoveNext);
            Assert.False(step.CanMoveBack);
        }, primary: 0, status: RunningStatus);
    }

    // ----- Render check 4: results -----

    [Fact]
    public void Results()
    {
        var status = DiscoveryUiText.StatusDoneWithProblems(1, 1, 1);
        RenderBoth("discovery-results", DiscoveryRenderState.Results, DiscoveryTestData.LitigationDraft, ResultsModel, step =>
        {
            var statuses = step.DocumentsGrid.Rows.Cast<DataGridViewRow>().Select(r => (string)r.Cells["Status"].Value!).ToList();
            Assert.Equal(new[] { "Generated", "Warnings", "Failed", "Out of date", "Not saved" }, statuses);
            Assert.Equal(Theme.Warning, step.DetailLabel.ForeColor);
            Assert.StartsWith("Generated with warnings: 1 check still failed", step.DetailLabel.Text);
            Assert.True(step.RegenerateButton.Visible);
            Assert.True(step.RegenerateButton.Enabled);
            Assert.False(step.SaveAgainButton.Visible);
            Assert.True(step.OpenWordButton.Enabled);
            Assert.Equal(status, step.StatusLabel.Text);
            Assert.Equal(Theme.Warning, step.StatusLabel.ForeColor);
            Assert.Equal("$4.37 spent", step.SpentLabel.Text);
        }, primary: 1, status: status, tone: SummaryTone.Warning, arrange: step => step.SelectDocument(step.Model.Rows[1].Key));

        // The Not saved row: Save Again takes the Regenerate... slot
        RenderBoth("discovery-results-not-saved", DiscoveryRenderState.Results, DiscoveryTestData.LitigationDraft, ResultsModel, step =>
        {
            Assert.True(step.SaveAgainButton.Visible);
            Assert.True(step.SaveAgainButton.Enabled);
            Assert.False(step.RegenerateButton.Visible);
            Assert.False(step.OpenWordButton.Enabled);
            Assert.StartsWith("Not saved: ", step.DetailLabel.Text);
            Assert.Equal(Theme.Warning, step.DetailLabel.ForeColor);
        }, primary: 1, status: status, tone: SummaryTone.Warning, arrange: step => step.SelectDocument("RFA"));
    }

    // ----- 09 render check 6: a row Out of date because a cited email changed (UI-SPEC S5) -----

    [Fact]
    public void ResultsExhibitsStale()
    {
        var status = DiscoveryUiText.StatusDoneWithProblems(1, 1, 1);
        RenderBoth("discovery-results-exhibits-stale", DiscoveryRenderState.Results, DiscoveryTestData.LitigationDraft,
            ExhibitsStaleModel, step =>
            {
                var row = step.Model.Rows[3];
                Assert.Equal(StaleSource.Exhibits, row.Stale);
                Assert.Equal("Out of date", step.DocumentsGrid.Rows[3].Cells["Status"].Value);
                Assert.StartsWith("Out of date: an email it cites", step.DetailLabel.Text);
                Assert.Equal(Theme.Warning, step.DetailLabel.ForeColor);
                // Rule 36: the detail line may wrap or ellipsize; the tooltip always carries the full status
                Assert.Equal(DiscoveryUiText.RowStatusFull(row), step.DetailTooltipText);
                Assert.True(step.RegenerateButton.Visible);
                Assert.True(step.RegenerateButton.Enabled);
                Assert.False(step.SaveAgainButton.Visible);
            }, primary: 1, status: status, tone: SummaryTone.Warning, arrange: step => step.SelectDocument(step.Model.Rows[3].Key));
    }

    // ----- Render check 5: memo Criminal -----

    [Fact]
    public void MemoCriminal()
    {
        RenderBoth("discovery-memo-criminal", DiscoveryRenderState.MemoCriminal, DiscoveryTestData.MemoCriminalDraft, null, step =>
        {
            Assert.False(step.RogRow.Visible);
            Assert.False(step.RfaRow.Visible);
            Assert.True(step.MemoNoteLabel.Visible);
            Assert.Equal(DiscoveryUiText.MemoNote(DiscoveryVariant.MemoCriminal), step.MemoNoteLabel.Text);
            Assert.Equal("Sworn witness interviews", step.DepositionsCaptionLabel.Text);
            Assert.Equal("Requested by", step.ServedByLabel.Text);
            Assert.Equal("Directed to", step.ServedOnLabel.Text);
            Assert.Equal(DiscoveryUiText.Notice(memo: true, corpusUsable: false), step.NoticeLabel.Text);
        }, primary: 1);
    }

    // ----- Render check 6: invalid inputs -----

    [Fact]
    public void Invalid()
    {
        RenderBoth("discovery-invalid", DiscoveryRenderState.Invalid, DiscoveryTestData.LitigationDraft, null, step =>
        {
            Assert.Equal(Theme.Danger, step.PagesHost.BorderColor);
            Assert.Equal(DiscoveryUiText.PagesHintInvalid, step.PagesHintLabel.Text);
            Assert.Equal(Theme.Danger, step.PagesHintLabel.ForeColor);
            Assert.Equal(Theme.Danger, step.RfpHost.BorderColor);
            Assert.Equal("Enter 5 to 80 requests for production.", step.CountsHintLabel.Text);
            Assert.Equal(Theme.Danger, step.CountsHintLabel.ForeColor);
            Assert.Equal(DiscoveryUiText.SamePartyHint, step.PartyHintLabel.Text);
            Assert.Equal(Theme.Danger, step.PartyHintLabel.ForeColor);
            Assert.True(step.PartyHintLabel.Visible);
            Assert.Equal(DiscoveryUiText.CapHint, step.SelectionHintLabel.Text);
            Assert.Equal(Theme.Warning, step.SelectionHintLabel.ForeColor);
            Assert.False(step.RunButton.Enabled);
        }, primary: 1);
    }

    // ----- Render check 7: no eligible witnesses -----

    [Fact]
    public void NoWitnesses()
    {
        RenderBoth("discovery-no-witnesses", DiscoveryRenderState.NoWitnesses, NoWitnessDraft, null, step =>
        {
            Assert.False(step.WitnessGrid.Visible);
            Assert.True(step.NoWitnessesLabel.Visible);
            Assert.Equal(DiscoveryUiText.NoWitnessesText, step.NoWitnessesLabel.Text);
            Assert.False(step.PagesText.Enabled);
            Assert.Equal(3, step.DocumentsGrid.Rows.Count);
            Assert.Equal(string.Empty, step.SelectionHintLabel.Text);
        }, primary: 1);
    }

    // ----- Harness -----

    private sealed class Measured
    {
        public List<string> Labels = new();
        public bool AnyGridHorizontalScroll;
        public int PrimaryCount;
        public List<(string Text, int Needed, int Width)> Buttons = new();
        public List<(string Grid, int Displayed, int Rows)> GridRows = new();
        public List<(string Text, int Needed, int Width)> Cells = new();
        public List<(string Text, int Needed, int Width)> Checks = new();
        public int WitnessWidth;
        public int DocumentWidth;
        public bool StateOverflow;
    }

    private static void RenderBoth(string name, DiscoveryRenderState render, Func<CaseDraft> createDraft,
        Func<CaseDraft, WizardState, DiscoveryStepModel>? createModel, Action<StepDiscoveryDocuments> verify, int? primary = null,
        string status = "", SummaryTone tone = SummaryTone.Muted, Action<StepDiscoveryDocuments>? arrange = null)
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.WizardMin })
            {
                using var fixture = new StepFixture(createDraft());
                StepDiscoveryDocuments? step = null;
                var measured = new Measured();
                var result = RenderHarness.RenderControl(() =>
                {
                    step = new StepDiscoveryDocuments();
                    step.SaveCase = () => Task.FromResult(true);
                    var model = createModel?.Invoke(fixture.Draft, fixture.State);
                    step.LoadForRender(fixture.State, render, model, status, tone);
                    return step;
                }, name, size, control =>
                {
                    control.PerformLayout();
                    Application.DoEvents();
                    arrange?.Invoke(step!);
                    control.PerformLayout();
                    Application.DoEvents();
                    Measure(step!, measured);
                    verify(step!);
                });

                Assert.True(File.Exists(result.PngPath), result.PngPath);
                Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
                Assert.Empty(result.Clipping);
                Assert.False(measured.AnyGridHorizontalScroll, $"horizontal grid scrollbar at {size}");
                Assert.False(measured.StateOverflow, $"state card content overflows at {size}");

                // Accent rule 1: at most one Primary besides the shell's Next
                Assert.InRange(measured.PrimaryCount, 0, 1);
                if (primary.HasValue)
                    Assert.True(primary.Value == measured.PrimaryCount, $"{name} at {size}: {measured.PrimaryCount} Primary buttons, expected {primary.Value}");

                // Rule 23: no visible button text is ellipsized
                foreach (var (text, needed, width) in measured.Buttons)
                    Assert.True(needed <= width, $"button '{text}' needs {needed}, has {width} at {size}");

                // Rule 34: the request checkbox labels are never cut by the 64-wide field
                foreach (var (text, needed, width) in measured.Checks)
                    Assert.True(needed <= width, $"checkbox '{text}' needs {needed}, has {width} at {size}");

                // Rules 35/36: at least 3 grid rows are visible; Side and Status texts fit without ellipsis
                foreach (var (grid, displayed, rows) in measured.GridRows)
                    Assert.True(displayed >= Math.Min(3, rows), $"{grid} shows {displayed} of {rows} rows at {size}");
                foreach (var (text, needed, width) in measured.Cells)
                    Assert.True(needed <= width, $"cell '{text}' needs {needed}, has {width} at {size}");
                var scale = result.DeviceDpi / 96.0;
                if (measured.WitnessWidth > 0)
                    Assert.True(measured.WitnessWidth >= (int)(96 * scale) - 1, $"Witness column {measured.WitnessWidth} at {size}");
                if (measured.DocumentWidth > 0)
                    Assert.True(measured.DocumentWidth >= (int)(112 * scale) - 1, $"Document column {measured.DocumentWidth} at {size}");
            }
        });
    }

    private static void Measure(StepDiscoveryDocuments step, Measured m)
    {
        m.Labels = AllControls(step).OfType<Label>().Where(l => l.Visible).Select(l => l.Text).ToList();
        m.AnyGridHorizontalScroll = new[] { step.WitnessGrid, step.DocumentsGrid }
            .Any(g => g.Visible && g.Controls.OfType<HScrollBar>().Any(b => b.Visible));
        var buttons = AllControls(step).OfType<ThemedButton>().Where(b => b.Visible).ToList();
        m.PrimaryCount = buttons.Count(b => b.Variant == ButtonStyle.Primary);
        m.Buttons = buttons.Select(b => (b.Text, TextRenderer.MeasureText(b.Text, b.Font).Width, b.Width)).ToList();
        foreach (var check in new[] { step.RfpCheck, step.RogCheck, step.RfaCheck }.Where(c => c.Visible))
            m.Checks.Add((check.Text, check.PreferredSize.Width, check.Width));

        if (step.StateCardPanel.Visible)
        {
            var content = step.StateCardPanel.Controls[0];
            m.StateOverflow = content.Bottom > step.StateCardPanel.ClientSize.Height || content.Right > step.StateCardPanel.ClientSize.Width;
        }

        using var body = Theme.BodyFont();
        if (step.WitnessGrid.Visible)
        {
            var grid = step.WitnessGrid;
            m.GridRows.Add(("Witness grid", grid.DisplayedRowCount(false), grid.Rows.Count));
            m.WitnessWidth = grid.Columns["Witness"]!.Width;
            var side = grid.Columns["Side"]!;
            foreach (var text in new[] { "Non-party", "Complainant", "Subject" })
                m.Cells.Add((text, TextRenderer.MeasureText(text, body).Width + side.DefaultCellStyle.Padding.Left
                    + grid.DefaultCellStyle.Padding.Left, side.Width));
        }
        if (step.DocumentsGrid.Visible)
        {
            var grid = step.DocumentsGrid;
            m.GridRows.Add(("Documents grid", grid.DisplayedRowCount(false), grid.Rows.Count));
            m.DocumentWidth = grid.Columns["Document"]!.Width;
            var statusColumn = grid.Columns["Status"]!;
            foreach (var text in new[] { "Not generated", "Out of date", "File missing", "Writing..." })
                m.Cells.Add((text, TextRenderer.MeasureText(text, body).Width + grid.DefaultCellStyle.Padding.Left, statusColumn.Width));
        }
    }

    // True when the label's text does not fit its client area (it would ellipsize)
    private static bool Ellipsized(Label label) =>
        TextRenderer.MeasureText(label.Text, label.Font, new Size(label.ClientSize.Width, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height > label.ClientSize.Height;

    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in AllControls(child))
                yield return nested;
        }
    }

    // ----- Seeded models -----

    private static readonly string RunningStatus = DiscoveryUiText.StatusRunning(1, 5, "Deposition of Raymond Harmon", 2, 3);

    private static DiscoveryStepModel BuildModel(CaseDraft draft, WizardState state)
    {
        DiscoveryStepModel.ApplyDefaults(draft);
        return DiscoveryStepModel.Build(draft, new DiscoveryDocumentSet(), DiscoveryCorpus.None(), state.StoryModelConfig,
            draft.FolderPath, _ => false);
    }

    private static DiscoveryStepModel RunningModel(CaseDraft draft, WizardState state)
    {
        var model = BuildModel(draft, state);
        var depositions = model.Rows.Where(r => r.Kind == DiscoveryKind.Deposition).Take(2).ToList();
        for (var i = 0; i < depositions.Count; i++)
        {
            depositions[i].State = DiscoveryRowState.Writing;
            depositions[i].Part = 2 - i;
            depositions[i].PartCount = 3;
        }
        return model;
    }

    private static DiscoveryStepModel ResultsModel(CaseDraft draft, WizardState state)
    {
        var model = BuildModel(draft, state);
        var rows = model.Rows;
        Assert.Equal(5, rows.Count);
        var generated = DateTime.UtcNow.AddHours(-2);

        rows[0].State = DiscoveryRowState.Generated;
        rows[0].GeneratedUtc = generated;
        rows[0].FileExists = true;
        rows[0].CostUsd = 1.21m;

        // The second deposition is the selected Warnings row (render check 4)
        rows[1].State = DiscoveryRowState.Warnings;
        rows[1].GeneratedUtc = generated;
        rows[1].FileExists = true;
        rows[1].Warnings = new List<DiscoveryViolation>
        {
            new() { Rule = DiscoveryRule.D3AmountVariant, Value = "$184,500.00", Canonical = "$184,250.00" }
        };

        rows[2].State = DiscoveryRowState.Failed;
        rows[2].FailureReason = "the request timed out";

        rows[3].State = DiscoveryRowState.OutOfDate;
        rows[3].Stale = StaleSource.Core;
        rows[3].GeneratedUtc = generated;
        rows[3].FileExists = true;

        rows[4].State = DiscoveryRowState.NotSaved;
        rows[4].GeneratedUtc = generated;
        rows[4].FileExists = false;

        model.SpentUsd = 4.37m;
        var failedKey = rows[2].Key;
        model.ToWrite = model.Selected.Where(i => i.Key == failedKey).ToList();
        model.EstimateUsd = model.EstimateByKey.TryGetValue(failedKey, out var cost) ? cost : 0m;
        return model;
    }

    // ResultsModel with the Out of date row stale because a cited email was regenerated or deleted (09 W3)
    private static DiscoveryStepModel ExhibitsStaleModel(CaseDraft draft, WizardState state)
    {
        var model = ResultsModel(draft, state);
        model.Rows[3].Stale = StaleSource.Exhibits;
        return model;
    }

    // Every key non-counsel character becomes background: nobody can be deposed
    private static CaseDraft NoWitnessDraft()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        foreach (var id in DiscoveryTestData.WitnessIds(draft))
            draft.Guide!.Cast.First(c => c.Id == id).Tier = CastTier.Background;
        return draft;
    }

    private sealed class StepFixture : IDisposable
    {
        private readonly string _root;

        public StepFixture(CaseDraft draft)
        {
            _root = Path.Combine(Path.GetTempPath(), "rd-discovery-render-" + Guid.NewGuid().ToString("N"));
            var store = new CaseStore(Path.Combine(_root, "Cases"), Path.Combine(_root, "AppData"));
            store.EnsureFolder(draft);
            Draft = draft;
            State = new WizardState { Case = draft, CaseStore = store, OpenAIApiKey = "sk-render-test", OpenAIKeyTested = true };
        }

        public CaseDraft Draft { get; }
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
