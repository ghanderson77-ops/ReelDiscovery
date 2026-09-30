using System.ComponentModel;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Services;

namespace ReelDiscovery.UserControls;

public class StepStorylines : UserControl, IWizardStep
{
    private WizardState _state = null!;
    private DataGridView _gridStorylines = null!;
    private Button _btnRegenerate = null!;
    private Button _btnDelete = null!;
    private Label _lblDateRange = null!;
    private Label _lblStatus = null!;
    private LoadingOverlay _loadingOverlay = null!;
    private bool _isLoading = false;
    private BindingList<Storyline> _bindingList = null!;
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;
    private int _sortColumnIndex = -1;

    public string StepTitle => "Review Storylines";
    public bool CanMoveNext => _state?.Storylines.Count > 0 && !_isLoading;
    public bool CanMoveBack => !_isLoading;
    public string NextButtonText => "Next >";

    public event EventHandler? StateChanged;

    public StepStorylines()
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

        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        // Header with regenerate button
        var headerPanel = new Panel { Dock = DockStyle.Fill };

        var lblHeader = new Label
        {
            Text = "The AI has generated the following storylines. You can edit or remove them, or regenerate.",
            AutoSize = true,
            Location = new Point(0, 5)
        };
        headerPanel.Controls.Add(lblHeader);

        _btnDelete = ButtonHelper.CreateButton("Delete Selected", 110, 32, ButtonStyle.Default);
        _btnDelete.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        _btnDelete.Click += BtnDelete_Click;
        headerPanel.Controls.Add(_btnDelete);

        _btnRegenerate = ButtonHelper.CreateButton("Regenerate", 110, 32, ButtonStyle.Primary);
        _btnRegenerate.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        _btnRegenerate.Click += BtnRegenerate_Click;
        headerPanel.Controls.Add(_btnRegenerate);

        headerPanel.Resize += (s, e) =>
        {
            _btnRegenerate.Location = new Point(headerPanel.Width - 120, 0);
            _btnDelete.Location = new Point(headerPanel.Width - 240, 0);
        };

        mainLayout.Controls.Add(headerPanel, 0, 0);

