namespace ReelDiscovery.Models.Evidence;

/// <summary>
/// How openly a message conveys its evidence. None = quoted-basis reaction; Counsel = privileged legal advice (D-17/A5).
/// </summary>
public enum Subtlety { None, Indirect, Clear, Explicit, Counsel }

public enum LengthBand { Short, Medium, Long }

/// <summary>
/// How a privileged thread is labeled. None = not privileged (D-18).
/// </summary>
public enum PrivilegeMarking { None, Banner, BannerAndSubject, Unlabeled }

public class LengthBandSpec
{
    public LengthBand Band { get; set; }
    public int MinWords { get; set; }
    public int MaxWords { get; set; }

    // Share of messages assigned this band (0..1)
    public double Share { get; set; }
}

/// <summary>
/// Code-chosen rendering knobs for one message.
/// </summary>
public class MessageAssignment
{
    public string MessageId { get; set; } = string.Empty;
    public int Seq { get; set; }
    public Subtlety Subtlety { get; set; }
    public LengthBand Length { get; set; }
}

/// <summary>
/// Code-chosen rendering knobs for one thread.
/// </summary>
public class ThreadAssignment
{
    public string ThreadId { get; set; } = string.Empty;
    public PrivilegeMarking Marking { get; set; }
    public List<MessageAssignment> Messages { get; set; } = new();

    public MessageAssignment? For(int seq) => Messages.FirstOrDefault(m => m.Seq == seq);
}
