using System.ComponentModel;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Services;

namespace ReelDiscovery.UserControls;

public class StepCharacters : UserControl, IWizardStep
{
    private WizardState _state = null!;
    private DataGridView _gridCharacters = null!;
    private Button _btnRegenerate = null!;
    private Button _btnDelete = null!;
    private Label _lblCompanyInfo = null!;
    private Label _lblStatus = null!;
    private LoadingOverlay _loadingOverlay = null!;
    private bool _isLoading = false;
    private BindingList<Character> _bindingList = null!;
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;
    private int _sortColumnIndex = -1;

    public string StepTitle => "Review Characters";
    public bool CanMoveNext => _state?.Characters.Count >= 2 && !_isLoading;
    public bool CanMoveBack => !_isLoading;
    public string NextButtonText => "Next >";

    public event EventHandler? StateChanged;

    public StepCharacters()
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
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        // Header with regenerate button
        var headerPanel = new Panel { Dock = DockStyle.Fill };

        var lblHeader = new Label
        {
            Text = "The AI has generated characters for your email dataset. You can edit their details.",
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

        // Characters grid
        _gridCharacters = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = true,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.Fixed3D
        };

        _gridCharacters.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "IsExternal",
            HeaderText = "Ext",
            DataPropertyName = "IsExternal",
            Width = 40
        });

        _gridCharacters.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "FirstName",
            HeaderText = "First Name",
            DataPropertyName = "FirstName",
            Width = 90
        });

        _gridCharacters.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "LastName",
            HeaderText = "Last Name",
            DataPropertyName = "LastName",
            Width = 90
        });

        _gridCharacters.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Email",
            HeaderText = "Email",
            DataPropertyName = "Email",
            Width = 180
        });

        _gridCharacters.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Organization",
            HeaderText = "Organization",
            DataPropertyName = "Organization",
            Width = 130
        });

        _gridCharacters.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Role",
            HeaderText = "Role/Title",
            DataPropertyName = "Role",
            Width = 120
        });

        _gridCharacters.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Department",
            HeaderText = "Dept",
            DataPropertyName = "Department",
            Width = 80
        });

        _gridCharacters.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "PersonalityNotes",
            HeaderText = "Personality",
            DataPropertyName = "PersonalityNotes",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });

        _gridCharacters.ColumnHeaderMouseClick += GridCharacters_ColumnHeaderMouseClick;
        _gridCharacters.SelectionChanged += GridCharacters_SelectionChanged;
        _gridCharacters.UserDeletingRow += GridCharacters_UserDeletingRow;

        mainLayout.Controls.Add(_gridCharacters, 0, 1);

        // Create loading overlay for the grid
        _loadingOverlay = new LoadingOverlay(LoadingOverlay.LoadingType.Characters);

        // Company info
        _lblCompanyInfo = new Label
        {
            Text = "Company Domain: Not yet determined",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(this.Font, FontStyle.Bold)
        };
        mainLayout.Controls.Add(_lblCompanyInfo, 0, 2);

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
        await GenerateCharactersAsync();
    }

    private void BtnDelete_Click(object? sender, EventArgs e)
    {
        if (_gridCharacters.SelectedRows.Count > 0)
        {
            var selectedRows = _gridCharacters.SelectedRows.Cast<DataGridViewRow>()
                .Where(r => !r.IsNewRow)
                .OrderByDescending(r => r.Index)
                .ToList();

            foreach (var row in selectedRows)
            {
                if (row.DataBoundItem is Character character)
                {
                    _bindingList.Remove(character);
                }
            }

            UpdateStatus();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void GridCharacters_SelectionChanged(object? sender, EventArgs e)
    {
        _btnDelete.Enabled = _gridCharacters.SelectedRows.Count > 0 &&
                             _gridCharacters.SelectedRows.Cast<DataGridViewRow>().Any(r => !r.IsNewRow);
    }

    private void GridCharacters_UserDeletingRow(object? sender, DataGridViewRowCancelEventArgs e)
    {
        // Allow deletion - the binding list will handle it
        UpdateStatus();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void GridCharacters_ColumnHeaderMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        var column = _gridCharacters.Columns[e.ColumnIndex];
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
            ? _state.Characters.OrderBy(c => GetPropertyValue(c, propertyName)).ToList()
            : _state.Characters.OrderByDescending(c => GetPropertyValue(c, propertyName)).ToList();

        _state.Characters.Clear();
        foreach (var item in sorted)
            _state.Characters.Add(item);

        RefreshGrid();

        // Update column header to show sort direction
        foreach (DataGridViewColumn col in _gridCharacters.Columns)
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
        var internalCount = _state.Characters.Count(c => !c.IsExternal);
        var externalCount = _state.Characters.Count(c => c.IsExternal);
        var domains = _state.Characters.Select(c => c.Domain).Distinct().Count();
        _lblStatus.Text = $"{_state.Characters.Count} characters ({internalCount} internal, {externalCount} external across {domains} domains).";
        _lblStatus.ForeColor = _state.Characters.Count >= 2 ? Color.Green : Color.Gray;
    }

    private async Task GenerateCharactersAsync()
    {
        _isLoading = true;
        _btnRegenerate.Enabled = false;
        _lblStatus.Text = "";
        _loadingOverlay.Show(_gridCharacters);
        StateChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            var llm = _state.CreateGateway();
            var generator = new CharacterGenerator(llm);

            // Create progress reporter that updates the status label
            IProgress<string> progress = new Progress<string>(status =>
            {
                _lblStatus.Text = status;
                _lblStatus.ForeColor = Color.Blue;
            });

            var result = await generator.GenerateCharactersAsync(
                _state.Topic,
                _state.Storylines,
                progress);

            _state.Characters = result.Characters;
            _state.CompanyDomain = result.CompanyDomain;

            RefreshGrid();

            _lblCompanyInfo.Text = $"Primary Company: {result.CompanyName} ({result.CompanyDomain})";

            // Generate presentation themes for each domain
            progress.Report("Generating presentation themes...");
            var themeGenerator = new ThemeGenerator(llm);
            _state.DomainThemes = await themeGenerator.GenerateThemesForDomainsAsync(
                _state.Topic,
                _state.Characters,
                progress);

            var internalCount = result.Characters.Count(c => !c.IsExternal);
            var externalCount = result.Characters.Count(c => c.IsExternal);
            var domains = result.Characters.Select(c => c.Domain).Distinct().Count();
            _lblStatus.Text = $"Generated {result.Characters.Count} characters ({internalCount} internal, {externalCount} external across {domains} domains).";
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
        _bindingList = new BindingList<Character>(_state.Characters);
        _gridCharacters.DataSource = _bindingList;
    }

    public void BindState(WizardState state)
    {
        _state = state;
    }

    public async Task OnEnterStepAsync()
    {
        RefreshGrid();

        if (!string.IsNullOrEmpty(_state.CompanyDomain))
        {
            _lblCompanyInfo.Text = $"Company Domain: {_state.CompanyDomain}";
        }

        // Auto-generate if no characters yet
        if (_state.Characters.Count == 0)
        {
            await GenerateCharactersAsync();
        }
        else
        {
            var internalCount = _state.Characters.Count(c => !c.IsExternal);
            var externalCount = _state.Characters.Count(c => c.IsExternal);
            var domains = _state.Characters.Select(c => c.Domain).Distinct().Count();
            _lblStatus.Text = $"{_state.Characters.Count} characters loaded ({internalCount} internal, {externalCount} external across {domains} domains).";
            _lblStatus.ForeColor = Color.Green;
        }
    }

    public Task OnLeaveStepAsync()
    {
        return Task.CompletedTask;
    }

    public Task<bool> ValidateStepAsync()
    {
        if (_state.Characters.Count < 2)
        {
            MessageBox.Show("At least 2 characters are required.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return Task.FromResult(false);
        }

        // Validate all characters have required fields
        foreach (var character in _state.Characters)
        {
            if (string.IsNullOrWhiteSpace(character.FirstName) ||
                string.IsNullOrWhiteSpace(character.LastName) ||
                string.IsNullOrWhiteSpace(character.Email))
            {
                MessageBox.Show("All characters must have a first name, last name, and email.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return Task.FromResult(false);
            }
        }

        return Task.FromResult(true);
    }
}
