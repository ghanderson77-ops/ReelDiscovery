using System.Runtime.ExceptionServices;
using ReelDiscovery.Forms;
using ReelDiscovery.Helpers;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Qa;

public class QaFindingsDialogTests
{
    private static void RunSta(Action body)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }

    internal static QaFindingRow Row(string thread, string doc, int day, string col1 = "Timeline", string col2 = "",
        bool relevant = true) => new()
    {
        ThreadId = thread,
        DocId = doc,
        DateSent = new DateTime(2024, 3, day, 10, 0, 0),
        Subject = "Subject " + doc,
        Col1 = col1,
        Col2 = col2,
        Text = "Mentions the March 9 meeting before it happened",
        Relevant = relevant,
        RegenerateSubject = "Subject " + doc,
        EmlCount = 3,
        AttachmentCount = 1
    };

    private static List<QaFindingRow> ThreeRows() => new()
    {
        Row("T2", "RD-000002", 20),
        Row("T1", "RD-000001", 5),
        Row("T3", "RD-000003", 12)
    };

    private static QaFindingsDialog Scans(List<QaFindingRow> rows, ISet<string>? queued = null,
        Func<string, Task<ThreadInvalidationResult>>? regenerate = null, Action<string>? open = null) =>
        new(QaFindingsMode.Scans, rows, null, "", queued ?? new HashSet<string>(), regenerate, open);

    private static void Select(QaFindingsDialog dialog, int index)
    {
        dialog.FindingsGrid.ClearSelection();
        dialog.FindingsGrid.CurrentCell = dialog.FindingsGrid.Rows[index].Cells[dialog.FindingsGrid.Columns.Count - 1];
        dialog.FindingsGrid.Rows[index].Selected = true;
    }

    private static string DocAt(QaFindingsDialog dialog, int index) => ((QaFindingRow)dialog.FindingsGrid.Rows[index].Tag!).DocId;

    [Fact]
    public void Scans_RowsSortedByDate_RegenerateNeedsSelection()
    {
        RunSta(() =>
        {
            using var dialog = Scans(ThreeRows(), regenerate: _ => Task.FromResult(new ThreadInvalidationResult()));
            Assert.Equal(3, dialog.FindingsGrid.Rows.Count);
            Assert.Equal(new[] { "RD-000001", "RD-000003", "RD-000002" }, Enumerable.Range(0, 3).Select(i => DocAt(dialog, i)));

            dialog.FindingsGrid.ClearSelection();
            dialog.UpdateButtons();
            Assert.False(dialog.RegenerateButton.Enabled);

            Select(dialog, 1);
            Assert.True(dialog.RegenerateButton.Enabled);
            Assert.Equal("Regenerate Thread...", dialog.RegenerateButton.Text);
            Assert.Equal(ButtonStyle.Primary, ((ThemedButton)dialog.RegenerateButton).Variant);
            Assert.Contains(dialog.FindingsGrid.Columns.Cast<DataGridViewColumn>(), c => c.Name == GridStyle.DotColumnName);
            Assert.Equal(new[] { "", "Doc ID", "Date", "Subject", "Check", "What was found" },
                dialog.FindingsGrid.Columns.Cast<DataGridViewColumn>().Select(c => c.HeaderText));
        });
    }

    [Fact]
    public void QueuedRow_KeepsRegenerateDisabled_AndShowsQueued()
    {
        RunSta(() =>
        {
            using var dialog = Scans(ThreeRows(), new HashSet<string> { "T3" }, _ => Task.FromResult(new ThreadInvalidationResult()));
            Select(dialog, 1);
            Assert.False(dialog.RegenerateButton.Enabled);
            Assert.Equal("Timeline · queued", dialog.FindingsGrid.Rows[1].Cells["Col1"].Value);
            Assert.Equal(Theme.BorderStrong, dialog.DotColorAt(1));
            Assert.Equal(Theme.Warning, dialog.DotColorAt(0));
        });
    }

    [Fact]
    public void Regenerate_Declined_DoesNotCallDelegate()
    {
        RunSta(() =>
        {
            var calls = 0;
            using var dialog = Scans(ThreeRows(), regenerate: _ =>
            {
                calls++;
                return Task.FromResult(new ThreadInvalidationResult { Appended = true });
            });
            string? title = null;
            string? text = null;
            dialog.Confirm = (t, x) =>
            {
                title = t;
                text = x;
                return false;
            };
            Select(dialog, 0);
            dialog.RegenerateSelectedAsync().GetAwaiter().GetResult();

            Assert.Equal(0, calls);
            Assert.Equal("Regenerate Thread", title);
            Assert.Equal(QaUiText.CQ1RegenerateThread("Subject RD-000001", 3, 1, true).Text, text);
            Assert.False(dialog.FooterLabel.Visible);
            Assert.False(dialog.QueuedAny);
        });
    }

    [Fact]
    public void Regenerate_Confirmed_QueuesRowAndShowsFooter()
    {
        RunSta(() =>
        {
            string? called = null;
            using var dialog = Scans(ThreeRows(), regenerate: id =>
            {
                called = id;
                return Task.FromResult(new ThreadInvalidationResult { Appended = true, FilesDeleted = 3 });
            });
            dialog.Confirm = (_, _) => true;
            Select(dialog, 2);
            dialog.RegenerateSelectedAsync().GetAwaiter().GetResult();

            Assert.Equal("T2", called);
            Assert.Equal("Timeline · queued", dialog.FindingsGrid.Rows[2].Cells["Col1"].Value);
            Assert.Equal(Theme.BorderStrong, dialog.DotColorAt(2));
            Assert.True(dialog.FooterLabel.Visible);
            Assert.Equal(QaUiText.FooterQueued(1), dialog.FooterLabel.Text);
            Assert.Equal(Theme.Success, dialog.FooterLabel.ForeColor);
            Assert.False(dialog.RegenerateButton.Enabled);
            Assert.True(dialog.QueuedAny);
        });
    }

    [Fact]
    public void Regenerate_WithErrors_ShowsNotDeletedWarning()
    {
        RunSta(() =>
        {
            using var dialog = Scans(ThreeRows(), regenerate: _ => Task.FromResult(new ThreadInvalidationResult
            {
                Appended = true,
                Errors = { "Mailboxes/a/RD-000001.eml: it is open in Outlook", "Mailboxes/b/RD-000001.eml: it is open in Outlook" }
            }));
            dialog.Confirm = (_, _) => true;
            Select(dialog, 0);
            dialog.RegenerateSelectedAsync().GetAwaiter().GetResult();

            var lines = dialog.FooterLabel.Text.Split('\n');
            Assert.Equal(QaUiText.FooterQueued(1), lines[0]);
            Assert.Equal(QaUiText.FooterNotDeleted(2, "it is open in Outlook"), lines[1]);
            Assert.Equal(2, lines.Length);
            Assert.Equal(Theme.Warning, dialog.FooterLabel.ForeColor);
        });
    }

    [Fact]
    public void Regenerate_WithKeyError_ShowsKeyNotUpdatedOnItsOwnLine()
    {
        RunSta(() =>
        {
            using var dialog = Scans(ThreeRows(), regenerate: _ => Task.FromResult(new ThreadInvalidationResult
            {
                Appended = true,
                Errors = { "Mailboxes/a/RD-000001.eml: locked" },
                KeyError = "answer-key.xlsx is open in Excel"
            }));
            dialog.Confirm = (_, _) => true;
            Select(dialog, 0);
            dialog.RegenerateSelectedAsync().GetAwaiter().GetResult();

            var lines = dialog.FooterLabel.Text.Split('\n');
            Assert.Equal(3, lines.Length);
            Assert.Equal(QaUiText.FooterNotDeleted(1, "locked"), lines[1]);
            Assert.Equal(QaUiText.FooterKeyNotUpdated("answer-key.xlsx is open in Excel"), lines[2]);
            Assert.Equal(Theme.Warning, dialog.FooterLabel.ForeColor);
        });
    }

    [Fact]
    public void Regenerate_KeyErrorOnly_WarningWithoutNotDeletedLine()
    {
        RunSta(() =>
        {
            using var dialog = Scans(ThreeRows(), regenerate: _ => Task.FromResult(new ThreadInvalidationResult
            {
                Appended = true,
                KeyError = "locked"
            }));
            dialog.Confirm = (_, _) => true;
            Select(dialog, 0);
            dialog.RegenerateSelectedAsync().GetAwaiter().GetResult();

            Assert.Equal(QaUiText.FooterQueued(1) + "\n" + QaUiText.FooterKeyNotUpdated("locked"), dialog.FooterLabel.Text);
            Assert.Equal(Theme.Warning, dialog.FooterLabel.ForeColor);
        });
    }

    [Fact]
    public void Regenerate_Refused_RowNotQueued()
    {
        RunSta(() =>
        {
            using var dialog = Scans(ThreeRows(), regenerate: _ => Task.FromResult(new ThreadInvalidationResult
            {
                Appended = false,
                Errors = { ThreadInvalidation.NotWrittenMessage }
            }));
            dialog.Confirm = (_, _) => true;
            Select(dialog, 0);
            dialog.RegenerateSelectedAsync().GetAwaiter().GetResult();

            Assert.Equal("Timeline", dialog.FindingsGrid.Rows[0].Cells["Col1"].Value);
            Assert.Equal(QaUiText.RegenerateFailed(ThreadInvalidation.NotWrittenMessage), dialog.FooterLabel.Text);
            Assert.Equal(Theme.Danger, dialog.FooterLabel.ForeColor);
            Assert.True(dialog.RegenerateButton.Enabled);
            Assert.False(dialog.QueuedAny);
        });
    }

    [Fact]
    public void OpenEmail_CallsDelegateWithDocId()
    {
        RunSta(() =>
        {
            var opened = new List<string>();
            using var dialog = Scans(ThreeRows(), open: opened.Add);
            Select(dialog, 1);
            dialog.OpenSelected();
            Assert.Equal(new[] { "RD-000003" }, opened);
        });
    }

    [Fact]
    public void BlindReviewMode_Columns()
    {
        RunSta(() =>
        {
            var rows = new List<QaFindingRow> { Row("T1", "RD-000001", 3, "Relevant", "Not relevant") };
            using var dialog = new QaFindingsDialog(QaFindingsMode.BlindReview, rows, null, "claude-haiku",
                new HashSet<string>(), _ => Task.FromResult(new ThreadInvalidationResult()), _ => { });
            Assert.Equal(new[] { "", "Doc ID", "Date", "Subject", "Answer key", "Reviewer", "Reviewer's reason" },
                dialog.FindingsGrid.Columns.Cast<DataGridViewColumn>().Select(c => c.HeaderText));
            Assert.Equal("Blind Review Disagreements", dialog.Text);
            Assert.Equal("Mar 3, 2024", dialog.FindingsGrid.Rows[0].Cells["Date"].FormattedValue);
        });
    }

    [Fact]
    public void BlindReviewMode_QueuedSuffixOnReviewerCell()
    {
        RunSta(() =>
        {
            var rows = new List<QaFindingRow> { Row("T1", "RD-000001", 3, "Relevant", "Not relevant") };
            using var dialog = new QaFindingsDialog(QaFindingsMode.BlindReview, rows, null, "m",
                new HashSet<string> { "T1" }, null, null);
            Assert.Equal("Not relevant · queued", dialog.FindingsGrid.Rows[0].Cells["Col2"].Value);
            Assert.Equal("Relevant", dialog.FindingsGrid.Rows[0].Cells["Col1"].Value);
        });
    }

    [Fact]
    public void LeakageMode_NoDotColumn_CloseOnly()
    {
        RunSta(() =>
        {
            var report = new ReelDiscovery.Models.Qa.LeakageReport
            {
                Verdict = ReelDiscovery.Models.Qa.LeakageVerdict.Warning,
                BalancedAccuracy = 0.65,
                RelevantEmails = 100,
                NonRelevantEmails = 400,
                Signals =
                {
                    new() { Feature = "sent on Sunday", Kind = ReelDiscovery.Models.Qa.LeakageSignalKind.Lift, RelevantShare = 0.83, BaseRate = 0.2, Support = 12, Lift = 0.63 }
                }
            };
            using var dialog = new QaFindingsDialog(QaFindingsMode.Leakage, new List<QaFindingRow>(), report, "",
                new HashSet<string>(), null, null);
            Assert.DoesNotContain(dialog.FindingsGrid.Columns.Cast<DataGridViewColumn>(), c => c.Name == GridStyle.DotColumnName);
            Assert.Equal(new[] { "Signal", "Relevant share", "Emails", "Lift" },
                dialog.FindingsGrid.Columns.Cast<DataGridViewColumn>().Select(c => c.HeaderText));
            Assert.Equal(1, dialog.FindingsGrid.Rows.Count);
            Assert.Equal("83%", dialog.FindingsGrid.Rows[0].Cells[1].FormattedValue);
            Assert.Equal("+63 pts", dialog.FindingsGrid.Rows[0].Cells[3].FormattedValue);
            Assert.False(dialog.RegenerateButton.Visible);
            Assert.False(dialog.OpenButton.Visible);
            Assert.True(dialog.CloseButton.Visible);
            Assert.Equal(dialog.HeadlineLabel.Text, QaUiText.LeakageLine(report).Text);
        });
    }

    [Fact]
    public void Empty_GridHidden_NoFindings_CloseFocused()
    {
        RunSta(() =>
        {
            using var dialog = Scans(new List<QaFindingRow>());
            Assert.False(dialog.FindingsGrid.Visible);
            Assert.Equal("No findings.", dialog.SummaryLabel.Text);
            Assert.Same(dialog.CloseButton, dialog.ActiveControl);
            Assert.False(dialog.RegenerateButton.Enabled);
        });
    }

    [Fact]
    public void Labels_DoNotUseMnemonics()
    {
        RunSta(() =>
        {
            using var dialog = Scans(ThreeRows());
            Assert.False(dialog.HeadlineLabel.UseMnemonic);
            Assert.False(dialog.SummaryLabel.UseMnemonic);
            Assert.False(dialog.FooterLabel.UseMnemonic);
        });
    }

    // --- BlindReviewDialog (S4) ---

    [Fact]
    public void BlindReview_SampleInput_Bounds_AndEstimateUpdates()
    {
        RunSta(() =>
        {
            var asked = new List<int>();
            using var dialog = new BlindReviewDialog("gpt-5-mini", "OpenAI", false, 3000, 400, 800, 0, n =>
            {
                asked.Add(n);
                return n / 100m;
            });
            var input = dialog.SampleInput;
            Assert.Equal(1, input.Minimum);
            Assert.Equal(3000, input.Maximum);
            Assert.Equal(50, input.Increment);
            Assert.True(input.ThousandsSeparator);
            Assert.Equal(0, input.DecimalPlaces);
            Assert.Equal(800, input.Value);
            Assert.Equal(QaUiText.Estimate(8m, 800, 0), dialog.EstimateLabel.Text);
            Assert.Equal(800, dialog.SampleSize);

            input.Value = 1000;
            Assert.Equal(QaUiText.Estimate(10m, 1000, 0), dialog.EstimateLabel.Text);
            Assert.Equal(1000, dialog.SampleSize);
            Assert.Contains(1000, asked);
            Assert.Null(dialog.FallbackLabel);
            Assert.Equal("Blind Review", dialog.Text);
        });
    }

    [Fact]
    public void BlindReview_Fallback_AndResumeClause()
    {
        RunSta(() =>
        {
            using var dialog = new BlindReviewDialog("claude-haiku", "Anthropic", true, 500, 100, 200, 50, n => 1m);
            Assert.NotNull(dialog.FallbackLabel);
            Assert.Equal(QaUiText.FallbackSentence, dialog.FallbackLabel!.Text);
            Assert.Equal(Theme.Warning, dialog.FallbackLabel.ForeColor);
            Assert.Equal(QaUiText.Estimate(1m, 150, 50), dialog.EstimateLabel.Text);
        });
    }

    [Fact]
    public void BlindReview_DefaultClampedToTotal_StartReviewIsPrimary()
    {
        RunSta(() =>
        {
            using var dialog = new BlindReviewDialog("m", "p", false, 120, 100, 200, 0, n => 0m);
            Assert.Equal(120, dialog.SampleInput.Value);
            Assert.Equal(DialogResult.OK, dialog.StartButton.DialogResult);
            Assert.Equal(ButtonStyle.Primary, ((ThemedButton)dialog.StartButton).Variant);
            Assert.Equal("Start Review", dialog.StartButton.Text);
            Assert.Same(dialog.CancelButton, dialog.ActiveControl);
        });
    }
}
