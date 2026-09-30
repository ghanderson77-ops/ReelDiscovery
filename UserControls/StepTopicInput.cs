using ReelDiscovery.Models;

namespace ReelDiscovery.UserControls;

public class StepTopicInput : UserControl, IWizardStep
{
    private WizardState _state = null!;
    private TextBox _txtTopic = null!;
    private TextBox _txtInstructions = null!;
    private NumericUpDown _numStorylineCount = null!;
    private CheckBox _chkDocuments = null!;
    private CheckBox _chkImages = null!;
    private CheckBox _chkVoicemails = null!;
    private Label _lblMediaNotice = null!;

    public string StepTitle => "Topic Selection";
    public bool CanMoveNext => !string.IsNullOrWhiteSpace(_txtTopic?.Text);
    public bool CanMoveBack => true;
    public string NextButtonText => "Generate Storylines >";

    public event EventHandler? StateChanged;

    public StepTopicInput()
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
            RowCount = 9,
            Padding = new Padding(10)
        };

        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));  // 0: Topic label
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));  // 1: Topic input
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));  // 2: Instructions label
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));   // 3: Instructions input
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));  // 4: Storyline count label
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));  // 5: Storyline count input
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));  // 6: Media types label
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));  // 7: Media type checkboxes + unavailable notice (wraps)
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));   // 8: Help text

        // Topic label
        var lblTopic = new Label
        {
            Text = "Topic (Movie, TV Show, Book, or Subject):",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Font = new Font(this.Font, FontStyle.Bold)
        };
        mainLayout.Controls.Add(lblTopic, 0, 0);

        // Topic input
        _txtTopic = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 11F),
            PlaceholderText = "e.g., The Office, Game of Thrones, To Kill a Mockingbird, Corporate Merger..."
        };
        _txtTopic.TextChanged += (s, e) => StateChanged?.Invoke(this, EventArgs.Empty);
        mainLayout.Controls.Add(_txtTopic, 0, 1);

        // Instructions label
        var lblInstructions = new Label
        {
            Text = "Additional Instructions (Optional):",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Font = new Font(this.Font, FontStyle.Bold)
        };
        mainLayout.Controls.Add(lblInstructions, 0, 2);

        // Instructions input
        _txtInstructions = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Segoe UI", 10F),
            PlaceholderText = "Add any specific instructions here...\n\nExamples:\n- Focus on legal issues and compliance problems\n- Include financial fraud storylines\n- Make the tone more dramatic\n- Include HR complaints and workplace issues"
        };
        mainLayout.Controls.Add(_txtInstructions, 0, 3);

        // Storyline count label
        var lblStorylineCount = new Label
        {
            Text = "Number of Storylines to Generate:",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Font = new Font(this.Font, FontStyle.Bold)
        };
        mainLayout.Controls.Add(lblStorylineCount, 0, 4);

        // Storyline count input
        _numStorylineCount = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 100,
            Value = 10,
            Width = 80,
            Font = new Font("Segoe UI", 10F)
        };
        mainLayout.Controls.Add(_numStorylineCount, 0, 5);

        // Media types label
        var lblMediaTypes = new Label
        {
            Text = "Include in Storylines (optional - helps AI craft relevant scenarios):",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Font = new Font(this.Font, FontStyle.Bold)
        };
        mainLayout.Controls.Add(lblMediaTypes, 0, 6);

        // Media type checkboxes panel
        var mediaPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };

        _chkDocuments = new CheckBox
        {
            Text = "📄 Documents (reports, spreadsheets)",
            AutoSize = true,
            Checked = true,
            Font = new Font("Segoe UI", 9.5F),
            Margin = new Padding(0, 5, 20, 0)
        };
        mediaPanel.Controls.Add(_chkDocuments);

        _chkImages = new CheckBox
        {
            Text = "🖼️ Images (photos, evidence)",
            AutoSize = true,
            Checked = false,
            Font = new Font("Segoe UI", 9.5F),
            Margin = new Padding(0, 5, 20, 0)
        };
        mediaPanel.Controls.Add(_chkImages);

        _chkVoicemails = new CheckBox
        {
            Text = "🎙️ Voicemails (audio messages)",
            AutoSize = true,
            Checked = false,
            Font = new Font("Segoe UI", 9.5F),
            Margin = new Padding(0, 5, 0, 0)
        };
        mediaPanel.Controls.Add(_chkVoicemails);

        // Shown when no tested OpenAI key exists (images/voicemail need OpenAI)
        _lblMediaNotice = new Label
        {
            AutoSize = true,
            ForeColor = Color.DarkOrange,
            Font = new Font("Segoe UI", 9F),
            Margin = new Padding(20, 7, 0, 0),
            Visible = false
        };
        mediaPanel.Controls.Add(_lblMediaNotice);

        mainLayout.Controls.Add(mediaPanel, 0, 7);

        // Help text
        var helpText = new Label
        {
            Text = "Tips:\n" +
                   "• For movies/TV shows/books, the AI will create storylines inspired by that content\n" +
                   "• Characters will be named appropriately for the topic\n" +
                   "• Email dates will be set to match the topic's time period when possible\n" +
                   "• You can use general topics like 'Healthcare Company' or 'Tech Startup'\n" +
                   "• Checking media types helps create storylines with natural attachment opportunities",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            ForeColor = Color.DimGray,
            Padding = new Padding(0, 10, 0, 0)
        };
        mainLayout.Controls.Add(helpText, 0, 8);

        this.Controls.Add(mainLayout);
    }

    public void BindState(WizardState state)
    {
        _state = state;
    }

    public Task OnEnterStepAsync()
    {
        if (!string.IsNullOrEmpty(_state.Topic))
        {
            _txtTopic.Text = _state.Topic;
        }
        if (!string.IsNullOrEmpty(_state.AdditionalInstructions))
        {
            _txtInstructions.Text = _state.AdditionalInstructions;
        }
        // Clamp storyline count to valid range
        var storylineCount = Math.Max((int)_numStorylineCount.Minimum,
            Math.Min((int)_numStorylineCount.Maximum, _state.StorylineCount));
        _numStorylineCount.Value = storylineCount;

        // Restore media type preferences
        _chkDocuments.Checked = _state.WantsDocuments;
        _chkImages.Checked = _state.WantsImages;
        _chkVoicemails.Checked = _state.WantsVoicemails;

        // Gate image/voicemail options on a tested OpenAI key (PROV-06)
        var mediaAvailable = _state.IsMediaAvailable;
        _chkImages.Enabled = mediaAvailable;
        _chkVoicemails.Enabled = mediaAvailable;
        if (!mediaAvailable)
        {
            _chkImages.Checked = false;
            _chkVoicemails.Checked = false;
        }
        _lblMediaNotice.Text = _state.MediaUnavailableNotice;
        _lblMediaNotice.Visible = !mediaAvailable;

        return Task.CompletedTask;
    }

    public Task OnLeaveStepAsync()
    {
        return Task.CompletedTask;
    }

    public Task<bool> ValidateStepAsync()
    {
        if (string.IsNullOrWhiteSpace(_txtTopic.Text))
        {
            MessageBox.Show("Please enter a topic.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return Task.FromResult(false);
        }

        _state.Topic = _txtTopic.Text.Trim();
        _state.AdditionalInstructions = _txtInstructions.Text.Trim();
        _state.StorylineCount = (int)_numStorylineCount.Value;

        // Save media type preferences
        _state.WantsDocuments = _chkDocuments.Checked;
        _state.WantsImages = _chkImages.Checked;
        _state.WantsVoicemails = _chkVoicemails.Checked;

        // Also pre-populate the generation config so these carry forward
        _state.Config.IncludeImages = _chkImages.Checked;
        _state.Config.IncludeVoicemails = _chkVoicemails.Checked;
        _state.ApplyMediaAvailability();

        // Clear previously generated data if topic changed
        if (_state.Storylines.Count > 0)
        {
            _state.Storylines.Clear();
            _state.Characters.Clear();
            _state.GeneratedThreads.Clear();
        }

        return Task.FromResult(true);
    }
}
