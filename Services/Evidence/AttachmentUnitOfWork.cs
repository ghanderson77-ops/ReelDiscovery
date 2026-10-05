using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Services.Evidence;

public enum AttachmentOutcomeKind { Written, Warnings, Failed, Skipped, NotRun }

/// <summary>
/// How one attachment's unit of work ended. Identity is filled first, so Failed, Skipped and NotRun outcomes produce
/// complete journal records without bytes (review concern 2). NotRun = a fatal text-provider error stopped the run; it
/// carries its spend but no record.
/// </summary>
public class AttachmentOutcome
{
    public string DocId { get; set; } = string.Empty;
    public AttachmentType AttachmentKind { get; set; }
    public string ThreadId { get; set; } = string.Empty;
    public string ParentDocId { get; set; } = string.Empty;
    public int ParentSeq { get; set; }
    public bool IsInline { get; set; }
    public bool Relevant { get; set; }
    public bool LateRepair { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    public AttachmentOutcomeKind Kind { get; set; }

    // In memory only; null for every outcome but Written / Warnings
    public ComposedAttachment? Part { get; set; }

    public List<string> Violations { get; set; } = new();
    public string FlattenedText { get; set; } = string.Empty;

    // Relevant only: Seq = parent seq, FromAttachment = true (D-18)
    public List<LedgerDelta> NewDetails { get; set; } = new();

    public int Attempts { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }

    // Text spend on the attachment's tier model; media spend comes from the media result (D-29)
    public decimal CostUsd { get; set; }
    public decimal MediaCostUsd { get; set; }

    public string Reason { get; set; } = string.Empty;
    public ThreadFailureKind FailureKind { get; set; }

    // Invites: the caller-resolved slot, persisted so every copy and Continue use one schedule (D-20)
    public string InviteMeetingKey { get; set; } = string.Empty;
    public DateTimeOffset? InviteStart { get; set; }
    public DateTimeOffset? InviteEnd { get; set; }

    public static AttachmentOutcome For(AttachmentRenderContext ctx)
    {
        // Identity never depends on Part: Failed/Skipped outcomes need it for replay, the key and repair (review concern 2)
        var spec = ctx.Spec ?? new PlannedAttachment();
        var parent = ctx.Parent ?? new PlannedMessage();
        return new AttachmentOutcome
        {
            DocId = spec.DocId ?? string.Empty,
            AttachmentKind = spec.Kind,
            ThreadId = ctx.Thread?.Thread?.Id ?? string.Empty,
            ParentDocId = parent.DocId ?? string.Empty,
            ParentSeq = parent.Seq,
            IsInline = spec.IsInline,
            Relevant = ctx.Relevant,
            LateRepair = ctx.LateRepair,
            FileName = ctx.FileName ?? string.Empty,
            Title = ctx.Title ?? string.Empty
        };
    }

    /// <summary>
    /// The attachment's journal record, or null for NotRun. Utc and Files are left to the caller, which nests the record in
    /// its ThreadDone / AttachmentsRepaired line (or sets Files for standalone use).
    /// </summary>
    public JournalRecord? ToRecord(Guid planId)
    {
        if (Kind == AttachmentOutcomeKind.NotRun)
            return null;

        var type = Kind switch
        {
            AttachmentOutcomeKind.Written or AttachmentOutcomeKind.Warnings => JournalRecordType.AttachmentDone,
            AttachmentOutcomeKind.Failed => JournalRecordType.AttachmentFailed,
            _ => JournalRecordType.AttachmentSkipped
        };
        var text = FlattenedText ?? string.Empty;
        return new JournalRecord
        {
            Type = type,
            PlanId = planId,
            ThreadId = ThreadId,
            Status = Kind == AttachmentOutcomeKind.Warnings ? ThreadWriteStatus.Warnings : ThreadWriteStatus.Written,
            Attempts = Attempts,
            Violations = Violations.ToList(),
            AttachmentDocId = DocId,
            ParentDocId = ParentDocId,
            AttachmentKind = AttachmentKind,
            FileName = FileName,
            Title = Title,
            FlattenedText = text.Length > JournalRecord.MaxFlattenedTextChars ? text[..JournalRecord.MaxFlattenedTextChars] : text,
            LedgerDelta = NewDetails.ToList(),
            InputTokens = InputTokens,
            OutputTokens = OutputTokens,
            CostUsd = CostUsd,
            MediaCostUsd = MediaCostUsd,
            FailureKind = FailureKind,
            Reason = Reason,
            LateRepair = LateRepair,
            InviteMeetingKey = InviteMeetingKey,
            InviteStart = InviteStart,
            InviteEnd = InviteEnd
        };
    }
}

/// <summary>
/// One planned attachment after its thread was accepted (D-14, D-17, RUN-02): Office and invite text call, checks, up to
/// two retries with violation feedback, keep the best attempt and build bytes; images and voicemail audio through the
/// media seam. Produces an outcome; never reports completion. The caller records status after the EML copies and the
/// journal line are durable (review concern 1).
/// </summary>
public class AttachmentUnitOfWork
{
    public const int MaxAttempts = 3;

