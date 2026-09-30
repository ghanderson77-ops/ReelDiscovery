using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Services;

namespace ReelDiscovery.UserControls;

public class StepGeneration : UserControl, IWizardStep
{
    private WizardState _state = null!;
    private ProgressBar _progressBar = null!;
    private Label _lblProgress = null!;
    private Label _lblCurrentOperation = null!;
    private Label _lblTokenUsage = null!;
    private RichTextBox _txtLog = null!;
    private Button _btnCancel = null!;
    private CancellationTokenSource? _cts;
    private System.Windows.Forms.Timer? _usageUpdateTimer;
    private bool _isGenerating = false;
    private bool _generationComplete = false;

    public string StepTitle => "Generating Emails";
    public bool CanMoveNext => _generationComplete;
    public bool CanMoveBack => !_isGenerating;
    public string NextButtonText => "View Results >";

    public event EventHandler? StateChanged;

    public StepGeneration()
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
            RowCount = 6,
            Padding = new Padding(10)
        };

        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        // Progress label
        _lblProgress = new Label
        {
            Text = "Preparing to generate emails...",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(this.Font, FontStyle.Bold)
        };
        mainLayout.Controls.Add(_lblProgress, 0, 0);

        // Progress bar
        _progressBar = new ProgressBar
        {
            Dock = DockStyle.Fill,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 30
        };
        mainLayout.Controls.Add(_progressBar, 0, 1);

        // Current operation label
        _lblCurrentOperation = new Label
        {
            Text = "",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.Gray
        };
        mainLayout.Controls.Add(_lblCurrentOperation, 0, 2);

        // Token usage label
        _lblTokenUsage = new Label
        {
            Text = "Tokens: 0 | Cost: $0.0000",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.DimGray,
            Font = new Font(this.Font.FontFamily, 8F)
        };
        mainLayout.Controls.Add(_lblTokenUsage, 0, 3);

        // Log text box
        _txtLog = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Font = new Font("Consolas", 9F),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.LightGray
        };
        mainLayout.Controls.Add(_txtLog, 0, 4);

        // Cancel button
        var buttonPanel = new Panel { Dock = DockStyle.Fill };
        _btnCancel = ButtonHelper.CreateButton("Cancel", 100, 35, ButtonStyle.Danger);
        _btnCancel.Location = new Point(0, 5);
        _btnCancel.Click += BtnCancel_Click;
        buttonPanel.Controls.Add(_btnCancel);
        mainLayout.Controls.Add(buttonPanel, 0, 5);

        this.Controls.Add(mainLayout);
    }

    private void BtnCancel_Click(object? sender, EventArgs e)
    {
        if (_isGenerating && _cts != null)
        {
            if (MessageBox.Show("Are you sure you want to cancel the generation?",
                "Confirm Cancel", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _cts.Cancel();
                AppendLog("Cancellation requested...", Color.Yellow);
            }
        }
    }

    private void AppendLog(string message, Color? color = null)
    {
        if (InvokeRequired)
        {
            Invoke(() => AppendLog(message, color));
            return;
        }

        _txtLog.SelectionStart = _txtLog.TextLength;
        _txtLog.SelectionLength = 0;
        _txtLog.SelectionColor = color ?? Color.LightGray;
        _txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
        _txtLog.ScrollToCaret();
    }

    // Orange = item skipped by a classified outcome (refused/truncated/invalid); Red = failure
    private void LogErrors(IEnumerable<string> errors)
    {
        foreach (var error in errors)
        {
            AppendLog($"  - {error}", error.Contains(" skipped (") ? Color.Orange : Color.Red);
        }
    }

    private void UpdateTokenUsageLabel()
    {
        if (_state?.UsageTracker == null) return;

        var tracker = _state.UsageTracker;
        var totalTokens = tracker.TotalInputTokens + tracker.TotalOutputTokens;
        _lblTokenUsage.Text = $"Tokens: {totalTokens:N0} ({tracker.TotalInputTokens:N0} in / {tracker.TotalOutputTokens:N0} out) | Cost: ${tracker.TotalCost:F4}";
    }

    private async Task StartGenerationAsync()
    {
        _isGenerating = true;
        _generationComplete = false;
        _cts = new CancellationTokenSource();

        // Note: We don't reset usage tracker here - it accumulates across all steps
        // (storyline generation, character generation, email generation)

        // Start timer to update token usage display
        _usageUpdateTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _usageUpdateTimer.Tick += (s, e) => UpdateTokenUsageLabel();
        _usageUpdateTimer.Start();

        _progressBar.Style = ProgressBarStyle.Marquee;
        _btnCancel.Enabled = true;
        _txtLog.Clear();

        AppendLog("Starting email generation...", Color.Cyan);
        AppendLog($"Topic: {_state.Topic}");
        AppendLog($"Storylines: {_state.Storylines.Count}");
        AppendLog($"Characters: {_state.Characters.Count}");
        AppendLog($"Target emails: {_state.Config.TotalEmailCount}");
        AppendLog($"Attachment percentage: {_state.Config.AttachmentPercentage}%");
        AppendLog("");

        try
        {
            var generator = new EmailGenerator(_state.CreateGateway(), _state.CreateMediaClient());

            var progress = new Progress<GenerationProgress>(p =>
            {
                if (InvokeRequired)
                {
                    Invoke(() => UpdateProgress(p));
                }
                else
                {
                    UpdateProgress(p);
                }
            });

            var result = await generator.GenerateEmailsAsync(_state, progress, _cts.Token);

            if (result.FatalError != null)
            {
                // Auth / spend-limit failure: the run was stopped to avoid further spend
                LogErrors(result.Errors);
                AppendLog($"Run stopped: {result.FatalError}", Color.Red);
                AppendLog("Completed threads were saved. Check your API key, billing or spend limit, then run again.", Color.Red);
                _lblProgress.Text = "Generation stopped by a provider error.";
                _generationComplete = true;
            }
            else if (result.WasCancelled)
            {
                AppendLog("Generation cancelled by user.", Color.Yellow);
                _lblProgress.Text = "Generation cancelled.";
            }
            else if (result.Errors.Count > 0)
            {
                AppendLog("Generation completed with errors:", Color.Orange);
                LogErrors(result.Errors);
                _lblProgress.Text = "Generation completed with errors.";
                _generationComplete = true;
            }
            else
            {
                AppendLog("", Color.White);
                AppendLog("========================================", Color.Green);
                AppendLog("GENERATION COMPLETE!", Color.Green);
                AppendLog("========================================", Color.Green);
                AppendLog($"Emails generated: {result.TotalEmailsGenerated}");
                AppendLog($"Threads created: {result.TotalThreadsGenerated}");
                AppendLog($"Attachments created: {result.TotalAttachmentsGenerated}");
                AppendLog($"  - Word documents: {result.WordDocumentsGenerated}");
                AppendLog($"  - Excel spreadsheets: {result.ExcelDocumentsGenerated}");
                AppendLog($"  - PowerPoint presentations: {result.PowerPointDocumentsGenerated}");
                AppendLog($"Time elapsed: {result.ElapsedTime:mm\\:ss}");
                AppendLog($"Output folder: {result.OutputFolder}");
                AppendLog("");
                AppendLog("--- Token Usage ---", Color.Cyan);
                AppendLog($"Input tokens: {_state.UsageTracker.TotalInputTokens:N0}");
                AppendLog($"Output tokens: {_state.UsageTracker.TotalOutputTokens:N0}");
                AppendLog($"Estimated cost: ${_state.UsageTracker.TotalCost:F4}");

                _lblProgress.Text = $"Generation complete! {result.TotalEmailsGenerated} emails created.";
                _generationComplete = true;

                // Verify files were created
                if (Directory.Exists(result.OutputFolder))
                {
                    var emlFiles = Directory.GetFiles(result.OutputFolder, "*.eml").Length;
                    AppendLog($"Verified: {emlFiles} .eml files in output folder", Color.Green);
                }
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog("Generation cancelled.", Color.Yellow);
            _lblProgress.Text = "Generation cancelled.";
        }
        catch (Exception ex)
        {
            AppendLog($"Error: {ex.Message}", Color.Red);
            _lblProgress.Text = "Generation failed.";
        }
        finally
        {
            _isGenerating = false;
            _btnCancel.Enabled = false;
            _progressBar.Style = ProgressBarStyle.Blocks;
            // Set Maximum before Value to avoid out-of-range exception
            _progressBar.Maximum = 100;
            _progressBar.Value = _generationComplete ? 100 : 0;

            // Stop and dispose timer
            _usageUpdateTimer?.Stop();
            _usageUpdateTimer?.Dispose();
            _usageUpdateTimer = null;

            // Final update of token usage
            UpdateTokenUsageLabel();

            _cts?.Dispose();
            _cts = null;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UpdateProgress(GenerationProgress p)
    {
        _lblCurrentOperation.Text = p.CurrentOperation;

        if (p.TotalEmails > 0)
        {
            _progressBar.Style = ProgressBarStyle.Blocks;

            // Calculate total work: emails + attachments (if any)
            var totalWork = p.TotalEmails + p.TotalAttachments;
            var completedWork = p.CompletedEmails + p.CompletedAttachments;

            _progressBar.Maximum = Math.Max(1, totalWork);
            _progressBar.Value = Math.Min(completedWork, totalWork);

            // Show different progress text depending on phase
            if (p.TotalAttachments > 0 && p.CompletedEmails >= p.TotalEmails)
            {
                // In attachment phase
                var overallPercent = totalWork > 0 ? (completedWork * 100.0 / totalWork) : 0;
                _lblProgress.Text = $"Progress: Emails done, attachments {p.CompletedAttachments}/{p.TotalAttachments} ({overallPercent:F0}%)";
            }
            else
            {
                // In email phase
                var overallPercent = totalWork > 0 ? (completedWork * 100.0 / totalWork) : 0;
                _lblProgress.Text = $"Progress: {p.CompletedEmails}/{p.TotalEmails} emails ({overallPercent:F0}%)";
            }
        }

        if (!string.IsNullOrEmpty(p.CurrentStoryline))
        {
            AppendLog($"Processing storyline: {p.CurrentStoryline}");
        }
    }

    public void BindState(WizardState state)
    {
        _state = state;
    }

    public async Task OnEnterStepAsync()
    {
        // Auto-start generation when entering this step
        if (!_isGenerating && !_generationComplete)
        {
            await StartGenerationAsync();
        }
    }

    public Task OnLeaveStepAsync()
    {
        return Task.CompletedTask;
    }

    public Task<bool> ValidateStepAsync()
    {
        if (!_generationComplete)
        {
            MessageBox.Show("Please wait for generation to complete.", "Please Wait", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }
}
