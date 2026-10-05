using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Tests.Evidence;

public partial class AnswerKeyOverlayTests
{
    private const string RenderedSubject = "Privileged & Confidential - Subject T-0003";

    // First planned email that carries an attachment (its .0001 row must follow the overlaid subject)
    private static PlannedMessage EmailWithAttachment(CorpusPlan plan) =>
        plan.Threads.SelectMany(t => t.Messages).OrderBy(m => m.DocId, StringComparer.Ordinal).First(m => m.Attachments.Count > 0);

    private static List<string> Fingerprint(IEnumerable<AnswerKeyRow> rows) =>
        rows.Select(r => string.Join("|", r.DocId, r.DocType, r.FilePath, r.Subject, r.Status, r.Relevance, r.FactIds, r.IssueCodes, r.Rationale)).ToList();

    [Fact]
    public void Build_NoOverlay_AllPlanned()
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();

        var rows = AnswerKeyBuilder.Build(plan, draft.Guide!);

        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal(AnswerKeyBuilder.StatusPlanned, r.Status));
        Assert.Equal(Fingerprint(rows), Fingerprint(AnswerKeyBuilder.Build(plan, draft.Guide!, null)));
        Assert.Equal(Fingerprint(rows), Fingerprint(AnswerKeyBuilder.Build(plan, draft.Guide!, new AnswerKeyOverlay())));
    }

    [Fact]
    public void Build_Overlay_SetsEmailStatusAndSubject_AttachmentCopiesSubject()
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();
        var target = EmailWithAttachment(plan);
        var baseline = AnswerKeyBuilder.Build(plan, draft.Guide!);
        var overlay = new AnswerKeyOverlay();
        overlay.ByDocId[target.DocId] = new AnswerKeyOverlayEntry { Status = AnswerKeyBuilder.StatusGenerated, Subject = RenderedSubject };

        var rows = AnswerKeyBuilder.Build(plan, draft.Guide!, overlay);

        var email = rows.Single(r => r.DocId == target.DocId);
        Assert.Equal(AnswerKeyBuilder.StatusGenerated, email.Status);
        Assert.Equal(RenderedSubject, email.Subject);

        var attachment = rows.Single(r => r.DocId == target.DocId + ".0001");
        Assert.Equal(AnswerKeyBuilder.DocTypeAttachment, attachment.DocType);
        Assert.Equal(AnswerKeyBuilder.StatusPlanned, attachment.Status);
        Assert.Equal(RenderedSubject, attachment.Subject);

        // Rows outside the overlay (and not attachments of the target) are unchanged
        var untouched = rows.Where(r => r.DocId != target.DocId && r.ParentDocId != target.DocId);
        var expected = baseline.Where(r => r.DocId != target.DocId && r.ParentDocId != target.DocId);
        Assert.Equal(Fingerprint(expected), Fingerprint(untouched));
    }

    [Fact]
    public void Build_OverlayEmptySubject_KeepsComputedSubject()
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();
        var target = EmailWithAttachment(plan);
        var computed = AnswerKeyBuilder.Build(plan, draft.Guide!).Single(r => r.DocId == target.DocId).Subject;
        var overlay = new AnswerKeyOverlay();
        overlay.ByDocId[target.DocId] = new AnswerKeyOverlayEntry { Status = AnswerKeyBuilder.StatusGeneratedWithWarnings, Subject = "  " };

        var rows = AnswerKeyBuilder.Build(plan, draft.Guide!, overlay);

        var email = rows.Single(r => r.DocId == target.DocId);
        Assert.Equal("Generated - with warnings", email.Status);
        Assert.Equal(computed, email.Subject);
        Assert.Equal(computed, rows.Single(r => r.DocId == target.DocId + ".0001").Subject);
    }

    [Fact]
    public void Build_OverlayBlankStatus_FallsBackToPlanned()
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();
        var target = EmailWithAttachment(plan);
        var overlay = new AnswerKeyOverlay();
        overlay.ByDocId[target.DocId] = new AnswerKeyOverlayEntry { Status = "", Subject = RenderedSubject };

        var email = AnswerKeyBuilder.Build(plan, draft.Guide!, overlay).Single(r => r.DocId == target.DocId);

        Assert.Equal(AnswerKeyBuilder.StatusPlanned, email.Status);
        Assert.Equal(RenderedSubject, email.Subject);
    }

    [Fact]
    public void StatusConstants_MatchLegendWording()
    {
        Assert.Equal("Planned", AnswerKeyBuilder.StatusPlanned);
        Assert.Equal("Generated", AnswerKeyBuilder.StatusGenerated);
        Assert.Equal("Generated - with warnings", AnswerKeyBuilder.StatusGeneratedWithWarnings);
        Assert.Equal("Failed", AnswerKeyBuilder.StatusFailed);
        Assert.Equal("Skipped", AnswerKeyBuilder.StatusSkipped);
    }

    [Theory]
    [InlineData("$7.5 million", 7_500_000)]
    [InlineData("$250,000", 250_000)]
    public void TryParseAmount_ParsesDollarAmounts(string text, decimal expected)
    {
        Assert.True(CanonicalValueScanner.TryParseAmount(text, out var amount));
        Assert.Equal(expected, amount);
    }

    [Fact]
    public void TryParseAmount_RejectsNonAmounts()
    {
        Assert.False(CanonicalValueScanner.TryParseAmount("abc", out _));
        Assert.False(CanonicalValueScanner.TryParseAmount(null, out _));
    }

    [Fact]
    public void Legend_ExplainsAllStatuses()
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();
        var bytes = AnswerKeyXlsxWriter.Build(AnswerKeyBuilder.Build(plan, draft.Guide!), draft.Guide!);

        using var doc = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        var sheet = doc.WorkbookPart!.Workbook!.Sheets!.Elements<Sheet>().Single(s => s.Name == "Legend");
        var worksheet = ((WorksheetPart)doc.WorkbookPart.GetPartById(sheet.Id!)).Worksheet!;
        var texts = worksheet.Descendants<Cell>()
            .Select(c => c.DataType?.Value == CellValues.InlineString ? c.InlineString!.Text!.Text : c.CellValue?.Text ?? string.Empty)
            .ToList();

        Assert.Contains(texts, t => t.Contains("Planned, Generated, Generated - with warnings, Failed or Skipped."));
        Assert.DoesNotContain(texts, t => t.Contains("Planned until the document is generated"));
    }

    // ---- 06-14: attachments, non-relevant and voicemail rows ----

    private const string NoKeyReason = "no tested OpenAI key this session";
    private const string VoicemailFrom = "Dunder Mifflin Voice Mail <voicemail@dundermifflin.com>";

    [GeneratedRegex(@"^(Document|Spreadsheet|Presentation|Photo|Image|Invite|Voicemail)( \d+)?\.")]
    private static partial Regex PlaceholderName();

    private static PlannedThread T(CorpusPlan plan, string id) => AttachmentTestData.Thread(plan, id);

    private static List<PlannedAttachment> A(CorpusPlan plan, string id) => AttachmentTestData.AttachmentsOf(plan, id).ToList();

    private static List<AnswerKeyRow> Key(CaseDraft draft, CorpusPlan plan, GenerationRunState state,
        IReadOnlyDictionary<string, ThreadAssignment>? assignments = null,
        IReadOnlyDictionary<string, AttachmentChainLink>? chains = null) =>
        AnswerKeyBuilder.Build(plan, draft.Guide!, state.BuildOverlay(plan, assignments ?? GenerationRunState.AllAssignments(plan), chains));

    private static string Unchained(PlannedAttachment attachment, CorpusPlan plan, IReadOnlyList<string>? titles = null) =>
        AttachmentNaming.NamesFor(GenerationRunStateTests.ParentOf(plan, attachment.DocId), titles,
            new Dictionary<string, AttachmentChainLink>())[attachment.DocId];

    [Fact]
    public void AllAssignments_RelevantAndFiller()
    {
        var (_, plan) = AttachmentTestData.MixedCase();

        var all = GenerationRunState.AllAssignments(plan);

        foreach (var (id, assignment) in RenderAssignments.Compute(plan))
            Assert.Equal(assignment.Marking, all[id].Marking);
        foreach (var id in FillerAssignments.Compute(plan).Keys)
            Assert.True(all.ContainsKey(id));
    }

    [Fact]
    public void WrittenNonRelevantThread_GeneratedWithFillerMarking()
    {
        var (draft, plan) = AttachmentTestData.MixedCase();
        var daily = T(plan, AttachmentTestData.DailyBusinessThreadId);
        var state = GenerationRunState.From(plan, GenerationRunStateTests.Journal(plan,
            GenerationRunStateTests.Done(plan, daily, ThreadWriteStatus.Written, 0.01m)));
        var assignments = GenerationRunState.AllAssignments(plan);
        var filler = FillerAssignments.Compute(plan);

        var rows = Key(draft, plan, state, assignments);
        foreach (var message in daily.Messages)
        {
            var row = rows.Single(r => r.DocId == message.DocId);
            Assert.Equal(AnswerKeyBuilder.StatusGenerated, row.Status);
            Assert.Equal(EmailComposer.SubjectFor(daily, message, filler[daily.Id].Marking), row.Subject);
        }

        assignments[daily.Id] = new ThreadAssignment { ThreadId = daily.Id, Marking = PrivilegeMarking.BannerAndSubject };
        var marked = Key(draft, plan, state, assignments);
        Assert.StartsWith("Privileged & Confidential - ", marked.Single(r => r.DocId == daily.Messages[0].DocId).Subject);
        Assert.All(daily.Messages, m => Assert.Contains("Privileged & Confidential - ", marked.Single(r => r.DocId == m.DocId).Subject));
        Assert.Equal(AnswerKeyBuilder.NotRelevantLabel, marked.Single(r => r.DocId == daily.Messages[0].DocId).Relevance);
    }

    [Fact]
    public void AttachmentRows_FollowCommittedRecords()
    {
        var (draft, plan) = AttachmentTestData.MixedCase();
        var docThread = T(plan, AttachmentTestData.RelevantDocThreadId);
        var parts = A(plan, docThread.Id);
        var invite = T(plan, AttachmentTestData.InviteThreadId);
        var image = A(plan, invite.Id).Single(a => a.Kind == AttachmentType.Image);
        var state = GenerationRunState.From(plan, GenerationRunStateTests.Journal(plan,
            GenerationRunStateTests.DoneWith(plan, docThread,
                GenerationRunStateTests.Attachment(plan, parts[0], JournalRecordType.AttachmentDone, fileName: "Q3 Supplier Review.docx"),
                GenerationRunStateTests.Attachment(plan, parts[1], JournalRecordType.AttachmentDone, ThreadWriteStatus.Warnings),
                GenerationRunStateTests.Attachment(plan, parts[2], JournalRecordType.AttachmentFailed, reason: "provider error")),
            GenerationRunStateTests.DoneWith(plan, invite,
                GenerationRunStateTests.Attachment(plan, image, JournalRecordType.AttachmentSkipped, reason: NoKeyReason))));

        var rows = Key(draft, plan, state);

        var word = rows.Single(r => r.DocId == parts[0].DocId);
        Assert.Equal(AnswerKeyBuilder.StatusGenerated, word.Status);
        Assert.Equal("Q3 Supplier Review.docx", word.FileName);
        Assert.Equal(AnswerKeyBuilder.StatusGeneratedWithWarnings, rows.Single(r => r.DocId == parts[1].DocId).Status);

        // Identity from AttachmentDocId even without a journaled name (review concern 2)
        var failed = rows.Single(r => r.DocId == parts[2].DocId);
        Assert.Equal(AnswerKeyBuilder.StatusFailed, failed.Status);
        Assert.Equal(Unchained(parts[2], plan), failed.FileName);

        var skipped = rows.Single(r => r.DocId == image.DocId);
        Assert.Equal(AnswerKeyBuilder.StatusSkipped, skipped.Status);
        Assert.EndsWith("Skipped: " + NoKeyReason, skipped.Rationale);

        // Family and parent are kept; parent subject is inherited
        var parent = rows.Single(r => r.DocId == GenerationRunStateTests.ParentOf(plan, parts[0].DocId).DocId);
        Assert.Equal(parent.DocId, word.FamilyId);
        Assert.Equal(parent.DocId, word.ParentDocId);
        Assert.Equal(parent.Subject, word.Subject);
        Assert.Equal(parent.From, word.From);
    }

    [Fact]
    public void PendingAttachmentRows_UseFinalNames_FillerKeepsPlannedNameUntilWritten()
    {
        var (draft, plan) = AttachmentTestData.MixedCase();
        var state = GenerationRunState.From(plan, new JournalReplay());
        var chains = AttachmentChains.Compute(plan, draft.Guide);
        var v2 = A(plan, AttachmentTestData.ChainThreadId).Single();
        var daily = A(plan, AttachmentTestData.DailyBusinessThreadId).Single();

        var chained = Key(draft, plan, state, chains: chains);
        var unchained = Key(draft, plan, state);

        Assert.EndsWith("_v2.xlsx", chained.Single(r => r.DocId == v2.DocId).FileName);
        Assert.Equal(Unchained(v2, plan), unchained.Single(r => r.DocId == v2.DocId).FileName);
        Assert.Equal(AttachmentNaming.NamesFor(GenerationRunStateTests.ParentOf(plan, v2.DocId), null, chains)[v2.DocId],
            chained.Single(r => r.DocId == v2.DocId).FileName);
        Assert.Equal("Document 1.docx", chained.Single(r => r.DocId == daily.DocId).FileName);
        Assert.All(chained.Where(r => r.DocType == AnswerKeyBuilder.DocTypeAttachment),
            r => Assert.Equal(AnswerKeyBuilder.StatusPlanned, r.Status));
    }

    [Fact]
    public void FullRun_NoPlaceholderFileNames()
    {
        var (draft, plan) = AttachmentTestData.MixedCase();
        var records = new List<JournalRecord>();
        foreach (var thread in GenerationRunStateTests.GenerationThreads(plan))
        {
            var record = GenerationRunStateTests.Done(plan, thread, ThreadWriteStatus.Written, 0.01m);
            if (thread.Relevance == ThreadRelevance.NotRelevant)
            {
                foreach (var message in record.Messages)
                {
                    var planned = thread.Messages.Single(m => m.DocId == message.DocId);
                    message.AttachmentTitles = planned.Attachments.Select((a, i) => $"{thread.Subject} part {i + 1}").ToList();
                }
            }

            records.Add(record);
        }

        var state = GenerationRunState.From(plan, GenerationRunStateTests.Journal(plan, records.ToArray()));
        var rows = Key(draft, plan, state, chains: AttachmentChains.Compute(plan, draft.Guide));

        var attachments = rows.Where(r => r.DocType == AnswerKeyBuilder.DocTypeAttachment).ToList();
        Assert.NotEmpty(attachments);
        Assert.All(attachments, r => Assert.DoesNotMatch(PlaceholderName(), r.FileName));
        var daily = A(plan, AttachmentTestData.DailyBusinessThreadId).Single();
        var dailyName = rows.Single(r => r.DocId == daily.DocId).FileName;
        Assert.Equal(Unchained(daily, plan, new[] { "Insurance certificate renewal part 1" }), dailyName);
        Assert.StartsWith("Insurance", dailyName);
    }

    [Fact]
    public void VoicemailRows_UseJournalSenderAndSubject()
    {
        var (draft, plan) = AttachmentTestData.MixedCase();
        var thread = T(plan, AttachmentTestData.RelevantVoicemailThreadId);
        var voicemail = A(plan, thread.Id).Single();
        var done = GenerationRunStateTests.DoneWith(plan, thread,
            GenerationRunStateTests.Attachment(plan, voicemail, JournalRecordType.AttachmentDone));
        done.Messages[0].From = VoicemailFrom;
        done.Messages[0].Subject = "Voicemail from Michael Scott (0:42)";
        var state = GenerationRunState.From(plan, GenerationRunStateTests.Journal(plan, done));

        var rows = Key(draft, plan, state);

        var email = rows.Single(r => r.DocId == thread.Messages[0].DocId);
        Assert.Equal(VoicemailFrom, email.From);
        Assert.Equal("Voicemail from Michael Scott (0:42)", email.Subject);
        Assert.Contains("Voicemail from Michael Scott", email.Rationale);
        var part = rows.Single(r => r.DocId == voicemail.DocId);
        Assert.Equal(AnswerKeyBuilder.StatusGenerated, part.Status);
        Assert.Equal(VoicemailFrom, part.From);
        Assert.Equal(email.Subject, part.Subject);
    }

    [Fact]
    public void Overlay_Phase5VoicemailThread_MatchesUnchangedEml()
    {
        var (draft, plan) = AttachmentTestData.MixedCase();
        var thread = T(plan, AttachmentTestData.RelevantVoicemailThreadId);
        var voicemail = A(plan, thread.Id).Single();
        var planned = AnswerKeyBuilder.Build(plan, draft.Guide!).Single(r => r.DocId == thread.Messages[0].DocId);
        var done = GenerationRunStateTests.Done(plan, thread, ThreadWriteStatus.Written, 0.01m);
        done.Messages[0].From = string.Empty;
        done.Messages[0].Subject = "Call me back re CO-007";

        var before = Key(draft, plan, GenerationRunState.From(plan, GenerationRunStateTests.Journal(plan, done)));

        var email = before.Single(r => r.DocId == thread.Messages[0].DocId);
        Assert.Equal(planned.From, email.From);
        Assert.Equal("Call me back re CO-007", email.Subject);
        Assert.Equal(AnswerKeyBuilder.StatusPlanned, before.Single(r => r.DocId == voicemail.DocId).Status);

        var after = Key(draft, plan, GenerationRunState.From(plan, GenerationRunStateTests.Journal(plan, done,
            GenerationRunStateTests.Repaired(plan, thread,
                GenerationRunStateTests.Attachment(plan, voicemail, JournalRecordType.AttachmentDone)))));

        var part = after.Single(r => r.DocId == voicemail.DocId);
        Assert.Equal(AnswerKeyBuilder.StatusGenerated, part.Status);
        Assert.Equal(planned.From, part.From);
        Assert.Equal("Call me back re CO-007", part.Subject);
    }
}
