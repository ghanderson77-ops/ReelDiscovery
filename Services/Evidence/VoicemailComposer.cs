using System.Globalization;
using System.Text.RegularExpressions;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Corpus;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// Turns a planned voicemail (a single-message thread with a Voicemail attachment) into the phone-system notice email
/// (D-28): sender "{Recipient org} Voice Mail", subject "Voicemail from {Caller} ({m:ss})", a code-written body with the
/// transcript and no signature. Pure and deterministic; the message body stays the spoken transcript in BodyPlain.
/// </summary>
public static partial class VoicemailComposer
{
    public const string TranscriptHeader = "Transcript (auto-generated):";

    private const string VoiceMailName = "Voice Mail";
    private const int MinimumSeconds = 5;

    [GeneratedRegex(@"\p{Cc}")]
    private static partial Regex ControlCharRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    /// <summary>
    /// true for the only message of a single-message thread that carries a Voicemail attachment (the plan rule).
    /// </summary>
    public static bool IsVoicemail(PlannedThread thread, PlannedMessage message)
    {
        if (thread?.Messages == null || message == null || thread.Messages.Count(m => m != null) != 1)
            return false;

        return (message.Attachments ?? new List<PlannedAttachment>()).Any(a => a != null && a.Kind == AttachmentType.Voicemail);
    }

    /// <summary>
    /// Spoken length of a transcript in seconds, never below 5.
    /// </summary>
    public static int DurationSeconds(string transcript)
    {
        // ~150 words per minute (06-RESEARCH A4)
        var words = WhitespaceRegex().Split((transcript ?? string.Empty).Trim()).Count(w => w.Length > 0);
        return Math.Max(MinimumSeconds, (int)Math.Round(words / 2.5));
    }

    /// <summary>
    /// "m:ss", e.g. "0:42" or "1:05".
    /// </summary>
    public static string Duration(int seconds)
    {
        var clamped = Math.Max(0, seconds);
        return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", clamped / 60, clamped % 60);
    }

    /// <summary>
    /// Overrides From, Subject, PlainText, Html and the fragments of a composed voicemail email. Message-ID, Date, To,
    /// Cc, threading, BodyPlain (the transcript, kept for TTS on retry) and attachments are left as planned.
    /// </summary>
    public static void Apply(ComposedEmail email, PlannedThread thread, PeopleDirectory people)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(people);

        var planned = email.Planned;
        var caller = people.Find(planned.FromId);
        var callerName = Clean(caller != null && caller.FullName.Trim().Length > 0 ? caller.FullName : email.From.Name);
        var phone = Clean(caller?.Cast?.Phone ?? string.Empty);

        // The phone system belongs to the first recipient's organization
        var recipient = people.Find((planned.ToIds ?? new List<string>()).FirstOrDefault());
        var organization = Clean(recipient?.OrganizationName ?? string.Empty);
        var domain = EmailComposer.SanitizeDomain(recipient?.Domain);

        var transcript = email.BodyPlain;
        var duration = Duration(DurationSeconds(transcript));
        var local = MessageTime.Local(planned);

        // Control characters are stripped so names can never inject headers (T-06-25)
        email.From = new EmailAddressPart
        {
            Name = organization.Length > 0 ? organization + " " + VoiceMailName : VoiceMailName,
            Email = "voicemail@" + domain
        };
        email.Subject = ControlCharRegex().Replace($"Voicemail from {callerName} ({duration})", string.Empty).Trim();

        var callerText = phone.Length > 0 ? $"{callerName} ({phone})" : callerName;
        var notice = string.Format(CultureInfo.InvariantCulture,
            "You received a voicemail from {0} on {1:ddd, MMM d, yyyy} at {1:h:mm tt}.\n\nDuration: {2}\n\n{3}\n{4}",
            callerText, local, duration, TranscriptHeader, transcript);

        // Business template, no signature; every string is encoded by the allowlist formatter (T-06-26)
        var theme = OrganizationThemes.ForDomain(domain);
        email.SignOff = string.Empty;
        email.Signature = string.Empty;
        email.PlainText = notice;
        email.HtmlFragment = HtmlEmailFormatter.ComposeFragment(notice, string.Empty, string.Empty, null, theme, false,
            allowTable: false, inlineImages: null);
        email.QuoteFragment = email.HtmlFragment;
        email.Html = HtmlEmailFormatter.ComposeHtml(notice, string.Empty, string.Empty, null, theme, false,
            allowTable: false, inlineImages: null);
    }

    // Single-line, control-free display text
    private static string Clean(string text) =>
        WhitespaceRegex().Replace(ControlCharRegex().Replace(text ?? string.Empty, " "), " ").Trim();
}
