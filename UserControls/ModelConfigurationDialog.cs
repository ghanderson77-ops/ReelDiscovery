using System.Globalization;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.UserControls;

public class ModelConfigurationDialog : Form
{
    private DataGridView _gridModels = null!;
    private Button _btnAdd = null!;
    private Button _btnRemove = null!;
    private Button _btnOk = null!;
    private Button _btnCancel = null!;
    private Button _btnResetDefaults = null!;
    private BindingSource _bindingSource = null!;
    private NumericUpDown _numImagePrice = null!;
    private NumericUpDown _numTtsPrice = null!;

    public List<AIModelConfig> Models { get; private set; }

    /// <summary>Media per-unit prices (D-29); a copy of the prices passed in, normalized on OK.</summary>
    public MediaPrices MediaPrices { get; private set; }

    // Window sizes in logical (96 DPI) units; scaled with LogicalToDeviceUnits and clamped to the working area
    internal static readonly Size LogicalSize = new(1180, 480);
    internal static readonly Size LogicalMinimumSize = new(820, 360);

    public ModelConfigurationDialog(List<AIModelConfig> models)
        : this(models, MediaPrices.Defaults())
    {
    }

    public ModelConfigurationDialog(List<AIModelConfig> models, MediaPrices prices)
    {
        ArgumentNullException.ThrowIfNull(prices);

        // Edit a copy so nothing changes before OK (review 06-24)
        MediaPrices = prices.Clone();
        MediaPrices.Normalize();

        // Clone the models to avoid modifying the original until OK is clicked
        Models = models.Select(m => new AIModelConfig
        {
            ModelId = m.ModelId,
            DisplayName = m.DisplayName,
            Provider = m.Provider,
            DefaultForTier = m.DefaultForTier,
            Effort = m.Effort,
            MaxOutputTokens = m.MaxOutputTokens,
            InputTokenPricePerMillion = m.InputTokenPricePerMillion,
            OutputTokenPricePerMillion = m.OutputTokenPricePerMillion,
            // Carry cache/batch prices through the copy so OK never zeroes them (07-01)
            CacheWriteTokenPricePerMillion = m.CacheWriteTokenPricePerMillion,
            CacheReadTokenPricePerMillion = m.CacheReadTokenPricePerMillion,
            BatchDiscountPercent = m.BatchDiscountPercent,
            MinCacheablePrefixTokens = m.MinCacheablePrefixTokens
        }).ToList();

        InitializeUI();
    }

    private void InitializeUI()
    {
        this.Text = "Configure AI Models";
        // Logical sizes scaled to the current DPI: fixed pixels left the grid row ~73 px tall at 144 DPI,
        // so the Add/Remove/Reset buttons and the grid rows were clipped (DPI rule 25)
        this.Size = new Size(LogicalToDeviceUnits(LogicalSize.Width), LogicalToDeviceUnits(LogicalSize.Height));
        this.MinimumSize = new Size(LogicalToDeviceUnits(LogicalMinimumSize.Width),
            LogicalToDeviceUnits(LogicalMinimumSize.Height));
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.Sizable;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.ShowInTaskbar = false;

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(10)
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        // Header and buttons size to their content so neither clips at 144 DPI (DPI rule 25)
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 0: Header
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // 1: Grid
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 2: Media prices
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 3: Buttons

        // Header label
        var lblHeader = new Label
        {
            Text = "Configure models per provider: one Story and one Volume default each, optional Effort, output ceiling and pricing (per million tokens). Images and voicemail are priced per unit below. Cache write and cache read apply when the shared prompt is cached; the batch discount applies when batch pricing is on.",
            // AutoSize inside the AutoSize row wraps to the column width instead of clipping
            AutoSize = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(6))
        };
        mainLayout.Controls.Add(lblHeader, 0, 0);

        // Grid panel
        var gridPanel = new Panel { Dock = DockStyle.Fill };

        _bindingSource = new BindingSource { DataSource = Models };