    public const string RefusedReason = "the model declined to write it";
    public const string TimeoutReason = "the request timed out";
    public const string UnusableReason = "the model returned no usable content";
    public const string BuildFailedPrefix = "the document could not be built";
    public const string EmptyDocumentText = "the document was empty";
    public const string NoMediaKeyReason = "no tested OpenAI key this session";

    private const string TimeoutPrefix = "The request timed out";

    private readonly ILlmGateway _llm;
    private readonly GenerationSnapshot _snapshot;
    private readonly IMediaGenerator? _media;
    private readonly MediaRunGate _gate;
    private readonly AIModelConfig? _storyModel;
    private readonly AIModelConfig? _volumeModel;
    private readonly OfficeDocumentService _office = new();
    private readonly CalendarService _calendar = new();

    public AttachmentUnitOfWork(ILlmGateway llm, GenerationSnapshot snapshot, IMediaGenerator? media, MediaRunGate gate,
        AIModelConfig? storyModel, AIModelConfig? volumeModel)
    {
        _llm = llm;
        _snapshot = snapshot;
        _media = media;
        _gate = gate ?? new MediaRunGate();
        _storyModel = storyModel;
        _volumeModel = volumeModel;
    }

    // D-28: set by the runner from the CastVoices journal record before any voicemail is spoken
    public CastVoiceSet? CastVoices { get; set; }

    // Test seam: runs just before bytes are built, so a test can simulate a builder failure
    internal Action<AttachmentContent>? BeforeBuild { get; set; }

    /// <summary>
    /// Runs one attachment. Throws only when the user cancels; every other problem becomes a Failed or Skipped outcome for
    /// this attachment, or NotRun when a fatal text-provider error stopped the run.
    /// </summary>
    internal async Task<AttachmentOutcome> RunAsync(AttachmentRenderContext ctx, PlanCallRun run)
    {
        var outcome = AttachmentOutcome.For(ctx);
        if (ctx.Spec.Kind is AttachmentType.Image or AttachmentType.Voicemail)
            return await RunMediaAsync(ctx, run, outcome).ConfigureAwait(false);

        return await RunTextAsync(ctx, run, outcome).ConfigureAwait(false);
    }

