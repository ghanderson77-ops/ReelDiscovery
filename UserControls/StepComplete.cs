using System.Diagnostics;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;

namespace ReelDiscovery.UserControls;

public class StepComplete : UserControl, IWizardStep
{
    private WizardState _state = null!;
    private Label _lblSummary = null!;
    private Button _btnOpenFolder = null!;
    private DataGridView _gridStats = null!;

    public string StepTitle => "Complete";
    public bool CanMoveNext => true;
    public bool CanMoveBack => false;
    public string NextButtonText => "Finish";

    // Final step never changes navigation state; explicit empty accessors satisfy IWizardStep without CS0067
    public event EventHandler? StateChanged { add { } remove { } }

    public StepComplete()
    {
        InitializeUI();
    }

    private void InitializeUI()
    {
        this.Padding = new Padding(20);

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(10)
        };

        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));

        // Success header
        var headerPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(40, 167, 69)
        };

        var lblHeader = new Label
        {
            Text = "Email Dataset Generated Successfully!",
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 18F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            AutoSize = false
        };
        headerPanel.Controls.Add(lblHeader);
        mainLayout.Controls.Add(headerPanel, 0, 0);

        // Statistics grid
        _gridStats = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.Fixed3D,
            ColumnHeadersVisible = false
        };

        _gridStats.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Metric",
            Width = 250
        });

        _gridStats.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Value",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });

        mainLayout.Controls.Add(_gridStats, 0, 1);

        // Open folder button
        var buttonPanel = new Panel { Dock = DockStyle.Fill };

        _btnOpenFolder = ButtonHelper.CreateButton("Open Output Folder", 180, 40, ButtonStyle.Success);
        _btnOpenFolder.Location = new Point(0, 10);
        _btnOpenFolder.Click += BtnOpenFolder_Click;
        buttonPanel.Controls.Add(_btnOpenFolder);

        mainLayout.Controls.Add(buttonPanel, 0, 2);

        // Summary label
        _lblSummary = new Label
        {
            Text = "",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            ForeColor = Color.DimGray
        };
        mainLayout.Controls.Add(_lblSummary, 0, 3);

        this.Controls.Add(mainLayout);
    }

    private void BtnOpenFolder_Click(object? sender, EventArgs e)
    {
        if (_state.Result != null && Directory.Exists(_state.Result.OutputFolder))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _state.Result.OutputFolder,
                UseShellExecute = true
            });
        }
    }

    private void LoadStatistics()
    {
        _gridStats.Rows.Clear();

        if (_state.Result == null) return;

        var result = _state.Result;

        AddStatRow("Status", DescribeStatus(result));
        if (result.Errors.Count > 0)
            AddStatRow("Errors / Skipped Items", result.Errors.Count.ToString());
        AddStatRow("Total Emails Generated", result.TotalEmailsGenerated.ToString());
        AddStatRow("Email Threads Created", result.TotalThreadsGenerated.ToString());
        AddStatRow("", "");
        AddStatRow("--- Attachments ---", "");
        AddStatRow("Document Attachments", result.TotalAttachmentsGenerated.ToString());
        AddStatRow("  - Word Documents", result.WordDocumentsGenerated.ToString());
        AddStatRow("  - Excel Spreadsheets", result.ExcelDocumentsGenerated.ToString());
        AddStatRow("  - PowerPoint Presentations", result.PowerPointDocumentsGenerated.ToString());
        if (result.ImagesGenerated > 0)
            AddStatRow("Images Generated", result.ImagesGenerated.ToString());
        if (result.CalendarInvitesGenerated > 0)
            AddStatRow("Calendar Invites", result.CalendarInvitesGenerated.ToString());
        if (result.VoicemailsGenerated > 0)
            AddStatRow("Voicemails Generated", result.VoicemailsGenerated.ToString());
        AddStatRow("", "");
        AddStatRow("Topic", _state.Topic);
        AddStatRow("Storylines Used", _state.Storylines.Count.ToString());
        AddStatRow("Characters Used", _state.Characters.Count.ToString());
        AddStatRow("", "");
        AddStatRow("Generation Time", result.ElapsedTime.ToString(@"mm\:ss"));
        AddStatRow("Output Folder", result.OutputFolder);
        AddStatRow("", "");
        AddStatRow("--- API Usage ---", "");
        AddStatRow("Models Used", _state.ProviderSummary);
        AddStatRow("Input Tokens", $"{_state.UsageTracker.TotalInputTokens:N0}");
        AddStatRow("Output Tokens", $"{_state.UsageTracker.TotalOutputTokens:N0}");
        AddStatRow("Total Tokens", $"{_state.UsageTracker.TotalInputTokens + _state.UsageTracker.TotalOutputTokens:N0}");
        AddStatRow("Estimated Cost", $"${_state.UsageTracker.TotalCost:F4}");

        _lblSummary.Text = BuildSummaryText(result);
        _lblSummary.ForeColor = result.FatalError != null ? Color.Firebrick : Color.DimGray;
    }

    /// <summary>
    /// Short run outcome for the Status row. Same precedence as StepGeneration: fatal, cancelled, errors, clean.
    /// </summary>
    internal static string DescribeStatus(GenerationResult result) =>
        result.FatalError != null ? "Stopped by provider error"
        : result.WasCancelled ? "Cancelled"
        : result.Errors.Count > 0 ? $"Completed with {result.Errors.Count} error(s)"
        : "Completed";

    /// <summary>
    /// Summary text for the completion page; only a clean run says the dataset is ready for import (WR-05).
    /// </summary>
    internal static string BuildSummaryText(GenerationResult result)
    {
        if (result.FatalError != null)
        {
            return $"Generation was stopped by a provider error: {result.FatalError}\n\n" +
                   "Threads completed before the stop were saved to the output folder. " +
                   "Check your API key, billing or spend limit, then run again.";
        }

        if (result.WasCancelled)
        {
            return "Generation was cancelled. Threads completed before the cancel were saved to the output folder.\n\n" +
                   "Click 'Finish' to close this wizard, or 'Open Output Folder' to view the generated files.";
        }

        if (result.Errors.Count > 0)
        {
            return $"Generation finished, but {result.Errors.Count} item(s) were skipped or failed " +
                   "(see the run log on the previous step). The saved .EML files are usable for import.\n\n" +
                   "Click 'Finish' to close this wizard, or 'Open Output Folder' to view the generated files.";
        }

        return "Your email dataset is ready for import into your e-discovery platform.\n\n" +
               "The .EML files contain proper threading headers (Message-ID, In-Reply-To, References)\n" +
               "so email threads will be grouped correctly when imported.\n\n" +
               "Click 'Finish' to close this wizard, or 'Open Output Folder' to view the generated files.";
    }

    private void AddStatRow(string metric, string value)
    {
        _gridStats.Rows.Add(metric, value);
    }

    public void BindState(WizardState state)
    {
        _state = state;
    }

    public Task OnEnterStepAsync()
    {
        LoadStatistics();
        return Task.CompletedTask;
    }

    public Task OnLeaveStepAsync()
    {
        return Task.CompletedTask;
    }

    public Task<bool> ValidateStepAsync()
    {
        return Task.FromResult(true);
    }
}
