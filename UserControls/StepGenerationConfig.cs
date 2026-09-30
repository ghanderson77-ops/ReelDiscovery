using ReelDiscovery.Helpers;
using ReelDiscovery.Models;

namespace ReelDiscovery.UserControls;

public class StepGenerationConfig : UserControl, IWizardStep
{
    private WizardState _state = null!;
    private NumericUpDown _numEmailCount = null!;
    private NumericUpDown _numParallelThreads = null!;
    private CheckBox _chkAISuggestDates = null!;
    private DateTimePicker _dtpStartDate = null!;
    private DateTimePicker _dtpEndDate = null!;
    private NumericUpDown _numAttachmentPercent = null!;
    private ComboBox _cboAttachmentComplexity = null!;
    private CheckBox _chkWord = null!;
    private CheckBox _chkExcel = null!;
    private CheckBox _chkPowerPoint = null!;
    private CheckBox _chkImages = null!;
    private NumericUpDown _numImagePercent = null!;
    private CheckBox _chkVoicemails = null!;
    private NumericUpDown _numVoicemailPercent = null!;
    private CheckBox _chkCalendarInvites = null!;
    private NumericUpDown _numCalendarPercent = null!;
    private TextBox _txtOutputFolder = null!;
    private Button _btnBrowse = null!;
    private CheckBox _chkOrganizeBySender = null!;
    private Label _lblMediaNotice = null!;

    public string StepTitle => "Generation Settings";
    public bool CanMoveNext => !string.IsNullOrWhiteSpace(_txtOutputFolder?.Text);
    public bool CanMoveBack => true;
    public string NextButtonText => "Generate Emails >";

    public event EventHandler? StateChanged;

    public StepGenerationConfig()
    {
        InitializeUI();
    }

    private void InitializeUI()
    {
        this.Padding = new Padding(20);

        // Use a scrollable panel for the content
        var scrollPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true
        };