    // Images and voicemail audio (D-06, D-27, D-28): a media stop ends only media, never the text run (UI-SPEC Decision 2)
    private async Task<AttachmentOutcome> RunMediaAsync(AttachmentRenderContext ctx, PlanCallRun run, AttachmentOutcome outcome)
    {
        if (_media == null)
            return Skip(outcome, NoMediaKeyReason);
        if (_gate.Stopped)
            return Skip(outcome, _gate.Reason);

        await _gate.Slots.WaitAsync(run.UserToken).ConfigureAwait(false);
        try
        {
            // Review MEDIUM: callers queued before a stop must not call after it
            if (_gate.Stopped)
                return Skip(outcome, _gate.Reason);

            var isImage = ctx.Spec.Kind == AttachmentType.Image;
            var result = isImage
                ? await _media.GenerateImageAsync(AttachmentPrompts.ImagePrompt(ctx), ctx.FileName, run.UserToken).ConfigureAwait(false)
                : await _media.GenerateSpeechAsync(ctx.ParentBody, VoiceAssignments.For(ctx.Parent.FromId, CastVoices), ctx.FileName,
                    run.UserToken).ConfigureAwait(false);
            outcome.Attempts = 1;
            outcome.MediaCostUsd = result?.CostUsd ?? 0m;

            if (result?.Kind == MediaResultKind.Stopped)
            {
                var reason = string.IsNullOrWhiteSpace(result.Reason) ? "the OpenAI media service stopped" : result.Reason.Trim();
                if (_gate.TryStop(reason))
                {
                    _snapshot.AddEvent(new GenerationEvent
                    {
                        Kind = GenerationEventKind.MediaStopped,
                        LocalTime = DateTime.Now,
                        ThreadId = outcome.ThreadId,
                        Subject = ctx.Thread.Thread.Subject ?? string.Empty,
                        WaveIndex = ctx.Thread.WaveIndex,
                        Reason = _gate.Reason,
                        FileName = ctx.FileName,
                        MediaRemaining = _gate.Remaining
                    });
                }

                // Every item after the first stop takes the gate's reason, so the run log reads one cause
                return Skip(outcome, _gate.Reason);
            }

            // Ok or Failed: the item was attempted, so it no longer counts as remaining
            _gate.Consume();

            if (result == null || result.Kind == MediaResultKind.Failed)
            {
                var reason = string.IsNullOrWhiteSpace(result?.Reason) ? UnusableReason : result!.Reason.Trim();
                return Fail(ctx, outcome, ThreadFailureKind.Provider, reason);
            }

            if (result.Bytes == null || result.Bytes.Length == 0)
                return Fail(ctx, outcome, ThreadFailureKind.Provider, UnusableReason);

            var inline = isImage && ctx.Spec.IsInline;
            var sender = ctx.Thread.People?.Find(ctx.Parent.FromId);
            outcome.Part = new ComposedAttachment
            {
                DocId = outcome.DocId,
                Kind = ctx.Spec.Kind,
                FileName = outcome.FileName,
                Title = outcome.Title,
                MimeType = MimeTypeFor(ctx.Spec.Kind),
                Bytes = result.Bytes,
                IsInline = inline,
                ContentId = inline ? AttachmentNaming.ContentIdFor(outcome.DocId, sender?.Domain) : string.Empty
            };
            outcome.Kind = AttachmentOutcomeKind.Written;
            return outcome;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // RUN-02: an attachment failure never escapes into the thread's Task.WhenAll
            return Fail(ctx, outcome, ThreadFailureKind.Provider, $"the media could not be created: {ex.Message}");
        }
        finally
        {
            _gate.Slots.Release();
        }
    }

