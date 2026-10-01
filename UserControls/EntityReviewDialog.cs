using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Case;

namespace ReelDiscovery.UserControls;

/// <summary>
/// Real-names review modal (UI-SPEC S2). Lists detected real entities with a fictional
/// substitute applied by default; unchecking a row keeps the real name after a confirm.
/// </summary>
public class EntityReviewDialog : Form
{
    private static readonly Color BannerTextColor = Color.FromArgb(133, 100, 4);

    private DataGridView _gridEntities = null!;
    private Button _btnApply = null!;
    private Button _btnBack = null!;

    public List<EntitySubstitution> Entities { get; private set; }

    public EntityReviewDialog(List<EntitySubstitution> entities)
    {
        // Clone so the caller's list changes only on Apply
        Entities = entities.Select(e => new EntitySubstitution
        {
            Original = e.Original,
            Aliases = new List<string>(e.Aliases),
            Kind = e.Kind,
            Reason = e.Reason,
            Substitute = e.Substitute,
            Apply = e.Apply
        }).ToList();

        InitializeUI();
    }

    private void InitializeUI()
    {
        this.Text = "Real Names Found";
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.StartPosition = FormStartPosition.CenterParent;
        this.ClientSize = new Size(720, 440);
        this.MinimizeBox = false;
        this.MaximizeBox = false;
        this.ShowInTaskbar = false;

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8)
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));  // 0: Warning banner
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // 1: Entity grid
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));  // 2: Buttons

        // Warning banner
        var banner = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(255, 243, 205),
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(16, 8, 16, 8)
        };
        var lblBody = new Label
        {
            Text = "Fictional substitutes will be used in every document and email. Uncheck a row to keep the real name.",
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 20,
            Font = new Font("Segoe UI", 9F),
            ForeColor = BannerTextColor
        };
        var lblHeadline = new Label
        {
            Text = $"Your scenario names {Entities.Count} real people or companies.",
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 24,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = BannerTextColor
        };
        // Dock.Top stacks in reverse add order: add body first so the headline sits above it
        banner.Controls.Add(lblBody);
        banner.Controls.Add(lblHeadline);
        mainLayout.Controls.Add(banner, 0, 0);

        // Entity grid
        _gridEntities = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            BackgroundColor = SystemColors.Window
        };
        _gridEntities.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "colApply",
            HeaderText = "Use fictional name",
            Width = 128
        });
        _gridEntities.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colName",
            HeaderText = "Name found",
            Width = 180,
            ReadOnly = true
        });
        _gridEntities.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colType",
            HeaderText = "Type",
            Width = 136,
            ReadOnly = true
        });
        _gridEntities.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colSubstitute",
            HeaderText = "Substitute",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });

        foreach (var entity in Entities)
        {
            var index = _gridEntities.Rows.Add(entity.Apply, NameWithAliases(entity), KindLabel(entity.Kind), entity.Substitute);
            var row = _gridEntities.Rows[index];
            row.Tag = entity;
            foreach (DataGridViewCell cell in row.Cells)
            {
                cell.ToolTipText = entity.Reason;
            }
            ApplyRowState(row, entity.Apply);
        }

        _gridEntities.CurrentCellDirtyStateChanged += GridEntities_CurrentCellDirtyStateChanged;
        _gridEntities.CellValueChanged += GridEntities_CellValueChanged;
        _gridEntities.CellPainting += GridEntities_CellPainting;
        mainLayout.Controls.Add(_gridEntities, 0, 1);

        // Footer buttons (right-aligned)
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };
        _btnBack = ButtonHelper.CreateButton("Back to Scenario", 140, 32, ButtonStyle.Default);
        _btnBack.DialogResult = DialogResult.Cancel;
        _btnApply = ButtonHelper.CreateButton("Apply Substitutes", 140, 32, ButtonStyle.Primary);
        _btnApply.Margin = new Padding(0, 0, 8, 0);
        _btnApply.Click += BtnApply_Click;
        buttonPanel.Controls.Add(_btnBack);
        buttonPanel.Controls.Add(_btnApply);
        mainLayout.Controls.Add(buttonPanel, 0, 2);

        this.AcceptButton = _btnApply;
        this.CancelButton = _btnBack;
        this.Controls.Add(mainLayout);
    }

    private void GridEntities_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
    {
        // Commit checkbox toggles immediately so CellValueChanged fires on click
        if (_gridEntities.IsCurrentCellDirty && _gridEntities.CurrentCell is DataGridViewCheckBoxCell)
        {
            _gridEntities.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }
    }

    private void GridEntities_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _gridEntities.Columns[e.ColumnIndex].Name != "colApply")
        {
            return;
        }

        var row = _gridEntities.Rows[e.RowIndex];
        ApplyRowState(row, row.Cells["colApply"].Value is true);
    }

    private static void ApplyRowState(DataGridViewRow row, bool apply)
    {
        var substituteCell = row.Cells["colSubstitute"];
        substituteCell.ReadOnly = !apply;
        substituteCell.Style.ForeColor = apply ? SystemColors.ControlText : Color.DimGray;
    }

    private void GridEntities_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Graphics == null
            || _gridEntities.Columns[e.ColumnIndex].Name != "colName"
            || _gridEntities.Rows[e.RowIndex].Tag is not EntitySubstitution entity)
        {
            return;
        }

        // Paint background/border, then draw the name in the default color and aliases in DimGray
        e.Paint(e.CellBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.ContentForeground);

        var selected = (e.State & DataGridViewElementStates.Selected) != 0;
        var nameColor = selected ? e.CellStyle!.SelectionForeColor : e.CellStyle!.ForeColor;
        var font = e.CellStyle.Font ?? _gridEntities.Font;
        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter
            | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;

        var bounds = new Rectangle(e.CellBounds.X + 4, e.CellBounds.Y, e.CellBounds.Width - 8, e.CellBounds.Height);
        TextRenderer.DrawText(e.Graphics, entity.Original, font, bounds, nameColor, flags);

        if (entity.Aliases.Count > 0)
        {
            var nameWidth = TextRenderer.MeasureText(e.Graphics, entity.Original, font, bounds.Size, flags).Width;
            var aliasBounds = new Rectangle(bounds.X + nameWidth, bounds.Y, Math.Max(0, bounds.Width - nameWidth), bounds.Height);
            var aliasColor = selected ? nameColor : Color.DimGray;
            TextRenderer.DrawText(e.Graphics, $" ({string.Join(", ", entity.Aliases)})", font, aliasBounds, aliasColor, flags);
        }

        e.Handled = true;
    }

    private void BtnApply_Click(object? sender, EventArgs e)
    {
        _gridEntities.EndEdit();

        foreach (DataGridViewRow row in _gridEntities.Rows)
        {
            if (row.Tag is not EntitySubstitution entity)
            {
                continue;
            }
            entity.Apply = row.Cells["colApply"].Value is true;
            entity.Substitute = (row.Cells["colSubstitute"].Value?.ToString() ?? string.Empty).Trim();
        }

        var blank = FindBlankSubstitute(Entities);
        if (blank != null)
        {
            MessageBox.Show($"Enter a fictional name for {blank.Original}, or uncheck the row to keep the real name.",
                "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var kept = Entities.Count(x => !x.Apply);
        if (kept > 0)
        {
            var confirm = MessageBox.Show(
                $"{kept} real names will appear in generated documents and emails. Use this only for data that will never be shared. Keep them?",
                "Keep Real Names?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes)
            {
                return;
            }
        }

        this.DialogResult = DialogResult.OK;
        Close();
    }

    public static string KindLabel(EntityKind kind) => kind switch
    {
        EntityKind.Person => "Person",
        EntityKind.Company => "Company",
        EntityKind.Brand => "Brand",
        EntityKind.FictionalWorkCharacter => "Fictional character",
        EntityKind.FictionalWorkOrganization => "Fictional organization",
        _ => kind.ToString()
    };

    public static string NameWithAliases(EntitySubstitution e) =>
        e.Aliases.Count > 0 ? $"{e.Original} ({string.Join(", ", e.Aliases)})" : e.Original;

    /// <summary>
    /// First entity that will be substituted but has no substitute name, or null when all are valid.
    /// </summary>
    public static EntitySubstitution? FindBlankSubstitute(IEnumerable<EntitySubstitution> entities) =>
        entities.FirstOrDefault(x => x.Apply && string.IsNullOrWhiteSpace(x.Substitute));
}