        var mainPanel = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 0, 20, 20)
        };

        int y = 0;
        const int rowHeight = 38;
        const int sectionGap = 20;
        const int labelWidth = 180;

        // === Generation Settings Section ===
        y = AddSectionHeader(mainPanel, "Generation", y);

        y = AddLabeledRow(mainPanel, "Number of Emails:", y, labelWidth, rowHeight, () =>
        {
            _numEmailCount = new NumericUpDown
            {
                Minimum = 5,
                Maximum = 500,
                Value = 50,
                Width = 100,
                Font = new Font(this.Font.FontFamily, 10F)
            };
            return _numEmailCount;
        });

        y = AddLabeledRow(mainPanel, "Parallel API Calls:", y, labelWidth, rowHeight, () =>
        {
            var panel = new Panel { Width = 450, Height = rowHeight };
            _numParallelThreads = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 10,
                Value = 3,
                Width = 70,
                Font = new Font(this.Font.FontFamily, 10F),
                Location = new Point(0, 5)
            };
            var helpLabel = new Label
            {
                Text = "(Higher = faster, but uses more API quota)",
                AutoSize = true,
                ForeColor = Color.DimGray,
                Font = new Font(this.Font.FontFamily, 9F),
                Location = new Point(80, 9)
            };
            panel.Controls.Add(_numParallelThreads);
            panel.Controls.Add(helpLabel);
            return panel;
        });

        y += sectionGap;

        // === Date Range Section ===
        y = AddSectionHeader(mainPanel, "Date Range", y);

        // AI suggest dates checkbox (spans both columns)
        _chkAISuggestDates = new CheckBox
        {
            Text = "Let AI suggest dates based on topic",
            Checked = true,
            AutoSize = true,
            Font = new Font(this.Font.FontFamily, 10F),
            Location = new Point(labelWidth, y + 6)
        };
        _chkAISuggestDates.CheckedChanged += (s, e) =>
        {
            _dtpStartDate.Enabled = !_chkAISuggestDates.Checked;
            _dtpEndDate.Enabled = !_chkAISuggestDates.Checked;
        };
        mainPanel.Controls.Add(_chkAISuggestDates);
        y += rowHeight;

        y = AddLabeledRow(mainPanel, "Start Date:", y, labelWidth, rowHeight, () =>
        {
            _dtpStartDate = new DateTimePicker
            {
                Format = DateTimePickerFormat.Short,
                Width = 160,
                Font = new Font(this.Font.FontFamily, 10F),
                Enabled = false
            };
            return _dtpStartDate;
        });

        y = AddLabeledRow(mainPanel, "End Date:", y, labelWidth, rowHeight, () =>
        {
            _dtpEndDate = new DateTimePicker
            {
                Format = DateTimePickerFormat.Short,
                Width = 160,
                Font = new Font(this.Font.FontFamily, 10F),
                Enabled = false
            };
            return _dtpEndDate;
        });

        y += sectionGap;

        // === Attachments Section ===
        y = AddSectionHeader(mainPanel, "Attachments", y);

        y = AddLabeledRow(mainPanel, "% with Attachments:", y, labelWidth, rowHeight, () =>
        {
            _numAttachmentPercent = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 100,
                Value = 20,
                Width = 90,
                Font = new Font(this.Font.FontFamily, 10F)
            };
            return _numAttachmentPercent;
        });

        y = AddLabeledRow(mainPanel, "Complexity:", y, labelWidth, rowHeight, () =>
        {
            _cboAttachmentComplexity = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 160,
                Font = new Font(this.Font.FontFamily, 10F)
            };
            _cboAttachmentComplexity.Items.Add("Simple");
            _cboAttachmentComplexity.Items.Add("Detailed");
            _cboAttachmentComplexity.SelectedIndex = 1;
            return _cboAttachmentComplexity;
        });

        y = AddLabeledRow(mainPanel, "Include Types:", y, labelWidth, 40, () =>
        {
            var panel = new Panel { Width = 500, Height = 40 };

            _chkWord = new CheckBox
            {
                Text = "Word (.docx)",
                Checked = true,
                AutoSize = true,
                Font = new Font(this.Font.FontFamily, 10F),
                Location = new Point(0, 8)
            };

            _chkExcel = new CheckBox
            {
                Text = "Excel (.xlsx)",
                Checked = true,
                AutoSize = true,
                Font = new Font(this.Font.FontFamily, 10F),
                Location = new Point(140, 8)
            };

            _chkPowerPoint = new CheckBox
            {
                Text = "PowerPoint (.pptx)",
                Checked = true,
                AutoSize = true,
                Font = new Font(this.Font.FontFamily, 10F),
                Location = new Point(280, 8)
            };

            panel.Controls.Add(_chkWord);
            panel.Controls.Add(_chkExcel);
            panel.Controls.Add(_chkPowerPoint);
            return panel;
        });

        y += sectionGap;

        // === Images Section ===
        y = AddSectionHeader(mainPanel, "AI-Generated Images (OpenAI gpt-image)", y);

        // Shown when no tested OpenAI key exists (images/voicemail need OpenAI, PROV-06)
        y = AddLabeledRow(mainPanel, "", y, labelWidth, rowHeight, () =>
        {
            var panel = new Panel { Width = 600, Height = rowHeight };
            _lblMediaNotice = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DarkOrange,
                Font = new Font(this.Font.FontFamily, 9.5F, FontStyle.Bold),
                Visible = false
            };
            panel.Controls.Add(_lblMediaNotice);
            return panel;
        });

        y = AddLabeledRow(mainPanel, "Include Images:", y, labelWidth, rowHeight, () =>
        {
            var panel = new Panel { Width = 500, Height = rowHeight };
            _chkImages = new CheckBox
            {
                Text = "Generate images for emails (uses the OpenAI image API)",
                Checked = false,
                AutoSize = true,
                Font = new Font(this.Font.FontFamily, 10F),
                Location = new Point(0, 8)
            };
            _chkImages.CheckedChanged += (s, e) =>
            {
                _numImagePercent.Enabled = _chkImages.Checked;
            };
            panel.Controls.Add(_chkImages);
            return panel;
        });

        y = AddLabeledRow(mainPanel, "% with Images:", y, labelWidth, rowHeight, () =>
        {
            var panel = new Panel { Width = 540, Height = rowHeight };
            _numImagePercent = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 50,
                Value = 10,
                Width = 70,
                Font = new Font(this.Font.FontFamily, 10F),
                Location = new Point(0, 5),
                Enabled = false
            };
            var helpLabel = new Label
            {
                Text = "(Images are inline or attachments, about $0.006/image at low quality)",
                AutoSize = true,
                ForeColor = Color.DimGray,
                Font = new Font(this.Font.FontFamily, 9F),
                Location = new Point(80, 9)
            };
            panel.Controls.Add(_numImagePercent);
            panel.Controls.Add(helpLabel);
            return panel;
        });

        y += sectionGap;

        // === Voicemails Section ===
        y = AddSectionHeader(mainPanel, "AI-Generated Voicemails (TTS)", y);

        y = AddLabeledRow(mainPanel, "Include Voicemails:", y, labelWidth, rowHeight, () =>
        {
            var panel = new Panel { Width = 500, Height = rowHeight };
            _chkVoicemails = new CheckBox
            {
                Text = "Generate voicemail audio attachments (uses TTS API)",
                Checked = false,
                AutoSize = true,
                Font = new Font(this.Font.FontFamily, 10F),
                Location = new Point(0, 8)
            };
            _chkVoicemails.CheckedChanged += (s, e) =>
            {
                _numVoicemailPercent.Enabled = _chkVoicemails.Checked;
            };
            panel.Controls.Add(_chkVoicemails);
            return panel;
        });

        y = AddLabeledRow(mainPanel, "% with Voicemails:", y, labelWidth, rowHeight, () =>
        {
            var panel = new Panel { Width = 450, Height = rowHeight };
            _numVoicemailPercent = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 25,
                Value = 5,
                Width = 70,
                Font = new Font(this.Font.FontFamily, 10F),
                Location = new Point(0, 5),
                Enabled = false
            };
            var helpLabel = new Label
            {
                Text = "(MP3 voicemails with character voices, ~$0.015/voicemail)",
                AutoSize = true,
                ForeColor = Color.DimGray,
                Font = new Font(this.Font.FontFamily, 9F),
                Location = new Point(80, 9)
            };
            panel.Controls.Add(_numVoicemailPercent);
            panel.Controls.Add(helpLabel);
            return panel;
        });

        y += sectionGap;

        // === Calendar Invites Section ===
        y = AddSectionHeader(mainPanel, "Calendar Invites", y);

        y = AddLabeledRow(mainPanel, "Include Invites:", y, labelWidth, rowHeight, () =>
        {
            var panel = new Panel { Width = 500, Height = rowHeight };
            _chkCalendarInvites = new CheckBox
            {
                Text = "Auto-detect meetings and attach .ics calendar invites",
                Checked = true,
                AutoSize = true,
                Font = new Font(this.Font.FontFamily, 10F),
                Location = new Point(0, 8)
            };
            _chkCalendarInvites.CheckedChanged += (s, e) =>
            {
                _numCalendarPercent.Enabled = _chkCalendarInvites.Checked;
            };
            panel.Controls.Add(_chkCalendarInvites);
            return panel;
        });

        y = AddLabeledRow(mainPanel, "% to Check:", y, labelWidth, rowHeight, () =>
        {
            var panel = new Panel { Width = 450, Height = rowHeight };
            _numCalendarPercent = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 50,
                Value = 10,
                Width = 70,
                Font = new Font(this.Font.FontFamily, 10F),
                Location = new Point(0, 5)
            };
            var helpLabel = new Label
            {
                Text = "(% of emails to check for meeting references)",
                AutoSize = true,
                ForeColor = Color.DimGray,
                Font = new Font(this.Font.FontFamily, 9F),
                Location = new Point(80, 9)
            };
            panel.Controls.Add(_numCalendarPercent);
            panel.Controls.Add(helpLabel);
            return panel;
        });

        y += sectionGap;

        // === Output Section ===
        y = AddSectionHeader(mainPanel, "Output", y);

        y = AddLabeledRow(mainPanel, "Output Folder:", y, labelWidth, rowHeight + 5, () =>
        {
            var panel = new Panel { Width = 530, Height = rowHeight + 5 };
            _txtOutputFolder = new TextBox
            {
                Width = 380,
                Font = new Font(this.Font.FontFamily, 10F),
                Location = new Point(0, 5)
            };
            _txtOutputFolder.TextChanged += (s, e) => StateChanged?.Invoke(this, EventArgs.Empty);
            _btnBrowse = ButtonHelper.CreateButton("Browse...", 90, 30, ButtonStyle.Default);
            _btnBrowse.Location = new Point(390, 3);
            _btnBrowse.Click += BtnBrowse_Click;
            panel.Controls.Add(_txtOutputFolder);
            panel.Controls.Add(_btnBrowse);
            return panel;
        });

        // Organize by sender checkbox
        _chkOrganizeBySender = new CheckBox
        {
            Text = "Organize emails into subfolders by sender",
            Checked = true,
            AutoSize = true,
            Font = new Font(this.Font.FontFamily, 10F),
            Location = new Point(labelWidth, y + 6)
        };
        mainPanel.Controls.Add(_chkOrganizeBySender);
        y += rowHeight + sectionGap;

        mainPanel.Height = y + 20;
        scrollPanel.Controls.Add(mainPanel);
        this.Controls.Add(scrollPanel);
    }

    private int AddSectionHeader(Panel parent, string text, int y)
    {
        var label = new Label
        {
            Text = text,
            Font = new Font(this.Font.FontFamily, 11F, FontStyle.Bold),
            ForeColor = Color.FromArgb(0, 120, 215),
            AutoSize = true,
            Location = new Point(0, y)
        };
        parent.Controls.Add(label);

        // Add a subtle line under the header
        var line = new Panel
        {
            BackColor = Color.FromArgb(200, 200, 200),
            Height = 2,
            Width = 580,
            Location = new Point(0, y + 26)
        };
        parent.Controls.Add(line);

        return y + 38;
    }

    private int AddLabeledRow(Panel parent, string labelText, int y, int labelWidth, int rowHeight, Func<Control> createControl)
    {
        var label = new Label
        {
            Text = labelText,
            AutoSize = false,
            Width = labelWidth,
            Height = rowHeight,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(this.Font.FontFamily, 10F),
            Location = new Point(0, y)
        };
        parent.Controls.Add(label);

        var control = createControl();
        control.Location = new Point(labelWidth, y + (rowHeight - control.Height) / 2);
        parent.Controls.Add(control);

        return y + rowHeight;
    }

    private void BtnBrowse_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select output folder for generated emails",
            UseDescriptionForTitle = true
        };

        if (!string.IsNullOrEmpty(_txtOutputFolder.Text) && Directory.Exists(_txtOutputFolder.Text))
        {
            dialog.InitialDirectory = _txtOutputFolder.Text;
        }

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            _txtOutputFolder.Text = dialog.SelectedPath;
        }
    }

    public void BindState(WizardState state)
    {
        _state = state;
    }

    public Task OnEnterStepAsync()
    {
        // Load from state
        _numEmailCount.Value = _state.Config.TotalEmailCount;
        _numParallelThreads.Value = Math.Max(1, Math.Min(10, _state.Config.ParallelThreads));
        _chkAISuggestDates.Checked = _state.Config.LetAISuggestDates;

        // Set date pickers
        if (_state.AISuggestedStartDate.HasValue)
            _dtpStartDate.Value = _state.AISuggestedStartDate.Value;
        else
            _dtpStartDate.Value = _state.Config.StartDate;

        if (_state.AISuggestedEndDate.HasValue)
            _dtpEndDate.Value = _state.AISuggestedEndDate.Value;
        else
            _dtpEndDate.Value = _state.Config.EndDate;

        _numAttachmentPercent.Value = _state.Config.AttachmentPercentage;
        _cboAttachmentComplexity.SelectedIndex = _state.Config.AttachmentComplexity == AttachmentComplexity.Detailed ? 1 : 0;
        _chkWord.Checked = _state.Config.IncludeWord;
        _chkExcel.Checked = _state.Config.IncludeExcel;
        _chkPowerPoint.Checked = _state.Config.IncludePowerPoint;
        _chkImages.Checked = _state.Config.IncludeImages;
        _numImagePercent.Value = _state.Config.ImagePercentage;
        _numImagePercent.Enabled = _state.Config.IncludeImages;

        _chkVoicemails.Checked = _state.Config.IncludeVoicemails;
        _numVoicemailPercent.Value = _state.Config.VoicemailPercentage;
        _numVoicemailPercent.Enabled = _state.Config.IncludeVoicemails;

        // Gate image/voicemail options on a tested OpenAI key (PROV-06)
        var mediaAvailable = _state.IsMediaAvailable;
        if (!mediaAvailable)
        {
            _chkImages.Checked = false;
            _chkVoicemails.Checked = false;
        }
        _chkImages.Enabled = mediaAvailable;
        _chkVoicemails.Enabled = mediaAvailable;
        _numImagePercent.Enabled = mediaAvailable && _chkImages.Checked;
        _numVoicemailPercent.Enabled = mediaAvailable && _chkVoicemails.Checked;
        _lblMediaNotice.Text = _state.MediaUnavailableNotice;
        _lblMediaNotice.Visible = !mediaAvailable;

        _chkCalendarInvites.Checked = _state.Config.IncludeCalendarInvites;
        _numCalendarPercent.Value = _state.Config.CalendarInvitePercentage;
        _numCalendarPercent.Enabled = _state.Config.IncludeCalendarInvites;

        if (!string.IsNullOrEmpty(_state.Config.OutputFolder))
            _txtOutputFolder.Text = _state.Config.OutputFolder;

        _chkOrganizeBySender.Checked = _state.Config.OrganizeBySender;

        return Task.CompletedTask;
    }

    public Task OnLeaveStepAsync()
    {
        return Task.CompletedTask;
    }

    public Task<bool> ValidateStepAsync()
    {
        if (string.IsNullOrWhiteSpace(_txtOutputFolder.Text))
        {
            MessageBox.Show("Please select an output folder.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return Task.FromResult(false);
        }

        if (!_chkAISuggestDates.Checked && _dtpStartDate.Value >= _dtpEndDate.Value)
        {
            MessageBox.Show("End date must be after start date.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return Task.FromResult(false);
        }

        if (_numAttachmentPercent.Value > 0 && !_chkWord.Checked && !_chkExcel.Checked && !_chkPowerPoint.Checked)
        {
            MessageBox.Show("Please select at least one attachment type, or set attachment percentage to 0.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return Task.FromResult(false);
        }

        // Save to state
        _state.Config.TotalEmailCount = (int)_numEmailCount.Value;
        _state.Config.ParallelThreads = (int)_numParallelThreads.Value;
        _state.Config.LetAISuggestDates = _chkAISuggestDates.Checked;
        _state.Config.StartDate = _dtpStartDate.Value;
        _state.Config.EndDate = _dtpEndDate.Value;
        _state.Config.AttachmentPercentage = (int)_numAttachmentPercent.Value;
        _state.Config.AttachmentComplexity = _cboAttachmentComplexity.SelectedIndex == 1
            ? AttachmentComplexity.Detailed
            : AttachmentComplexity.Simple;
        _state.Config.IncludeWord = _chkWord.Checked;
        _state.Config.IncludeExcel = _chkExcel.Checked;
        _state.Config.IncludePowerPoint = _chkPowerPoint.Checked;
        _state.Config.IncludeImages = _chkImages.Checked;
        _state.Config.ImagePercentage = (int)_numImagePercent.Value;
        _state.Config.IncludeVoicemails = _chkVoicemails.Checked;
        _state.Config.VoicemailPercentage = (int)_numVoicemailPercent.Value;
        _state.ApplyMediaAvailability();
        _state.Config.IncludeCalendarInvites = _chkCalendarInvites.Checked;
        _state.Config.CalendarInvitePercentage = (int)_numCalendarPercent.Value;
        _state.Config.OutputFolder = _txtOutputFolder.Text;
        _state.Config.OrganizeBySender = _chkOrganizeBySender.Checked;

        // Create output folder if it doesn't exist
        try
        {
            Directory.CreateDirectory(_txtOutputFolder.Text);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Cannot create output folder: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }
}