    private async Task<AttachmentOutcome> RunTextAsync(AttachmentRenderContext ctx, PlanCallRun run, AttachmentOutcome outcome)
    {
        try
        {
            AttachmentContent? best = null;
            var bestUsable = false;
            var bestViolations = new List<RenderViolation>();
            List<RenderViolation>? feedback = null;

            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                var call = await CallAsync(ctx, attempt, feedback, run).ConfigureAwait(false);
                outcome.InputTokens += call.InputTokens;
                outcome.OutputTokens += call.OutputTokens;
                outcome.Attempts = attempt;

                // Left pending: the caller accounts for the spend and Continue writes it later (D-06)
                if (run.IsFatal)
                {
                    outcome.Kind = AttachmentOutcomeKind.NotRun;
                    outcome.Reason = run.FatalError;
                    outcome.CostUsd = Cost(ctx, outcome);
                    return outcome;
                }

                if (call.Value == null)
                {
                    // An earlier attempt is still the best available text
                    if (best != null)
                        break;

                    // No blind re-ask after a refusal or transport failure; the gateway already retried transient errors (D-15)
                    if (call.Refused)
                        return Fail(ctx, outcome, ThreadFailureKind.Refused, RefusedReason);
                    if (call.Error.StartsWith(TimeoutPrefix, StringComparison.Ordinal))
                        return Fail(ctx, outcome, ThreadFailureKind.Timeout, TimeoutReason);
                    return Fail(ctx, outcome, ThreadFailureKind.Provider, UnusableReason);
                }

                var content = AttachmentRenderer.Map(call.Value, ctx);
                var usable = AttachmentRenderer.IsUsable(content);
                var violations = usable
                    ? Check(ctx, content)
                    : new List<RenderViolation> { new() { Code = "V5", Seq = ctx.Parent.Seq, Text = EmptyDocumentText } };

                // A usable document beats an empty one; then fewest violations wins and a tie goes to the later attempt (D-14)
                var better = best == null
                    || (!bestUsable && usable)
                    || (bestUsable == usable && violations.Count <= bestViolations.Count);
                if (better)
                {
                    best = content;
                    bestUsable = usable;
                    bestViolations = violations;
                }

                if (violations.Count == 0)
                    break;

                if (attempt < MaxAttempts)
                {
                    _snapshot.AddEvent(new GenerationEvent
                    {
                        Kind = GenerationEventKind.AttachmentRetry,
                        LocalTime = DateTime.Now,
                        ThreadId = outcome.ThreadId,
                        Subject = ctx.Thread.Thread.Subject ?? string.Empty,
                        WaveIndex = ctx.Thread.WaveIndex,
                        Attempt = attempt,
                        Violations = violations.Select(Describe).ToList(),
                        FileName = ctx.FileName,
                        Relevant = ctx.Relevant
                    });

                    // Same prompt plus the specific problems (D-13)
                    feedback = violations;
                }
            }

            if (best == null || !bestUsable)
                return Fail(ctx, outcome, ThreadFailureKind.Provider, UnusableReason);

            var kept = bestViolations.ToList();

            // D-20: the schedule is code-owned; an untied slot is a warning that no retry can fix
            if (ctx.Spec.Kind == AttachmentType.CalendarInvite)
            {
                var slot = ctx.Slot ?? FallbackSlot(ctx.Parent);
                outcome.InviteMeetingKey = slot.MeetingKey ?? string.Empty;
                outcome.InviteStart = slot.Start;
                outcome.InviteEnd = slot.End;
                if (slot.Warning)
                {
                    var text = string.IsNullOrWhiteSpace(slot.WarningText) ? InviteScheduler.NotTiedWarning : slot.WarningText.Trim();
                    kept.Add(new RenderViolation { Code = "I1", Seq = ctx.Parent.Seq, Text = text });
                }
            }

            BeforeBuild?.Invoke(best);
            var part = Build(ctx, best, outcome);

            if (outcome.Title.Length == 0)
                outcome.Title = part.Title;
            outcome.Part = part;
            outcome.FlattenedText = AttachmentRenderer.Flatten(best);
            outcome.NewDetails = ctx.Relevant ? best.NewDetails.ToList() : new List<LedgerDelta>();
            outcome.Violations = kept.Select(Describe).ToList();
            outcome.Kind = kept.Count == 0 ? AttachmentOutcomeKind.Written : AttachmentOutcomeKind.Warnings;
            outcome.CostUsd = Cost(ctx, outcome);
            return outcome;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // RUN-02: an attachment failure never escapes into the thread's Task.WhenAll
            return Fail(ctx, outcome, ThreadFailureKind.Provider, $"{BuildFailedPrefix}: {ex.Message}");
        }
    }

    // Relevant: the full attachment check; decoy: the must-not-mention scan (a no-op for other filler, which only needs
    // usable content) (D-12, D-17, D-23)
    private static List<RenderViolation> Check(AttachmentRenderContext ctx, AttachmentContent content)
    {
        var flat = AttachmentRenderer.Flatten(content);
        if (!ctx.Relevant)
            return DecoyScanner.ScanText(flat, ctx.Parent.Seq, ctx.Thread.Thread);

        var labels = (ctx.Thread.Guide?.Values ?? new List<CanonicalValue>())
            .Where(v => v != null)
            .Select(v => v.Label)
            .Concat((ctx.Thread.LedgerView ?? new List<LedgerEntry>()).Where(e => e != null).Select(e => e.About))
            .Concat((ctx.VisibleThreadDeltas ?? new List<LedgerDelta>()).Where(d => d != null).Select(d => d.About))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return RenderValidator.CheckAttachment(new AttachmentCheckInput
        {
            ParentSeq = ctx.Parent.Seq,
            Text = flat,
            VariantText = AttachmentRenderer.VariantText(content, labels),
            PeopleNamed = content.PeopleNamed,
            AcceptedThread = ctx.AcceptedThread,
            NewDetails = content.NewDetails,
            LateRepair = ctx.LateRepair,
            LaterEstablished = ctx.LaterEstablished ?? new List<LedgerEntry>()
        }, ctx.Thread);
    }

