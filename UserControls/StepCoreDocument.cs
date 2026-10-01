using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.UserControls;

/// <summary>
/// Core document review step (UI-SPEC S3): inline generation with progress and cancel, a section list
/// with a per-section editor and full preview, the issues grid, and the Finish gate on Error issues.
/// </summary>
public class StepCoreDocument : UserControl, IWizardStep
{
    private static readonly Color Accent = Color.FromArgb(0, 120, 215);
    private const string EmDash = "—";
    private const string DefaultEmptyBody = "Click Generate Document to draft the complaint, petition or investigation memo from your scenario.";
    private const string CancelledText = "Generation cancelled. Completed sections were kept.";

    private WizardState _state = null!;
    private Label _lblTitle = null!;
    private Button _btnRegenerateAll = null!;
    private Panel _bodyHost = null!;
    private SplitContainer _split = null!;
    private ListBox _lstSections = null!;
    private TabControl _tabs = null!;
    private TabPage _tabSection = null!;
    private TabPage _tabFull = null!;
    private Label _lblSectionHeading = null!;
    private TextBox _txtEditor = null!;
    private Button _btnApply = null!;
    private TextBox _txtGuidance = null!;
    private Button _btnRegenerateSection = null!;
    private TextBox _txtPreview = null!;
    private Label _lblIssues = null!;
    private DataGridView _gridIssues = null!;
    private Label _lblNoIssues = null!;
    private Label _lblStatus = null!;
    private ProgressBar _progress = null!;
    private Button _btnCancel = null!;
    private Panel _emptyPanel = null!;
    private FlowLayoutPanel _emptyContent = null!;
    private Label _lblEmptyHeading = null!;
    private Label _lblEmptyBody = null!;
    private Button _btnGenerate = null!;

    private CancellationTokenSource? _cts;
    private bool _isGenerating;
    private bool _splitterInitialized;
    private bool _suppressSelection;
    private List<CoreDocumentIssue> _issues = new();
    private string _editorSectionId = string.Empty;
    private string _editorOriginalText = string.Empty;
    private string _declinedStaleHash = string.Empty;

    public string StepTitle => "Review Core Document";
    public bool CanMoveNext => !_isGenerating && _state?.Case.CoreDocument != null;
    public bool CanMoveBack => !_isGenerating;

    // Last case-flow step in Phase 2; Phase 3 changes this to "Next >"
    public string NextButtonText => "Finish";

    public event EventHandler? StateChanged;

