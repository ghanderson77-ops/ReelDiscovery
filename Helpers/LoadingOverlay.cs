namespace ReelDiscovery.Helpers;

/// <summary>
/// A semi-transparent overlay panel with rotating fun loading messages.
/// Shows over a control while AI generation is in progress.
/// </summary>
public class LoadingOverlay : Panel
{
    private readonly DoubleBufferedLabel _lblMessage;
    private readonly System.Windows.Forms.Timer _messageTimer;
    private readonly System.Windows.Forms.Timer _dotsTimer;
    private int _currentMessageIndex = 0;
    private int _dotCount = 0;
    private readonly string[] _messages;
    private string _currentBaseMessage = "";

    private static readonly string[] EntityCheckMessages = new[]
    {
        "Checking for real names",
        "Looking for real people and companies",
        "Preparing fictional substitutes"
    };

    public enum LoadingType
    {
        EntityCheck
    }

    public LoadingOverlay(LoadingType type)
    {
        // Only the entity check uses the overlay since the legacy wizard steps were removed
        _messages = EntityCheckMessages;

        // Enable double buffering to prevent flickering
        this.SetStyle(ControlStyles.OptimizedDoubleBuffer |
                      ControlStyles.AllPaintingInWmPaint |
                      ControlStyles.UserPaint, true);
        this.UpdateStyles();

        // Semi-transparent dark background
        this.BackColor = Color.FromArgb(220, 30, 30, 35);
        this.Visible = false;

        // Single centered message label with double buffering
        _lblMessage = new DoubleBufferedLabel
        {
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 18F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            AutoSize = false,
            BackColor = Color.Transparent,
            Dock = DockStyle.Fill
        };

        this.Controls.Add(_lblMessage);

        // Timer to rotate messages every 5-8 seconds
        _messageTimer = new System.Windows.Forms.Timer
        {
            Interval = 5000 + Random.Shared.Next(3000)
        };
        _messageTimer.Tick += (s, e) =>
        {
            _currentMessageIndex = (_currentMessageIndex + 1) % _messages.Length;
            _currentBaseMessage = _messages[_currentMessageIndex];
            UpdateDisplayText();
            _messageTimer.Interval = 5000 + Random.Shared.Next(3000);
        };

        // Timer for animated dots
        _dotsTimer = new System.Windows.Forms.Timer
        {
            Interval = 400
        };
        _dotsTimer.Tick += (s, e) =>
        {
            _dotCount = (_dotCount + 1) % 4;
            UpdateDisplayText();
        };
    }

    private void UpdateDisplayText()
    {
        var dots = new string('.', _dotCount);
        _lblMessage.Text = _currentBaseMessage + dots;
    }

    public void Show(Control parent)
    {
        if (parent == null) return;

        // Position overlay to cover the parent
        this.Location = new Point(0, 0);
        this.Size = parent.Size;
        this.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        // Randomize starting message
        _currentMessageIndex = Random.Shared.Next(_messages.Length);
        _currentBaseMessage = _messages[_currentMessageIndex];
        _dotCount = 0;
        UpdateDisplayText();

        // Add to parent's controls and bring to front
        if (!parent.Controls.Contains(this))
        {
            parent.Controls.Add(this);
        }
        this.BringToFront();
        this.Visible = true;

        // Start timers
        _messageTimer.Start();
        _dotsTimer.Start();
    }

    public new void Hide()
    {
        _messageTimer.Stop();
        _dotsTimer.Stop();
        this.Visible = false;

        // Remove from parent
        if (this.Parent != null)
        {
            this.Parent.Controls.Remove(this);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _messageTimer?.Stop();
            _messageTimer?.Dispose();
            _dotsTimer?.Stop();
            _dotsTimer?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// A label with double buffering enabled to prevent flickering during updates.
    /// </summary>
    private class DoubleBufferedLabel : Label
    {
        public DoubleBufferedLabel()
        {
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer |
                          ControlStyles.AllPaintingInWmPaint |
                          ControlStyles.UserPaint |
                          ControlStyles.SupportsTransparentBackColor, true);
            this.UpdateStyles();
        }
    }
}