        _gridModels = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            BackgroundColor = SystemColors.Window,
            DataSource = _bindingSource,
            // 07-UI-SPEC S5: long headers (Cache Write $/M, Default For Tier, Batch Discount %) wrap to two lines
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
        };
        _gridModels.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;

        _gridModels.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "Provider",
            HeaderText = "Provider",
            DataPropertyName = "Provider",
            DataSource = Enum.GetValues<LlmProviderKind>(),
            ValueType = typeof(LlmProviderKind),
            Width = LogicalToDeviceUnits(90),
            // Provider and Model ID stay in view when the grid scrolls sideways at the minimum size
            Frozen = true
        });

        _gridModels.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ModelId",
            HeaderText = "Model ID",
            DataPropertyName = "ModelId",
            Width = LogicalToDeviceUnits(150),
            Frozen = true
        });

        _gridModels.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "DisplayName",
            HeaderText = "Display Name",
            DataPropertyName = "DisplayName",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = LogicalToDeviceUnits(100)
        });

        _gridModels.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "InputPrice",
            HeaderText = "Input $/M",
            DataPropertyName = "InputTokenPricePerMillion",
            Width = LogicalToDeviceUnits(72),
            DefaultCellStyle = new DataGridViewCellStyle { Format = "F2" }
        });

        _gridModels.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "OutputPrice",
            HeaderText = "Output $/M",
            DataPropertyName = "OutputTokenPricePerMillion",
            Width = LogicalToDeviceUnits(72),
            DefaultCellStyle = new DataGridViewCellStyle { Format = "F2" }
        });

        // Cache and batch prices (07-UI-SPEC S5, D-18); bounds enforced in CellValidating
        _gridModels.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "CacheWritePrice",
            HeaderText = "Cache Write $/M",
            DataPropertyName = nameof(AIModelConfig.CacheWriteTokenPricePerMillion),
            Width = LogicalToDeviceUnits(84),
            DefaultCellStyle = new DataGridViewCellStyle { Format = "0.00#" }
        });

        _gridModels.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "CacheReadPrice",
            HeaderText = "Cache Read $/M",
            DataPropertyName = nameof(AIModelConfig.CacheReadTokenPricePerMillion),
            Width = LogicalToDeviceUnits(84),
            DefaultCellStyle = new DataGridViewCellStyle { Format = "0.00#" }
        });

        _gridModels.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "BatchDiscount",
            HeaderText = "Batch Discount %",
            DataPropertyName = nameof(AIModelConfig.BatchDiscountPercent),
            Width = LogicalToDeviceUnits(72),
            DefaultCellStyle = new DataGridViewCellStyle { Format = "0" }
        });

        var tierColumn = new DataGridViewComboBoxColumn
        {
            Name = "DefaultForTier",
            HeaderText = "Default For Tier",
            DataPropertyName = "DefaultForTierText",
            Width = LogicalToDeviceUnits(100)
        };
        tierColumn.Items.AddRange("None", "Story", "Volume");
        _gridModels.Columns.Add(tierColumn);

        _gridModels.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Effort",
            HeaderText = "Effort",
            DataPropertyName = "EffortText",
            Width = LogicalToDeviceUnits(64)
        });

        _gridModels.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "MaxOutputTokens",
            HeaderText = "Max Output",
            DataPropertyName = "MaxOutputTokens",
            Width = LogicalToDeviceUnits(80),
            ValueType = typeof(int)
        });

        _gridModels.DataError += GridModels_DataError;
        _gridModels.CellParsing += GridModels_CellParsing;
        _gridModels.CurrentCellDirtyStateChanged += GridModels_CurrentCellDirtyStateChanged;

        // Button panel on the right side of grid
        var gridButtonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            // Sized to the DPI-scaled buttons rather than a fixed 110 px
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(LogicalToDeviceUnits(5), 0, 0, 0)
        };

        _btnAdd = ButtonHelper.CreateButton("Add Model", 100, 30, ButtonStyle.Primary);
        _btnAdd.Click += BtnAdd_Click;
        gridButtonPanel.Controls.Add(_btnAdd);

        _btnRemove = ButtonHelper.CreateButton("Remove", 100, 30, ButtonStyle.Danger);
        _btnRemove.Click += BtnRemove_Click;
        gridButtonPanel.Controls.Add(_btnRemove);

        _btnResetDefaults = ButtonHelper.CreateButton("Reset Defaults", 100, 30, ButtonStyle.Secondary);
        _btnResetDefaults.Margin = new Padding(0, LogicalToDeviceUnits(20), 0, 0);
        _btnResetDefaults.Click += BtnResetDefaults_Click;
        gridButtonPanel.Controls.Add(_btnResetDefaults);

        gridPanel.Controls.Add(_gridModels);
        gridPanel.Controls.Add(gridButtonPanel);
        mainLayout.Controls.Add(gridPanel, 0, 1);

        // Media prices row (06-UI-SPEC S4): top margin only, so its left edge lines up with the grid; may wrap
        var mediaRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            // Same left cell margin as the grid panel, so both left edges line up
            Margin = new Padding(gridPanel.Margin.Left, LogicalToDeviceUnits(16), gridPanel.Margin.Right, 0),
            Padding = Padding.Empty
        };

        _numImagePrice = new NumericUpDown
        {
            DecimalPlaces = 3,
            Minimum = 0,
            Maximum = 10,
            Increment = 0.001m,
            Width = LogicalToDeviceUnits(80),
            Margin = new Padding(0, 0, LogicalToDeviceUnits(16), 0)
        };
        _numTtsPrice = new NumericUpDown
        {
            DecimalPlaces = 2,
            Minimum = 0,
            Maximum = 1000,
            Increment = 0.5m,
            Width = LogicalToDeviceUnits(80),
            Margin = Padding.Empty
        };
        SetPriceInputs(MediaPrices);

        mediaRow.Controls.Add(CreatePriceLabel("Image ($ per image):"));
        mediaRow.Controls.Add(_numImagePrice);
        mediaRow.Controls.Add(CreatePriceLabel("Voicemail audio ($ per 1M characters):"));
        mediaRow.Controls.Add(_numTtsPrice);
        mainLayout.Controls.Add(mediaRow, 0, 2);

        // Bottom button panel
        var bottomPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, LogicalToDeviceUnits(5), 0, 0)
        };

        _btnCancel = ButtonHelper.CreateButton("Cancel", 80, 30, ButtonStyle.Default);
        _btnCancel.DialogResult = DialogResult.Cancel;
        bottomPanel.Controls.Add(_btnCancel);

        _btnOk = ButtonHelper.CreateButton("OK", 80, 30, ButtonStyle.Primary);
        _btnOk.Margin = new Padding(0, 0, LogicalToDeviceUnits(10), 0);
        _btnOk.Click += BtnOk_Click;
        bottomPanel.Controls.Add(_btnOk);

        mainLayout.Controls.Add(bottomPanel, 0, 3);

        this.Controls.Add(mainLayout);
        this.AcceptButton = _btnOk;
        this.CancelButton = _btnCancel;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Keep the scaled window on screen on small displays at high scaling
        var area = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min(area.Width, MinimumSize.Width), Math.Min(area.Height, MinimumSize.Height));
        Size = new Size(Math.Min(area.Width, Width), Math.Min(area.Height, Height));
    }

    private Label CreatePriceLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        UseMnemonic = false,
        Anchor = AnchorStyles.Left,
        // Label-to-field gap 8; top inset centers the text on the NumericUpDown line
        Margin = new Padding(0, LogicalToDeviceUnits(3), LogicalToDeviceUnits(8), 0)
    };

    private void SetPriceInputs(MediaPrices prices)
    {
        _numImagePrice.Value = Math.Clamp(prices.ImagePerUnit, _numImagePrice.Minimum, _numImagePrice.Maximum);
        _numTtsPrice.Value = Math.Clamp(prices.TtsPerMillionChars, _numTtsPrice.Minimum, _numTtsPrice.Maximum);
    }

    private void GridModels_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
    {
        // Commit combo box changes immediately
        if (_gridModels.IsCurrentCellDirty && _gridModels.CurrentCell is DataGridViewComboBoxCell)
        {
            _gridModels.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }
    }

    private void GridModels_DataError(object? sender, DataGridViewDataErrorEventArgs e)
    {
        // UI-SPEC S5: a bad entry reverts the cell silently (no modal prompt)
        e.ThrowException = false;
        _gridModels.CancelEdit();
        e.Cancel = false;
    }

    // Upper bound of each price/discount column (lower bound 0); other columns are not range-checked here
    internal static decimal? UpperBoundFor(string columnName) => columnName switch
    {
        "InputPrice" or "OutputPrice" or "CacheWritePrice" or "CacheReadPrice" => 1000m,
        "BatchDiscount" => 100m,
        _ => null
    };

    // Out-of-range or non-numeric prices revert the cell (T-07-37); no MessageBox (T-07-38).
    // Parsing (not CellValidating) does the revert: cancelling the edit inside CellValidating still commits the typed value.
    private void GridModels_CellParsing(object? sender, DataGridViewCellParsingEventArgs e)
    {
        if (e.ColumnIndex < 0 || e.RowIndex < 0)
            return;

        var max = UpperBoundFor(_gridModels.Columns[e.ColumnIndex].Name);
        if (max == null)
            return;

        var text = Convert.ToString(e.Value, CultureInfo.CurrentCulture);
        e.Value = IsValidPrice(text, max.Value)
            ? decimal.Parse(text!, NumberStyles.Number, CultureInfo.CurrentCulture)
            : _gridModels[e.ColumnIndex, e.RowIndex].Value;
        e.ParsingApplied = true;
    }

    internal static bool IsValidPrice(string? text, decimal max) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value)
        && value >= 0m && value <= max;

    private void BtnAdd_Click(object? sender, EventArgs e)
    {
        var newModel = new AIModelConfig
        {
            ModelId = "new-model",
            DisplayName = "New Model",
            InputTokenPricePerMillion = 1.00m,
            OutputTokenPricePerMillion = 3.00m,
            Provider = LlmProviderKind.OpenAI,
            MaxOutputTokens = 16_000
        };
        Models.Add(newModel);
        _bindingSource.ResetBindings(false);

        // Select the new row
        _gridModels.ClearSelection();
        _gridModels.Rows[_gridModels.Rows.Count - 1].Selected = true;
        _gridModels.CurrentCell = _gridModels.Rows[_gridModels.Rows.Count - 1].Cells["ModelId"];
        _gridModels.BeginEdit(true);
    }

    private void BtnRemove_Click(object? sender, EventArgs e)
    {
        if (_gridModels.SelectedRows.Count > 0)
        {
            var selectedModel = _gridModels.SelectedRows[0].DataBoundItem as AIModelConfig;
            if (selectedModel != null)
            {
                if (Models.Count <= 1)
                {
                    MessageBox.Show("You must have at least one model configured.", "Cannot Remove",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                Models.Remove(selectedModel);
                _bindingSource.ResetBindings(false);
            }
        }
    }

    private void BtnResetDefaults_Click(object? sender, EventArgs e)
    {
        if (MessageBox.Show("Reset all models to default configuration?", "Reset Defaults",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            ResetDefaults();
        }
    }

    // Reset Defaults without the confirmation prompt: models and media prices (0.006 / 15.00)
    private void ResetDefaults()
    {
        Models = AIModelConfig.GetDefaultModels();
        _bindingSource.DataSource = Models;
        _bindingSource.ResetBindings(false);
        SetPriceInputs(MediaPrices.Defaults());
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        // Validate
        foreach (var model in Models)
        {
            if (string.IsNullOrWhiteSpace(model.ModelId))
            {
                MessageBox.Show("All models must have a Model ID.", "Validation Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(model.DisplayName))
            {
                MessageBox.Show("All models must have a Display Name.", "Validation Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        // Keep only the first default per (Provider, Tier)
        foreach (var group in Models.Where(m => m.DefaultForTier != null).GroupBy(m => (m.Provider, m.DefaultForTier)))
        {
            foreach (var extra in group.Skip(1))
            {
                extra.DefaultForTier = null;
            }
        }
        _bindingSource.ResetBindings(false);

        // Every provider needs a Story and a Volume default
        foreach (var provider in Enum.GetValues<LlmProviderKind>())
        {
            foreach (var tier in Enum.GetValues<LlmTier>())
            {
                if (AIModelConfig.GetTierDefault(Models, provider, tier) == null)
                {
                    MessageBox.Show($"Pick a {tier} default for {provider}.", "Validation Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
        }

        CommitMediaPrices();

        this.DialogResult = DialogResult.OK;
        this.Close();
    }

    private void CommitMediaPrices()
    {
        var prices = new MediaPrices
        {
            ImagePerUnit = _numImagePrice.Value,
            TtsPerMillionChars = _numTtsPrice.Value
        };
        prices.Normalize();
        MediaPrices = prices;
    }

    // Test accessors (the dialog is never shown modally in tests)
    internal NumericUpDown ImagePriceInput => _numImagePrice;
    internal NumericUpDown TtsPriceInput => _numTtsPrice;
    internal DataGridView ModelsGrid => _gridModels;
    internal void ResetDefaultsForTest() => ResetDefaults();
    internal void CommitMediaPricesForTest() => CommitMediaPrices();
}
