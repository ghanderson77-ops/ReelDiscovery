using System.Text.Json;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;

namespace ReelDiscovery.Tests.Corpus;

public class CorpusModelTests
{
    private static CorpusPlan SamplePlan()
    {
        var offset = TimeSpan.FromHours(-5);
        return new CorpusPlan
        {
            PlanId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            CreatedUtc = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc),
            GuideHash = "abc",
            SettingsHash = "def",
            DocIdPrefix = "BL",
            Threads =
            {
                new PlannedThread
                {
                    Id = "T-0001",
                    Relevance = ThreadRelevance.Relevant,
                    Category = NonRelevantCategory.None,
                    IssueIds = { "ISS-01" },
                    FactIds = { "F-003" },
                    Subject = "Change Order 7 approval",
                    Purpose = "Harmon approves CO-007",
                    Rationale = "Evidences approval of the change order",
                    OwnerCustodianId = "CH-001",
                    Messages =
                    {
                        new PlannedMessage
                        {
                            Id = "T-0001-M01",
                            DocId = "BL-000001",
                            Seq = 1,
                            Kind = MessageKind.New,
                            FromId = "CH-001",
                            ToIds = { "CH-002" },
                            CcIds = { "CH-003" },
                            SentAt = new DateTimeOffset(2024, 3, 18, 10, 14, 0, offset),
                            TimeZoneId = "America/Chicago",
                            FactIds = { "F-003" },
                            Basis = RelevanceBasis.Own,
                            IsHot = true,
                            Intent = "Ask for approval",
                            MustConvey = { "$184,250.00" },
                            Attachments =
                            {
                                new PlannedAttachment
                                {
                                    DocId = "BL-000001.0001",
                                    Kind = AttachmentType.Word,
                                    FileName = "CO-007.docx",
                                    Title = "Change Order 7",
                                    Description = "Signed change order"
                                }
                            },
                            FilePath = "Mail/CH-001/BL-000001.eml",
                            CustodianIds = { "CH-001", "CH-002" }
                        },
                        new PlannedMessage
                        {
                            Id = "T-0001-M02",
                            DocId = "BL-000002",
                            Seq = 2,
                            ReplyToSeq = 1,
                            Kind = MessageKind.ReplyAll,
                            FromId = "CH-002",
                            ToIds = { "CH-001" },
                            CcIds = { "CH-003" },
                            SentAt = new DateTimeOffset(2024, 3, 18, 11, 2, 0, offset),
                            TimeZoneId = "America/Chicago",
                            Basis = RelevanceBasis.Quoted
                        }
                    }
                },
                new PlannedThread
                {
                    Id = "T-0002",
                    Relevance = ThreadRelevance.NotRelevant,
                    Category = NonRelevantCategory.Decoy,
                    Subject = "Lakeview Tower parking",
                    DecoyAngle = "Mentions the project but not the change order",
                    MustNotMention = { "Change Order 7" },
                    Messages =
                    {
                        new PlannedMessage
                        {
                            Id = "T-0002-M01",
                            DocId = "BL-000003",
                            Seq = 1,
                            FromId = "EXT-001",
                            ToIds = { "CH-001" },
                            SentAt = new DateTimeOffset(2024, 3, 19, 8, 0, 0, offset)
                        }
                    }
                }
            },
            Externals = { new ExternalParty { Id = "EXT-001", Name = "Lot Co", Email = "info@lotco.com", Domain = "lotco.com", Kind = ExternalKind.Vendor } },
            Protocol = new ReviewProtocolProse
            {
                Purpose = "p",
                Issues = { new IssueProse { IssueId = "ISS-01", RelevantExamples = { "a" }, NotRelevantExamples = { "b" } } }
            },
            Notes = { "note" }
        };
    }

    [Fact]
    public void RoundTrip_SameJsonAndStringEnums()
    {
        var plan = SamplePlan();
        var json = JsonSerializer.Serialize(plan, CaseDraft.JsonOptions);

        var copy = JsonSerializer.Deserialize<CorpusPlan>(json, CaseDraft.JsonOptions)!;

        Assert.Equal(json, JsonSerializer.Serialize(copy, CaseDraft.JsonOptions));
        Assert.Contains("\"ReplyAll\"", json);
        Assert.Contains("\"Decoy\"", json);
        Assert.Contains("\"Quoted\"", json);
        Assert.Contains("\"Word\"", json);
        Assert.DoesNotContain("EmailCount", json);
        Assert.DoesNotContain("AttachmentCount", json);
    }

    [Fact]
    public void RoundTrip_KeepsSentAtOffset()
    {
        var json = JsonSerializer.Serialize(SamplePlan(), CaseDraft.JsonOptions);

        var copy = JsonSerializer.Deserialize<CorpusPlan>(json, CaseDraft.JsonOptions)!;

        var sent = copy.Threads[0].Messages[0].SentAt;
        Assert.Equal(TimeSpan.FromHours(-5), sent.Offset);
        Assert.Equal(new DateTimeOffset(2024, 3, 18, 10, 14, 0, TimeSpan.FromHours(-5)), sent);
        Assert.Contains("2024-03-18T10:14:00-05:00", json);
    }

    [Fact]
    public void Normalize_NullHeavyJson_NoNulls()
    {
        var plan = JsonSerializer.Deserialize<CorpusPlan>(
            "{\"threads\":[null,{\"messages\":null,\"id\":null,\"subject\":null,\"issueIds\":null}],\"externals\":null,\"notes\":null,\"protocol\":null,\"settings\":null,\"guideHash\":null}",
            CaseDraft.JsonOptions)!;

        plan.Normalize();

        var thread = Assert.Single(plan.Threads);
        Assert.Empty(thread.Messages);
        Assert.Equal("", thread.Id);
        Assert.Equal("", thread.Subject);
        Assert.Empty(thread.IssueIds);
        Assert.NotNull(thread.FactIds);
        Assert.NotNull(thread.MustNotMention);
        Assert.NotNull(thread.LinkedThreadIds);
        Assert.Equal("", thread.Purpose);
        Assert.Equal("", thread.Rationale);
        Assert.Equal("", thread.DecoyAngle);
        Assert.Equal("", thread.OwnerCustodianId);
        Assert.Empty(plan.Externals);
        Assert.Empty(plan.Notes);
        Assert.NotNull(plan.Protocol);
        Assert.Empty(plan.Protocol.Issues);
        Assert.Equal("", plan.Protocol.Purpose);
        Assert.NotNull(plan.Settings);
        Assert.Equal("", plan.GuideHash);
        Assert.Equal("", plan.SettingsHash);
        Assert.Equal("", plan.DocIdPrefix);
        Assert.Equal("", plan.Guidance);
    }

    [Fact]
    public void Normalize_NestedNulls_Cleaned()
    {
        var plan = JsonSerializer.Deserialize<CorpusPlan>(
            "{\"threads\":[{\"messages\":[null,{\"toIds\":null,\"ccIds\":[null,\"CH-001\"],\"attachments\":[null,{\"fileName\":null}],\"mustConvey\":null,\"intent\":null}]}]," +
            "\"externals\":[null,{\"name\":null}],\"protocol\":{\"issues\":[null,{\"relevantExamples\":null}],\"purpose\":null}}",
            CaseDraft.JsonOptions)!;

        plan.Normalize();

        var message = Assert.Single(plan.Threads[0].Messages);
        Assert.Empty(message.ToIds);
        Assert.Equal(new[] { "CH-001" }, message.CcIds);
        Assert.Empty(message.MustConvey);
        Assert.Empty(message.FactIds);
        Assert.Empty(message.CustodianIds);
        Assert.Equal("", message.Intent);
        Assert.Equal("", message.Id);
        Assert.Equal("", message.FromId);
        Assert.Equal("", message.FilePath);
        var attachment = Assert.Single(message.Attachments);
        Assert.Equal("", attachment.FileName);
        Assert.Equal("", attachment.DocId);
        var external = Assert.Single(plan.Externals);
        Assert.Equal("", external.Name);
        var issue = Assert.Single(plan.Protocol.Issues);
        Assert.Empty(issue.RelevantExamples);
        Assert.Empty(issue.NotRelevantExamples);
        Assert.Equal("", plan.Protocol.Purpose);
    }

    [Fact]
    public void AllMessages_ThreadOrderThenSeq()
    {
        var plan = SamplePlan();
        plan.Threads[0].Messages.Reverse();

        var ids = plan.AllMessages().Select(m => m.Id).ToList();

        Assert.Equal(new[] { "T-0001-M01", "T-0001-M02", "T-0002-M01" }, ids);
        Assert.Equal(3, plan.EmailCount);
        Assert.Equal(1, plan.AttachmentCount);
    }

    [Fact]
    public void AnswerKeyColumns_ExactOrder()
    {
        Assert.Equal(new[]
        {
            "DocID", "Doc Type", "File Path", "File Name", "Custodian", "All Custodians", "Date Sent", "From", "To", "CC",
            "Subject", "Thread ID", "Family ID", "Parent DocID", "Relevance", "Issue Codes", "Fact IDs", "Hot", "Privileged",
            "Near-Miss Decoy", "Non-Relevant Category", "Rationale", "Status"
        }, AnswerKeyColumns.Headers);
        Assert.Equal(23, AnswerKeyColumns.Headers.Length);
    }

    [Fact]
    public void AnswerKeyRow_ToValues_HeaderOrder()
    {
        var row = new AnswerKeyRow
        {
            DocId = "c1", DocType = "c2", FilePath = "c3", FileName = "c4", Custodian = "c5", AllCustodians = "c6",
            DateSent = "c7", From = "c8", To = "c9", Cc = "c10", Subject = "c11", ThreadId = "c12", FamilyId = "c13",
            ParentDocId = "c14", Relevance = "c15", IssueCodes = "c16", FactIds = "c17", Hot = "c18", Privileged = "c19",
            NearMiss = "c20", NonRelevantCategory = "c21", Rationale = "c22", Status = "c23"
        };

        var values = row.ToValues();

        Assert.Equal(Enumerable.Range(1, 23).Select(i => $"c{i}").ToArray(), values);
    }

    [Fact]
    public void AnswerKeyRow_DefaultsEmpty()
    {
        Assert.All(new AnswerKeyRow().ToValues(), v => Assert.Equal("", v));
    }
}
