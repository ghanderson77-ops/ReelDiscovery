using System.Globalization;
using ReelDiscovery.Forms;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.UserControls;

/// <summary>
/// Corpus Settings step (04-UI-SPEC S1): corpus size, % relevant with live counts, the date window, attachment types and
/// a collapsible Advanced card (non-relevant mix, overrides, DocID prefix). Every field commits on Leave / Enter (never
/// per keystroke) and autosaves with the case; Next is blocked only by a mix total other than 100 or an invalid date.
/// </summary>
public class StepCorpusSettings : UserControl, IWizardStep
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private WizardState _state = null!;

    private TableLayoutPanel _root = null!;
    private Label _lblTitle = null!;
    private Label _lblSubtitle = null!;
    private Button _btnResetDefaults = null!;
    private Button _btnSkipToDiscovery = null!;
    private ToolTip _toolTip = null!;
    private Label _lblNotice = null!;
    private RowStyle _noticeRow = null!;
    private Panel _scrollHost = null!;
    private TableLayoutPanel _inner = null!;
    private CardPanel _cardSize = null!;
    private CardPanel _cardDates = null!;
    private CardPanel _cardAdvanced = null!;

    // Size and relevance
    private readonly List<ThemedButton> _sizeToggles = new();
    private ThemedButton _btnSize250 = null!;
    private ThemedButton _btnSize1000 = null!;
    private ThemedButton _btnSize2500 = null!;
    private ThemedButton _btnSize5000 = null!;
    private TextBox _txtSize = null!;
    private Label _lblSizeHint = null!;
    private PercentSlider _slider = null!;
    private TextBox _txtPercent = null!;
    private Label _lblPercentSuffix = null!;
    private Label _lblLiveCounts = null!;
    private Label _lblRelevantWarning = null!;

    // Dates and attachments
    private TextBox _txtStart = null!;
    private TextBox _txtEnd = null!;
    private CardPanel _hostStart = null!;
    private CardPanel _hostEnd = null!;
    private Button _btnResetWindow = null!;
    private Label _lblWindowHint = null!;
    private Label _lblDateLine = null!;
    private CheckBox _chkOffice = null!;
    private CheckBox _chkCalendar = null!;
    private CheckBox _chkImages = null!;
    private CheckBox _chkVoicemail = null!;
    private Label _lblMediaNotice = null!;
    private Label _lblAttachmentHint = null!;

    // Advanced
    private TableLayoutPanel _advancedLayout = null!;
    private TableLayoutPanel _advancedHeader = null!;
    private int _cornerInset;
    private Label _lblAdvancedSummary = null!;
    private Button _btnAdvanced = null!;
    private Panel _advancedDivider = null!;
    private RowStyle _advancedDividerRow = null!;
    private TableLayoutPanel _advancedBody = null!;
    private Label _lblMixHint = null!;
    private TextBox _txtDaily = null!;
    private TextBox _txtAncillary = null!;
    private TextBox _txtNewsletter = null!;
    private TextBox _txtDecoy = null!;
    private Label _lblMixTotal = null!;
    private Label _lblDecoyHint = null!;
    private Button _btnDefaultMix = null!;
    private Label _lblOverridesHint = null!;
    private TextBox _txtHot = null!;
    private TextBox _txtPrivileged = null!;
    private TextBox _txtAttachment = null!;
    private TextBox _txtPrefix = null!;
    private CardPanel _hostPrefix = null!;
    private Label _lblHotHint = null!;
    private Label _lblPrivilegedHint = null!;
    private Label _lblAttachmentRateHint = null!;
    private Label _lblPrefixHint = null!;
    private Label _lblHotLine = null!;
    private Label _lblPrivilegedLine = null!;
    private Label _lblAttachmentLine = null!;
    private Label _lblPrefixLine = null!;
    private readonly List<Label> _overrideLabels = new();

    private Label _lblStatus = null!;

    private bool _loading;

    // 10 D-08: relabels of the current plan, counted off the UI thread on entry
    private int _relabelCount;
    private bool _advancedExpanded;
    // Transient clamp notices shown in place of the hint until the next commit
    private string _sizeClampText = string.Empty;
    private string _percentClampText = string.Empty;

    public string StepTitle => "Corpus Settings";
    public string NextButtonText => "Next >";
    public bool CanMoveNext => true;
    public bool CanMoveBack => true;

    public event EventHandler? StateChanged;

    public StepCorpusSettings()
    {
        InitializeUI();
    }

    private CaseDraft Draft => _state.Case;

    private CorpusSettings Settings => Draft.Corpus!.Settings;

    private static string MyDocuments => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    // ----- Pure helpers (unit-tested) -----

    /// <summary>
    /// Digits with optional thousands commas; anything else (or empty) is null. Huge values cap at int.MaxValue so they clamp.
    /// </summary>
    public static int? ParseCount(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return null;

        long value = 0;
        var digits = 0;
        foreach (var c in trimmed)
        {
            if (c == ',')
                continue;
            if (c is < '0' or > '9')
                return null;
            digits++;
            value = Math.Min(int.MaxValue, value * 10 + (c - '0'));
        }

        return digits == 0 ? null : (int)value;
    }

    public static string FormatCount(int n) => n.ToString("N0", Invariant);

    public static int? MatchingPreset(int total) =>
        CorpusSettings.SizePresets.Contains(total) ? total : null;

    public static (int Value, bool Clamped) ClampInt(int value, int min, int max)
    {
        var clamped = Math.Clamp(value, min, max);
        return (clamped, clamped != value);
    }

    public static string SanitizePrefix(string? text) => CorpusSettings.SanitizePrefix(text);

    /// <summary>
    /// A successful Next (mix totals 100 and both dates valid) asks Review Plan to plan (D-12, 09 W2).
    /// </summary>
    public static bool RequestsPlanning(bool mixOk, bool datesOk) => mixOk && datesOk;

    /// <summary>09 W2 / U1: the zero-spend route to Discovery Documents shows whenever there is no approved plan.</summary>
    public static bool ShowsSkipToDiscovery(bool hasPlan, bool approved) => !(hasPlan && approved);

    // ----- Layout -----

    private void InitializeUI()
    {
        // Content aligns with the wizard's page margin (the shell supplies it)
        this.Padding = new Padding(0);
        this.BackColor = Theme.Background;
        var gap = LogicalToDeviceUnits(Theme.Gap);
        var smallGap = LogicalToDeviceUnits(Theme.SmallGap);
        var pad = LogicalToDeviceUnits(Theme.CardPadding);
        var inset = LogicalToDeviceUnits(15);
        var buttonHeight = LogicalToDeviceUnits(34);
        var xs = LogicalToDeviceUnits(4);
        _toolTip = new ToolTip();

        _root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(0),
            BackColor = Theme.Background
        };
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 0: Header
        // Sized in SizeNoticeRow from the wrapped text: an AutoSize row kept a height measured at an earlier width
        _noticeRow = new RowStyle(SizeType.Absolute, 0);
        _root.RowStyles.Add(_noticeRow);                          // 1: Notice line
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 2: Scroll host
        _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 3: Status strip

        // ----- Row 0, header: title + save state, Reset to Defaults on the right -----
        var headerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, smallGap)
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Title
        headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 1: Subtitle

        _lblTitle = new Label
        {
            Text = "Corpus Settings",
            AutoSize = false,
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Padding(0),
            Font = Theme.TitleFont(),
            ForeColor = Theme.Text
        };
        _lblTitle.Height = _lblTitle.PreferredHeight;
        _lblSubtitle = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            UseMnemonic = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Padding(0, LogicalToDeviceUnits(2), 0, 0),
            Font = Theme.BodyFont(),
            ForeColor = Theme.TextMuted
        };
        _lblSubtitle.Height = _lblSubtitle.PreferredHeight;
        // Header buttons (09 UI-SPEC S4): Skip to Discovery Documents, then Reset to Defaults, on one row
        var headerButtons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(gap, 0, 0, 0),
            Padding = new Padding(0)
        };
        _btnSkipToDiscovery = ButtonHelper.CreateButton("Skip to Discovery Documents", 0, buttonHeight, ButtonStyle.Default);
        _btnSkipToDiscovery.Margin = new Padding(0, 0, smallGap, 0);
        _toolTip.SetToolTip(_btnSkipToDiscovery, "Go straight to Discovery Documents without planning the corpus. No model calls are made.");
        _btnResetDefaults = ButtonHelper.CreateButton("Reset to Defaults", 0, buttonHeight, ButtonStyle.Default);
        _btnResetDefaults.Margin = new Padding(0);
        headerButtons.Controls.Add(_btnSkipToDiscovery);
        headerButtons.Controls.Add(_btnResetDefaults);

        headerLayout.Controls.Add(_lblTitle, 0, 0);
        headerLayout.Controls.Add(_lblSubtitle, 0, 1);
        headerLayout.Controls.Add(headerButtons, 1, 0);
        headerLayout.SetRowSpan(headerButtons, 2);
        _root.Controls.Add(headerLayout, 0, 0);

        // ----- Row 1, notice line (wraps; width follows the step in UpdateWidths) -----
        _lblNotice = WrapLabel(Theme.SmallFont(), Theme.TextMuted, new Padding(0, 0, 0, gap));
        _lblNotice.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        _root.Controls.Add(_lblNotice, 0, 1);

        // ----- Row 2, scroll host: inner layout spans the client width, so it only ever scrolls vertically -----
        _scrollHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Theme.Background,
            Margin = new Padding(0)
        };
        _inner = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Background,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        _inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _inner.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Two-card row
        _inner.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 1: Advanced card

        var cardsRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Background,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        cardsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        cardsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        cardsRow.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Cards (both Dock Fill, so one height)

        _cardSize = BuildSizeCard(pad, gap, smallGap, xs, buttonHeight);
        _cardSize.Margin = new Padding(0, 0, smallGap, 0);
        _cardDates = BuildDatesCard(pad, gap, smallGap, xs, buttonHeight);
        _cardDates.Margin = new Padding(smallGap, 0, 0, 0);
        cardsRow.Controls.Add(_cardSize, 0, 0);
        cardsRow.Controls.Add(_cardDates, 1, 0);
        _inner.Controls.Add(cardsRow, 0, 0);

        _cardAdvanced = BuildAdvancedCard(gap, smallGap, inset, xs, buttonHeight);
        _cardAdvanced.Margin = new Padding(0, gap, 0, 0);
        _inner.Controls.Add(_cardAdvanced, 0, 1);

        _scrollHost.Controls.Add(_inner);
        _root.Controls.Add(_scrollHost, 0, 2);

        // ----- Row 3, status strip: status label only -----
        _lblStatus = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            UseMnemonic = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, LogicalToDeviceUnits(12), 0, 0),
            MinimumSize = new Size(0, buttonHeight),
            Font = Theme.BodyFont(),
            ForeColor = Theme.TextMuted
        };
        _lblStatus.Height = buttonHeight;
        _root.Controls.Add(_lblStatus, 0, 3);

        this.Controls.Add(_root);

        // Wire handlers only after every control exists (handlers read other controls)
        _scrollHost.ClientSizeChanged += (s, e) => UpdateWidths();
        this.SizeChanged += (s, e) => UpdateWidths();
        _btnResetDefaults.Click += (s, e) => ResetDefaults();
        _btnSkipToDiscovery.Click += (s, e) => SkipToDiscovery();

        foreach (var toggle in _sizeToggles)
            toggle.Click += (s, e) => SelectPreset((int)toggle.Tag!);
        _txtSize.TextChanged += (s, e) =>
        {
            if (!_loading)
                SelectToggle(ParseCount(_txtSize.Text) is int typed ? MatchingPreset(typed) : null);
        };
        WireCommit(_txtSize, () => CommitSize());

        _slider.ValueChanged += (s, e) =>
        {
            if (_loading)
                return;
            _txtPercent.Text = _slider.Value.ToString(Invariant);
            _percentClampText = string.Empty;
            RefreshCounts(_slider.Value);
        };
        _slider.ValueCommitted += (s, e) => CommitPercent(_slider.Value, clamped: false);
        _slider.Leave += (s, e) => CommitPercent(_slider.Value, clamped: false);
        WireCommit(_txtPercent, () => CommitPercentField());

        WireCommit(_txtStart, () => CommitWindow());
        WireCommit(_txtEnd, () => CommitWindow());
        _btnResetWindow.Click += (s, e) => ResetWindow();
        _chkOffice.CheckedChanged += (s, e) => CommitMedia();
        _chkCalendar.CheckedChanged += (s, e) => CommitMedia();
        _chkImages.CheckedChanged += (s, e) => CommitMedia();
        _chkVoicemail.CheckedChanged += (s, e) => CommitMedia();

        _btnAdvanced.Click += (s, e) => SetAdvancedExpanded(!_advancedExpanded);
        foreach (var field in new[] { _txtDaily, _txtAncillary, _txtNewsletter, _txtDecoy })
            WireCommit(field, () => CommitMix());
        _btnDefaultMix.Click += (s, e) => UseDefaultMix();
        WireCommit(_txtHot, () => CommitOverrides());
        WireCommit(_txtPrivileged, () => CommitOverrides());
        WireCommit(_txtAttachment, () => CommitOverrides());
        WireCommit(_txtPrefix, () => CommitPrefix());
    }

    private CardPanel BuildSizeCard(int pad, int gap, int smallGap, int xs, int buttonHeight)
    {
        var card = NewCard(new Padding(pad));
        var layout = NewCardLayout(1);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Corpus size caption
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 1: Size row
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 2: Size hint
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 3: Relevant emails caption
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 4: Slider row
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 5: Live counts
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 6: Relevant warning
        layout.RowCount = 7;

        layout.Controls.Add(Caption("Corpus size", new Padding(0, 0, 0, smallGap)), 0, 0);

        // Size row: segmented presets, the number field, then empty space
        var sizeRow = NewRowLayout(3);
        sizeRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        sizeRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        sizeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sizeRow.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Toggles + field
        var toggles = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Surface,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        foreach (var preset in CorpusSettings.SizePresets)
        {
            var toggle = (ThemedButton)ButtonHelper.CreateButton(FormatCount(preset), 0, buttonHeight, ButtonStyle.Toggle);
            toggle.Tag = preset;
            _sizeToggles.Add(toggle);
        }
        // One segmented control: equal widths, 4 apart
        var toggleWidth = _sizeToggles.Max(t => t.Width);
        for (var i = 0; i < _sizeToggles.Count; i++)
        {
            _sizeToggles[i].Width = toggleWidth;
            _sizeToggles[i].Margin = new Padding(0, 0, i < _sizeToggles.Count - 1 ? xs : 0, 0);
            toggles.Controls.Add(_sizeToggles[i]);
        }
        _btnSize250 = _sizeToggles[0];
        _btnSize1000 = _sizeToggles[1];
        _btnSize2500 = _sizeToggles[2];
        _btnSize5000 = _sizeToggles[3];

        _txtSize = new TextBox { TextAlign = HorizontalAlignment.Right, MaxLength = 6 };
        var sizeHost = TextFieldHost.Create(this, _txtSize);
        sizeHost.Dock = DockStyle.None;
        sizeHost.Anchor = AnchorStyles.Left;
        sizeHost.Width = LogicalToDeviceUnits(96);
        sizeHost.Margin = new Padding(smallGap, 0, 0, 0);
        sizeRow.Controls.Add(toggles, 0, 0);
        sizeRow.Controls.Add(sizeHost, 1, 0);
        layout.Controls.Add(sizeRow, 0, 1);

        _lblSizeHint = WrapLabel(Theme.SmallFont(), Theme.TextMuted, new Padding(0, xs, 0, 0));
        layout.Controls.Add(_lblSizeHint, 0, 2);

        layout.Controls.Add(Caption("Relevant emails", new Padding(0, gap, 0, smallGap)), 0, 3);

        // Slider row: slider, % field and % suffix share one vertical center
        var sliderRow = NewRowLayout(3);
        sliderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sliderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        sliderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        sliderRow.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Slider + field + suffix
        _slider = new PercentSlider
        {
            Minimum = CorpusSettings.MinRelevantPercent,
            Maximum = CorpusSettings.MaxRelevantPercent,
            Value = CorpusSettings.DefaultRelevantPercent,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Height = LogicalToDeviceUnits(28),
            BackColor = Theme.Surface,
            Margin = new Padding(0)
        };
        _txtPercent = new TextBox { TextAlign = HorizontalAlignment.Right, MaxLength = 3 };
        var percentHost = TextFieldHost.Create(this, _txtPercent);
        percentHost.Dock = DockStyle.None;
        percentHost.Anchor = AnchorStyles.Left;
        percentHost.Width = LogicalToDeviceUnits(72);
        percentHost.Margin = new Padding(LogicalToDeviceUnits(12), 0, 0, 0);
        _lblPercentSuffix = Suffix(xs);
        sliderRow.Controls.Add(_slider, 0, 0);
        sliderRow.Controls.Add(percentHost, 1, 0);
        sliderRow.Controls.Add(_lblPercentSuffix, 2, 0);
        layout.Controls.Add(sliderRow, 0, 4);

        _lblLiveCounts = WrapLabel(Theme.BodyFont(), Theme.Text, new Padding(0, smallGap, 0, 0));
        layout.Controls.Add(_lblLiveCounts, 0, 5);
        _lblRelevantWarning = WrapLabel(Theme.SmallFont(), Theme.TextMuted, new Padding(0, xs, 0, 0));
        _lblRelevantWarning.Visible = false;
        layout.Controls.Add(_lblRelevantWarning, 0, 6);

        card.Controls.Add(layout);
        return card;
    }

    private CardPanel BuildDatesCard(int pad, int gap, int smallGap, int xs, int buttonHeight)
    {
        var card = NewCard(new Padding(pad));
        var layout = NewCardLayout(1);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Date window caption
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 1: Date row
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 2: Window hint
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 3: Clamp / validation line
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 4: Attachments caption
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 5: Checkboxes
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 6: Media notice
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 7: Attachment hint
        layout.RowCount = 8;

        layout.Controls.Add(Caption("Date window", new Padding(0, 0, 0, smallGap)), 0, 0);

        // Two equal date columns 8 apart (4 + 4 margins), then Reset Window bottom-aligned with the fields
        var dateRow = NewRowLayout(3);
        dateRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        dateRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        dateRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        dateRow.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Captions
        dateRow.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 1: Fields + Reset Window
        dateRow.RowCount = 2;
        var half = smallGap / 2;
        var startCaption = SmallCaption("Start", new Padding(0, 0, half, xs));
        var endCaption = SmallCaption("End", new Padding(half, 0, 0, xs));
        _txtStart = new TextBox { PlaceholderText = "YYYY-MM-DD", MaxLength = 10 };
        _txtEnd = new TextBox { PlaceholderText = "YYYY-MM-DD", MaxLength = 10 };
        _hostStart = TextFieldHost.Create(this, _txtStart);
        _hostStart.Dock = DockStyle.None;
        _hostStart.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _hostStart.Margin = new Padding(0, 0, half, 0);
        _hostEnd = TextFieldHost.Create(this, _txtEnd);
        _hostEnd.Dock = DockStyle.None;
        _hostEnd.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _hostEnd.Margin = new Padding(half, 0, 0, 0);
        _btnResetWindow = ButtonHelper.CreateButton("Reset Window", 0, buttonHeight, ButtonStyle.Default);
        _btnResetWindow.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        _btnResetWindow.Margin = new Padding(smallGap, 0, 0, 0);
        dateRow.Controls.Add(startCaption, 0, 0);
        dateRow.Controls.Add(endCaption, 1, 0);
        dateRow.Controls.Add(_hostStart, 0, 1);
        dateRow.Controls.Add(_hostEnd, 1, 1);
        dateRow.Controls.Add(_btnResetWindow, 2, 1);
        layout.Controls.Add(dateRow, 0, 1);

        _lblWindowHint = WrapLabel(Theme.SmallFont(), Theme.TextMuted, new Padding(0, smallGap, 0, 0));
        layout.Controls.Add(_lblWindowHint, 0, 2);
        _lblDateLine = WrapLabel(Theme.SmallFont(), Theme.Warning, new Padding(0, xs, 0, 0));
        _lblDateLine.Visible = false;
        layout.Controls.Add(_lblDateLine, 0, 3);

        layout.Controls.Add(Caption("Attachments", new Padding(0, gap, 0, smallGap)), 0, 4);

        var checks = NewRowLayout(2);
        checks.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        checks.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        checks.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Office documents · Calendar invites
        checks.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 1: Images · Voicemail
        checks.RowCount = 2;
        _chkOffice = NewCheckBox("Office documents", new Padding(0));
        _chkCalendar = NewCheckBox("Calendar invites", new Padding(0));
        _chkImages = NewCheckBox("Images", new Padding(0, smallGap, 0, 0));
        _chkVoicemail = NewCheckBox("Voicemail", new Padding(0, smallGap, 0, 0));
        checks.Controls.Add(_chkOffice, 0, 0);
        checks.Controls.Add(_chkCalendar, 1, 0);
        checks.Controls.Add(_chkImages, 0, 1);
        checks.Controls.Add(_chkVoicemail, 1, 1);
        layout.Controls.Add(checks, 0, 5);

        _lblMediaNotice = WrapLabel(Theme.SmallFont(), Theme.TextMuted, new Padding(0, smallGap, 0, 0));
        _lblMediaNotice.Text = CorpusUiText.MediaNotice;
        layout.Controls.Add(_lblMediaNotice, 0, 6);
        _lblAttachmentHint = WrapLabel(Theme.SmallFont(), Theme.TextMuted, new Padding(0, smallGap, 0, 0));
        _lblAttachmentHint.Text = CorpusUiText.AttachmentTypesHint;
        layout.Controls.Add(_lblAttachmentHint, 0, 7);

        card.Controls.Add(layout);
        return card;
    }

    private CardPanel BuildAdvancedCard(int gap, int smallGap, int inset, int xs, int buttonHeight)
    {
        // Full-bleed sides (Padding 1) but top and bottom inset by the corner radius, so the white content never covers
        // the rounded corners (deferred-items 04-12); the header and body paddings subtract that inset
        _cornerInset = LogicalToDeviceUnits(8);
        var card = NewCard(new Padding(1, _cornerInset, 1, _cornerInset));
        _advancedLayout = NewCardLayout(1);
        _advancedLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Header
        _advancedDividerRow = new RowStyle(SizeType.Absolute, 0);
        _advancedLayout.RowStyles.Add(_advancedDividerRow);              // 1: Divider (1 when expanded)
        _advancedLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 2: Body (expanded only)
        _advancedLayout.RowCount = 3;

        // Header: caption · summary (ellipsis) · Show / Hide Advanced
        _advancedHeader = NewRowLayout(3);
        var header = _advancedHeader;
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Caption + summary + button
        var caption = Caption("Advanced", new Padding(0));
        caption.Anchor = AnchorStyles.Left;
        _lblAdvancedSummary = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            UseMnemonic = false,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(LogicalToDeviceUnits(12), 0, smallGap, 0),
            Font = Theme.SmallFont(),
            ForeColor = Theme.TextMuted
        };
        _lblAdvancedSummary.Height = _lblAdvancedSummary.PreferredHeight;
        // Sized for the longer caption so the header never shifts when it flips
        _btnAdvanced = ButtonHelper.CreateButton("Show Advanced", 0, buttonHeight, ButtonStyle.Default);
        var hideWidth = ButtonHelper.CreateButton("Hide Advanced", 0, buttonHeight, ButtonStyle.Default).Width;
        ((ThemedButton)_btnAdvanced).FitToText(Math.Max(_btnAdvanced.Width, hideWidth), buttonHeight);
        _btnAdvanced.Anchor = AnchorStyles.Right;
        _btnAdvanced.Margin = new Padding(0);
        header.Controls.Add(caption, 0, 0);
        header.Controls.Add(_lblAdvancedSummary, 1, 0);
        header.Controls.Add(_btnAdvanced, 2, 0);
        _advancedLayout.Controls.Add(header, 0, 0);

        _advancedDivider = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Border, Margin = new Padding(0), Visible = false };
        _advancedLayout.Controls.Add(_advancedDivider, 0, 1);

        // Body: Non-relevant mix | Overrides
        _advancedBody = NewRowLayout(2);
        _advancedBody.Padding = new Padding(inset, gap, inset, Math.Max(0, gap - _cornerInset + 1));
        _advancedBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _advancedBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _advancedBody.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Both columns
        _advancedBody.Visible = false;

        _advancedBody.Controls.Add(BuildMixColumn(smallGap, xs, buttonHeight), 0, 0);
        _advancedBody.Controls.Add(BuildOverridesColumn(smallGap, xs), 1, 0);
        _advancedLayout.Controls.Add(_advancedBody, 0, 2);

        card.Controls.Add(_advancedLayout);
        return card;
    }

    private TableLayoutPanel BuildMixColumn(int smallGap, int xs, int buttonHeight)
    {
        var column = NewRowLayout(1);
        column.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        column.Dock = DockStyle.None;
        column.Margin = new Padding(0, 0, smallGap, 0);
        column.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        column.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Caption
        column.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 1: Hint
        column.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 2: Share rows
        column.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 3: Total line
        column.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 4: Decoy hint
        column.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 5: Use Default Mix
        column.RowCount = 6;

        column.Controls.Add(Caption("Non-relevant mix", new Padding(0, 0, 0, xs)), 0, 0);
        _lblMixHint = WrapLabel(Theme.SmallFont(), Theme.TextMuted, new Padding(0, 0, 0, LogicalToDeviceUnits(12)));
        column.Controls.Add(_lblMixHint, 0, 1);

        var shares = NewRowLayout(3);
        shares.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shares.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(64)));
        shares.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var rows = new (string Label, Func<TextBox> Create)[]
        {
            ("Custodians' daily business", () => _txtDaily = NumberBox(3)),
            ("Ancillary staff traffic", () => _txtAncillary = NumberBox(3)),
            ("Newsletters, spam and personal", () => _txtNewsletter = NumberBox(3)),
            ("Near-miss decoys", () => _txtDecoy = NumberBox(3))
        };
        for (var i = 0; i < rows.Length; i++)
        {
            shares.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Share row i
            var top = i == 0 ? 0 : smallGap;
            var label = BodyLabel(rows[i].Label, new Padding(0, top, smallGap, 0));
            label.Anchor = AnchorStyles.Left;
            var host = TextFieldHost.Create(this, rows[i].Create());
            host.Dock = DockStyle.None;
            host.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            host.Margin = new Padding(0, top, 0, 0);
            var suffix = Suffix(xs);
            suffix.Margin = new Padding(xs, top, 0, 0);
            shares.Controls.Add(label, 0, i);
            shares.Controls.Add(host, 1, i);
            shares.Controls.Add(suffix, 2, i);
        }
        shares.RowCount = rows.Length;
        column.Controls.Add(shares, 0, 2);

        _lblMixTotal = WrapLabel(Theme.SmallFont(), Theme.Success, new Padding(0, smallGap, 0, 0));
        column.Controls.Add(_lblMixTotal, 0, 3);
        _lblDecoyHint = WrapLabel(Theme.SmallFont(), Theme.TextMuted, new Padding(0, xs, 0, 0));
        _lblDecoyHint.Text = CorpusUiText.DecoyHint;
        column.Controls.Add(_lblDecoyHint, 0, 4);
        _btnDefaultMix = ButtonHelper.CreateButton("Use Default Mix", 0, buttonHeight, ButtonStyle.Default);
        _btnDefaultMix.Anchor = AnchorStyles.Left;
        _btnDefaultMix.Margin = new Padding(0, LogicalToDeviceUnits(12), 0, 0);
        column.Controls.Add(_btnDefaultMix, 0, 5);
        return column;
    }

    private TableLayoutPanel BuildOverridesColumn(int smallGap, int xs)
    {
        var column = NewRowLayout(1);
        column.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        column.Dock = DockStyle.None;
        column.Margin = new Padding(smallGap, 0, 0, 0);
        column.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        column.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Caption
        column.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 1: Hint
        column.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 2: Override rows
        column.RowCount = 3;

        column.Controls.Add(Caption("Overrides", new Padding(0, 0, 0, xs)), 0, 0);
        _lblOverridesHint = WrapLabel(Theme.SmallFont(), Theme.TextMuted, new Padding(0, 0, 0, LogicalToDeviceUnits(12)));
        _lblOverridesHint.Text = CorpusUiText.OverridesHint;
        column.Controls.Add(_lblOverridesHint, 0, 1);

        // One grid for all four overrides, so every field shares one column: label above hint in column 0, the field
        // (vertically centered on the pair) in column 1, a % suffix (or nothing) in column 2, a clamp line under the pair
        var grid = NewRowLayout(3);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(96)));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, SuffixColumnWidth(xs)));

        _txtHot = NumberBox(1);
        _txtHot.PlaceholderText = "Auto";
        _txtPrivileged = NumberBox(2);
        _txtPrivileged.PlaceholderText = "Auto";
        _txtAttachment = NumberBox(2);
        _txtAttachment.PlaceholderText = "Auto";
        _txtPrefix = new TextBox { MaxLength = CorpusSettings.MaxPrefixLength, CharacterCasing = CharacterCasing.Upper };

        _lblHotHint = WrapLabel(Theme.SmallFont(), Theme.TextMuted, new Padding(0, LogicalToDeviceUnits(2), smallGap, 0));
        _lblPrivilegedHint = WrapLabel(Theme.SmallFont(), Theme.TextMuted, new Padding(0, LogicalToDeviceUnits(2), smallGap, 0));
        _lblAttachmentRateHint = WrapLabel(Theme.SmallFont(), Theme.TextMuted, new Padding(0, LogicalToDeviceUnits(2), smallGap, 0));
        _lblPrefixHint = WrapLabel(Theme.SmallFont(), Theme.TextMuted, new Padding(0, LogicalToDeviceUnits(2), smallGap, 0));
        _lblHotLine = OverrideLine(xs);
        _lblPrivilegedLine = OverrideLine(xs);
        _lblAttachmentLine = OverrideLine(xs);
        _lblPrefixLine = OverrideLine(xs);

        AddOverrideRow(grid, 0, "Hot emails per hot fact", _lblHotHint, _txtHot, false, _lblHotLine, smallGap, xs);
        AddOverrideRow(grid, 1, "Privileged share of relevant threads", _lblPrivilegedHint, _txtPrivileged, true, _lblPrivilegedLine, smallGap, xs);
        AddOverrideRow(grid, 2, "Attachment rate", _lblAttachmentRateHint, _txtAttachment, true, _lblAttachmentLine, smallGap, xs);
        _hostPrefix = AddOverrideRow(grid, 3, "DocID prefix", _lblPrefixHint, _txtPrefix, false, _lblPrefixLine, smallGap, xs);
        grid.RowCount = grid.RowStyles.Count;
        column.Controls.Add(grid, 0, 2);
        return column;
    }

    private CardPanel AddOverrideRow(TableLayoutPanel grid, int index, string text, Label hint, TextBox field, bool percent,
        Label line, int smallGap, int xs)
    {
        var top = index == 0 ? 0 : smallGap;
        var baseRow = grid.RowStyles.Count;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Label
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Hint
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Clamp line

        var label = WrapLabel(Theme.BodyFont(), Theme.Text, new Padding(0, top, smallGap, 0));
        label.Text = text;
        _overrideLabels.Add(label);
        var host = TextFieldHost.Create(this, field);
        host.Dock = DockStyle.None;
        host.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        host.Margin = new Padding(0, top, 0, 0);
        grid.Controls.Add(label, 0, baseRow);
        grid.Controls.Add(host, 1, baseRow);
        grid.SetRowSpan(host, 2);
        if (percent)
        {
            var suffix = Suffix(xs);
            suffix.Margin = new Padding(xs, top, 0, 0);
            grid.Controls.Add(suffix, 2, baseRow);
            grid.SetRowSpan(suffix, 2);
        }
        grid.Controls.Add(hint, 0, baseRow + 1);
        grid.Controls.Add(line, 0, baseRow + 2);
        grid.SetColumnSpan(line, 3);
        return host;
    }

    // ----- Control factories -----

    private CardPanel NewCard(Padding padding) => new()
    {
        Dock = DockStyle.Fill,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = padding,
        BackColor = Theme.Surface
    };

    private static TableLayoutPanel NewCardLayout(int columns)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = columns,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Surface,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        for (var i = 0; i < columns; i++)
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
        return layout;
    }

    private static TableLayoutPanel NewRowLayout(int columns) => new()
    {
        Dock = DockStyle.Fill,
        ColumnCount = columns,
        RowCount = 1,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        BackColor = Theme.Surface,
        Margin = new Padding(0),
        Padding = new Padding(0)
    };

    private static Label Caption(string text, Padding margin) => new()
    {
        Text = text,
        AutoSize = true,
        UseMnemonic = false,
        Margin = margin,
        Font = Theme.LabelFont(),
        ForeColor = Theme.Text,
        BackColor = Theme.Surface
    };

    private static Label SmallCaption(string text, Padding margin) => new()
    {
        Text = text,
        AutoSize = true,
        UseMnemonic = false,
        Margin = margin,
        Font = Theme.SmallFont(),
        ForeColor = Theme.TextMuted,
        BackColor = Theme.Surface
    };

    private static Label BodyLabel(string text, Padding margin) => new()
    {
        Text = text,
        AutoSize = true,
        UseMnemonic = false,
        Margin = margin,
        Font = Theme.BodyFont(),
        ForeColor = Theme.Text,
        BackColor = Theme.Surface
    };

    // Wrapping label: AutoSize, with its preferred size pinned (Minimum = Maximum) to the UpdateWidths width and the
    // height its text needs there. Table layouts measure labels through GetPreferredSize and keep stale wrap heights
    // otherwise (a row once measured at the 150 px default width stayed 8 lines tall)
    private Label WrapLabel(Font font, Color color, Padding margin)
    {
        var label = new Label
        {
            AutoSize = true,
            UseMnemonic = false,
            Margin = margin,
            Padding = new Padding(0),
            Font = font,
            ForeColor = color,
            BackColor = Color.Transparent,
            Tag = 1
        };
        label.TextChanged += (s, e) => FitWrap(label);
        FitWrap(label);
        return label;
    }

    private static void SetWrapWidth(Label label, int width)
    {
        width = Math.Max(1, width);
        if (label.Tag is int current && current == width)
            return;
        label.Tag = width;
        FitWrap(label);
    }

    private static void FitWrap(Label label)
    {
        var width = label.Tag is int w ? w : 1;
        var height = string.IsNullOrEmpty(label.Text)
            ? 0
            : TextRenderer.MeasureText(label.Text, label.Font, new Size(width, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
        label.MinimumSize = Size.Empty;
        label.MaximumSize = new Size(width, Math.Max(1, height));
        label.MinimumSize = new Size(width, height);
    }

    private Label Suffix(int xs) => new()
    {
        Text = "%",
        AutoSize = true,
        UseMnemonic = false,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(xs, 0, 0, 0),
        Font = Theme.BodyFont(),
        ForeColor = Theme.TextMuted,
        BackColor = Theme.Surface
    };

    private int SuffixColumnWidth(int xs)
    {
        using var font = Theme.BodyFont();
        return TextRenderer.MeasureText("%", font).Width + xs;
    }

    private Label OverrideLine(int xs)
    {
        var line = WrapLabel(Theme.SmallFont(), Theme.Warning, new Padding(0, xs, 0, 0));
        line.Visible = false;
        return line;
    }

    private static TextBox NumberBox(int maxLength) => new() { TextAlign = HorizontalAlignment.Right, MaxLength = maxLength };

    private static CheckBox NewCheckBox(string text, Padding margin) => new()
    {
        Text = text,
        AutoSize = true,
        FlatStyle = FlatStyle.System,
        Font = Theme.BodyFont(),
        ForeColor = Theme.Text,
        BackColor = Theme.Surface,
        UseMnemonic = false,
        Margin = margin
    };

    // Commit on Leave and on Enter (never per keystroke, T-04-54)
    private static void WireCommit(TextBox box, Action commit)
    {
        box.Leave += (s, e) => commit();
        box.KeyDown += (s, e) =>
        {
            if (e.KeyCode != Keys.Enter)
                return;
            e.SuppressKeyPress = true;
            commit();
        };
    }

    // ----- Widths (wrapping labels follow the step and card widths) -----

    private void UpdateWidths()
    {
        if (_inner == null || _scrollHost == null)
            return;

        // The notice spans the step (the root has no padding)
        if (ClientSize.Width > 0)
            SetWrapWidth(_lblNotice, ClientSize.Width);
        SizeNoticeRow();

        var width = _scrollHost.ClientSize.Width;
        if (width <= 0)
            return;

        _inner.Width = width;

        var smallGap = LogicalToDeviceUnits(Theme.SmallGap);
        var pad = LogicalToDeviceUnits(Theme.CardPadding);
        var inset = LogicalToDeviceUnits(15);
        var cardContent = Math.Max(1, width / 2 - smallGap - 2 * pad);
        foreach (var label in new[] { _lblSizeHint, _lblLiveCounts, _lblRelevantWarning, _lblWindowHint, _lblDateLine, _lblMediaNotice, _lblAttachmentHint })
            SetWrapWidth(label, cardContent);

        // Advanced body: 1 px border + 15 inset each side, two columns 8 + 8 apart
        var column = Math.Max(1, (width - 2 - 2 * inset) / 2 - smallGap);
        foreach (var label in new[] { _lblMixHint, _lblMixTotal, _lblDecoyHint, _lblOverridesHint })
            SetWrapWidth(label, column);
        var overrideText = Math.Max(1, column - LogicalToDeviceUnits(96) - SuffixColumnWidth(LogicalToDeviceUnits(4)) - smallGap);
        foreach (var label in _overrideLabels.Concat(new[] { _lblHotHint, _lblPrivilegedHint, _lblAttachmentRateHint, _lblPrefixHint }))
            SetWrapWidth(label, overrideText);
        foreach (var label in new[] { _lblHotLine, _lblPrivilegedLine, _lblAttachmentLine, _lblPrefixLine })
            SetWrapWidth(label, column);
    }

    private void SizeNoticeRow()
    {
        var height = _lblNotice.MinimumSize.Height + _lblNotice.Margin.Vertical;
        if ((int)_noticeRow.Height == height)
            return;

        // A style change made while the step itself is being sized does not lay the root out again on its own
        _noticeRow.Height = height;
        _root.PerformLayout();
    }

    // Widths first, so the root lays out with the current notice height and label widths
    protected override void OnLayout(LayoutEventArgs e)
    {
        UpdateWidths();
        base.OnLayout(e);
    }

    // ----- IWizardStep -----

    public void BindState(WizardState state)
    {
        _state = state;
    }

    /// <summary>
    /// Binds and enters synchronously for render tests (no save; the fixture has already saved the case).
    /// </summary>
    internal void LoadForRender(WizardState state, bool expandAdvanced = false)
    {
        BindState(state);
        EnterCore();
        if (expandAdvanced)
            SetAdvancedExpanded(true);
    }

    public async Task OnEnterStepAsync()
    {
        if (_state == null)
            return;

        _relabelCount = 0;
        EnterCore();
        if (!string.IsNullOrWhiteSpace(_state.PendingNotice))
        {
            SetStatus(_state.PendingNotice, Theme.Success);
            _state.PendingNotice = string.Empty;
        }

        // 10 D-08: count the current plan's relabels off the UI thread, then refresh only the notice (user edits stay)
        var draft = Draft;
        _relabelCount = await Task.Run(() => RelabelCount(draft));
        if (IsDisposed)
            return;
        if (_relabelCount > 0)
        {
            _lblNotice.Text = NoticeText();
            SizeNoticeRow();
        }

        Draft.AdvanceStage(CaseStage.CorpusSettings);
        await SaveAsync();
        if (IsDisposed)
            return;

        // Focus starts on the selected size Toggle (UI-SPEC focal point)
        _sizeToggles.FirstOrDefault(t => t.Selected)?.Select();
    }

    private void EnterCore()
    {
        Draft.Corpus ??= new CorpusState { Settings = CorpusSettings.CreateDefault(Draft) };
        Draft.Corpus.Settings ??= CorpusSettings.CreateDefault(Draft);

        // A window never set (settings created before the guide had dated facts) takes the default
        if (string.IsNullOrEmpty(Settings.WindowStart) && string.IsNullOrEmpty(Settings.WindowEnd))
        {
            var (start, end) = CorpusSettings.DefaultWindow(Draft.Guide);
            Settings.WindowStart = start;
            Settings.WindowEnd = end;
        }

        SetStatus(string.Empty, Theme.TextMuted);
        FillControls();
        SetAdvancedExpanded(false);
        RefreshHeader();
        UpdateWidths();
    }

    public async Task OnLeaveStepAsync()
    {
        if (_state == null || Draft.Corpus == null)
            return;

        FlushCommits();
        await SaveAsync();
    }

    /// <summary>
    /// Next gate: flush pending commits; a mix total other than 100 (M-S1) or an invalid date (M-S2) blocks; otherwise
    /// the user explicitly asked to continue, so a successful Next asks Review Plan to plan (D-12).
    /// </summary>
    public Task<bool> ValidateStepAsync()
    {
        if (_state == null || Draft.Corpus == null)
            return Task.FromResult(false);
        if (!ValidateFields())
            return Task.FromResult(false);

        if (RequestsPlanning(true, datesOk: true))
            _state.PlanOnEntryOnce = true;
        return Task.FromResult(true);
    }

    // Shared by Next and Skip to Discovery Documents: flush pending text, then M-S1 (mix total) and M-S2 (dates) block
    private bool ValidateFields()
    {
        FlushCommits();

        if (Settings.MixTotal != 100)
        {
            var (title, text) = CorpusUiText.MS1MixTotal(Settings.MixTotal);
            MessageBox.Show(FindForm(), text, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        var badDate = InvalidDateField();
        if (badDate != null)
        {
            var (title, text) = CorpusUiText.MS2InvalidDate(badDate);
            MessageBox.Show(FindForm(), text, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        return true;
    }

    // Skip to Discovery Documents (09 W2, UI-SPEC S4): same validation as Next, but no planning request, so Review Plan
    // never plans on the way (PlanOnEntryOnce untouched). The wizard's leave path (OnLeaveStepAsync) saves the settings.
    private void SkipToDiscovery()
    {
        if (_state == null || Draft.Corpus == null || !ValidateFields())
            return;
        (FindForm() as WizardForm)?.NavigateToStep(typeof(StepDiscoveryDocuments));
    }

    /// <summary>
    /// Applies every field's pending text (Next, leave, and render tests).
    /// </summary>
    internal void FlushCommits()
    {
        if (_state == null || Draft.Corpus == null)
            return;

        CommitSize();
        CommitPercentField();
        CommitWindow();
        CommitMix();
        CommitOverrides();
        CommitPrefix();
    }

    // "start" / "end" for the first invalid date field, or null when the window is valid
    private string? InvalidDateField()
    {
        var startText = _txtStart.Text.Trim();
        var endText = _txtEnd.Text.Trim();
        if (startText.Length == 0 && endText.Length == 0 && CorpusSettings.FactRange(Draft.Guide) == null)
            return null;
        if (!CaseDraft.TryParseDate(startText, out var start))
            return "start";
        if (!CaseDraft.TryParseDate(endText, out var end) || end < start)
            return "end";
        return null;
    }

    // ----- Fill and refresh -----

    // 10 D-08: manual relabels of the current plan that a re-plan would drop (counted off the UI thread on entry)
    internal static int RelabelCount(CaseDraft draft)
    {
        var plan = draft.Corpus?.Plan;
        var folder = draft.FolderPath;
        if (plan == null || string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return 0;
        return AnswerKeyFiles.PeekRelabels(folder, plan, draft.Guide ?? new CaseGuide()).Count;
    }

    private string NoticeText() =>
        CorpusUiText.SettingsNotice(Draft.Corpus!.HasPlan, Draft.Corpus.Approved, Draft.Corpus.ApprovedUtc?.ToLocalTime(),
            relabels: _relabelCount);

    private void FillControls()
    {
        _loading = true;
        try
        {
            var settings = Settings;
            _sizeClampText = string.Empty;
            _percentClampText = string.Empty;

            _lblNotice.Text = NoticeText();
            _btnSkipToDiscovery.Visible = ShowsSkipToDiscovery(Draft.Corpus!.HasPlan, Draft.Corpus.Approved);
            SizeNoticeRow();

            _txtSize.Text = FormatCount(settings.TotalEmails);
            SelectToggle(MatchingPreset(settings.TotalEmails));
            _slider.Value = settings.RelevantPercent;
            _txtPercent.Text = settings.RelevantPercent.ToString(Invariant);

            _txtStart.Text = settings.WindowStart;
            _txtEnd.Text = settings.WindowEnd;
            TextFieldHost.SetInvalid(_hostStart, false);
            TextFieldHost.SetInvalid(_hostEnd, false);
            ShowLine(_lblDateLine, string.Empty, Theme.Warning);
            var range = CorpusSettings.FactRange(Draft.Guide);
            _lblWindowHint.Text = range == null ? string.Empty : CorpusUiText.WindowHint(range.Value.First, range.Value.Last);
            _lblWindowHint.Visible = range != null;

            // Effective media: the saved choice stays in settings and is re-applied when a tested key appears (D-11)
            var media = _state.IsMediaAvailable;
            _chkOffice.Checked = settings.IncludeOffice;
            _chkCalendar.Checked = settings.IncludeCalendar;
            _chkImages.Enabled = media;
            _chkVoicemail.Enabled = media;
            _chkImages.Checked = settings.IncludeImages && media;
            _chkVoicemail.Checked = settings.IncludeVoicemail && media;
            _lblMediaNotice.Visible = !media;

            FillMix();
            _txtHot.Text = settings.HotPerHotFact?.ToString(Invariant) ?? string.Empty;
            _txtPrivileged.Text = settings.PrivilegedPercent?.ToString(Invariant) ?? string.Empty;
            _txtAttachment.Text = settings.AttachmentPercent?.ToString(Invariant) ?? string.Empty;
            _txtPrefix.Text = settings.DocIdPrefix;
            _txtPrefix.PlaceholderText = DocIdPrefix.Derive(Draft);
            TextFieldHost.SetInvalid(_hostPrefix, false);
            foreach (var line in new[] { _lblHotLine, _lblPrivilegedLine, _lblAttachmentLine, _lblPrefixLine })
                ShowLine(line, string.Empty, Theme.Warning);

            var guide = Draft.Guide;
            var hotFacts = guide?.Facts.Count(f => f != null && f.Significance == FactSignificance.Hot) ?? 0;
            _lblHotHint.Text = CorpusUiText.HotHint(hotFacts);
            var counsel = guide?.Cast
                .Where(c => c != null && c.Role is CastRole.InHouseCounsel or CastRole.OutsideCounsel)
                .Select(c => c.FullName).ToList() ?? new List<string>();
            _lblPrivilegedHint.Text = CorpusUiText.PrivilegedHint(counsel);
            _txtPrivileged.Enabled = counsel.Count > 0;
            _lblAttachmentRateHint.Text = CorpusUiText.AttachmentHint();
        }
        finally
        {
            _loading = false;
        }

        RefreshCounts(Settings.RelevantPercent);
        RefreshAdvancedText();
    }

    private void FillMix()
    {
        _txtDaily.Text = Settings.DailyBusinessPercent.ToString(Invariant);
        _txtAncillary.Text = Settings.AncillaryPercent.ToString(Invariant);
        _txtNewsletter.Text = Settings.NewsletterPercent.ToString(Invariant);
        _txtDecoy.Text = Settings.DecoyPercent.ToString(Invariant);
    }

    // Live counts through the planner's own split (Pitfall 4); percent may be an uncommitted slider value
    private void RefreshCounts(int percent)
    {
        var total = Settings.TotalEmails;
        var relevant = CorpusSettings.RelevantCount(total, percent);
        _lblLiveCounts.Text = CorpusUiText.LiveCounts(relevant, total - relevant);

        if (_sizeClampText.Length > 0)
            ShowLine(_lblSizeHint, _sizeClampText, Theme.Warning);
        else
            ShowLine(_lblSizeHint, CorpusUiText.SizeHint(Draft.Complexity), Theme.TextMuted);

        if (_percentClampText.Length > 0)
        {
            ShowLine(_lblRelevantWarning, _percentClampText, Theme.Warning);
        }
        else
        {
            var warning = CorpusUiText.RelevantWarning(total, percent, DatedFactCount());
            ShowLine(_lblRelevantWarning, warning?.Text ?? string.Empty, warning?.IsWarning == true ? Theme.Warning : Theme.TextMuted);
        }

        _lblMixHint.Text = CorpusUiText.MixHint(total - relevant);
    }

    private int DatedFactCount() =>
        Draft.Guide?.Facts.Count(f => f != null && CaseDraft.TryParseDate(f.Date, out _)) ?? 0;

    private void RefreshAdvancedText()
    {
        var settings = Settings;
        var prefix = DocIdPrefix.Effective(settings, Draft);
        _lblAdvancedSummary.Text = CorpusUiText.AdvancedSummary(settings, prefix);
        var (text, ok) = CorpusUiText.MixTotalLine(settings.MixTotal);
        _lblMixTotal.Text = text;
        _lblMixTotal.ForeColor = ok ? Theme.Success : Theme.Danger;
        _lblPrefixHint.Text = CorpusUiText.PrefixHint(prefix);
    }

    private static void ShowLine(Label label, string text, Color color)
    {
        label.Text = text;
        label.ForeColor = color;
        label.Visible = text.Length > 0;
    }

    private void SelectToggle(int? preset)
    {
        foreach (var toggle in _sizeToggles)
            toggle.Selected = preset.HasValue && (int)toggle.Tag! == preset.Value;
    }

    private void SetAdvancedExpanded(bool expanded)
    {
        _advancedExpanded = expanded;
        _btnAdvanced.Text = expanded ? "Hide Advanced" : "Show Advanced";
        _advancedDividerRow.Height = expanded ? 1 : 0;
        _advancedDivider.Visible = expanded;
        // Header 14 / 10 inside the 1 px border; the card already supplies the corner inset at the top and, when collapsed,
        // at the bottom
        var inset = LogicalToDeviceUnits(15);
        var bottom = expanded ? LogicalToDeviceUnits(10) : Math.Max(0, LogicalToDeviceUnits(10) - _cornerInset + 1);
        _advancedHeader.Padding = new Padding(inset, Math.Max(0, LogicalToDeviceUnits(14) - _cornerInset + 1), inset, bottom);
        _advancedBody.Visible = expanded;
    }

    // ----- Commits -----

    private void SelectPreset(int preset)
    {
        if (_state == null || Draft.Corpus == null)
            return;

        _loading = true;
        _txtSize.Text = FormatCount(preset);
        _loading = false;
        SelectToggle(preset);
        _sizeClampText = string.Empty;
        var changed = Settings.TotalEmails != preset;
        Settings.TotalEmails = preset;
        AfterCommit(changed);
    }

    private void CommitSize()
    {
        if (_state == null || Draft.Corpus == null)
            return;

        var parsed = ParseCount(_txtSize.Text);
        var changed = false;
        if (parsed == null)
        {
            // Empty or not a number: restore the previous value
            _sizeClampText = string.Empty;
        }
        else
        {
            var (value, clamped) = ClampInt(parsed.Value, CorpusSettings.MinEmails, CorpusSettings.MaxEmails);
            _sizeClampText = clamped ? CorpusUiText.SizeClampNotice(value) : string.Empty;
            changed = Settings.TotalEmails != value;
            Settings.TotalEmails = value;
        }

        _loading = true;
        _txtSize.Text = FormatCount(Settings.TotalEmails);
        _loading = false;
        SelectToggle(MatchingPreset(Settings.TotalEmails));
        AfterCommit(changed);
    }

    private void CommitPercentField()
    {
        if (_state == null || Draft.Corpus == null)
            return;

        var parsed = ParseCount(_txtPercent.Text);
        if (parsed == null)
        {
            CommitPercent(Settings.RelevantPercent, clamped: false);
            return;
        }

        var (value, clamped) = ClampInt(parsed.Value, CorpusSettings.MinRelevantPercent, CorpusSettings.MaxRelevantPercent);
        CommitPercent(value, clamped);
    }

    private void CommitPercent(int value, bool clamped)
    {
        if (_state == null || Draft.Corpus == null)
            return;

        _percentClampText = clamped
            ? CorpusUiText.OverrideClampNotice(value, CorpusSettings.MinRelevantPercent, CorpusSettings.MaxRelevantPercent)
            : string.Empty;
        var changed = Settings.RelevantPercent != value;
        Settings.RelevantPercent = value;
        _loading = true;
        _slider.Value = value;
        _txtPercent.Text = value.ToString(Invariant);
        _loading = false;
        AfterCommit(changed);
    }

    private void CommitWindow()
    {
        if (_state == null || Draft.Corpus == null)
            return;

        var startText = _txtStart.Text.Trim();
        var endText = _txtEnd.Text.Trim();
        if (startText.Length == 0 && endText.Length == 0 && CorpusSettings.FactRange(Draft.Guide) == null)
        {
            TextFieldHost.SetInvalid(_hostStart, false);
            TextFieldHost.SetInvalid(_hostEnd, false);
            ShowLine(_lblDateLine, string.Empty, Theme.Warning);
            return;
        }

        var clamp = CorpusSettings.ClampWindow(startText, endText, Draft.Guide);
        TextFieldHost.SetInvalid(_hostStart, clamp.StartInvalid);
        TextFieldHost.SetInvalid(_hostEnd, clamp.EndInvalid);
        if (clamp.StartInvalid || clamp.EndInvalid)
        {
            // Not committed
            ShowLine(_lblDateLine, CorpusUiText.DateInvalidText, Theme.Danger);
            return;
        }

        CaseDraft.TryParseDate(clamp.Start, out var start);
        CaseDraft.TryParseDate(clamp.End, out var end);
        if (end < start)
        {
            TextFieldHost.SetInvalid(_hostEnd, true);
            ShowLine(_lblDateLine, CorpusUiText.EndBeforeStartText, Theme.Danger);
            return;
        }

        var notices = new List<string>();
        if (clamp.StartClamped)
            notices.Add(CorpusUiText.StartClampedText(start));
        if (clamp.EndClamped)
            notices.Add(CorpusUiText.EndClampedText(end));
        ShowLine(_lblDateLine, string.Join(" ", notices), Theme.Warning);

        _txtStart.Text = clamp.Start;
        _txtEnd.Text = clamp.End;
        var changed = Settings.WindowStart != clamp.Start || Settings.WindowEnd != clamp.End;
        Settings.WindowStart = clamp.Start;
        Settings.WindowEnd = clamp.End;
        AfterCommit(changed);
    }

    private void ResetWindow()
    {
        if (_state == null || Draft.Corpus == null)
            return;

        var (start, end) = CorpusSettings.DefaultWindow(Draft.Guide);
        _txtStart.Text = start;
        _txtEnd.Text = end;
        TextFieldHost.SetInvalid(_hostStart, false);
        TextFieldHost.SetInvalid(_hostEnd, false);
        ShowLine(_lblDateLine, string.Empty, Theme.Warning);
        var changed = Settings.WindowStart != start || Settings.WindowEnd != end;
        Settings.WindowStart = start;
        Settings.WindowEnd = end;
        AfterCommit(changed);
    }

    private void CommitMedia()
    {
        if (_loading || _state == null || Draft.Corpus == null)
            return;

        var settings = Settings;
        var before = (settings.IncludeOffice, settings.IncludeCalendar, settings.IncludeImages, settings.IncludeVoicemail);
        settings.IncludeOffice = _chkOffice.Checked;
        settings.IncludeCalendar = _chkCalendar.Checked;
        // Disabled boxes keep the saved choice (re-applied when a tested key appears)
        if (_chkImages.Enabled)
            settings.IncludeImages = _chkImages.Checked;
        if (_chkVoicemail.Enabled)
            settings.IncludeVoicemail = _chkVoicemail.Checked;
        var after = (settings.IncludeOffice, settings.IncludeCalendar, settings.IncludeImages, settings.IncludeVoicemail);
        AfterCommit(before != after);
    }

    private void CommitMix()
    {
        if (_state == null || Draft.Corpus == null)
            return;

        var settings = Settings;
        var before = settings.MixTotal + "|" + settings.DailyBusinessPercent + "|" + settings.AncillaryPercent + "|" + settings.NewsletterPercent;
        settings.DailyBusinessPercent = ShareFrom(_txtDaily, settings.DailyBusinessPercent);
        settings.AncillaryPercent = ShareFrom(_txtAncillary, settings.AncillaryPercent);
        settings.NewsletterPercent = ShareFrom(_txtNewsletter, settings.NewsletterPercent);
        settings.DecoyPercent = ShareFrom(_txtDecoy, settings.DecoyPercent);
        FillMix();
        var after = settings.MixTotal + "|" + settings.DailyBusinessPercent + "|" + settings.AncillaryPercent + "|" + settings.NewsletterPercent;
        AfterCommit(before != after);
    }

    // 0-100; empty or not a number restores the previous share
    private static int ShareFrom(TextBox box, int previous)
    {
        var parsed = ParseCount(box.Text);
        return parsed == null ? previous : ClampInt(parsed.Value, 0, 100).Value;
    }

    private void UseDefaultMix()
    {
        if (_state == null || Draft.Corpus == null)
            return;

        var settings = Settings;
        var changed = settings.MixTotal != 100 || settings.DailyBusinessPercent != CorpusSettings.DefaultMix[0]
            || settings.AncillaryPercent != CorpusSettings.DefaultMix[1] || settings.NewsletterPercent != CorpusSettings.DefaultMix[2];
        settings.DailyBusinessPercent = CorpusSettings.DefaultMix[0];
        settings.AncillaryPercent = CorpusSettings.DefaultMix[1];
        settings.NewsletterPercent = CorpusSettings.DefaultMix[2];
        settings.DecoyPercent = CorpusSettings.DefaultMix[3];
        FillMix();
        AfterCommit(changed);
    }

    private void CommitOverrides()
    {
        if (_state == null || Draft.Corpus == null)
            return;

        var settings = Settings;
        var changed = false;
        settings.HotPerHotFact = OverrideFrom(_txtHot, _lblHotLine, settings.HotPerHotFact, 1, CorpusSettings.MaxHotPerHotFact, ref changed);
        if (_txtPrivileged.Enabled)
            settings.PrivilegedPercent = OverrideFrom(_txtPrivileged, _lblPrivilegedLine, settings.PrivilegedPercent, 0, CorpusSettings.MaxPrivilegedPercent, ref changed);
        settings.AttachmentPercent = OverrideFrom(_txtAttachment, _lblAttachmentLine, settings.AttachmentPercent, 0, CorpusSettings.MaxAttachmentPercent, ref changed);
        AfterCommit(changed);
    }

    // Empty = automatic (null); a number is clamped with a Warning line; anything else restores the previous value
    private static int? OverrideFrom(TextBox box, Label line, int? previous, int min, int max, ref bool changed)
    {
        int? value;
        var text = box.Text.Trim();
        if (text.Length == 0)
        {
            value = null;
            ShowLine(line, string.Empty, Theme.Warning);
        }
        else if (ParseCount(text) is int parsed)
        {
            var (clampedValue, clamped) = ClampInt(parsed, min, max);
            value = clampedValue;
            ShowLine(line, clamped ? CorpusUiText.OverrideClampNotice(clampedValue, min, max) : string.Empty, Theme.Warning);
        }
        else
        {
            value = previous;
            ShowLine(line, string.Empty, Theme.Warning);
        }

        box.Text = value?.ToString(Invariant) ?? string.Empty;
        if (value != previous)
            changed = true;
        return value;
    }

    private void CommitPrefix()
    {
        if (_state == null || Draft.Corpus == null)
            return;

        var prefix = SanitizePrefix(_txtPrefix.Text);
        if (prefix.Length == 1)
        {
            // Not committed (T-04-53)
            TextFieldHost.SetInvalid(_hostPrefix, true);
            ShowLine(_lblPrefixLine, CorpusUiText.PrefixInvalidText, Theme.Danger);
            return;
        }

        TextFieldHost.SetInvalid(_hostPrefix, false);
        ShowLine(_lblPrefixLine, string.Empty, Theme.Danger);
        _txtPrefix.Text = prefix;
        var changed = Settings.DocIdPrefix != prefix;
        Settings.DocIdPrefix = prefix;
        AfterCommit(changed);
    }

    private void ResetDefaults()
    {
        if (_state == null || Draft.Corpus == null)
            return;

        var (title, text) = CorpusUiText.CS1ResetDefaults(Draft.Complexity);
        var answer = MessageBox.Show(FindForm(), text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes || IsDisposed)
            return;

        Draft.Corpus.Settings = CorpusSettings.CreateDefault(Draft);
        FillControls();
        SetAdvancedExpanded(false);
        AfterCommit(true);
    }

    // Every commit refreshes the dependent text and autosaves when something changed (D-04)
    private void AfterCommit(bool changed)
    {
        RefreshCounts(Settings.RelevantPercent);
        RefreshAdvancedText();
        if (!changed)
            return;

        Draft.Revision++;
        QueueSave();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    // ----- Autosave -----

    // Saves in the background; the store coalesces queued snapshots (latest wins)
    private void QueueSave()
    {
        _ = SaveAsync();
    }

    /// <summary>
    /// Writes case.json and shows the outcome: the subtitle's save time, or "Not saved: ..." in the status line.
    /// Saving is core work, so a failure is always surfaced and never swallowed.
    /// </summary>
    private async Task SaveAsync()
    {
        if (_state == null || !CaseStore.CanSave(Draft))
            return;

        var ok = await _state.CaseStore.SaveAsync(Draft);
        if (IsDisposed)
            return;

        RefreshHeader();
        if (!ok)
            SetStatus(CaseGuideUiText.SaveFailedText(_state.CaseStore.LastSaveError), Theme.Danger);
    }

    private void RefreshHeader()
    {
        var store = _state.CaseStore;
        var saveFailed = !string.IsNullOrEmpty(store.LastSaveError);
        _lblSubtitle.Text = CaseGuideUiText.SubtitleText(Draft.FolderPath, store.LastSavedUtc?.ToLocalTime(), saveFailed, MyDocuments);
        _lblSubtitle.ForeColor = saveFailed ? Theme.Danger : Theme.TextMuted;
    }

    private void SetStatus(string text, Color color)
    {
        _lblStatus.Text = text;
        _lblStatus.ForeColor = color;
    }

    // ----- Render-test access -----

    internal CaseDraft DraftForTest => Draft;
    internal Panel ScrollHost => _scrollHost;
    internal Button SkipToDiscoveryButton => _btnSkipToDiscovery;
    internal Button ResetDefaultsButton => _btnResetDefaults;
    internal Label NoticeLabel => _lblNotice;
    internal Label TitleLabel => _lblTitle;
    internal CardPanel SizeCard => _cardSize;
    internal CardPanel DatesCard => _cardDates;
    internal PercentSlider Slider => _slider;
    internal Label PercentSuffix => _lblPercentSuffix;
    internal TextBox PercentField => _txtPercent;
    internal TextBox StartField => _txtStart;
    internal TextBox HotField => _txtHot;
    internal TextBox DailyField => _txtDaily;
    internal CheckBox ImagesBox => _chkImages;
    internal CheckBox VoicemailBox => _chkVoicemail;
    internal IReadOnlyList<ThemedButton> SizeToggles => new[] { _btnSize250, _btnSize1000, _btnSize2500, _btnSize5000 };
}
