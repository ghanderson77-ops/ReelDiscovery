using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Tests.Corpus;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Evidence;

/// <summary>
/// Fake responder for attachment text calls, keyed by the request's SchemaName (one of AttachmentRenderer.SchemaTypes)
/// and the prompt's "Attachment id: {DocId}" line; every other request goes to EvidenceTestData.RenderResponder, so one
/// responder answers thread renders and attachments together. Later plans (06-16) extend it for filler thread DTOs.
/// </summary>
internal static partial class AttachmentResponder
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new JsonStringEnumConverter() } };

    [GeneratedRegex(@"Attachment id: (\S+)")]
    private static partial Regex AttachmentIdLine();

    /// <summary>
    /// <paramref name="overrideJson"/> gets (docId, attempt) and may return replacement JSON (null = default). Attempts
    /// are counted per DocId.
    /// </summary>
    public static Func<LlmProviderRequest, LlmProviderResponse> Create(CorpusPlan plan, CaseGuide guide,
        Func<string, int, string?>? overrideJson = null)
    {
        var bySchema = AttachmentRenderer.SchemaTypes.ToDictionary(JsonSchemaRegistry.GetSchemaName, t => t, StringComparer.Ordinal);
        var attempts = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        var render = EvidenceTestData.RenderResponder(plan, guide);

        return request =>
        {
            if (!bySchema.TryGetValue(request.SchemaName ?? string.Empty, out var type))
                return render(request);

            var match = AttachmentIdLine().Match(request.UserPrompt ?? string.Empty);
            if (!match.Success)
                return FakeLlmProvider.Failed(LlmFailureKind.Transient, "no attachment id");

            var docId = match.Groups[1].Value;
            var attempt = attempts.AddOrUpdate(docId, 1, (_, n) => n + 1);
            var custom = overrideJson?.Invoke(docId, attempt);
            if (custom != null)
                return FakeLlmProvider.Ok(custom, 800, 300);

            return FakeLlmProvider.Ok(JsonSerializer.Serialize(DefaultDto(type), type, JsonOptions), 800, 300);
        };
    }

    /// <summary>
    /// The deterministic valid DTO for a kind: Word 3 sections of 2 paragraphs and a 2x3 table; Excel headers
    /// ["Item","Amount"] and 12 rows; PowerPoint 5 slides of 3 bullets; invite title, agenda and location. Relevant DTOs
    /// carry one Figure detail.
    /// </summary>
    public static object DefaultDto(AttachmentType kind, bool relevant) => DefaultDto(AttachmentRenderer.DtoTypeFor(kind, relevant));

    private static object DefaultDto(Type type)
    {
        var sections = Enumerable.Range(1, 3)
            .Select(i => new AttachmentRenderer.WordSectionDto
            {
                Heading = $"Section {i}",
                Paragraphs = new() { $"The schedule for part {i} is updated and ready for review.", $"Next steps for part {i} follow this week." }
            })
            .ToList();
        var table = new AttachmentRenderer.SimpleTableDto
        {
            Headers = new() { "Item", "Status", "Owner" },
            Rows = new() { new() { "Frames", "Open", "Site team" }, new() { "Glass", "Closed", "Office" } }
        };
        var headers = new List<string> { "Item", "Amount" };
        var rows = Enumerable.Range(1, 12)
            .Select(i => new List<string> { $"Line item {i}", string.Format(CultureInfo.InvariantCulture, "${0:N0}", i * 1_000) })
            .ToList();
        var slides = Enumerable.Range(1, 5)
            .Select(i => new AttachmentRenderer.SlideDto { Title = $"Update {i}", Bullets = new() { "Schedule reviewed", "Next steps agreed", "Follow up next week" } })
            .ToList();
        List<ThreadRenderer.LedgerDeltaDto> Details() => new()
        {
            new()
            {
                Seq = 0,
                Kind = LedgerKind.Figure,
                About = "line item subtotal",
                Statement = "The line items total $78,000.",
                Value = "$78,000",
                FactIds = new()
            }
        };

        if (type == typeof(AttachmentRenderer.WordAttachmentDto))
            return new AttachmentRenderer.WordAttachmentDto { Title = "Project Memo", Sections = sections, Table = table, PeopleNamed = new(), NewDetails = Details() };
        if (type == typeof(AttachmentRenderer.FillerWordDto))
            return new AttachmentRenderer.FillerWordDto { Title = "Office Memo", Sections = sections, Table = table };
        if (type == typeof(AttachmentRenderer.ExcelAttachmentDto))
            return new AttachmentRenderer.ExcelAttachmentDto { Title = "Cost Backup", SheetName = "Costs", Headers = headers, Rows = rows, Notes = "Amounts exclude tax.", PeopleNamed = new(), NewDetails = Details() };
        if (type == typeof(AttachmentRenderer.FillerExcelDto))
            return new AttachmentRenderer.FillerExcelDto { Title = "Office Supplies", SheetName = "Supplies", Headers = headers, Rows = rows, Notes = string.Empty };
        if (type == typeof(AttachmentRenderer.PowerPointAttachmentDto))
            return new AttachmentRenderer.PowerPointAttachmentDto { Title = "Project Update", Slides = slides, PeopleNamed = new(), NewDetails = Details() };
        if (type == typeof(AttachmentRenderer.FillerPowerPointDto))
            return new AttachmentRenderer.FillerPowerPointDto { Title = "Team Update", Slides = slides };
        if (type == typeof(AttachmentRenderer.InviteAttachmentDto))
            return new AttachmentRenderer.InviteAttachmentDto { Title = "Project Meeting", Agenda = "Review the schedule and agree next steps.", Location = "Main conference room" };
        throw new ArgumentOutOfRangeException(nameof(type), type.Name, "Not an attachment DTO");
    }
}