        // Storylines grid
        _gridStorylines = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.Fixed3D
        };

        _gridStorylines.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Title",
            HeaderText = "Title",
            DataPropertyName = "Title",
            Width = 200
        });

        _gridStorylines.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Description",
            HeaderText = "Description",
            DataPropertyName = "Description",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });

        _gridStorylines.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Timeline",
            HeaderText = "Timeline",
            DataPropertyName = "TimelineHint",
            Width = 120
        });

        _gridStorylines.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Emails",
            HeaderText = "Est. Emails",
            DataPropertyName = "SuggestedEmailCount",
            Width = 80
        });

        _gridStorylines.ColumnHeaderMouseClick += GridStorylines_ColumnHeaderMouseClick;
        _gridStorylines.SelectionChanged += GridStorylines_SelectionChanged;
        _gridStorylines.UserDeletingRow += GridStorylines_UserDeletingRow;

        mainLayout.Controls.Add(_gridStorylines, 0, 1);

        // Create loading overlay for the grid
        _loadingOverlay = new LoadingOverlay(LoadingOverlay.LoadingType.Storylines);

        // Date range info
        var datePanel = new Panel { Dock = DockStyle.Fill };

        _lblDateRange = new Label
        {
            Text = "Suggested Date Range: Not yet determined",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(this.Font, FontStyle.Italic)
        };
        datePanel.Controls.Add(_lblDateRange);

        mainLayout.Controls.Add(datePanel, 0, 2);

        // Status label
        _lblStatus = new Label
        {
            Text = "",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.Gray
        };
        mainLayout.Controls.Add(_lblStatus, 0, 3);

        this.Controls.Add(mainLayout);
    }

    private async void BtnRegenerate_Click(object? sender, EventArgs e)
    {
        await GenerateStorylinesAsync();
    }

    private void BtnDelete_Click(object? sender, EventArgs e)
    {
        if (_gridStorylines.SelectedRows.Count > 0)
        {
            var selectedRows = _gridStorylines.SelectedRows.Cast<DataGridViewRow>()
                .Where(r => !r.IsNewRow)
                .OrderByDescending(r => r.Index)
                .ToList();

            foreach (var row in selectedRows)
            {
                if (row.DataBoundItem is Storyline storyline)
                {
                    _bindingList.Remove(storyline);
                }
            }

            UpdateStatus();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void GridStorylines_SelectionChanged(object? sender, EventArgs e)
    {
        _btnDelete.Enabled = _gridStorylines.SelectedRows.Count > 0 &&
                             _gridStorylines.SelectedRows.Cast<DataGridViewRow>().Any(r => !r.IsNewRow);
    }

    private void GridStorylines_UserDeletingRow(object? sender, DataGridViewRowCancelEventArgs e)
    {
        // Allow deletion - the binding list will handle it
        UpdateStatus();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void GridStorylines_ColumnHeaderMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        var column = _gridStorylines.Columns[e.ColumnIndex];
        if (column == null || _bindingList == null || _bindingList.Count == 0)
            return;

        // Toggle sort direction if clicking the same column
        if (_sortColumnIndex == e.ColumnIndex)
        {
            _sortDirection = _sortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
        }
        else
        {
            _sortColumnIndex = e.ColumnIndex;
            _sortDirection = ListSortDirection.Ascending;
        }

        // Get the property name for sorting
        var propertyName = column.DataPropertyName;
        if (string.IsNullOrEmpty(propertyName))
            return;

        // Sort the list
        var sorted = _sortDirection == ListSortDirection.Ascending
            ? _state.Storylines.OrderBy(s => GetPropertyValue(s, propertyName)).ToList()
            : _state.Storylines.OrderByDescending(s => GetPropertyValue(s, propertyName)).ToList();

        _state.Storylines.Clear();
        foreach (var item in sorted)
            _state.Storylines.Add(item);

        RefreshGrid();

        // Update column header to show sort direction
        foreach (DataGridViewColumn col in _gridStorylines.Columns)
        {
            col.HeaderCell.SortGlyphDirection = SortOrder.None;
        }
        column.HeaderCell.SortGlyphDirection = _sortDirection == ListSortDirection.Ascending
            ? SortOrder.Ascending
            : SortOrder.Descending;
    }

    private static object? GetPropertyValue(object obj, string propertyName)
    {
        return obj.GetType().GetProperty(propertyName)?.GetValue(obj);
    }

    private void UpdateStatus()
    {
        _lblStatus.Text = $"{_state.Storylines.Count} storylines.";
        _lblStatus.ForeColor = _state.Storylines.Count > 0 ? Color.Green : Color.Gray;
    }

    private async Task GenerateStorylinesAsync()
    {
        _isLoading = true;
        _btnRegenerate.Enabled = false;
        _lblStatus.Text = "";
        _loadingOverlay.Show(_gridStorylines);
        StateChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            var generator = new StorylineGenerator(_state.CreateGateway());

            // Create progress reporter that updates the status label
            var progress = new Progress<string>(status =>
            {
                _lblStatus.Text = status;
                _lblStatus.ForeColor = Color.Blue;
            });

            var result = await generator.GenerateStorylinesAsync(
                _state.Topic,
                _state.AdditionalInstructions,
                _state.StorylineCount,
                _state.WantsDocuments,
                _state.WantsImages,
                _state.WantsVoicemails,
                progress);

            _state.Storylines = result.Storylines;
            _state.AISuggestedStartDate = result.SuggestedStartDate;
            _state.AISuggestedEndDate = result.SuggestedEndDate;
            _state.AISuggestedDateReasoning = result.DateRangeReasoning;

            RefreshGrid();

            if (result.SuggestedStartDate.HasValue && result.SuggestedEndDate.HasValue)
            {
                _lblDateRange.Text = $"Suggested Date Range: {result.SuggestedStartDate:MMM d, yyyy} - {result.SuggestedEndDate:MMM d, yyyy}" +
                    (string.IsNullOrEmpty(result.DateRangeReasoning) ? "" : $"\n({result.DateRangeReasoning})");
            }

            _lblStatus.Text = $"Generated {result.Storylines.Count} storylines.";
            _lblStatus.ForeColor = Color.Green;
        }
        catch (Exception ex)
        {
            var errorMsg = ex.InnerException != null
                ? $"{ex.Message} ({ex.InnerException.Message})"
                : ex.Message;
            _lblStatus.Text = $"Error: {errorMsg}";
            _lblStatus.ForeColor = Color.Red;
        }
        finally
        {
            _isLoading = false;
            _btnRegenerate.Enabled = true;
            _loadingOverlay.Hide();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RefreshGrid()
    {
        _bindingList = new BindingList<Storyline>(_state.Storylines);
        _gridStorylines.DataSource = _bindingList;
    }

    public void BindState(WizardState state)
    {
        _state = state;
    }

    public async Task OnEnterStepAsync()
    {
        RefreshGrid();

        // Auto-generate if no storylines yet
        if (_state.Storylines.Count == 0)
        {
            await GenerateStorylinesAsync();
        }
        else
        {
            _lblStatus.Text = $"{_state.Storylines.Count} storylines loaded.";
            _lblStatus.ForeColor = Color.Green;

            if (_state.AISuggestedStartDate.HasValue && _state.AISuggestedEndDate.HasValue)
            {
                _lblDateRange.Text = $"Suggested Date Range: {_state.AISuggestedStartDate:MMM d, yyyy} - {_state.AISuggestedEndDate:MMM d, yyyy}";
            }
        }
    }

    public Task OnLeaveStepAsync()
    {
        // Sync any edits from the grid back to state
        return Task.CompletedTask;
    }

    public Task<bool> ValidateStepAsync()
    {
        if (_state.Storylines.Count == 0)
        {
            MessageBox.Show("At least one storyline is required.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }
}
