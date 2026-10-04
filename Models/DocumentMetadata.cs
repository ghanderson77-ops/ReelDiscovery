namespace ReelDiscovery.Models;

/// <summary>
/// Package properties stamped on a generated Office attachment: the sender as author and dates just before the email.
/// </summary>
public class DocumentMetadata
{
    public string Author { get; set; } = string.Empty;
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset Modified { get; set; }
}

/// <summary>A headed section of a Word document.</summary>
public class WordSection
{
    public string Heading { get; set; } = string.Empty;
    public List<string> Paragraphs { get; set; } = new();
}

/// <summary>A simple header-plus-rows table for a Word document.</summary>
public class SimpleTable
{
    public List<string> Headers { get; set; } = new();
    public List<List<string>> Rows { get; set; } = new();
}

/// <summary>A content slide: a title and its bullets.</summary>
public class SlideContent
{
    public string Title { get; set; } = string.Empty;
    public List<string> Bullets { get; set; } = new();
}