    private ComposedAttachment Build(AttachmentRenderContext ctx, AttachmentContent content, AttachmentOutcome outcome)
    {
        var parent = ctx.Parent;
        var sender = ctx.Thread.People?.Find(parent.FromId);
        var organizationName = sender?.OrganizationName?.Trim() ?? string.Empty;
        var named = (ctx.Title ?? string.Empty).Trim();
        var title = named.Length > 0 ? named : content.Title;

        byte[] bytes;
        switch (content.Kind)
        {
            case AttachmentType.Word:
                bytes = _office.CreateWordDocument(title, content.Sections, content.Table, Theme(sender), Metadata(ctx, sender));
                break;
            case AttachmentType.Excel:
                bytes = _office.CreateExcelDocument(title, content.SheetName, content.Headers, content.Rows, Theme(sender),
                    Metadata(ctx, sender));
                break;
            case AttachmentType.PowerPoint:
                var month = MessageTime.Local(parent).ToString("MMMM yyyy", CultureInfo.InvariantCulture);
                var subtitle = organizationName.Length > 0 ? $"{organizationName} | {month}" : month;
                bytes = _office.CreatePowerPointDocument(title, subtitle, content.Slides, Theme(sender), Metadata(ctx, sender));
                break;
            case AttachmentType.CalendarInvite:
                bytes = _calendar.CreateInvite(InviteSpec(ctx, content, sender, outcome));
                title = content.Title.Length > 0 ? content.Title : title;
                break;
            default:
                throw new InvalidOperationException($"No document builder for {content.Kind}");
        }

        return new ComposedAttachment
        {
            DocId = outcome.DocId,
            Kind = content.Kind,
            FileName = outcome.FileName,
            Title = title,
            MimeType = MimeTypeFor(content.Kind),
            Bytes = bytes,
            IsInline = false
        };
    }

    private static OrganizationTheme Theme(PersonInfo? sender) =>
        OrganizationThemes.ForOrganization(sender?.Domain, sender?.OrganizationName);

    // D-19: the sender is the author; created one hour to three days and saved minutes before the email, both stable per DocId
    private static DocumentMetadata Metadata(AttachmentRenderContext ctx, PersonInfo? sender)
    {
        var sent = MessageTime.Local(ctx.Parent);
        var docId = ctx.Spec.DocId ?? string.Empty;
        var createdMinutes = 60 + (int)(RenderAssignments.StableRank(docId + ":created") % (3UL * 24 * 60));
        var modifiedMinutes = 5 + (int)(RenderAssignments.StableRank(docId + ":modified") % 55UL);
        return new DocumentMetadata
        {
            Author = sender?.FullName ?? string.Empty,
            Created = sent.AddMinutes(-createdMinutes),
            Modified = sent.AddMinutes(-modifiedMinutes)
        };
    }