    public StepCoreDocument()
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
            RowCount = 5,
            Padding = new Padding(8)
        };

        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));  // 0: Header
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // 1: Sections + editor
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));  // 2: Issues caption
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 112)); // 3: Issues grid
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));  // 4: Progress strip

        // Header: document title + Regenerate All
        var headerPanel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        _lblTitle = new Label
        {
            Text = "Core Document",
            AutoSize = false,
            AutoEllipsis = true,
            Location = new Point(0, 0),
            Height = 32,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold)
        };
        headerPanel.Controls.Add(_lblTitle);

        _btnRegenerateAll = ButtonHelper.CreateButton("Regenerate All", 120, 32, ButtonStyle.Default);
        _btnRegenerateAll.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        headerPanel.Controls.Add(_btnRegenerateAll);

        headerPanel.Resize += (s, e) =>
        {
            _btnRegenerateAll.Location = new Point(headerPanel.Width - _btnRegenerateAll.Width, 0);
            _lblTitle.Width = Math.Max(0, headerPanel.Width - _btnRegenerateAll.Width - 8);
        };
        mainLayout.Controls.Add(headerPanel, 0, 0);

        // Body host: split view and empty state share row 1
        _bodyHost = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };

        // SplitterDistance is set on first layout (setting it before the control has a width throws)
        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 8
        };
        _split.Layout += (s, e) => InitializeSplitter();
        _split.SizeChanged += (s, e) => InitializeSplitter();

        // Section list (owner-drawn: heading + colored status suffix)
        _lstSections = new ListBox
        {
            Dock = DockStyle.Fill,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 24,
            Font = new Font("Segoe UI", 9F),
            BackColor = SystemColors.Window,
            IntegralHeight = false
        };
        _split.Panel1.Controls.Add(_lstSections);

        // Tabs: Section editor + Full Document preview
        _tabs = new TabControl { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9F) };
        _tabSection = new TabPage("Section");
        _tabFull = new TabPage("Full Document");

        var sectionLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(4)
        };
        sectionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); // 0: Section heading
        sectionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 1: Editor
        sectionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); // 2: Editor toolbar

        _lblSectionHeading = new Label
        {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold)
        };
        sectionLayout.Controls.Add(_lblSectionHeading, 0, 0);

        _txtEditor = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            AcceptsReturn = true,
            // Default 32,767 would silently truncate long sections
            MaxLength = 0,
            Font = new Font("Segoe UI", 10F),
            Enabled = false
        };
        sectionLayout.Controls.Add(_txtEditor, 0, 1);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0)
        };
        _btnApply = ButtonHelper.CreateButton("Apply Edits", 100, 32, ButtonStyle.Default);
        _btnApply.Margin = new Padding(0);
        _btnApply.Enabled = false;
        _txtGuidance = new TextBox
        {
            Width = 320,
            Font = new Font("Segoe UI", 10F),
            PlaceholderText = "Optional guidance, e.g. add the March 3 site meeting",
            Margin = new Padding(8, 4, 8, 0)
        };
        _btnRegenerateSection = ButtonHelper.CreateButton("Regenerate Section", 150, 32, ButtonStyle.Primary);
        _btnRegenerateSection.Margin = new Padding(0);
        _btnRegenerateSection.Enabled = false;
        toolbar.Controls.Add(_btnApply);
        toolbar.Controls.Add(_txtGuidance);
        toolbar.Controls.Add(_btnRegenerateSection);
        sectionLayout.Controls.Add(toolbar, 0, 2);
        _tabSection.Controls.Add(sectionLayout);

        _txtPreview = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            MaxLength = 0,
            Font = new Font("Segoe UI", 10F),
            BackColor = SystemColors.Window
        };
        _tabFull.Controls.Add(_txtPreview);

        _tabs.TabPages.Add(_tabSection);
        _tabs.TabPages.Add(_tabFull);
        _split.Panel2.Controls.Add(_tabs);
        _bodyHost.Controls.Add(_split);

        // Empty state (cancelled/fatal before any section was written, or refused)
        _emptyPanel = new Panel { Dock = DockStyle.Fill, Visible = false };
        _emptyContent = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        _lblEmptyHeading = new Label
        {
            Text = "No core document yet",
            AutoSize = true,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 8)
        };
        _lblEmptyBody = new Label
        {
            Text = DefaultEmptyBody,
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            Font = new Font("Segoe UI", 9F),
            Margin = new Padding(0, 0, 0, 16)
        };
        _btnGenerate = ButtonHelper.CreateButton("Generate Document", 160, 32, ButtonStyle.Primary);
        _emptyContent.Controls.Add(_lblEmptyHeading);
        _emptyContent.Controls.Add(_lblEmptyBody);
        _emptyContent.Controls.Add(_btnGenerate);
        _emptyPanel.Controls.Add(_emptyContent);
        _emptyPanel.Resize += (s, e) => CenterEmptyContent();
        _emptyContent.SizeChanged += (s, e) => CenterEmptyContent();
        _bodyHost.Controls.Add(_emptyPanel);

        mainLayout.Controls.Add(_bodyHost, 0, 1);

        // Issues caption
        _lblIssues = new Label
        {
            Text = "Issues",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };
        mainLayout.Controls.Add(_lblIssues, 0, 2);

        // Issues grid (or the no-issues label)
        var issuesHost = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        _gridIssues = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = SystemColors.Window,
            Font = new Font("Segoe UI", 9F)
        };
        _gridIssues.Columns.Add(new DataGridViewTextBoxColumn { Name = "Severity", HeaderText = "Severity", Width = 80 });
        _gridIssues.Columns.Add(new DataGridViewTextBoxColumn { Name = "Section", HeaderText = "Section", Width = 180 });
        _gridIssues.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Issue",
            HeaderText = "Issue",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        _lblNoIssues = new Label
        {
            Text = "No issues found. No case-law citations or real names detected.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            Font = new Font("Segoe UI", 9F),
            ForeColor = Color.Green,
            Visible = false
        };
        issuesHost.Controls.Add(_gridIssues);
        issuesHost.Controls.Add(_lblNoIssues);
        mainLayout.Controls.Add(issuesHost, 0, 3);

        // Progress strip: status + progress bar + cancel (bar and cancel only while generating)
        var progressStrip = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0)
        };
        progressStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        progressStrip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        progressStrip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _lblStatus = new Label
        {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9F)
        };
        _progress = new ProgressBar
        {
            Width = 200,
            Style = ProgressBarStyle.Continuous,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(8, 4, 8, 4),
            Visible = false
        };
        _btnCancel = ButtonHelper.CreateButton("Cancel Generation", 140, 32, ButtonStyle.Danger);
        _btnCancel.Margin = new Padding(0);
        _btnCancel.Visible = false;
        progressStrip.Controls.Add(_lblStatus, 0, 0);
        progressStrip.Controls.Add(_progress, 1, 0);
        progressStrip.Controls.Add(_btnCancel, 2, 0);
        mainLayout.Controls.Add(progressStrip, 0, 4);

        this.Controls.Add(mainLayout);

        // Wire handlers only after every control exists (handlers read other controls)
        _lstSections.DrawItem += LstSections_DrawItem;
        _lstSections.SelectedIndexChanged += LstSections_SelectedIndexChanged;
        _tabs.SelectedIndexChanged += (s, e) => RefreshPreview();
        _btnRegenerateAll.Click += BtnRegenerateAll_Click;
        _btnGenerate.Click += async (s, e) => await RunGenerationAsync(fullRegenerate: true);
        _btnCancel.Click += BtnCancel_Click;
    }

    private void InitializeSplitter()
    {
        if (_splitterInitialized || _split.Width < 200 + 240)
            return;

        _splitterInitialized = true;
        _split.Panel1MinSize = 200;
        _split.SplitterDistance = 240;
    }

    private void CenterEmptyContent()
    {
        _emptyContent.Location = new Point(
            Math.Max(0, (_emptyPanel.Width - _emptyContent.Width) / 2),
            Math.Max(0, (_emptyPanel.Height - _emptyContent.Height) / 2));
    }

    // ----- Pure display helpers (unit tested) -----

    public static string SectionSuffix(CoreDocumentSection s, IReadOnlyCollection<CoreDocumentIssue> sectionIssues)
    {
        return s.Status switch
        {
            SectionStatus.Writing => $" {EmDash} writing...",
            SectionStatus.Pending => $" {EmDash} pending",
            SectionStatus.Failed => $" {EmDash} failed",
            _ when sectionIssues.Count > 0 => $" {EmDash} {sectionIssues.Count} issues",
            _ when s.IsUserEdited => $" {EmDash} edited",
            _ => string.Empty
        };
    }

    public static Color SectionSuffixColor(CoreDocumentSection s, IReadOnlyCollection<CoreDocumentIssue> sectionIssues)
    {
        return s.Status switch
        {
            SectionStatus.Writing => Accent,
            SectionStatus.Pending => Color.DimGray,
            SectionStatus.Failed => Color.Red,
            _ when sectionIssues.Any(i => i.Severity == IssueSeverity.Error) => Color.Red,
            _ when sectionIssues.Count > 0 => Color.DarkOrange,
            _ => SystemColors.ControlText
        };
    }

    public static string DocumentTitle(CoreDocument doc)
    {
        var label = doc.DocumentType switch
        {
            CoreDocumentType.Petition => "Petition",
            CoreDocumentType.InvestigationMemo => "Investigation Memo",
            _ => "Complaint"
        };
        var detail = doc.DocumentType == CoreDocumentType.InvestigationMemo
            ? doc.Memo.Organization
            : doc.Jurisdiction.CourtName;

        return string.IsNullOrWhiteSpace(detail) ? label : $"{label} {EmDash} {detail}";
    }

    public static (string Text, Color Color) ReadyStatus(CaseDraft draft, IReadOnlyCollection<CoreDocumentIssue> issues)
    {
        if (issues.Count == 0)
        {
            var doc = draft.CoreDocument;
            var sections = doc?.Sections.Count ?? 0;
            var paragraphs = doc == null ? 0 : CoreDocumentAssembler.CountNumberedParagraphs(doc);
            return ($"Document ready: {sections} sections, {paragraphs} numbered paragraphs.", Color.Green);
        }

        var errors = CoreDocumentValidator.CountErrors(issues);
        var warnings = CoreDocumentValidator.CountWarnings(issues);
        return ($"Document ready with {errors} errors and {warnings} warnings. Review the Issues list.", Color.DarkOrange);
    }

    public static string IssuesCaption(IReadOnlyCollection<CoreDocumentIssue> issues) =>
        $"Issues ({CoreDocumentValidator.CountErrors(issues)} errors, {CoreDocumentValidator.CountWarnings(issues)} warnings)";

    private static string FatalText(string? message) =>
        $"Generation stopped: {message}. Completed sections were kept. Fix the problem on the API Configuration step, then click Retry Section or Regenerate All.";

    private static string ErrorText(Exception ex) =>
        ex.InnerException != null ? $"Error: {ex.Message} ({ex.InnerException.Message})" : $"Error: {ex.Message}";

    // ----- Refresh -----

    private List<CoreDocumentIssue> IssuesFor(string sectionId) =>
        _issues.Where(i => i.SectionId == sectionId).ToList();

    private CoreDocumentSection? SelectedSection => _lstSections.SelectedItem as CoreDocumentSection;

    private void RefreshAll()
    {
        if (_state == null)
            return;

        var doc = _state.Case.CoreDocument;
        _issues = CoreDocumentValidator.Validate(_state.Case);

        _lblTitle.Text = doc == null ? "Core Document" : DocumentTitle(doc);

        // Rebind the section list only when the section objects changed; otherwise just repaint
        var sections = doc?.Sections ?? new List<CoreDocumentSection>();
        if (!_lstSections.Items.Cast<object>().SequenceEqual(sections))
        {
            var selectedId = SelectedSection?.Id ?? _editorSectionId;
            _suppressSelection = true;
            try
            {
                _lstSections.BeginUpdate();
                _lstSections.Items.Clear();
                _lstSections.Items.AddRange(sections.Cast<object>().ToArray());
                _lstSections.EndUpdate();
                var index = sections.FindIndex(s => s.Id == selectedId);
                if (index >= 0)
                    _lstSections.SelectedIndex = index;
            }
            finally
            {
                _suppressSelection = false;
            }
        }
        _lstSections.Invalidate();

        // Issues grid
        _lblIssues.Text = doc == null ? "Issues" : IssuesCaption(_issues);
        _gridIssues.Rows.Clear();
        foreach (var issue in _issues)
        {
            var rowIndex = _gridIssues.Rows.Add(
                issue.Severity == IssueSeverity.Error ? "Error" : "Warning",
                SectionLabel(doc, issue.SectionId),
                issue.Message);
            var row = _gridIssues.Rows[rowIndex];
            row.Tag = issue.SectionId;
            row.DefaultCellStyle.ForeColor = issue.Severity == IssueSeverity.Error ? Color.Red : Color.DarkOrange;
        }
        _gridIssues.ClearSelection(); // Keep severity colors visible on the first row
        _gridIssues.Visible = _issues.Count > 0;
        _lblNoIssues.Visible = doc != null && _issues.Count == 0;

        RefreshPreview();

        // Empty state: no document, or nothing written yet (never while a run is filling the list)
        _emptyPanel.Visible = !_isGenerating && (doc == null || doc.Sections.All(s => s.Status == SectionStatus.Pending));
        _split.Visible = !_emptyPanel.Visible;
        if (_emptyPanel.Visible)
            CenterEmptyContent();

        _btnRegenerateAll.Enabled = !_isGenerating && doc != null;
    }

    private static string SectionLabel(CoreDocument? doc, string sectionId)
    {
        if (string.IsNullOrEmpty(sectionId))
            return "Document";
        if (sectionId == "outline")
            return "Outline";
        return doc?.FindSection(sectionId)?.Heading ?? sectionId;
    }

    private void RefreshPreview()
    {
        if (_tabs.SelectedTab != _tabFull)
            return;

        var doc = _state?.Case.CoreDocument;
        _txtPreview.Text = doc == null ? string.Empty : CoreDocumentAssembler.RenderPlainText(doc);
    }

    private void LstSections_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _lstSections.Items.Count)
            return;

        var section = (CoreDocumentSection)_lstSections.Items[e.Index];
        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var font = e.Font ?? _lstSections.Font;

        using (var background = new SolidBrush(selected ? Accent : _lstSections.BackColor))
        {
            e.Graphics.FillRectangle(background, e.Bounds);
        }

        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        var textBounds = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 4), e.Bounds.Height);
        var heading = section.Heading;
        TextRenderer.DrawText(e.Graphics, heading, font, textBounds,
            selected ? Color.White : SystemColors.ControlText, flags | TextFormatFlags.EndEllipsis);

        var sectionIssues = IssuesFor(section.Id);
        var suffix = SectionSuffix(section, sectionIssues);
        if (suffix.Length > 0)
        {
            var headingWidth = TextRenderer.MeasureText(e.Graphics, heading, font, Size.Empty, flags).Width;
            var suffixBounds = new Rectangle(textBounds.X + headingWidth, textBounds.Y,
                textBounds.Width - headingWidth, textBounds.Height);
            if (suffixBounds.Width > 0)
            {
                TextRenderer.DrawText(e.Graphics, suffix, font, suffixBounds,
                    selected ? Color.White : SectionSuffixColor(section, sectionIssues), flags | TextFormatFlags.EndEllipsis);
            }
        }

        e.DrawFocusRectangle();
    }

    private void LstSections_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_suppressSelection || _isGenerating)
            return;

        LoadSelectedSection();
    }

    private void LoadSelectedSection()
    {
        var s = SelectedSection;
        if (s == null)
        {
            _editorSectionId = string.Empty;
            _lblSectionHeading.Text = string.Empty;
            _txtEditor.Text = string.Empty;
            _editorOriginalText = string.Empty;
            _btnApply.Enabled = false;
            UpdateEditorEnabled();
            return;
        }

        _editorSectionId = s.Id;
        _lblSectionHeading.Text = s.Heading;
        _txtEditor.Text = CoreDocumentEditing.JoinParagraphs(s.Paragraphs);
        _editorOriginalText = _txtEditor.Text;
        _btnApply.Enabled = false;
        _btnRegenerateSection.Text = s.Status == SectionStatus.Failed || s.Status == SectionStatus.Pending
            ? "Retry Section"
            : "Regenerate Section";
        UpdateEditorEnabled();
    }

    private void UpdateEditorEnabled()
    {
        var hasSection = !string.IsNullOrEmpty(_editorSectionId);
        _lstSections.Enabled = !_isGenerating;
        _txtEditor.Enabled = !_isGenerating && hasSection;
        _txtGuidance.Enabled = !_isGenerating && hasSection;
        _btnRegenerateSection.Enabled = !_isGenerating && hasSection;
        _btnApply.Enabled = !_isGenerating && hasSection && _txtEditor.Text != _editorOriginalText;
        _btnRegenerateAll.Enabled = !_isGenerating && _state?.Case.CoreDocument != null;
        _btnGenerate.Enabled = !_isGenerating;
    }

    // Selects the first section when nothing is selected, otherwise reloads the editor from the model
    private void ReloadEditorAfterRun()
    {
        if (_lstSections.Items.Count == 0)
        {
            LoadSelectedSection();
            return;
        }

        if (_lstSections.SelectedIndex < 0)
            _lstSections.SelectedIndex = 0; // Raises SelectedIndexChanged -> LoadSelectedSection
        else
            LoadSelectedSection();
    }

    // ----- Generation -----

    private void SetBusy(bool busy)
    {
        _isGenerating = busy;
        _progress.Visible = busy;
        _btnCancel.Visible = busy;
        _btnCancel.Enabled = busy;
        UpdateEditorEnabled();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task RunGenerationAsync(bool fullRegenerate)
    {
        if (_isGenerating || _state == null)
            return;

        _cts = new CancellationTokenSource();
        _lblEmptyBody.Text = DefaultEmptyBody;
        _progress.Value = 0;
        SetBusy(true);
        RefreshAll();

        try
        {
            var generator = new CoreDocumentGenerator(_state.CreateGateway());
            var progress = new Progress<string>(msg =>
            {
                _lblStatus.Text = msg;
                _lblStatus.ForeColor = Accent;
                var doc = _state.Case.CoreDocument;
                var max = Math.Max(1, doc?.Sections.Count ?? 1);
                var ready = doc?.Sections.Count(s => s.Status == SectionStatus.Ready) ?? 0;
                _progress.Value = 0;
                _progress.Maximum = max;
                _progress.Value = Math.Min(max, ready);
                RefreshAll();
            });

            _lblStatus.Text = "Writing the outline...";
            _lblStatus.ForeColor = Accent;

            var result = fullRegenerate || _state.Case.CoreDocument == null
                ? await generator.GenerateAsync(_state.Case, progress, _cts.Token)
                : await generator.GenerateSectionsAsync(_state.Case, progress, _cts.Token);

            var failed = _state.Case.CoreDocument?.Sections.FirstOrDefault(s => s.Status == SectionStatus.Failed);
            if (result.IsFatal)
            {
                _lblStatus.Text = FatalText(result.FatalError);
                _lblStatus.ForeColor = Color.Red;
            }
            else if (failed != null)
            {
                _lblStatus.Text = $"{failed.Heading} failed: {failed.FailureMessage}. Select it and click Retry Section.";
                _lblStatus.ForeColor = Color.Red;
            }
            else
            {
                var (text, color) = ReadyStatus(_state.Case, CoreDocumentValidator.Validate(_state.Case));
                _lblStatus.Text = text;
                _lblStatus.ForeColor = color;
            }
        }
        catch (OperationCanceledException)
        {
            _lblStatus.Text = CancelledText;
            _lblStatus.ForeColor = Color.DarkOrange;
        }
        catch (LlmCallException ex) when (ex.Result.Outcome == LlmOutcome.Refused)
        {
            var refusedText = $"The model declined to write this scenario: {ex.Result.Message}. Go Back and rephrase it. Keep criminal methods general and focus on who knew what, when, and where the money went.";
            // Always shown: a refused Regenerate All keeps the old document visible, so the status explains why
            _lblStatus.Text = refusedText;
            _lblStatus.ForeColor = Color.Red;
            if (_state.Case.CoreDocument == null)
                _lblEmptyBody.Text = refusedText;
        }
        catch (LlmCallException ex) when (ex.Result.IsFatal)
        {
            _lblStatus.Text = FatalText(ex.Result.Message ?? ex.Message);
            _lblStatus.ForeColor = Color.Red;
        }
        catch (Exception ex)
        {
            _lblStatus.Text = ErrorText(ex);
            _lblStatus.ForeColor = Color.Red;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            SetBusy(false);
            RefreshAll();
            ReloadEditorAfterRun();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void BtnCancel_Click(object? sender, EventArgs e)
    {
        if (!_isGenerating || _cts == null)
            return;

        var answer = MessageBox.Show(
            "Stop generating? Completed sections will be kept.",
            "Cancel Generation",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (answer == DialogResult.Yes)
        {
            _btnCancel.Enabled = false;
            _cts?.Cancel();
        }
    }

    private async void BtnRegenerateAll_Click(object? sender, EventArgs e)
    {
        if (_isGenerating)
            return;

        if (_state.Case.CoreDocument != null)
        {
            var answer = MessageBox.Show(
                "Regenerate the whole document? Every section, including your edits, will be replaced. This makes a new set of Story-model calls.",
                "Regenerate Document",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
                return;
        }

        await RunGenerationAsync(fullRegenerate: true);
    }

    // ----- IWizardStep -----

    public void BindState(WizardState state)
    {
        _state = state;
    }

    public async Task OnEnterStepAsync()
    {
        if (_isGenerating)
            return;

        var doc = _state.Case.CoreDocument;
        if (doc == null)
        {
            // Auto-generate on first entry, inline (partial results stay visible)
            await RunGenerationAsync(fullRegenerate: true);
            return;
        }

        RefreshAll();
        ReloadEditorAfterRun();

        // Never regenerate silently: only a changed scenario/settings hash prompts, and only once per hash
        var currentHash = _state.Case.ComputeSourceHash();
        if (doc.SourceHash != currentHash && currentHash != _declinedStaleHash)
        {
            var answer = MessageBox.Show(
                "Your scenario or settings changed after this document was written. Regenerate it now? Your edits will be replaced.",
                "Scenario Changed",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (answer == DialogResult.Yes)
                await RunGenerationAsync(fullRegenerate: true);
            else
                _declinedStaleHash = currentHash; // The validator reports the stale document as a Warning
        }
    }

    public Task OnLeaveStepAsync()
    {
        return Task.CompletedTask;
    }

    public Task<bool> ValidateStepAsync()
    {
        return Task.FromResult(!_isGenerating && _state.Case.CoreDocument != null);
    }
}
