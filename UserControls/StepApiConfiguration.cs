using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Services;
using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.UserControls;

public class StepApiConfiguration : UserControl, IWizardStep
{
    private const string ProviderItemOpenAI = "OpenAI";
    private const string ProviderItemClaude = "Claude";

    private WizardState _state = null!;
    private readonly ApiKeyStore _keyStore = new();

    // OpenAI section
    private TextBox _txtOpenAIKey = null!;
    private Button _btnTestOpenAI = null!;
    private Button _btnClearOpenAI = null!;
    private Label _lblOpenAIStatus = null!;
    private Label _lblMediaStatus = null!;

    // Anthropic section
    private TextBox _txtAnthropicKey = null!;
    private Button _btnTestAnthropic = null!;
    private Button _btnClearAnthropic = null!;
    private Label _lblAnthropicStatus = null!;

    // Provider and tier models
    private ComboBox _cboProvider = null!;
    private ComboBox _cboStoryModel = null!;
    private ComboBox _cboVolumeModel = null!;
    private Label _lblPricing = null!;
    private Button _btnConfigureModels = null!;

    private bool _keysLoaded;
    private bool _suppressEvents;

    public string StepTitle => "API Configuration";
    public bool CanMoveNext => _state != null && _state.CanProceedFromKeys();
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
        this.AutoScroll = true;

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 13,
            Padding = new Padding(5),
            // Keeps every row reachable (via the control's scrollbar) on small windows
            MinimumSize = new Size(0, 520)
        };

        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));  // 0: OpenAI header
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));  // 1: OpenAI key + Test + Clear
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));  // 2: OpenAI status
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));  // 3: Media status
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));  // 4: Anthropic header
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));  // 5: Anthropic key + Test + Clear
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));  // 6: Anthropic status
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));  // 7: Text provider
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));  // 8: Story model
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));  // 9: Volume model + Configure Models
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));  // 10: Pricing
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // 11: Instructions
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));  // 12: Disclaimer

        // OpenAI section
        var openAIHeader = CreateSectionHeader("OpenAI");
        mainLayout.Controls.Add(openAIHeader, 0, 0);
        mainLayout.SetColumnSpan(openAIHeader, 2);

        mainLayout.Controls.Add(CreateFieldLabel("OpenAI API Key:"), 0, 1);
        _txtOpenAIKey = CreateKeyTextBox();
        _txtOpenAIKey.TextChanged += (s, e) => OnKeyTextChanged(LlmProviderKind.OpenAI);
        _btnTestOpenAI = ButtonHelper.CreateButton("Test", 100, 34, ButtonStyle.Primary);
        _btnTestOpenAI.Click += async (s, e) => await TestKeyAsync(LlmProviderKind.OpenAI);
        _btnClearOpenAI = ButtonHelper.CreateButton("Clear", 100, 34, ButtonStyle.Default);
        _btnClearOpenAI.Click += (s, e) => ClearKey(LlmProviderKind.OpenAI);
        mainLayout.Controls.Add(CreateKeyRow(_txtOpenAIKey, _btnTestOpenAI, _btnClearOpenAI), 1, 1);

        _lblOpenAIStatus = CreateStatusLabel();
        mainLayout.Controls.Add(_lblOpenAIStatus, 1, 2);

        _lblMediaStatus = CreateStatusLabel();
        mainLayout.Controls.Add(_lblMediaStatus, 1, 3);

        // Anthropic section
        var anthropicHeader = CreateSectionHeader("Anthropic (Claude)");
        mainLayout.Controls.Add(anthropicHeader, 0, 4);
        mainLayout.SetColumnSpan(anthropicHeader, 2);

        mainLayout.Controls.Add(CreateFieldLabel("Anthropic API Key:"), 0, 5);
        _txtAnthropicKey = CreateKeyTextBox();
        _txtAnthropicKey.TextChanged += (s, e) => OnKeyTextChanged(LlmProviderKind.Anthropic);
        _btnTestAnthropic = ButtonHelper.CreateButton("Test", 100, 34, ButtonStyle.Primary);
        _btnTestAnthropic.Click += async (s, e) => await TestKeyAsync(LlmProviderKind.Anthropic);
        _btnClearAnthropic = ButtonHelper.CreateButton("Clear", 100, 34, ButtonStyle.Default);
        _btnClearAnthropic.Click += (s, e) => ClearKey(LlmProviderKind.Anthropic);
        mainLayout.Controls.Add(CreateKeyRow(_txtAnthropicKey, _btnTestAnthropic, _btnClearAnthropic), 1, 5);

        _lblAnthropicStatus = CreateStatusLabel();
        mainLayout.Controls.Add(_lblAnthropicStatus, 1, 6);

        // Text provider
        mainLayout.Controls.Add(CreateFieldLabel("Text provider:"), 0, 7);
        _cboProvider = CreateComboBox();
        _cboProvider.Items.AddRange(new object[] { ProviderItemOpenAI, ProviderItemClaude });
        _cboProvider.SelectedIndexChanged += CboProvider_SelectedIndexChanged;
        mainLayout.Controls.Add(_cboProvider, 1, 7);

        // Story model
        mainLayout.Controls.Add(CreateFieldLabel("Story model:"), 0, 8);
        _cboStoryModel = CreateComboBox();
        _cboStoryModel.SelectedIndexChanged += CboTierModel_SelectedIndexChanged;
        mainLayout.Controls.Add(_cboStoryModel, 1, 8);

        // Volume model + Configure Models
        mainLayout.Controls.Add(CreateFieldLabel("Volume model:"), 0, 9);
        var volumePanel = new Panel { Dock = DockStyle.Fill };
        _cboVolumeModel = CreateComboBox();
        _cboVolumeModel.Dock = DockStyle.None;
        _cboVolumeModel.Location = new Point(0, 6);
        _cboVolumeModel.SelectedIndexChanged += CboTierModel_SelectedIndexChanged;
        volumePanel.Controls.Add(_cboVolumeModel);

        _btnConfigureModels = ButtonHelper.CreateButton("Configure Models...", 160, 32, ButtonStyle.Default);
        _btnConfigureModels.Location = new Point(270, 4);
        _btnConfigureModels.Click += BtnConfigureModels_Click;
        volumePanel.Controls.Add(_btnConfigureModels);
        mainLayout.Controls.Add(volumePanel, 1, 9);

        // Pricing
        _lblPricing = new Label
        {
            Text = "",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.DimGray,
            Font = new Font(this.Font.FontFamily, 9F)
        };
        mainLayout.Controls.Add(_lblPricing, 1, 10);

        // Instructions
        var instructions = new Label
        {
            Text = "Enter and test an API key for the text provider you want to use. Only the selected text provider's key " +
                   "must pass its test before you can continue.\n" +
                   "Images and voicemail always use OpenAI: add and test an OpenAI key to enable them, even when Claude writes the text.\n" +
                   "Keys: https://platform.openai.com/api-keys and https://console.anthropic.com/settings/keys. " +
                   "Tested keys are saved encrypted for your Windows account; Clear removes a saved key.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            ForeColor = Color.DimGray,
            Font = new Font(this.Font.FontFamily, 9F)
        };
        mainLayout.Controls.Add(instructions, 0, 11);
        mainLayout.SetColumnSpan(instructions, 2);

        // Disclaimer about API charges
        var lblDisclaimer = new Label
        {
            Text = "NOTICE: QuikData is not responsible for any API charges incurred through usage of this application. " +
                   "You are solely responsible for monitoring and managing your OpenAI and Anthropic API usage and costs.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            ForeColor = Color.DarkRed,
            Font = new Font(this.Font.FontFamily, 9F, FontStyle.Bold),
            AutoSize = false
        };
        mainLayout.Controls.Add(lblDisclaimer, 0, 12);
        mainLayout.SetColumnSpan(lblDisclaimer, 2);

        this.Controls.Add(mainLayout);
    }

    private Label CreateSectionHeader(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.BottomLeft,
        Font = new Font(this.Font.FontFamily, 10.5F, FontStyle.Bold)
    };

    private Label CreateFieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font(this.Font.FontFamily, 10F)
    };

    private Label CreateStatusLabel() => new()
    {
        Text = "",
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.Gray,
        Font = new Font(this.Font.FontFamily, 9.5F)
    };

    private static TextBox CreateKeyTextBox() => new()
    {
        Dock = DockStyle.Fill,
        UseSystemPasswordChar = true,
        Font = new Font("Consolas", 11F),
        Margin = new Padding(0, 6, 8, 0)
    };

    private ComboBox CreateComboBox() => new()
    {
        Width = 260,
        DropDownStyle = ComboBoxStyle.DropDownList,
        Font = new Font(this.Font.FontFamily, 10F),
        Margin = new Padding(0, 5, 0, 0)
    };

    private static TableLayoutPanel CreateKeyRow(TextBox keyBox, Button testButton, Button clearButton)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        testButton.Margin = new Padding(0, 3, 6, 0);
        clearButton.Margin = new Padding(0, 3, 0, 0);
        row.Controls.Add(keyBox, 0, 0);
        row.Controls.Add(testButton, 1, 0);
        row.Controls.Add(clearButton, 2, 0);
        return row;
    }

    private TextBox GetKeyBox(LlmProviderKind provider) =>
        provider == LlmProviderKind.OpenAI ? _txtOpenAIKey : _txtAnthropicKey;

    private Label GetStatusLabel(LlmProviderKind provider) =>
        provider == LlmProviderKind.OpenAI ? _lblOpenAIStatus : _lblAnthropicStatus;

    private Button GetTestButton(LlmProviderKind provider) =>
        provider == LlmProviderKind.OpenAI ? _btnTestOpenAI : _btnTestAnthropic;

    private static string GetProviderName(LlmProviderKind provider) =>
        provider == LlmProviderKind.OpenAI ? "OpenAI" : "Anthropic";

    private void SetKeyState(LlmProviderKind provider, string key, bool tested)
    {
        if (provider == LlmProviderKind.OpenAI)
        {
            _state.OpenAIApiKey = key;
            _state.OpenAIKeyTested = tested;
        }
        else
        {
            _state.AnthropicApiKey = key;
            _state.AnthropicKeyTested = tested;
        }
    }

    private void SetTested(LlmProviderKind provider, bool tested)
    {
        if (provider == LlmProviderKind.OpenAI)
        {
            _state.OpenAIKeyTested = tested;
        }
        else
        {
            _state.AnthropicKeyTested = tested;
        }
    }

    private void SetStatus(LlmProviderKind provider, string text, Color color)
    {
        var label = GetStatusLabel(provider);
        label.Text = text;
        label.ForeColor = color;
    }

    private void OnKeyTextChanged(LlmProviderKind provider)
    {
        if (_suppressEvents || _state == null)
        {
            return;
        }

        SetTested(provider, false);
        SetStatus(provider, "", Color.Gray);
        UpdateMediaStatus();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task TestKeyAsync(LlmProviderKind provider)
    {
        var key = GetKeyBox(provider).Text.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            SetStatus(provider, "Please enter a key.", Color.Red);
            return;
        }

        var button = GetTestButton(provider);
        button.Enabled = false;
        SetStatus(provider, "Testing...", Color.Gray);

        try
        {
            var modelId = _state.GetKeyTestModelId(provider);
            var result = await LlmProviderFactory.Create(provider, key).TestKeyAsync(modelId);

            if (result.Succeeded)
            {
                SetStatus(provider, result.Message, Color.Green);
                SetKeyState(provider, key, tested: true);

                // Keys are persisted only after a passing test (D-07)
                try
                {
                    _keyStore.SetKey(provider, key);
                }
                catch (Exception)
                {
                    // Ignore save errors - persistence is a convenience; the key is still used this session
                }
            }
            else
            {
                SetStatus(provider, result.Message, Color.Red);
                SetTested(provider, false);
            }
        }
        catch (Exception ex)
        {
            SetStatus(provider, $"Test failed: {ex.Message}", Color.Red);
            SetTested(provider, false);
        }
        finally
        {
            button.Enabled = true;
            UpdateMediaStatus();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ClearKey(LlmProviderKind provider)
    {
        try
        {
            _keyStore.ClearKey(provider);
        }
        catch (Exception)
        {
            // Ignore store errors - the key is still removed from this session below
        }

        _suppressEvents = true;
        try
        {
            GetKeyBox(provider).Text = string.Empty;
        }
        finally
        {
            _suppressEvents = false;
        }

        SetKeyState(provider, string.Empty, tested: false);
        SetStatus(provider, "Key removed.", Color.Gray);
        UpdateMediaStatus();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CboProvider_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_suppressEvents || _state == null)
        {
            return;
        }

        var selected = _cboProvider.SelectedIndex == 1 ? LlmProviderKind.Anthropic : LlmProviderKind.OpenAI;
        if (selected != _state.TextProvider)
        {
            _state.SetTextProvider(selected);
        }

        RefreshModelDropdowns();
        UpdatePricingLabel();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CboTierModel_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_suppressEvents || _state == null)
        {
            return;
        }

        if (_cboStoryModel.SelectedItem is AIModelConfig story)
        {
            _state.StoryModelId = story.ModelId;
        }

        if (_cboVolumeModel.SelectedItem is AIModelConfig volume)
        {
            _state.VolumeModelId = volume.ModelId;
        }

        UpdatePricingLabel();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshModelDropdowns()
    {
        var models = _state.AvailableModelConfigs.Where(m => m.Provider == _state.TextProvider).ToList();

        _suppressEvents = true;
        try
        {
            FillTierCombo(_cboStoryModel, models, _state.StoryModelId, LlmTier.Story);
            FillTierCombo(_cboVolumeModel, models, _state.VolumeModelId, LlmTier.Volume);
        }
        finally
        {
            _suppressEvents = false;
        }

        // Write back the effective selection (covers fallback choices)
        _state.StoryModelId = (_cboStoryModel.SelectedItem as AIModelConfig)?.ModelId ?? string.Empty;
        _state.VolumeModelId = (_cboVolumeModel.SelectedItem as AIModelConfig)?.ModelId ?? string.Empty;
    }

    private void FillTierCombo(ComboBox combo, List<AIModelConfig> models, string selectedId, LlmTier tier)
    {
        combo.Items.Clear();
        foreach (var model in models)
        {
            combo.Items.Add(model);
        }

        var toSelect = models.FirstOrDefault(m => m.ModelId == selectedId)
            ?? AIModelConfig.GetTierDefault(models, _state.TextProvider, tier)
            ?? models.FirstOrDefault();

        if (toSelect != null)
        {
            combo.SelectedItem = toSelect;
        }
    }

    private void UpdateMediaStatus()
    {
        if (_state.IsMediaAvailable)
        {
            _lblMediaStatus.Text = "Images and voicemail available (OpenAI)";
            _lblMediaStatus.ForeColor = Color.Green;
        }
        else
        {
            _lblMediaStatus.Text = _state.MediaUnavailableNotice;
            _lblMediaStatus.ForeColor = Color.DarkOrange;
        }
    }

    private void UpdatePricingLabel()
    {
        var story = _state.StoryModelConfig;
        var volume = _state.VolumeModelConfig;
        if (story == null || volume == null)
        {
            _lblPricing.Text = "";
            return;
        }

        _lblPricing.Text =
            $"Story: {story.DisplayName} ${story.InputTokenPricePerMillion}/${story.OutputTokenPricePerMillion} per 1M tokens | " +
            $"Volume: {volume.DisplayName} ${volume.InputTokenPricePerMillion}/${volume.OutputTokenPricePerMillion} per 1M tokens";
    }

    private void BtnConfigureModels_Click(object? sender, EventArgs e)
    {
        using var dialog = new ModelConfigurationDialog(_state.AvailableModelConfigs);
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            _state.AvailableModelConfigs = dialog.Models;

            // Re-apply the provider's tier defaults when the selected models no longer exist
            if (_state.StoryModelConfig == null)
            {
                _state.StoryModelId = AIModelConfig.GetTierDefault(_state.AvailableModelConfigs, _state.TextProvider, LlmTier.Story)?.ModelId ?? string.Empty;
            }

            if (_state.VolumeModelConfig == null)
            {
                _state.VolumeModelId = AIModelConfig.GetTierDefault(_state.AvailableModelConfigs, _state.TextProvider, LlmTier.Volume)?.ModelId ?? string.Empty;
            }

            RefreshModelDropdowns();
            UpdatePricingLabel();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void BindState(WizardState state)
    {
        _state = state;
    }

    public Task OnEnterStepAsync()
    {
        // Load saved keys once per session; they still need a test before use
        if (!_keysLoaded)
        {
            _keysLoaded = true;
            foreach (var provider in new[] { LlmProviderKind.OpenAI, LlmProviderKind.Anthropic })
            {
                if (!string.IsNullOrEmpty(_state.GetApiKey(provider)))
                {
                    continue;
                }

                try
                {
                    var saved = _keyStore.GetKey(provider);
                    if (!string.IsNullOrEmpty(saved))
                    {
                        SetKeyState(provider, saved, tested: false);
                    }
                }
                catch (Exception)
                {
                    // Ignore load errors - the user can re-enter the key
                }
            }
        }

        _suppressEvents = true;
        try
        {
            _txtOpenAIKey.Text = _state.OpenAIApiKey;
            _txtAnthropicKey.Text = _state.AnthropicApiKey;
            _cboProvider.SelectedIndex = _state.TextProvider == LlmProviderKind.Anthropic ? 1 : 0;
        }
        finally
        {
            _suppressEvents = false;
        }

        foreach (var provider in new[] { LlmProviderKind.OpenAI, LlmProviderKind.Anthropic })
        {
            var tested = provider == LlmProviderKind.OpenAI ? _state.OpenAIKeyTested : _state.AnthropicKeyTested;
            if (tested)
            {
                SetStatus(provider, "Key verified.", Color.Green);
            }
            else if (!string.IsNullOrEmpty(_state.GetApiKey(provider)))
            {
                SetStatus(provider, "Saved key loaded. Click Test to verify.", Color.Gray);
            }
            else
            {
                SetStatus(provider, "", Color.Gray);
            }
        }

        RefreshModelDropdowns();
        UpdateMediaStatus();
        UpdatePricingLabel();
        StateChanged?.Invoke(this, EventArgs.Empty);

        return Task.CompletedTask;
    }

    public Task OnLeaveStepAsync()
    {
        return Task.CompletedTask;
    }

    public Task<bool> ValidateStepAsync()
    {
        if (!_state.CanProceedFromKeys())
        {
            var providerName = GetProviderName(_state.TextProvider);
            MessageBox.Show($"Please test your {providerName} API key before proceeding.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return Task.FromResult(false);
        }

        if (_state.StoryModelConfig == null || _state.VolumeModelConfig == null)
        {
            MessageBox.Show("Please select a Story model and a Volume model.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }
}
