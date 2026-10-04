using System.Text;
using MimeKit;
using ReelDiscovery.Models.Corpus;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// One attachment part read back from an existing EML copy: its decoded bytes and, for inline images, its Content-Id.
/// </summary>
public class HarvestedPart
{
    public byte[] Bytes { get; set; } = Array.Empty<byte>();
    public string ContentId { get; set; } = string.Empty;
}

/// <summary>
/// Reads succeeded attachment parts back out of the EML copies already on disk so a repair can rewrite an email
/// without regenerating them (D-26). Works per parent message: callers pass that message's copy paths and the DocIds
/// it should carry. Every read is bounded (V12, T-06-29) and never leaves the case folder (T-06-28).
/// </summary>
public static class AttachmentHarvester
{
    public const long MaxEmlBytes = 64L * 1024 * 1024;
    public const long MaxPartBytes = 25L * 1024 * 1024;
    public const long MaxHarvestBytes = 100L * 1024 * 1024;

    // Tests only: 0 means use the constant
    internal static long EmlCapOverride, PartCapOverride, TotalCapOverride;

    private static long EmlCap => EmlCapOverride > 0 ? EmlCapOverride : MaxEmlBytes;
    private static long PartCap => PartCapOverride > 0 ? PartCapOverride : MaxPartBytes;
    private static long TotalCap => TotalCapOverride > 0 ? TotalCapOverride : MaxHarvestBytes;

    // Long enough to hold the root closing boundary line ("--=-rd-" + 24 hex + "--") plus line endings
    private const int TailBytes = 512;

    /// <summary>
    /// Harvests the expected attachment parts of one message. Copies are tried in order; each expected DocId is taken
    /// from the first copy that has it complete, and the search continues in later copies for the rest, so a readable
    /// but incomplete copy never hides a complete one. DocIds found nowhere are absent from the result (the caller
    /// regenerates them); parts whose DocId is not expected are ignored.
    /// </summary>
    public static Dictionary<string, HarvestedPart> HarvestMessage(string caseFolder, IEnumerable<string> copyPaths, IReadOnlyCollection<string> expectedDocIds)
    {
        var result = new Dictionary<string, HarvestedPart>(StringComparer.Ordinal);
        var expected = new HashSet<string>((expectedDocIds ?? Array.Empty<string>()).Where(id => !string.IsNullOrEmpty(id)), StringComparer.Ordinal);
        if (expected.Count == 0 || copyPaths == null)
            return result;

        long total = 0;
        foreach (var relative in copyPaths)
        {
            if (result.Count == expected.Count || total >= TotalCap)
                break;

            // T-06-28: only safe relative mailbox paths that resolve under the case folder are ever opened
            if (!CorpusPlan.IsSafeEmlPath(relative))
                continue;
            var full = MailboxWriter.ResolveUnderCase(caseFolder, relative);
            if (full == null)
                continue;

            try
            {
                var info = new FileInfo(full);

                // T-06-29: oversized files are skipped before anything is loaded
                if (!info.Exists || info.Length > EmlCap)
                    continue;

                using var stream = info.OpenRead();
                var message = MimeMessage.Load(stream);

                // A copy cut short (no closing root boundary) could decode a partial part; trust only complete files
                if (message.Body is not Multipart root || !EndsWithClosingBoundary(stream, root.Boundary))
                    continue;

                foreach (var part in message.BodyParts.OfType<MimePart>())
                {
                    var docId = part.Headers[MailboxWriter.DocIdHeader];
                    if (string.IsNullOrEmpty(docId) || !expected.Contains(docId) || result.ContainsKey(docId) || part.Content == null)
                        continue;

                    var remaining = TotalCap - total;
                    var limit = Math.Min(PartCap, remaining);
                    try
                    {
                        using var bounded = new BoundedStream(limit);
                        part.Content.DecodeTo(bounded);
                        var bytes = bounded.ToArray();
                        result[docId] = new HarvestedPart { Bytes = bytes, ContentId = part.ContentId ?? string.Empty };
                        total += bytes.Length;
                    }
                    catch (InvalidDataException)
                    {
                        // An unreadable or incomplete copy just means another copy, or regeneration, provides the part (review concern 3)
                        if (remaining < PartCap)
                            return result; // the per-call total is spent: stop with what we have
                        continue;
                    }

                    if (result.Count == expected.Count || total >= TotalCap)
                        return result;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ParseException)
            {
                // An unreadable or incomplete copy just means another copy, or regeneration, provides the part (review concern 3)
            }
        }

        return result;
    }

    private static bool EndsWithClosingBoundary(Stream stream, string? boundary)
    {
        if (string.IsNullOrEmpty(boundary))
            return false;

        var length = (int)Math.Min(stream.Length, TailBytes);
        stream.Seek(-length, SeekOrigin.End);
        var tail = new byte[length];
        stream.ReadExactly(tail, 0, length);
        return Encoding.ASCII.GetString(tail).Contains("--" + boundary + "--", StringComparison.Ordinal);
    }

    /// <summary>
    /// Write-only memory sink that refuses to grow past its limit, so a hostile part cannot force a large allocation.
    /// </summary>
    private class BoundedStream : Stream
    {
        private readonly MemoryStream _inner = new();
        private readonly long _limit;

        public BoundedStream(long limit)
        {
            _limit = limit;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public byte[] ToArray() => _inner.ToArray();

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_inner.Length + count > _limit)
                throw new InvalidDataException($"Attachment part exceeds {_limit} bytes");
            _inner.Write(buffer, offset, count);
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
