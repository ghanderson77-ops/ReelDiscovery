using System.Text;
using System.Text.Json;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Evidence;
using Xunit;

namespace ReelDiscovery.Tests.Evidence;

public class GenerationJournalTests : IDisposable
{
    private readonly string _folder;
    private readonly CorpusPlan _plan;

    public GenerationJournalTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "rd-journal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _plan = new CorpusPlan
        {
            PlanId = Guid.NewGuid(),
            Threads = new List<PlannedThread>
            {
                new()
                {
                    Id = "T-0003",
                    Relevance = ThreadRelevance.Relevant,
                    Messages = new List<PlannedMessage>
                    {
                        new()
                        {
                            Seq = 1,
                            DocId = "PL-000001",
                            Attachments = new List<PlannedAttachment>
                            {
                                new() { DocId = "PL-000001.0001", Kind = AttachmentType.Word },
                                new() { DocId = "PL-000001.0002", Kind = AttachmentType.Excel }
                            }
                        }
                    }
                },
                new()
                {
                    Id = "T-0005",
                    Relevance = ThreadRelevance.Relevant,
                    Messages = new List<PlannedMessage>
                    {
                        new()
                        {
                            Seq = 1,
                            DocId = "PL-000002",
                            Attachments = new List<PlannedAttachment> { new() { DocId = "PL-000002.0001", Kind = AttachmentType.Voicemail } }
                        }
                    }
                },
                new() { Id = "T-0007", Relevance = ThreadRelevance.Relevant },
                new()
                {
                    Id = "T-0011",
                    Relevance = ThreadRelevance.NotRelevant,
                    Category = NonRelevantCategory.DailyBusiness,
                    Messages = new List<PlannedMessage> { new() { Seq = 1, DocId = "PL-000003" } }
                }
            }
        };
    }

    public void Dispose()
    {
        GenerationJournal.SizeCapOverride = 0;
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private JournalRecord Started() => new() { Type = JournalRecordType.RunStarted, PlanId = _plan.PlanId, Utc = DateTime.UtcNow };

    private JournalRecord Done(string threadId, Guid? planId = null, params string[] files) => new()
    {
        Type = JournalRecordType.ThreadDone,
        PlanId = planId ?? _plan.PlanId,
        ThreadId = threadId,
        Status = ThreadWriteStatus.Written,
        Attempts = 1,
        Files = files.Length == 0 ? new List<string> { $"Mailboxes/pat.lee/Sent Items/{threadId}.eml" } : files.ToList()
    };

    private JournalRecord Failed(string threadId) => new()
    {
        Type = JournalRecordType.ThreadFailed,
        PlanId = _plan.PlanId,
        ThreadId = threadId,
        FailureKind = ThreadFailureKind.Refused,
        Reason = "The model declined."
    };

    private string JournalPath => GenerationJournal.PathFor(_folder);

    [Fact]
    public void PathFor_CaseFolderPlusConstant()
    {
        Assert.Equal(Path.Combine(_folder, GenerationState.JournalFileName), GenerationJournal.PathFor(_folder));
        Assert.Equal(JournalPath, new GenerationJournal(_folder).FilePath);
    }

    [Fact]
    public async Task AppendThenReplay_KeepsRecords()
    {
        var journal = new GenerationJournal(_folder);
        await journal.AppendAsync(Started());
        await journal.AppendAsync(Done("T-0003"));
        await journal.AppendAsync(Failed("T-0005"));

        var replay = GenerationJournal.Replay(_folder, _plan);

        Assert.True(replay.Exists);
        Assert.True(replay.Readable);
        Assert.False(replay.PlanChanged);
        Assert.True(replay.HasRunStarted);
        Assert.Equal(2, replay.LastByThread.Count);
        Assert.Equal(JournalRecordType.ThreadDone, replay.LastByThread["T-0003"].Type);
        Assert.Equal(JournalRecordType.ThreadFailed, replay.LastByThread["T-0005"].Type);
        Assert.Equal(0, replay.SkippedLines);
        Assert.NotEqual(default, replay.LastWriteUtc);
    }

    [Fact]
    public async Task Replay_LaterRecordWins()
    {
        var journal = new GenerationJournal(_folder);
        await journal.AppendAsync(Failed("T-0003"));
        await journal.AppendAsync(Done("T-0003"));

        var replay = GenerationJournal.Replay(_folder, _plan);

        Assert.Equal(JournalRecordType.ThreadDone, replay.LastByThread["T-0003"].Type);
    }

    [Fact]
    public async Task Replay_SpentCost_CountsReplacedFailuresAndRunEnded()
    {
        var journal = new GenerationJournal(_folder);
        var failed = Failed("T-0003");
        failed.CostUsd = 0.10m;
        var done = Done("T-0003");
        done.CostUsd = 0.25m;
        var other = Done("T-0005");
        other.CostUsd = 0.05m;
        await journal.AppendAsync(Started());
        await journal.AppendAsync(failed);
        await journal.AppendAsync(done);
        await journal.AppendAsync(other);
        await journal.AppendAsync(new JournalRecord
        {
            Type = JournalRecordType.RunEnded, PlanId = _plan.PlanId, Outcome = GenerationOutcome.Fatal, CostUsd = 0.02m
        });

        var replay = GenerationJournal.Replay(_folder, _plan);
        var state = GenerationRunState.From(_plan, replay);

        Assert.Equal(0.42m, replay.SpentCostUsd);
        Assert.Equal(0.42m, state.CostUsd);
        Assert.Equal(JournalRecordType.ThreadDone, replay.LastByThread["T-0003"].Type);
    }

    [Fact]
    public async Task Replay_RunEnded_KeepsLast()
    {
        var journal = new GenerationJournal(_folder);
        await journal.AppendAsync(new JournalRecord { Type = JournalRecordType.RunEnded, PlanId = _plan.PlanId, Outcome = GenerationOutcome.Cancelled });
        await journal.AppendAsync(new JournalRecord { Type = JournalRecordType.RunEnded, PlanId = _plan.PlanId, Outcome = GenerationOutcome.Finished });

        var replay = GenerationJournal.Replay(_folder, _plan);

        Assert.NotNull(replay.LastRunEnded);
        Assert.Equal(GenerationOutcome.Finished, replay.LastRunEnded!.Outcome);
        Assert.False(replay.HasRunStarted);
    }

    [Fact]
    public async Task ParallelAppends_OneParseableLineEach()
    {
        var journal = new GenerationJournal(_folder);
        await Task.WhenAll(Enumerable.Range(0, 50).Select(i => Task.Run(() => journal.AppendAsync(new JournalRecord
        {
            Type = JournalRecordType.ThreadDone,
            PlanId = _plan.PlanId,
            ThreadId = $"T-{i:0000}",
            Reason = new string('x', 2000)
        }))));

        var lines = File.ReadAllLines(JournalPath);
        Assert.Equal(50, lines.Length);
        Assert.All(lines, line => Assert.NotNull(System.Text.Json.JsonSerializer.Deserialize<JournalRecord>(line,
            ReelDiscovery.Models.Case.CaseDraft.JsonOptions)));
    }

    [Fact]
    public async Task Replay_TornLastLineAndBlankLine_Skipped()
    {
        var journal = new GenerationJournal(_folder);
        await journal.AppendAsync(Done("T-0003"));
        await File.AppendAllTextAsync(JournalPath, "\n");
        await journal.AppendAsync(Done("T-0005"));
        await File.AppendAllTextAsync(JournalPath, "{\"Type\":\"ThreadDone\",\"PlanId\":\"" + _plan.PlanId + "\",\"Thread");

        var replay = GenerationJournal.Replay(_folder, _plan);

        Assert.True(replay.Readable);
        Assert.Equal(2, replay.SkippedLines);
        Assert.Equal(2, replay.LastByThread.Count);
        Assert.Equal("Mailboxes/pat.lee/Sent Items/T-0005.eml", Assert.Single(replay.LastByThread["T-0005"].Files));
    }

    [Fact]
    public async Task Append_AfterTornLastLine_StartsFreshLine()
    {
        var journal = new GenerationJournal(_folder);
        await journal.AppendAsync(Done("T-0003"));
        await File.AppendAllTextAsync(JournalPath, "{\"Type\":\"ThreadDone\",\"PlanId\":\"" + _plan.PlanId + "\",\"Thread");
        await journal.AppendAsync(Done("T-0005"));

        var replay = GenerationJournal.Replay(_folder, _plan);

        Assert.Equal(1, replay.SkippedLines);
        Assert.Equal(2, replay.LastByThread.Count);
        Assert.True(replay.LastByThread.ContainsKey("T-0005"));
    }

    [Fact]
    public async Task Replay_UnknownTypeUnknownThreadForeignPlan_Dropped()
    {
        var journal = new GenerationJournal(_folder);
        var foreign = Guid.NewGuid();
        await journal.AppendAsync(Done("T-0003"));
        await File.AppendAllTextAsync(JournalPath, "{\"Type\":\"Exploded\",\"PlanId\":\"" + _plan.PlanId + "\",\"ThreadId\":\"T-0005\"}\n");
        await File.AppendAllTextAsync(JournalPath, "{\"Type\":42,\"PlanId\":\"" + _plan.PlanId + "\",\"ThreadId\":\"T-0005\"}\n");
        await journal.AppendAsync(Done("T-9999"));
        await journal.AppendAsync(Done("T-0007", foreign));
        await journal.AppendAsync(Done("T-0008", foreign));
        await journal.AppendAsync(Done("T-0008", foreign));
        await journal.AppendAsync(new JournalRecord { Type = JournalRecordType.ThreadFailed, PlanId = foreign, ThreadId = "T-0009" });

        var replay = GenerationJournal.Replay(_folder, _plan);

        Assert.True(replay.Readable);
        Assert.Equal(new[] { "T-0003" }, replay.LastByThread.Keys.ToArray());
        Assert.True(replay.PlanChanged);
        Assert.Equal(foreign, replay.JournalPlanId);
        Assert.Equal(2, replay.ForeignThreadCount);
    }

    [Fact]
    public async Task Replay_UnsafeFiles_RemovedByNormalize()
    {
        var journal = new GenerationJournal(_folder);
        await journal.AppendAsync(Done("T-0003", null, "../x.eml", "Mailboxes/pat.lee/Inbox/PL-000001.eml", "C:\\Windows\\x.eml"));

        var replay = GenerationJournal.Replay(_folder, _plan);

        Assert.Equal(new[] { "Mailboxes/pat.lee/Inbox/PL-000001.eml" }, replay.LastByThread["T-0003"].Files);
    }

    [Fact]
    public async Task Replay_Oversize_ReasonNoThrow()
    {
        var journal = new GenerationJournal(_folder);
        await journal.AppendAsync(Done("T-0003"));
        await journal.AppendAsync(Done("T-0005"));
        GenerationJournal.SizeCapOverride = 100;

        var replay = GenerationJournal.Replay(_folder, _plan);

        Assert.True(replay.Exists);
        Assert.False(replay.Readable);
        Assert.Equal("The saved run is larger than 200 MB.", replay.Reason);
        Assert.Empty(replay.LastByThread);
    }

    [Fact]
    public void Replay_Missing_NotExists()
    {
        var replay = GenerationJournal.Replay(_folder, _plan);

        Assert.False(replay.Exists);
        Assert.Equal(string.Empty, replay.Reason);
        Assert.False(GenerationJournal.Exists(_folder));
    }

    [Fact]
    public async Task Replay_Locked_ReasonCouldNotBeRead()
    {
        await new GenerationJournal(_folder).AppendAsync(Done("T-0003"));

        using (new FileStream(JournalPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var replay = GenerationJournal.Replay(_folder, _plan);

            Assert.True(replay.Exists);
            Assert.StartsWith("The saved run could not be read:", replay.Reason);
            Assert.EndsWith(".", replay.Reason);
        }
    }

    [Fact]
    public async Task Replay_GarbageOnly_SkipsEveryLine()
    {
        await File.WriteAllTextAsync(JournalPath, "not json\nnull\n[1,2]\n", Encoding.UTF8);

        var replay = GenerationJournal.Replay(_folder, _plan);

        Assert.True(replay.Readable);
        Assert.Equal(3, replay.SkippedLines);
        Assert.Empty(replay.LastByThread);
    }

    [Fact]
    public async Task ListAllFiles_EveryThreadDone_AnyPlan_SafeDistinct()
    {
        var journal = new GenerationJournal(_folder);
        await journal.AppendAsync(Done("T-0003", null, "Mailboxes/a/Inbox/PL-000001.eml", "../x.eml"));
        await journal.AppendAsync(Done("T-0003", null, "Mailboxes/a/Inbox/PL-000001.eml"));
        await journal.AppendAsync(Done("T-0004", Guid.NewGuid(), "Mailboxes/b/Sent Items/PL-000002.eml"));
        await journal.AppendAsync(Failed("T-0005"));

        var files = GenerationJournal.ListAllFiles(_folder);

        Assert.Equal(new[] { "Mailboxes/a/Inbox/PL-000001.eml", "Mailboxes/b/Sent Items/PL-000002.eml" }, files);
    }

    [Fact]
    public void ListAllFiles_Missing_Empty()
    {
        Assert.Empty(GenerationJournal.ListAllFiles(_folder));
    }

    [Fact]
    public async Task Delete_RemovesJournal_NoOpWhenMissing()
    {
        await new GenerationJournal(_folder).AppendAsync(Started());
        Assert.True(GenerationJournal.Exists(_folder));

        GenerationJournal.Delete(_folder);
        Assert.False(File.Exists(JournalPath));

        GenerationJournal.Delete(_folder);
        Assert.False(GenerationJournal.Exists(_folder));
    }

    // ---- Phase 6 record contracts (06-01 Task 1) ----

    private static T RoundTrip<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, CaseDraft.JsonOptions), CaseDraft.JsonOptions)!;

    private static JournalRecord Nested(JournalRecordType type, string docId, string parent, AttachmentType kind,
        string threadId = "T-0003", string fileName = "") => new()
    {
        Type = type,
        AttachmentDocId = docId,
        ParentDocId = parent,
        AttachmentKind = kind,
        ThreadId = threadId,
        FileName = fileName
    };

    [Fact]
    public void ThreadDone_NestedAttachments_RoundTripWithIdentity()
    {
        var record = Done("T-0003");
        record.Attachments.Add(Nested(JournalRecordType.AttachmentDone, "PL-000001.0001", "PL-000001", AttachmentType.Word,
            fileName: "Memo.docx"));
        record.Attachments.Add(Nested(JournalRecordType.AttachmentFailed, "PL-000001.0002", "PL-000001", AttachmentType.Excel));
        record.Attachments.Add(Nested(JournalRecordType.AttachmentSkipped, "PL-000001.0003", "PL-000001", AttachmentType.Voicemail));

        var copy = RoundTrip(record);
        copy.Normalize();

        Assert.Equal(3, copy.Attachments.Count);
        Assert.Equal(new[] { JournalRecordType.AttachmentDone, JournalRecordType.AttachmentFailed, JournalRecordType.AttachmentSkipped },
            copy.Attachments.Select(a => a.Type));
        Assert.Equal("PL-000001.0002", copy.Attachments[1].AttachmentDocId);
        Assert.Equal("PL-000001", copy.Attachments[1].ParentDocId);
        Assert.Equal(AttachmentType.Excel, copy.Attachments[1].AttachmentKind);
        Assert.Equal("T-0003", copy.Attachments[2].ThreadId);
        Assert.Equal(string.Empty, copy.Attachments[2].FileName);
        Assert.Equal("Memo.docx", copy.Attachments[0].FileName);
    }

    [Fact]
    public void Normalize_NestedAttachments_FilteredCappedFlat()
    {
        var record = Done("T-0003");
        record.Attachments.Add(null!);
        record.Attachments.Add(Nested(JournalRecordType.ThreadDone, "PL-000001.0001", "PL-000001", AttachmentType.Word));
        record.Attachments.Add(Nested(JournalRecordType.AttachmentDone, " ", "PL-000001", AttachmentType.Word));
        for (var i = 0; i < 40; i++)
        {
            var nested = Nested(JournalRecordType.AttachmentDone, $"PL-000001.{i + 1:0000}", "PL-000001", AttachmentType.Word);
            nested.Attachments.Add(Nested(JournalRecordType.AttachmentDone, "PL-000009.0001", "PL-000009", AttachmentType.Word));
            nested.Title = "Bad\u0007Title";
            record.Attachments.Add(nested);
        }

        record.Normalize();

        Assert.Equal(JournalRecord.MaxNestedAttachments, record.Attachments.Count);
        Assert.All(record.Attachments, a => Assert.Equal(JournalRecordType.AttachmentDone, a.Type));
        Assert.All(record.Attachments, a => Assert.Empty(a.Attachments));
        Assert.All(record.Attachments, a => Assert.Equal("BadTitle", a.Title));
        Assert.Equal("PL-000001.0001", record.Attachments[0].AttachmentDocId);
    }

    [Fact]
    public void Normalize_AttachmentFields_Repaired()
    {
        var record = new JournalRecord
        {
            Type = JournalRecordType.AttachmentDone,
            AttachmentDocId = "PL-000001.0001",
            FlattenedText = new string('a', 25_000),
            FileName = "..\\evil.docx",
            Title = "Line\r\nTwo\tTab",
            AttachmentKind = (AttachmentType)99,
            MediaCostUsd = -1m,
            InviteMeetingKey = new string('k', 200),
            InviteStart = new DateTimeOffset(2026, 3, 2, 10, 0, 0, TimeSpan.Zero),
            InviteEnd = new DateTimeOffset(2026, 3, 2, 9, 0, 0, TimeSpan.Zero)
        };

        record.Normalize();

        Assert.Equal(JournalRecord.MaxFlattenedTextChars, record.FlattenedText.Length);
        Assert.Equal(string.Empty, record.FileName);
        Assert.Equal("LineTwoTab", record.Title);
        Assert.Equal(AttachmentType.Word, record.AttachmentKind);
        Assert.Equal(0m, record.MediaCostUsd);
        Assert.Equal(JournalRecord.MaxMeetingKeyChars, record.InviteMeetingKey.Length);
        Assert.Null(record.InviteStart);
        Assert.Null(record.InviteEnd);
    }

    [Fact]
    public void Normalize_NullStrings_Repaired()
    {
        var record = new JournalRecord
        {
            AttachmentDocId = null!, ParentDocId = null!, FileName = null!, Title = null!, FlattenedText = null!,
            InviteMeetingKey = null!, Attachments = null!,
            Messages = new List<JournalMessage> { new() { From = null!, AttachmentTitles = null! } }
        };

        record.Normalize();

        Assert.Equal(string.Empty, record.AttachmentDocId);
        Assert.Equal(string.Empty, record.ParentDocId);
        Assert.Equal(string.Empty, record.FileName);
        Assert.Equal(string.Empty, record.Title);
        Assert.Equal(string.Empty, record.FlattenedText);
        Assert.Equal(string.Empty, record.InviteMeetingKey);
        Assert.Empty(record.Attachments);
        Assert.Equal(string.Empty, record.Messages[0].From);
        Assert.Empty(record.Messages[0].AttachmentTitles);
    }

    [Fact]
    public void Invite_ScheduleRoundTrips()
    {
        var start = new DateTimeOffset(2026, 3, 9, 10, 0, 0, TimeSpan.FromHours(-5));
        var record = new JournalRecord
        {
            Type = JournalRecordType.AttachmentDone,
            AttachmentDocId = "PL-000001.0001",
            AttachmentKind = AttachmentType.CalendarInvite,
            InviteMeetingKey = "site walk|2026-03-09",
            InviteStart = start,
            InviteEnd = start.AddHours(1)
        };

        var copy = RoundTrip(record);
        copy.Normalize();

        Assert.Equal(start, copy.InviteStart);
        Assert.Equal(start.AddHours(1), copy.InviteEnd);
        Assert.Equal("site walk|2026-03-09", copy.InviteMeetingKey);
    }

    [Fact]
    public void JournalMessage_FromAndTitles_DefaultsAndCaps()
    {
        var message = new JournalMessage();
        Assert.Equal(string.Empty, message.From);
        Assert.Empty(message.AttachmentTitles);

        var record = new JournalRecord
        {
            Messages = new List<JournalMessage>
            {
                new()
                {
                    From = "Pat Lee <pat@example.com>",
                    AttachmentTitles = Enumerable.Range(0, 12).Select(i => i == 0 ? new string('t', 300) : $"Title {i}").ToList()
                }
            }
        };
        record.Messages[0].AttachmentTitles.Insert(1, null!);

        var copy = RoundTrip(record);
        copy.Normalize();

        var titles = copy.Messages[0].AttachmentTitles;
        Assert.Equal("Pat Lee <pat@example.com>", copy.Messages[0].From);
        Assert.Equal(JournalRecord.MaxAttachmentTitles, titles.Count);
        Assert.Equal(JournalRecord.MaxTitleChars, titles[0].Length);
        Assert.Equal("Title 1", titles[1]);
    }

    [Fact]
    public void SideWorld_Normalize_Caps()
    {
        var world = new SideWorld
        {
            Organizations = Enumerable.Range(0, 30).Select(i => new SideWorldOrganization
            {
                OrganizationId = $"ORG-{i:000}",
                Projects = Enumerable.Range(0, 6).Select(p => new SideProject
                {
                    Name = p == 0 ? new string('n', 100) : p == 1 ? "  " : $"Project {p}",
                    Topics = Enumerable.Range(0, 6).Select(t => t == 0 ? " " : $"Topic {t}").ToList()
                }).ToList()
            }).ToList()
        };
        world.Organizations.Insert(0, null!);

        world.Normalize();

        Assert.Equal(24, SideWorld.MaxOrganizations);
        Assert.Equal(SideWorld.MaxOrganizations, world.Organizations.Count);
        var first = world.Organizations[0];
        Assert.Equal("ORG-000", first.OrganizationId);
        Assert.Equal(SideWorld.MaxProjects, first.Projects.Count);
        Assert.Equal(SideWorld.MaxNameChars, first.Projects[0].Name.Length);
        Assert.DoesNotContain(first.Projects, p => string.IsNullOrWhiteSpace(p.Name));
        Assert.All(first.Projects, p => Assert.Equal(SideWorld.MaxTopics, p.Topics.Count));
        Assert.All(first.Projects, p => Assert.DoesNotContain(p.Topics, string.IsNullOrWhiteSpace));
    }

    [Fact]
    public void SideWorld_NullLists_Repaired()
    {
        var world = new SideWorld
        {
            Organizations = new List<SideWorldOrganization> { new() { OrganizationId = null!, Projects = null! } }
        };

        world.Normalize();

        Assert.Single(world.Organizations);
        Assert.Equal(string.Empty, world.Organizations[0].OrganizationId);
        Assert.Empty(world.Organizations[0].Projects);
    }

    [Fact]
    public void CastVoiceSet_Normalize_AllowedVoicesFirstPerPerson()
    {
        var set = new CastVoiceSet
        {
            Voices = new List<CastVoice>
            {
                new() { PersonId = "CH-001", Voice = "Onyx" },
                new() { PersonId = "CH-002", Voice = "banana" },
                new() { PersonId = " ", Voice = "nova" },
                new() { PersonId = "CH-001", Voice = "nova" },
                null!,
                new() { PersonId = "CH-003", Voice = " shimmer " }
            }
        };

        set.Normalize();

        Assert.Equal(new[] { "CH-001", "CH-003" }, set.Voices.Select(v => v.PersonId));
        Assert.Equal("onyx", set.VoiceFor("CH-001"));
        Assert.Equal("shimmer", set.VoiceFor("CH-003"));
        Assert.Null(set.VoiceFor("CH-002"));
        Assert.Null(set.VoiceFor(null));
        Assert.Equal(6, CastVoiceSet.AllowedVoices.Count);
    }

    [Fact]
    public void CastVoiceSet_Normalize_CapsAt200()
    {
        var set = new CastVoiceSet
        {
            Voices = Enumerable.Range(0, 250).Select(i => new CastVoice { PersonId = $"CH-{i:000}", Voice = "alloy" }).ToList()
        };

        set.Normalize();

        Assert.Equal(CastVoiceSet.MaxVoices, set.Voices.Count);
    }

    [Fact]
    public void CastVoicesAndSideWorldRecords_RoundTrip()
    {
        var record = new JournalRecord
        {
            Type = JournalRecordType.CastVoices,
            PlanId = _plan.PlanId,
            CastVoices = new CastVoiceSet { Voices = new List<CastVoice> { new() { PersonId = "CH-001", Voice = "fable" } } }
        };
        var world = new JournalRecord
        {
            Type = JournalRecordType.SideWorld,
            SideWorld = new SideWorld
            {
                FromFallback = true,
                Organizations = new List<SideWorldOrganization>
                {
                    new()
                    {
                        OrganizationId = "ORG-001",
                        Projects = new List<SideProject> { new() { Name = "Harbor Point", Topics = new List<string> { "permits" } } }
                    }
                }
            }
        };

        var voices = RoundTrip(record);
        voices.Normalize();
        var side = RoundTrip(world);
        side.Normalize();

        Assert.Equal(JournalRecordType.CastVoices, voices.Type);
        Assert.Equal("fable", voices.CastVoices!.VoiceFor("CH-001"));
        Assert.True(side.SideWorld!.FromFallback);
        Assert.Equal("permits", side.SideWorld.Organizations[0].Projects[0].Topics[0]);
    }

    [Fact]
    public void LedgerDelta_AttachmentIdentity_FlagRepaired()
    {
        var delta = new LedgerDelta();
        Assert.False(delta.FromAttachment);
        Assert.Equal(string.Empty, delta.AttachmentDocId);

        var record = new JournalRecord
        {
            LedgerDelta = new List<LedgerDelta>
            {
                new() { AttachmentDocId = "PL-000001.0001" },
                new() { AttachmentDocId = null! }
            },
            Attachments = new List<JournalRecord>
            {
                new()
                {
                    Type = JournalRecordType.AttachmentDone,
                    AttachmentDocId = "PL-000001.0002",
                    LedgerDelta = new List<LedgerDelta> { new() { AttachmentDocId = "PL-000001.0002" } }
                }
            }
        };

        record.Normalize();

        Assert.True(record.LedgerDelta[0].FromAttachment);
        Assert.False(record.LedgerDelta[1].FromAttachment);
        Assert.Equal(string.Empty, record.LedgerDelta[1].AttachmentDocId);
        Assert.True(record.Attachments[0].LedgerDelta[0].FromAttachment);
    }

    [Fact]
    public void RenderedMessage_AttachmentTitles_DefaultEmpty()
    {
        Assert.Empty(new RenderedMessage().AttachmentTitles);
    }

    // ---- Phase 6 replay (06-01 Task 2) ----

    private static JournalRecord Outcome(JournalRecordType type, string docId, string parent, decimal cost = 0m,
        decimal media = 0m, string threadId = "") => new()
    {
        Type = type,
        AttachmentDocId = docId,
        ParentDocId = parent,
        ThreadId = threadId,
        CostUsd = cost,
        MediaCostUsd = media
    };

    private JournalRecord Repaired(string threadId, params JournalRecord[] attachments) => new()
    {
        Type = JournalRecordType.AttachmentsRepaired,
        PlanId = _plan.PlanId,
        ThreadId = threadId,
        Files = new List<string> { $"Mailboxes/pat.lee/Inbox/{threadId}-repaired.eml" },
        Attachments = attachments.ToList()
    };

    [Fact]
    public async Task Replay_NestedAttachment_StateAndSpend()
    {
        var done = Done("T-0003");
        done.CostUsd = 0.20m;
        done.Attachments.Add(Outcome(JournalRecordType.AttachmentDone, "PL-000001.0001", "PL-000001", 0.03m, 0.04m));
        await new GenerationJournal(_folder).AppendAsync(done);

        var replay = GenerationJournal.Replay(_folder, _plan);

        var attachment = replay.LastByAttachment["PL-000001.0001"];
        Assert.Equal(JournalRecordType.AttachmentDone, attachment.Type);
        Assert.Equal("T-0003", attachment.ThreadId);
        Assert.Equal(0.27m, replay.SpentCostUsd);
        Assert.Equal(0.04m, replay.MediaSpentCostUsd);
    }

    [Fact]
    public async Task Replay_ForeignIdentity_IgnoredButSpendCounted()
    {
        var done = Done("T-0003");
        done.Attachments.Add(Outcome(JournalRecordType.AttachmentDone, "PL-000099.0001", "PL-000099", 0.01m));
        done.Attachments.Add(Outcome(JournalRecordType.AttachmentDone, "PL-000001.0001", "PL-000002", 0.01m));
        done.Attachments.Add(Outcome(JournalRecordType.AttachmentDone, "PL-000002.0001", "PL-000002", 0.01m, 0.02m));
        await new GenerationJournal(_folder).AppendAsync(done);

        var replay = GenerationJournal.Replay(_folder, _plan);

        Assert.Empty(replay.LastByAttachment);
        Assert.Equal(0.05m, replay.SpentCostUsd);
        Assert.Equal(0.02m, replay.MediaSpentCostUsd);
    }

    [Fact]
    public async Task Replay_RepairAfterFailure_LastWinsSpendSums()
    {
        var journal = new GenerationJournal(_folder);
        var done = Done("T-0003");
        done.Attachments.Add(Outcome(JournalRecordType.AttachmentFailed, "PL-000001.0001", "PL-000001", 0.01m, 0.02m));
        await journal.AppendAsync(done);
        await journal.AppendAsync(Repaired("T-0003",
            Outcome(JournalRecordType.AttachmentDone, "PL-000001.0001", "PL-000001", 0.01m, 0.05m)));

        var replay = GenerationJournal.Replay(_folder, _plan);

        Assert.Equal(JournalRecordType.AttachmentDone, replay.LastByAttachment["PL-000001.0001"].Type);
        Assert.Equal(0.09m, replay.SpentCostUsd);
        Assert.Equal(0.07m, replay.MediaSpentCostUsd);
        Assert.Equal(JournalRecordType.ThreadDone, replay.LastByThread["T-0003"].Type);
    }

    [Fact]
    public async Task Replay_RepairWithoutWrittenThread_Ignored()
    {
        var journal = new GenerationJournal(_folder);
        await journal.AppendAsync(Repaired("T-0005",
            Outcome(JournalRecordType.AttachmentDone, "PL-000002.0001", "PL-000002", 0.01m)));
        await journal.AppendAsync(Failed("T-0003"));
        await journal.AppendAsync(Repaired("T-0003",
            Outcome(JournalRecordType.AttachmentDone, "PL-000001.0001", "PL-000001", 0.02m, 0.01m)));

        var replay = GenerationJournal.Replay(_folder, _plan);

        Assert.Empty(replay.LastByAttachment);
        Assert.Equal(0.04m, replay.SpentCostUsd);
        Assert.Equal(0.01m, replay.MediaSpentCostUsd);
    }

    [Fact]
    public async Task Replay_FreshThreadDone_ResetsThatThreadsAttachments()
    {
        var journal = new GenerationJournal(_folder);
        var first = Done("T-0003");
        first.Attachments.Add(Outcome(JournalRecordType.AttachmentDone, "PL-000001.0001", "PL-000001"));
        first.Attachments.Add(Outcome(JournalRecordType.AttachmentDone, "PL-000001.0002", "PL-000001"));
        var other = Done("T-0005");
        other.Attachments.Add(Outcome(JournalRecordType.AttachmentDone, "PL-000002.0001", "PL-000002"));
        await journal.AppendAsync(first);
        await journal.AppendAsync(other);
        await journal.AppendAsync(Failed("T-0003"));
        var second = Done("T-0003");
        second.Attachments.Add(Outcome(JournalRecordType.AttachmentSkipped, "PL-000001.0002", "PL-000001"));
        await journal.AppendAsync(second);

        var replay = GenerationJournal.Replay(_folder, _plan);

        Assert.False(replay.LastByAttachment.ContainsKey("PL-000001.0001"));
        Assert.Equal(JournalRecordType.AttachmentSkipped, replay.LastByAttachment["PL-000001.0002"].Type);
        Assert.Equal(JournalRecordType.AttachmentDone, replay.LastByAttachment["PL-000002.0001"].Type);
    }

    [Fact]
    public async Task Replay_StandaloneAttachmentLines_Validated()
    {
        var journal = new GenerationJournal(_folder);
        foreach (var record in new[]
                 {
                     Outcome(JournalRecordType.AttachmentDone, "PL-000001.0001", "PL-000001", 0.01m, threadId: "T-0003"),
                     Outcome(JournalRecordType.AttachmentFailed, "PL-000002.0001", "PL-000002", 0.01m, threadId: "T-0003"),
                     Outcome(JournalRecordType.AttachmentSkipped, "PL-000001.0002", "PL-000001", threadId: "T-0003")
                 })
        {
            record.PlanId = _plan.PlanId;
            await journal.AppendAsync(record);
        }

        var replay = GenerationJournal.Replay(_folder, _plan);

        Assert.Equal(new[] { "PL-000001.0001", "PL-000001.0002" }, replay.LastByAttachment.Keys.OrderBy(k => k).ToArray());
        Assert.Equal(0.02m, replay.SpentCostUsd);
    }

    [Fact]
    public async Task Replay_LatestAttemptEnded_TracksNewestRunStarted()
    {
        var journal = new GenerationJournal(_folder);
        var ended = new JournalRecord { Type = JournalRecordType.RunEnded, PlanId = _plan.PlanId, Outcome = GenerationOutcome.Cancelled };
        await journal.AppendAsync(Started());
        await journal.AppendAsync(ended);

        var once = GenerationJournal.Replay(_folder, _plan);
        Assert.True(once.LatestAttemptEnded);

        await journal.AppendAsync(Started());
        var interrupted = GenerationJournal.Replay(_folder, _plan);
        Assert.NotNull(interrupted.LastRunEnded);
        Assert.False(interrupted.LatestAttemptEnded);
    }

    [Fact]
    public async Task Replay_LatestAttemptEnded_LegacyRunEndedOnly()
    {
        await new GenerationJournal(_folder).AppendAsync(
            new JournalRecord { Type = JournalRecordType.RunEnded, PlanId = _plan.PlanId, Outcome = GenerationOutcome.Finished });

        var replay = GenerationJournal.Replay(_folder, _plan);

        Assert.True(replay.LatestAttemptEnded);
        Assert.False(GenerationJournal.Replay(Path.Combine(_folder, "missing"), _plan).LatestAttemptEnded);
    }

    [Fact]
    public async Task Replay_SideWorldAndCastVoices_LastWinsCostCounted()
    {
        var journal = new GenerationJournal(_folder);
        foreach (var name in new[] { "first", "second" })
        {
            await journal.AppendAsync(new JournalRecord
            {
                Type = JournalRecordType.SideWorld,
                PlanId = _plan.PlanId,
                CostUsd = 0.01m,
                SideWorld = new SideWorld
                {
                    Organizations = new List<SideWorldOrganization>
                    {
                        new() { OrganizationId = name, Projects = new List<SideProject> { new() { Name = name } } }
                    }
                }
            });
            await journal.AppendAsync(new JournalRecord
            {
                Type = JournalRecordType.CastVoices,
                PlanId = _plan.PlanId,
                CostUsd = 0.02m,
                CastVoices = new CastVoiceSet
                {
                    Voices = new List<CastVoice> { new() { PersonId = "CH-001", Voice = name == "first" ? "echo" : "nova" } }
                }
            });
        }

        var replay = GenerationJournal.Replay(_folder, _plan);

        Assert.Equal("second", replay.LastSideWorld!.SideWorld!.Organizations[0].OrganizationId);
        Assert.Equal("nova", replay.LastCastVoices!.CastVoices!.VoiceFor("CH-001"));
        Assert.Equal(0.06m, replay.SpentCostUsd);
    }

    [Fact]
    public async Task Replay_FutureTypeName_SkippedLine()
    {
        await new GenerationJournal(_folder).AppendAsync(Done("T-0003"));
        await File.AppendAllTextAsync(JournalPath, "{\"Type\":\"FutureThing\",\"PlanId\":\"" + _plan.PlanId + "\"}\n");

        var replay = GenerationJournal.Replay(_folder, _plan);

        Assert.Equal(1, replay.SkippedLines);
        Assert.Single(replay.LastByThread);
    }

    [Fact]
    public async Task Replay_NotRelevantThread_Kept()
    {
        await new GenerationJournal(_folder).AppendAsync(Done("T-0011"));

        var replay = GenerationJournal.Replay(_folder, _plan);

        Assert.Equal(JournalRecordType.ThreadDone, replay.LastByThread["T-0011"].Type);
    }

    [Fact]
    public async Task ListAllFiles_IncludesRepairedCopies()
    {
        var journal = new GenerationJournal(_folder);
        await journal.AppendAsync(Done("T-0003", null, "Mailboxes/a/Inbox/PL-000001.eml"));
        var repair = Repaired("T-0003");
        repair.Files = new List<string> { "Mailboxes/b/Inbox/PL-000001.eml", "../evil.eml" };
        await journal.AppendAsync(repair);

        var files = GenerationJournal.ListAllFiles(_folder);

        Assert.Equal(new[] { "Mailboxes/a/Inbox/PL-000001.eml", "Mailboxes/b/Inbox/PL-000001.eml" }, files);
    }

    [Fact]
    public async Task Replay_Locked_ClearsPhase6State()
    {
        var done = Done("T-0003");
        done.Attachments.Add(Outcome(JournalRecordType.AttachmentDone, "PL-000001.0001", "PL-000001", 0.01m, 0.01m));
        await new GenerationJournal(_folder).AppendAsync(done);

        using (new FileStream(JournalPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var replay = GenerationJournal.Replay(_folder, _plan);

            Assert.False(replay.Readable);
            Assert.Empty(replay.LastByAttachment);
            Assert.Null(replay.LastSideWorld);
            Assert.Null(replay.LastCastVoices);
            Assert.Equal(0m, replay.MediaSpentCostUsd);
            Assert.Equal(-1, replay.LastRunStartedLine);
            Assert.Equal(-1, replay.LastRunEndedLine);
            Assert.False(replay.LatestAttemptEnded);
        }
    }
}
