using ReelDiscovery.Helpers;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Forms;

/// <summary>
/// Blind review setup (07-UI-SPEC S4, D-11, D-12): reviewer model and provider, the same-provider fallback sentence,
/// the sample size and a live cost estimate. Start Review returns the size; the review itself runs on Case Summary.
/// </summary>
public class BlindReviewDialog : ThemedDialog
{
    private readonly string _model;
    private readonly string _provider;
    private readonly bool _fallback;
    private readonly int _total;
    private readonly int _relevant;
    private readonly int _defaultSize;
    private readonly int _alreadyReviewed;
    private readonly Func<int, decimal> _estimateFor;

    private NumericUpDown _numSample = null!;
    private Label _lblEstimate = null!;
    private Label? _lblFallback;
    private Button _btnCancel = null!;
    private Button _btnStart = null!;

    public BlindReviewDialog(string model, string provider, bool sameProviderFallback, int totalEmails, int relevantEmails,
        int defaultSize, int alreadyReviewed, Func<int, decimal> estimateFor)
        : base(QaUiText.BlindReviewTitle)
    {
        ArgumentNullException.ThrowIfNull(estimateFor);
        _model = model ?? string.Empty;
        _provider = provider ?? string.Empty;
        _fallback = sameProviderFallback;
        _total = Math.Max(1, totalEmails);
        _relevant = Math.Max(0, relevantEmails);
        _defaultSize = Math.Clamp(defaultSize, 1, _total);
        _alreadyReviewed = Math.Max(0, alreadyReviewed);
        _estimateFor = estimateFor;
        InitializeUI();
    }

    /// <summary>
    /// Emails to review (the first N of the deterministic stratified sample order).
    /// </summary>
    public int SampleSize { get; private set; }

    internal NumericUpDown SampleInput => _numSample;
    internal Label EstimateLabel => _lblEstimate;
    internal Label? FallbackLabel => _lblFallback;
    internal Button StartButton => _btnStart;

    private void InitializeUI()
    {
        // 0: headline, 1: body, 1a: fallback (D-12 only), 2: sample row, 3: default rule, 4: estimate, 5: buttons
        AddHeadline(QaUiText.BlindReviewHeadline);
        AddBody(QaUiText.BlindReviewBody(_model, _provider));

        if (_fallback)
        {
            _lblFallback = CreateWrappingLabel(QaUiText.FallbackSentence, Theme.BodyFont(), Theme.Warning);
            _lblFallback.Margin = new Padding(0, 0, 0, LDU(16));
            AddContent(_lblFallback);
        }

        var sampleRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            // ContentWidth is already in device units (LDU)
            MaximumSize = new(ContentWidth, 0),
            BackColor = Theme.Background,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        sampleRow.Controls.Add(CreateRowLabel(QaUiText.SampleLabel));
        _numSample = new NumericUpDown
        {
            Minimum = 1,
            Maximum = _total,
            Increment = 50,
            DecimalPlaces = 0,
            ThousandsSeparator = true,
            Value = _defaultSize,
            Width = LDU(96),
            Font = Theme.BodyFont(),
            TextAlign = HorizontalAlignment.Right,
            Margin = new Padding(0, 0, LDU(Theme.SmallGap), 0)
        };
        sampleRow.Controls.Add(_numSample);
        sampleRow.Controls.Add(CreateRowLabel(QaUiText.SampleOfTotal(_total)));
        AddContent(sampleRow);

        var defaultRule = CreateWrappingLabel(QaUiText.DefaultRule(_relevant), Theme.SmallFont(), Theme.TextMuted);
        defaultRule.Margin = new Padding(0, LDU(4), 0, LDU(8));
        AddContent(defaultRule);

        _lblEstimate = CreateWrappingLabel(string.Empty, Theme.SmallFont(), Theme.TextMuted);
        _lblEstimate.Margin = new Padding(0, 0, 0, LDU(16));
        AddContent(_lblEstimate);

        _btnCancel = ButtonHelper.CreateButton("Cancel", 0, LDU(34), ButtonStyle.Default);
        _btnCancel.DialogResult = DialogResult.Cancel;
        _btnStart = ButtonHelper.CreateButton(QaUiText.StartReviewText, 0, LDU(34), ButtonStyle.Primary);
        _btnStart.DialogResult = DialogResult.OK;
        AddButtons(_btnCancel, _btnStart);

        // Handlers wired after every control exists
        _numSample.ValueChanged += (s, e) => UpdateEstimate();
        UpdateEstimate();
        FinishLayout();
    }

    private Label CreateRowLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = Theme.BodyFont(),
        ForeColor = Theme.Text,
        BackColor = Theme.Background,
        UseMnemonic = false,
        // Label-to-field gap 8; top inset centers the text on the NumericUpDown line
        Margin = new Padding(0, LDU(3), LDU(Theme.SmallGap), 0)
    };

    private void UpdateEstimate()
    {
        SampleSize = (int)_numSample.Value;
        var remaining = Math.Max(0, SampleSize - _alreadyReviewed);
        var text = QaUiText.Estimate(_estimateFor(SampleSize), remaining, _alreadyReviewed);
        if (_lblEstimate.Text == text)
            return;

        _lblEstimate.Text = text;
        // The resume clause can change the line count; resize once the dialog is built
        if (IsHandleCreated)
            FinishLayout();
    }
}