public class AttachmentResponderTests
{
    [Fact]
    public async Task WordRequest_ThroughGateway_ReturnsDeserializedDto()
    {
        var ctx = AttachmentRendererTests.Context(AttachmentTestData.RelevantDocThreadId, AttachmentType.Word);
        var (gateway, fake) = CorpusTestData.CreateGateway();
        fake.Respond = AttachmentResponder.Create(ctx.Thread.Plan, ctx.Thread.Guide);

        var result = await gateway.GenerateAsync<AttachmentRenderer.WordAttachmentDto>(AttachmentRenderer.BuildRequest(ctx, 1, null));

        var dto = result.GetValueOrThrow();
        Assert.Equal(3, dto.Sections.Count);
        Assert.All(dto.Sections, s => Assert.Equal(2, s.Paragraphs.Count));
        Assert.Equal(3, dto.Table.Headers.Count);
        Assert.Equal(2, dto.Table.Rows.Count);
        var detail = Assert.Single(dto.NewDetails);
        Assert.Equal(LedgerKind.Figure, detail.Kind);
        Assert.Equal(JsonSchemaRegistry.GetSchemaName(typeof(AttachmentRenderer.WordAttachmentDto)), fake.Requests.Single().SchemaName);
    }

    [Fact]
    public async Task OverrideAndAttempts_ArePerDocId_ThreadRendersDelegate()
    {
        var ctx = AttachmentRendererTests.Context(AttachmentTestData.RelevantDocThreadId, AttachmentType.CalendarInvite);
        var (gateway, fake) = CorpusTestData.CreateGateway();
        var seen = new List<(string DocId, int Attempt)>();
        fake.Respond = AttachmentResponder.Create(ctx.Thread.Plan, ctx.Thread.Guide, (docId, attempt) =>
        {
            seen.Add((docId, attempt));
            return attempt == 1 ? "{\"title\":\"Custom\",\"agenda\":\"\",\"location\":\"\"}" : null;
        });

        var first = await gateway.GenerateAsync<AttachmentRenderer.InviteAttachmentDto>(AttachmentRenderer.BuildRequest(ctx, 1, null));
        var second = await gateway.GenerateAsync<AttachmentRenderer.InviteAttachmentDto>(AttachmentRenderer.BuildRequest(ctx, 2, null));
        var thread = await gateway.GenerateAsync<ThreadRenderer.ThreadRenderDto>(ThreadRenderer.BuildRequest(ctx.Thread, 1, null));

        Assert.Equal("Custom", first.GetValueOrThrow().Title);
        Assert.Equal("Project Meeting", second.GetValueOrThrow().Title);
        Assert.Equal(new[] { (ctx.Spec.DocId, 1), (ctx.Spec.DocId, 2) }, seen);
        Assert.Equal(ctx.Thread.Thread.Messages.Count, thread.GetValueOrThrow().Messages.Count);
    }

    [Fact]
    public void DefaultDtos_AreUsableForEveryKind()
    {
        foreach (var kind in new[] { AttachmentType.Word, AttachmentType.Excel, AttachmentType.PowerPoint, AttachmentType.CalendarInvite })
        {
            foreach (var relevant in new[] { true, false })
            {
                var dto = AttachmentResponder.DefaultDto(kind, relevant);
                var ctx = AttachmentRendererTests.Context(kind: kind);
                ctx.Relevant = relevant;

                var content = AttachmentRenderer.Map(dto, ctx);

                Assert.Equal(AttachmentRenderer.DtoTypeFor(kind, relevant), dto.GetType());
                Assert.True(AttachmentRenderer.IsUsable(content), $"{kind} relevant={relevant}");
                Assert.Equal(relevant && kind != AttachmentType.CalendarInvite ? 1 : 0, content.NewDetails.Count);
            }
        }
    }
}
