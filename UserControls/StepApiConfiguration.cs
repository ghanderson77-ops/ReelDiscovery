using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Services;

namespace ReelDiscovery.UserControls;

public class StepApiConfiguration : UserControl, IWizardStep
{
    private WizardState _state = null!;
    private TextBox _txtApiKey = null!;
    private ComboBox _cboModel = null!;
    private Button _btnTestConnection = null!;
    private Button _btnConfigureModels = null!;
    private Label _lblStatus = null!;
    private Label _lblPricing = null!;
    private bool _connectionTested = false;

    public string StepTitle => "API Configuration";
    public bool CanMoveNext => _connectionTested;
    public bool CanMoveBack => true;
    public string NextButtonText => "Next >";

    public event EventHandler? StateChanged;

    public StepApiConfiguration()
    {
        InitializeUI();
    }

    private void InitializeUI()
    {
        this.Padding = new Padding(10, 5, 10, 5);

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 7,
            Padding = new Padding(5)
        };

        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));  // API Key
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));  // Model + button
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));  // Pricing
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));  // Test button
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));  // Status
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // Instructions
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));  // Disclaimer

        // API Key
        var lblApiKey = new Label
        {
            Text = "OpenAI API Key:",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(this.Font.FontFamily, 10F)
        };
        mainLayout.Controls.Add(lblApiKey, 0, 0);

        _txtApiKey = new TextBox
        {
            Dock = DockStyle.Fill,
            UseSystemPasswordChar = true,
            Font = new Font("Consolas", 11F),
            Margin = new Padding(0, 8, 0, 8)
        };
        _txtApiKey.TextChanged += (s, e) => _connectionTested = false;
        mainLayout.Controls.Add(_txtApiKey, 1, 0);

        // Model selection
        var lblModel = new Label
        {
            Text = "Model:",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(this.Font.FontFamily, 10F)
        };
        mainLayout.Controls.Add(lblModel, 0, 1);

        var modelPanel = new Panel { Dock = DockStyle.Fill };
        _cboModel = new ComboBox
        {
            Width = 220,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font(this.Font.FontFamily, 10F),
            Location = new Point(0, 10)
        };
        _cboModel.SelectedIndexChanged += CboModel_SelectedIndexChanged;
        modelPanel.Controls.Add(_cboModel);

        _btnConfigureModels = ButtonHelper.CreateButton("Configure Models...", 160, 32, ButtonStyle.Default);
        _btnConfigureModels.Location = new Point(230, 8);
        _btnConfigureModels.Click += BtnConfigureModels_Click;
        modelPanel.Controls.Add(_btnConfigureModels);
        mainLayout.Controls.Add(modelPanel, 1, 1);

        // Pricing info label
        _lblPricing = new Label
        {
            Text = "",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.DimGray,
            Font = new Font(this.Font.FontFamily, 9F)
        };
        mainLayout.Controls.Add(_lblPricing, 1, 2);

        // Test connection button
        mainLayout.Controls.Add(new Label(), 0, 3);

        _btnTestConnection = ButtonHelper.CreateButton("Test Connection", 160, 38, ButtonStyle.Primary);
        _btnTestConnection.Margin = new Padding(0, 8, 0, 8);
        _btnTestConnection.Click += BtnTestConnection_Click;
        mainLayout.Controls.Add(_btnTestConnection, 1, 3);

        // Status label
        _lblStatus = new Label
        {
            Text = "",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.Gray,
            Font = new Font(this.Font.FontFamily, 10F)
        };
        mainLayout.Controls.Add(_lblStatus, 1, 4);

        // Instructions
        var instructions = new Label
        {
            Text = "Enter your OpenAI API key to connect to the AI service.\n\n" +
                   "You can get an API key from https://platform.openai.com/api-keys\n\n" +
                   "Click 'Configure Models...' to adjust model pricing for accurate cost tracking.\n\n" +
                   "Recommended model:\n" +
                   "  - GPT-4o Mini: Fast and cost-effective (recommended)\n" +
                   "  - GPT-4o: Higher quality, more expensive",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            ForeColor = Color.DimGray,
            Font = new Font(this.Font.FontFamily, 9.5F)
        };
        mainLayout.Controls.Add(instructions, 0, 5);
        mainLayout.SetColumnSpan(instructions, 2);

        // Disclaimer about API charges
        var lblDisclaimer = new Label
        {
            Text = "NOTICE: QuikData is not responsible for any API charges incurred through usage of this application. " +
                   "You are solely responsible for monitoring and managing your OpenAI API usage and costs.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            ForeColor = Color.DarkRed,
            Font = new Font(this.Font.FontFamily, 9F, FontStyle.Bold),
            AutoSize = false
        };
        mainLayout.Controls.Add(lblDisclaimer, 0, 6);
        mainLayout.SetColumnSpan(lblDisclaimer, 2);

        this.Controls.Add(mainLayout);
    }

    private void CboModel_SelectedIndexChanged(object? sender, EventArgs e)
    {
        UpdatePricingLabel();
    }

    private void UpdatePricingLabel()
    {
        if (_cboModel.SelectedItem is AIModelConfig model)
        {
            _lblPricing.Text = $"Pricing: ${model.InputTokenPricePerMillion}/M input, ${model.OutputTokenPricePerMillion}/M output";
        }
        else
        {
            _lblPricing.Text = "";
        }
    }

    private void BtnConfigureModels_Click(object? sender, EventArgs e)
    {
        using var dialog = new ModelConfigurationDialog(_state.AvailableModelConfigs);
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            _state.AvailableModelConfigs = dialog.Models;
            RefreshModelDropdown();
        }
    }

    private void RefreshModelDropdown()
    {
        var currentSelection = (_cboModel.SelectedItem as AIModelConfig)?.ModelId;
        _cboModel.Items.Clear();
        foreach (var model in _state.AvailableModelConfigs)
        {
            _cboModel.Items.Add(model);
        }

        // Restore selection
        var toSelect = _state.AvailableModelConfigs.FirstOrDefault(m => m.ModelId == currentSelection)
            ?? _state.AvailableModelConfigs.FirstOrDefault(m => m.IsDefault)
            ?? _state.AvailableModelConfigs.FirstOrDefault();

        if (toSelect != null)
        {
            _cboModel.SelectedItem = toSelect;
        }

        UpdatePricingLabel();
    }

    private async void BtnTestConnection_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtApiKey.Text))
        {
            _lblStatus.Text = "Please enter an API key.";
            _lblStatus.ForeColor = Color.Red;
            return;
        }

        _btnTestConnection.Enabled = false;
        _lblStatus.Text = "Testing connection...";
        _lblStatus.ForeColor = Color.Gray;

        try
        {
            var modelConfig = _cboModel.SelectedItem as AIModelConfig;
            var modelId = modelConfig?.ModelId ?? "gpt-4o-mini";
            var service = new OpenAIService(_txtApiKey.Text.Trim(), modelId);
            var success = await service.TestConnectionAsync();

            if (success)
            {
                _lblStatus.Text = "Connection successful!";
                _lblStatus.ForeColor = Color.Green;
                _connectionTested = true;

                // Save API key for future sessions
                SaveApiKey(_txtApiKey.Text.Trim());

                // Notify wizard to update navigation buttons
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                _lblStatus.Text = "Connection failed. Please check your API key.";
                _lblStatus.ForeColor = Color.Red;
                _connectionTested = false;
            }
        }
        catch (Exception ex)
        {
            _lblStatus.Text = $"Error: {ex.Message}";
            _lblStatus.ForeColor = Color.Red;
            _connectionTested = false;
        }
        finally
        {
            _btnTestConnection.Enabled = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void BindState(WizardState state)
    {
        _state = state;
    }

    public Task OnEnterStepAsync()
    {
        // Populate model dropdown from state
        RefreshModelDropdown();

        // Try to load saved API key if state doesn't have one
        if (string.IsNullOrEmpty(_state.ApiKey))
        {
            var savedKey = LoadApiKey();
            if (!string.IsNullOrEmpty(savedKey))
            {
                _state.ApiKey = savedKey;
            }
        }

        if (!string.IsNullOrEmpty(_state.ApiKey))
        {
            _txtApiKey.Text = _state.ApiKey;
        }

        // Select the model from state
        if (!string.IsNullOrEmpty(_state.SelectedModel))
        {
            var modelToSelect = _state.AvailableModelConfigs.FirstOrDefault(m => m.ModelId == _state.SelectedModel);
            if (modelToSelect != null)
            {
                _cboModel.SelectedItem = modelToSelect;
            }
        }

        _connectionTested = _state.ConnectionTested;

        if (_connectionTested)
        {
            _lblStatus.Text = "Connection verified.";
            _lblStatus.ForeColor = Color.Green;
        }

        UpdatePricingLabel();

        return Task.CompletedTask;
    }

    public Task OnLeaveStepAsync()
    {
        return Task.CompletedTask;
    }

    public Task<bool> ValidateStepAsync()
    {
        if (string.IsNullOrWhiteSpace(_txtApiKey.Text))
        {
            MessageBox.Show("Please enter an API key.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return Task.FromResult(false);
        }

        if (!_connectionTested)
        {
            MessageBox.Show("Please test the connection before proceeding.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return Task.FromResult(false);
        }

        _state.ApiKey = _txtApiKey.Text.Trim();
        var selectedModel = _cboModel.SelectedItem as AIModelConfig;
        _state.SelectedModel = selectedModel?.ModelId ?? "gpt-4o-mini";
        _state.ConnectionTested = true;

        return Task.FromResult(true);
    }

    private static void SaveApiKey(string apiKey)
    {
        try
        {
            // Interim bridge until the keys screen (plan 01-07): persist via the DPAPI store
            new ApiKeyStore().SetKey(Services.Ai.LlmProviderKind.OpenAI, apiKey);
        }
        catch (Exception)
        {
            // Ignore save errors - persistence is a convenience; the key is still used this session
        }
    }

    private static string? LoadApiKey()
    {
        try
        {
            return new ApiKeyStore().GetKey(Services.Ai.LlmProviderKind.OpenAI);
        }
        catch (Exception)
        {
            // Ignore load errors - the user can re-enter the key
        }
        return null;
    }
}
