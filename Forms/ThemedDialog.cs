using ReelDiscovery.Helpers;

namespace ReelDiscovery.Forms;

/// <summary>
/// Shared chrome for the Case Guide dialogs (03-UI-SPEC Dialog Contracts): Theme background, 24 px padding,
/// 520 logical width, content-sized height, headline / body / content rows and a right-aligned button footer
/// whose Cancel button is the safe choice with initial focus.
/// </summary>
public class ThemedDialog : Form
{
    private const int DialogWidth = 520;
    private const int DialogPadding = 24;

    private readonly TableLayoutPanel _root;
    private FlowLayoutPanel? _footer;

    protected ThemedDialog(string title)
    {
        // Scale the empty form once (same settings as WizardForm) before any child exists, so the
        // LogicalToDeviceUnits sizes given to children below are never scaled a second time
        this.SuspendLayout();
        this.AutoScaleDimensions = new SizeF(7F, 15F);
        this.AutoScaleMode = AutoScaleMode.Font;
        this.Font = Theme.BodyFont();
        this.ResumeLayout(false);
        this.PerformAutoScale();

        this.Text = title;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.StartPosition = FormStartPosition.CenterParent;
        this.MinimizeBox = false;
        this.MaximizeBox = false;
        this.ShowInTaskbar = false;
        this.BackColor = Theme.Background;
        this.Padding = new Padding(LDU(DialogPadding));

        // Rows are added in call order, all AutoSize:
        // 0: headline, 1: body, 2..n: optional content, last: footer
        _root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 0,
            BackColor = Theme.Background,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        this.Controls.Add(_root);
        this.ClientSize = new Size(LDU(DialogWidth), LDU(DialogPadding * 2));
    }

    /// <summary>
    /// Width available to content inside the dialog padding (device pixels); wrapping labels use it as MaximumSize.
    /// </summary>
    protected int ContentWidth => LDU(DialogWidth - DialogPadding * 2);

    protected int LDU(int logical) => LogicalToDeviceUnits(logical);

    protected Label AddHeadline(string text)
    {
        var label = CreateWrappingLabel(text, Theme.HeadingFont(), Theme.Text);
        label.Margin = new Padding(0, 0, 0, LDU(8));
        AddContent(label);
        return label;
    }

    protected Label AddBody(string text)
    {
        var label = CreateWrappingLabel(text, Theme.BodyFont(), Theme.Text);
        label.Margin = new Padding(0, 0, 0, LDU(16));
        AddContent(label);
        return label;
    }

    /// <summary>
    /// Wrapping label at the content width; data text never turns '&amp;' into a mnemonic.
    /// </summary>
    protected Label CreateWrappingLabel(string text, Font font, Color color)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),
            Font = font,
            ForeColor = color,
            BackColor = Theme.Background,
            UseMnemonic = false,
            Padding = Padding.Empty
        };
    }

    /// <summary>
    /// A white card listing items: each a Body line in Text, with an optional Small second line in TextMuted under it,
    /// and an optional Small footer line. The card is sized to its wrapped text (no scrolling).
    /// </summary>
    protected CardPanel AddItemsCard(IReadOnlyList<(string Text, string Detail)> items, string footer = "")
    {
        var padX = LDU(ItemsCardPaddingX);
        var padY = LDU(ItemsCardPaddingY);
        var card = new CardPanel
        {
            BorderColor = Theme.Border,
            BackColor = Theme.Surface,
            Dock = DockStyle.Fill,
            Padding = new Padding(padX, padY, padX, padY),
            Margin = new Padding(0, 0, 0, LDU(16))
        };

        var list = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            BackColor = Theme.Surface,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // 1 px border on each side
        var textWidth = ContentWidth - padX * 2 - 2;
        void AddLine(string text, Font font, Color color, int top)
        {
            var label = new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(textWidth, 0),
                Font = font,
                ForeColor = color,
                BackColor = Theme.Surface,
                UseMnemonic = false,
                Margin = new Padding(0, top, 0, 0),
                Padding = Padding.Empty
            };
            list.RowCount++;
            list.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // one row per line
            list.Controls.Add(label, 0, list.RowCount - 1);
        }

        for (var i = 0; i < items.Count; i++)
        {
            AddLine(items[i].Text, Theme.BodyFont(), Theme.Text, i == 0 ? 0 : LDU(ItemGap));
            if (!string.IsNullOrEmpty(items[i].Detail))
                AddLine(items[i].Detail, Theme.SmallFont(), Theme.TextMuted, LDU(2));
        }
        if (!string.IsNullOrEmpty(footer))
            AddLine(footer, Theme.SmallFont(), Theme.TextMuted, items.Count == 0 ? 0 : LDU(ItemGap));

        card.Controls.Add(list);
        card.Height = list.GetPreferredSize(new Size(textWidth, 0)).Height + card.Padding.Vertical + 2;
        AddContent(card);
        return card;
    }

    // Same text inset as the D2 fact list (10)
    private const int ItemsCardPaddingX = 10;
    private const int ItemsCardPaddingY = 10;
    private const int ItemGap = 10;

    protected void AddContent(Control control)
    {
        var row = _root.RowCount;
        _root.RowCount = row + 1;
        _root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // content row (see row map in the constructor)
        _root.Controls.Add(control, 0, row);
    }

    /// <summary>
    /// Adds the footer. Buttons read left to right as given (cancel first); cancel is the CancelButton and has focus.
    /// </summary>
    protected void AddButtons(Button cancel, params Button[] others)
    {
        _footer = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            MaximumSize = new Size(ContentWidth, 0),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = Theme.Background,
            Margin = new Padding(0, LDU(24), 0, 0),
            Padding = Padding.Empty
        };

        // RightToLeft flow: add the rightmost button first so the row reads cancel, others... left to right
        var ordered = new List<Button> { cancel };
        ordered.AddRange(others);
        for (var i = ordered.Count - 1; i >= 0; i--)
        {
            ordered[i].Margin = i == 0 ? Padding.Empty : new Padding(LDU(Theme.SmallGap), 0, 0, 0);
            _footer.Controls.Add(ordered[i]);
        }

        AddContent(_footer);
        this.CancelButton = cancel;
        this.ActiveControl = cancel;
    }

    /// <summary>
    /// Sizes the dialog to its content: 520 logical wide, height from the root layout's preferred size.
    /// Safe to call again after content heights change (e.g. once list item heights are known).
    /// </summary>
    protected void FinishLayout()
    {
        // The footer's own top margin (24) is the only gap above it
        if (_footer != null)
        {
            var footerRow = _root.GetRow(_footer);
            var previous = footerRow > 0 ? _root.GetControlFromPosition(0, footerRow - 1) : null;
            if (previous != null)
                previous.Margin = new Padding(previous.Margin.Left, previous.Margin.Top, previous.Margin.Right, 0);
        }

        _root.Width = ContentWidth;
        _root.PerformLayout();
        var height = _root.GetPreferredSize(new Size(ContentWidth, 0)).Height;
        this.ClientSize = new Size(LDU(DialogWidth), height + this.Padding.Vertical);
        this.PerformLayout();
    }
}