    // D-20: organizer = sender, attendees = To + Cc, time from the caller-resolved slot (copied onto the outcome)
    private static CalendarInviteSpec InviteSpec(AttachmentRenderContext ctx, AttachmentContent content, PersonInfo? sender,
        AttachmentOutcome outcome)
    {
        var parent = ctx.Parent;
        var people = ctx.Thread.People;
        var domain = string.IsNullOrWhiteSpace(sender?.Domain) ? "reeldiscovery.local" : sender!.Domain.Trim();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(outcome.DocId))).ToLowerInvariant();
        var attendees = (parent.ToIds ?? new List<string>())
            .Concat(parent.CcIds ?? new List<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .Select(id => people?.Find(id))
            .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Email))
            .Select(p => (p!.FullName, p.Email))
            .ToList();

        return new CalendarInviteSpec
        {
            Uid = hash[..32] + "@" + domain,
            DtStamp = parent.SentAt,
            Start = outcome.InviteStart ?? parent.SentAt,
            End = outcome.InviteEnd ?? parent.SentAt.AddHours(1),
            Summary = content.Title.Length > 0 ? content.Title : ctx.Title,
            Description = content.Agenda,
            Location = content.Location,
            OrganizerName = sender?.FullName ?? string.Empty,
            OrganizerEmail = sender?.Email ?? string.Empty,
            Attendees = attendees
        };
    }

    // Only when the caller resolved no slot: the next day at the email's time, flagged like any untied invite
    private static InviteSlot FallbackSlot(PlannedMessage parent) => new()
    {
        Start = parent.SentAt.AddDays(1),
        End = parent.SentAt.AddDays(1).AddHours(1),
        Source = InviteSource.Fallback,
        Warning = true,
        WarningText = InviteScheduler.NotTiedWarning
    };

    private async Task<CallResult> CallAsync(AttachmentRenderContext ctx, int attempt, List<RenderViolation>? feedback, PlanCallRun run)
    {
        var request = AttachmentRenderer.BuildRequest(ctx, attempt, feedback);
        var type = AttachmentRenderer.DtoTypeFor(ctx.Spec.Kind, ctx.Relevant);
        if (type == typeof(AttachmentRenderer.WordAttachmentDto))
            return From(await PlanCallRunner.RunAsync<AttachmentRenderer.WordAttachmentDto>(_llm, request, run).ConfigureAwait(false));
        if (type == typeof(AttachmentRenderer.FillerWordDto))
            return From(await PlanCallRunner.RunAsync<AttachmentRenderer.FillerWordDto>(_llm, request, run).ConfigureAwait(false));
        if (type == typeof(AttachmentRenderer.ExcelAttachmentDto))
            return From(await PlanCallRunner.RunAsync<AttachmentRenderer.ExcelAttachmentDto>(_llm, request, run).ConfigureAwait(false));
        if (type == typeof(AttachmentRenderer.FillerExcelDto))
            return From(await PlanCallRunner.RunAsync<AttachmentRenderer.FillerExcelDto>(_llm, request, run).ConfigureAwait(false));
        if (type == typeof(AttachmentRenderer.PowerPointAttachmentDto))
            return From(await PlanCallRunner.RunAsync<AttachmentRenderer.PowerPointAttachmentDto>(_llm, request, run).ConfigureAwait(false));
        if (type == typeof(AttachmentRenderer.FillerPowerPointDto))
            return From(await PlanCallRunner.RunAsync<AttachmentRenderer.FillerPowerPointDto>(_llm, request, run).ConfigureAwait(false));
        return From(await PlanCallRunner.RunAsync<AttachmentRenderer.InviteAttachmentDto>(_llm, request, run).ConfigureAwait(false));
    }

    private static CallResult From<T>(PlanCallOutcome<T> outcome) where T : class => new()
    {
        Value = outcome.Value,
        Refused = outcome.Refused,
        Error = outcome.Error ?? string.Empty,
        InputTokens = outcome.InputTokens,
        OutputTokens = outcome.OutputTokens
    };

    private AttachmentOutcome Fail(AttachmentRenderContext ctx, AttachmentOutcome outcome, ThreadFailureKind kind, string reason)
    {
        outcome.Kind = AttachmentOutcomeKind.Failed;
        outcome.FailureKind = kind;
        outcome.Reason = reason;
        outcome.Part = null;
        outcome.NewDetails = new List<LedgerDelta>();
        outcome.CostUsd = Cost(ctx, outcome);
        return outcome;
    }

    private static AttachmentOutcome Skip(AttachmentOutcome outcome, string reason)
    {
        outcome.Kind = AttachmentOutcomeKind.Skipped;
        outcome.Reason = reason;
        outcome.Part = null;
        return outcome;
    }

    // Text cost uses the attachment's tier model (D-29)
    private decimal Cost(AttachmentRenderContext ctx, AttachmentOutcome outcome)
    {
        var model = ctx.Tier == LlmTier.Story ? _storyModel : _volumeModel;
        return model?.CalculateTotalCost(outcome.InputTokens, outcome.OutputTokens) ?? 0m;
    }

    private static string MimeTypeFor(AttachmentType kind) => kind switch
    {
        AttachmentType.Word => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        AttachmentType.Excel => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        AttachmentType.PowerPoint => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        AttachmentType.Image => "image/png",
        AttachmentType.CalendarInvite => "text/calendar",
        AttachmentType.Voicemail => "audio/mpeg",
        _ => "application/octet-stream"
    };

    private static string Describe(RenderViolation violation) =>
        violation.Seq > 0 ? $"msg {violation.Seq}: {violation.Text}" : violation.Text;

    private sealed class CallResult
    {
        public object? Value { get; set; }
        public bool Refused { get; set; }
        public string Error { get; set; } = string.Empty;
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
    }
}
