using ReelDiscovery.Models;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Evidence;

/// <summary>
/// AttachmentRenderer: strict per-kind DTOs, clamping and mapping (T-06-32, T-06-35), flattening for checks and chains,
/// and the per-kind usable-content rule (review 06-11).
/// </summary>
public class AttachmentRendererTests
{
    internal static AttachmentRenderContext Context(string threadId = AttachmentTestData.RelevantDocThreadId,
        AttachmentType kind = AttachmentType.Word, int parentSeq = 1)
    {
        var (draft, plan) = AttachmentTestData.MixedCase();
        var thread = AttachmentTestData.Thread(plan, threadId);
        var assignment = new ThreadAssignment
        {
            ThreadId = thread.Id,
            Messages = thread.Messages.Select(m => new MessageAssignment { Seq = m.Seq, Length = LengthBand.Medium }).ToList()
        };
        var people = PeopleDirectory.Create(draft.Guide!, plan);
        var threadCtx = ThreadRenderContext.Create(draft, plan, thread, assignment, people, new List<LedgerEntry>(), 2);
        var parent = thread.Messages.Single(m => m.Seq == parentSeq);
        var spec = parent.Attachments.FirstOrDefault(a => a.Kind == kind)
            ?? new PlannedAttachment { DocId = parent.DocId + ".0009", Kind = kind, Title = "Test document", FileName = "Test document.docx" };
        var relevant = thread.Relevance == ThreadRelevance.Relevant;
        return new AttachmentRenderContext
        {
            Thread = threadCtx,
            Parent = parent,
            Spec = spec,
            Title = spec.Title,
            FileName = spec.FileName,
            ParentBody = "Attached is the document we discussed.",
            Relevant = relevant,
            Tier = relevant ? LlmTier.Story : LlmTier.Volume
        };
    }

    private static List<string> Strings(int count, string prefix) =>
        Enumerable.Range(1, count).Select(i => $"{prefix} {i}").ToList();

    // ---------- strict schemas (D-01) ----------

    public static TheoryData<Type> AttachmentSchemaTypes()
    {
        var data = new TheoryData<Type>();
        foreach (var type in AttachmentRenderer.SchemaTypes)
            data.Add(type);
        return data;
    }

    [Theory]
    [MemberData(nameof(AttachmentSchemaTypes))]
    public void AllAttachmentSchemaTypes_AreStrict(Type type)
    {
        var schema = JsonSchemaRegistry.GetSchemaJson(type);

        var errors = StrictSchemaValidator.Validate(schema);

        Assert.True(errors.Count == 0, $"{type.Name}: {string.Join("; ", errors)}");
        Assert.True(StrictSchemaValidator.CountUnionTypes(schema) <= 16, $"{type.Name} exceeds 16 union types");
    }

    [Fact]
    public void SchemaTypes_HoldsSevenDtos_FillerHaveNoNewDetails()
    {
        Assert.Equal(7, AttachmentRenderer.SchemaTypes.Count);
        foreach (var filler in new[] { typeof(AttachmentRenderer.FillerWordDto), typeof(AttachmentRenderer.FillerExcelDto), typeof(AttachmentRenderer.FillerPowerPointDto) })
        {
            Assert.Null(filler.GetProperty("NewDetails"));
            Assert.Null(filler.GetProperty("PeopleNamed"));
        }

        Assert.Null(typeof(AttachmentRenderer.InviteAttachmentDto).GetProperty("NewDetails"));
    }

    [Fact]
    public void DtoTypeFor_PicksRelevantOrFillerShape()
    {
        Assert.Equal(typeof(AttachmentRenderer.WordAttachmentDto), AttachmentRenderer.DtoTypeFor(AttachmentType.Word, true));
        Assert.Equal(typeof(AttachmentRenderer.FillerWordDto), AttachmentRenderer.DtoTypeFor(AttachmentType.Word, false));
        Assert.Equal(typeof(AttachmentRenderer.ExcelAttachmentDto), AttachmentRenderer.DtoTypeFor(AttachmentType.Excel, true));
        Assert.Equal(typeof(AttachmentRenderer.FillerExcelDto), AttachmentRenderer.DtoTypeFor(AttachmentType.Excel, false));
        Assert.Equal(typeof(AttachmentRenderer.PowerPointAttachmentDto), AttachmentRenderer.DtoTypeFor(AttachmentType.PowerPoint, true));
        Assert.Equal(typeof(AttachmentRenderer.FillerPowerPointDto), AttachmentRenderer.DtoTypeFor(AttachmentType.PowerPoint, false));
        Assert.Equal(typeof(AttachmentRenderer.InviteAttachmentDto), AttachmentRenderer.DtoTypeFor(AttachmentType.CalendarInvite, true));
        Assert.Equal(typeof(AttachmentRenderer.InviteAttachmentDto), AttachmentRenderer.DtoTypeFor(AttachmentType.CalendarInvite, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => AttachmentRenderer.DtoTypeFor(AttachmentType.Image, true));
    }

    // ---------- clamping (T-06-32) ----------

    [Fact]
    public void Map_Word_ClampsSectionsParagraphsTableAndChars()
    {
        var dto = new AttachmentRenderer.WordAttachmentDto
        {
            Title = new string('t', 2_500),
            Sections = Enumerable.Range(1, 10)
                .Select(i => new AttachmentRenderer.WordSectionDto { Heading = $"Section {i}", Paragraphs = Strings(20, "Paragraph") })
                .ToList(),
            Table = new AttachmentRenderer.SimpleTableDto
            {
                Headers = Strings(12, "Col"),
                Rows = Enumerable.Range(1, 100).Select(r => Strings(12, $"R{r}C")).ToList()
            }
        };

        var content = AttachmentRenderer.Map(dto, Context());

        Assert.Equal(AttachmentType.Word, content.Kind);
        Assert.Equal(AttachmentRenderer.MaxChars, content.Title.Length);
        Assert.Equal(AttachmentRenderer.MaxSections, content.Sections.Count);
        Assert.All(content.Sections, s => Assert.Equal(AttachmentRenderer.MaxParagraphs, s.Paragraphs.Count));
        Assert.NotNull(content.Table);
        Assert.Equal(AttachmentRenderer.MaxColumns, content.Table!.Headers.Count);
        Assert.Equal(AttachmentRenderer.MaxRows, content.Table.Rows.Count);
        Assert.All(content.Table.Rows, r => Assert.Equal(AttachmentRenderer.MaxColumns, r.Count));
    }

    [Fact]
    public void Map_Word_EmptyTableHeaders_MapToNoTable()
    {
        var dto = new AttachmentRenderer.WordAttachmentDto
        {
            Title = "Memo",
            Sections = new() { new() { Heading = "Summary", Paragraphs = new() { "Text." } } },
            Table = new AttachmentRenderer.SimpleTableDto { Headers = new() { " ", "" }, Rows = new() { new() { "a", "b" } } }
        };

        Assert.Null(AttachmentRenderer.Map(dto, Context()).Table);
    }

    [Fact]
    public void Map_Excel_PadsOrCutsRowsToHeaders_SheetNameFallsBackToTitle()
    {
        var dto = new AttachmentRenderer.ExcelAttachmentDto
        {
            Title = "Pricing Backup",
            SheetName = "  ",
            Headers = new() { "Item", "Amount", "Note" },
            Rows = new()
            {
                new() { "Frames" },
                new() { "Glass", "$1,000", "ok", "extra", "more" }
            },
            Notes = "Totals exclude tax."
        };

        var content = AttachmentRenderer.Map(dto, Context(kind: AttachmentType.Excel));

        Assert.Equal("Pricing Backup", content.SheetName);
        Assert.Equal(new[] { "Frames", "", "" }, content.Rows[0]);
        Assert.Equal(new[] { "Glass", "$1,000", "ok" }, content.Rows[1]);
        Assert.Equal("Totals exclude tax.", content.Notes);
    }

    [Fact]
    public void Map_Excel_ClampsColumnsAndRows()
    {
        var dto = new AttachmentRenderer.ExcelAttachmentDto
        {
            Title = "Big",
            SheetName = "Data",
            Headers = Strings(12, "Col"),
            Rows = Enumerable.Range(1, 100).Select(r => Strings(12, $"R{r}C")).ToList()
        };

        var content = AttachmentRenderer.Map(dto, Context(kind: AttachmentType.Excel));

        Assert.Equal(AttachmentRenderer.MaxColumns, content.Headers.Count);
        Assert.Equal(AttachmentRenderer.MaxRows, content.Rows.Count);
        Assert.Equal("Data", content.SheetName);
    }

    [Fact]
    public void Map_PowerPoint_ClampsSlidesAndBullets()
    {
        var dto = new AttachmentRenderer.PowerPointAttachmentDto
        {
            Title = "Update",
            Slides = Enumerable.Range(1, 30)
                .Select(i => new AttachmentRenderer.SlideDto { Title = $"Slide {i}", Bullets = Strings(15, "Bullet") })
                .ToList()
        };

        var content = AttachmentRenderer.Map(dto, Context(kind: AttachmentType.PowerPoint));

        Assert.Equal(AttachmentRenderer.MaxSlides, content.Slides.Count);
        Assert.All(content.Slides, s => Assert.Equal(AttachmentRenderer.MaxBullets, s.Bullets.Count));
    }

    // ---------- ledger deltas (D-18) ----------

    [Fact]
    public void Map_RelevantNewDetails_UseParentSeqAndFromAttachment()
    {
        var ctx = Context(AttachmentTestData.SiblingBranchThreadId, AttachmentType.Word, parentSeq: 2);
        var dto = new AttachmentRenderer.WordAttachmentDto
        {
            Title = "Branch A Summary",
            Sections = new() { new() { Heading = "Summary", Paragraphs = new() { "Text." } } },
            NewDetails = new()
            {
                new ThreadRenderer.LedgerDeltaDto { Seq = 9, Kind = LedgerKind.Figure, About = "glass line item", Statement = "Glass is $1,000.", Value = "$1,000" }
            }
        };

        var delta = Assert.Single(AttachmentRenderer.Map(dto, ctx).NewDetails);

        Assert.Equal(2, delta.Seq);
        Assert.True(delta.FromAttachment);
        Assert.Equal(ctx.Spec.DocId, delta.AttachmentDocId);
        Assert.Equal(LedgerKind.Figure, delta.Kind);
        Assert.Equal("$1,000", delta.Value);
    }

    [Fact]
    public void Map_FillerDtos_ProduceNoDeltas()
    {
        var ctx = Context(AttachmentTestData.DailyBusinessThreadId, AttachmentType.Word);

        var word = AttachmentRenderer.Map(new AttachmentRenderer.FillerWordDto
        {
            Title = "Certificate",
            Sections = new() { new() { Heading = "Coverage", Paragraphs = new() { "General liability renewed." } } }
        }, ctx);
        var excel = AttachmentRenderer.Map(new AttachmentRenderer.FillerExcelDto { Title = "Forms", Headers = new() { "A" }, Rows = new() { new() { "1" } } }, ctx);
        var deck = AttachmentRenderer.Map(new AttachmentRenderer.FillerPowerPointDto { Title = "Deck", Slides = new() { new() { Title = "One" } } }, ctx);

        Assert.Empty(word.NewDetails);
        Assert.Empty(excel.NewDetails);
        Assert.Empty(deck.NewDetails);
        Assert.Equal(AttachmentType.Excel, excel.Kind);
        Assert.Equal(AttachmentType.PowerPoint, deck.Kind);
    }

    [Fact]
    public void Map_StripsControlCharactersEverywhere()
    {
        var bad = "Line\u0001one\r\ntwo\u0007\tthree\u001b";
        var dto = new AttachmentRenderer.WordAttachmentDto
        {
            Title = bad,
            Sections = new() { new() { Heading = bad, Paragraphs = new() { bad } } },
            Table = new AttachmentRenderer.SimpleTableDto { Headers = new() { bad }, Rows = new() { new() { bad } } },
            PeopleNamed = new() { bad },
            NewDetails = new() { new ThreadRenderer.LedgerDeltaDto { About = bad, Statement = bad, Value = bad, FactIds = new() { "F-003" } } }
        };
        var invite = AttachmentRenderer.Map(new AttachmentRenderer.InviteAttachmentDto { Title = bad, Agenda = bad, Location = bad },
            Context(AttachmentTestData.InviteThreadId, AttachmentType.CalendarInvite));

        var content = AttachmentRenderer.Map(dto, Context());

        var all = new List<string> { content.Title, content.Sections[0].Heading, content.Table!.Headers[0], content.Table.Rows[0][0] }
            .Concat(content.Sections[0].Paragraphs)
            .Concat(content.PeopleNamed)
            .Concat(content.NewDetails.SelectMany(d => new[] { d.About, d.Statement, d.Value }))
            .Concat(new[] { invite.Title, invite.Agenda, invite.Location })
            .ToList();
        Assert.All(all, s => Assert.DoesNotMatch(@"\p{Cc}", s));
        Assert.Equal("Lineone two three", content.Title);
    }

    // ---------- flattening ----------

    [Fact]
    public void Flatten_Word_TitleHeadingsParagraphsAndCells()
    {
        var content = new AttachmentContent
        {
            Kind = AttachmentType.Word,
            Title = "Memo",
            Sections = new() { new WordSection { Heading = "Scope", Paragraphs = new() { "First.", "Second." } } },
            Table = new SimpleTable { Headers = new() { "Item", "Amount" }, Rows = new() { new() { "Frames", "$1,000" } } }
        };

        Assert.Equal("Memo\nScope\nFirst.\nSecond.\nItem\nAmount\nFrames\n$1,000", AttachmentRenderer.Flatten(content));
    }

    [Fact]
    public void Flatten_Excel_RowsJoinedByPipes()
    {
        var content = new AttachmentContent
        {
            Kind = AttachmentType.Excel,
            Title = "Backup",
            Headers = new() { "Item", "Amount" },
            Rows = new() { new() { "Frames", "$1,000" }, new() { "Glass", "$2,000" } },
            Notes = "Excludes tax."
        };

        Assert.Equal("Backup\nItem | Amount\nFrames | $1,000\nGlass | $2,000\nExcludes tax.", AttachmentRenderer.Flatten(content));
    }

    [Fact]
    public void Flatten_PowerPointAndInvite()
    {
        var deck = new AttachmentContent
        {
            Kind = AttachmentType.PowerPoint,
            Title = "Update",
            Slides = new() { new SlideContent { Title = "Status", Bullets = new() { "On track", "Two weeks late" } } }
        };
        var invite = new AttachmentContent { Kind = AttachmentType.CalendarInvite, Title = "Site walk", Agenda = "Walk floors 7-9", Location = "Site trailer" };

        Assert.Equal("Update\nStatus\nOn track\nTwo weeks late", AttachmentRenderer.Flatten(deck));
        Assert.Equal("Site walk\nWalk floors 7-9\nSite trailer", AttachmentRenderer.Flatten(invite));
    }

    [Fact]
    public void VariantText_Excel_KeepsLabelledRowsAndNotesOnly()
    {
        var content = new AttachmentContent
        {
            Kind = AttachmentType.Excel,
            Title = "Backup",
            Headers = new() { "Item", "Amount" },
            Rows = new()
            {
                new() { "Frames", "$1,240,000" },
                new() { "Contract value", "$1,250,000" },
                new() { "Contract values history", "$9" },
                new() { "Retainage on pay app 7", "$5,000" }
            },
            Notes = "Totals exclude tax."
        };

        var text = AttachmentRenderer.VariantText(content, new[] { "Contract value", "retainage" });

        Assert.Equal("Contract value | $1,250,000\nRetainage on pay app 7 | $5,000\nTotals exclude tax.", text);
        Assert.DoesNotContain("Frames", text);
    }

    [Fact]
    public void VariantText_OtherKinds_EqualsFlatten()
    {
        var content = new AttachmentContent
        {
            Kind = AttachmentType.Word,
            Title = "Memo",
            Sections = new() { new WordSection { Heading = "Scope", Paragraphs = new() { "First." } } }
        };

        Assert.Equal(AttachmentRenderer.Flatten(content), AttachmentRenderer.VariantText(content, new[] { "Contract value" }));
    }

    // ---------- usable content (review 06-11) ----------

    [Fact]
    public void IsUsable_Word_NeedsANonEmptyParagraph()
    {
        var ctx = Context();
        Assert.False(AttachmentRenderer.IsUsable(AttachmentRenderer.Map(new AttachmentRenderer.WordAttachmentDto { Title = "Memo" }, ctx)));
        Assert.False(AttachmentRenderer.IsUsable(AttachmentRenderer.Map(new AttachmentRenderer.WordAttachmentDto
        {
            Title = "Memo",
            Sections = new() { new() { Heading = "Scope", Paragraphs = new() { " ", "" } } }
        }, ctx)));
        Assert.True(AttachmentRenderer.IsUsable(AttachmentRenderer.Map(new AttachmentRenderer.WordAttachmentDto
        {
            Title = "Memo",
            Sections = new() { new() { Heading = "Scope", Paragraphs = new() { "Real text." } } }
        }, ctx)));
        Assert.False(AttachmentRenderer.IsUsable(AttachmentRenderer.Map(new AttachmentRenderer.FillerWordDto { Title = "Memo" }, ctx)));
    }

    [Fact]
    public void IsUsable_Excel_NeedsHeaderAndARowWithANonEmptyCell()
    {
        var ctx = Context(kind: AttachmentType.Excel);
        Assert.False(AttachmentRenderer.IsUsable(AttachmentRenderer.Map(new AttachmentRenderer.ExcelAttachmentDto
        {
            Title = "Backup",
            Headers = new() { "Item", "Amount" },
            Rows = new() { new() { "", " " } }
        }, ctx)));
        Assert.False(AttachmentRenderer.IsUsable(AttachmentRenderer.Map(new AttachmentRenderer.ExcelAttachmentDto
        {
            Title = "Backup",
            Rows = new() { new() { "Frames" } }
        }, ctx)));
        Assert.True(AttachmentRenderer.IsUsable(AttachmentRenderer.Map(new AttachmentRenderer.ExcelAttachmentDto
        {
            Title = "Backup",
            Headers = new() { "Item", "Amount" },
            Rows = new() { new() { "Frames", "$1,000" } }
        }, ctx)));
        Assert.False(AttachmentRenderer.IsUsable(AttachmentRenderer.Map(new AttachmentRenderer.FillerExcelDto
        {
            Title = "Forms",
            Headers = new() { "A" }
        }, ctx)));
    }

    [Fact]
    public void IsUsable_PowerPoint_NeedsASlideTitleOrBullet_InviteNeedsTitle()
    {
        var ctx = Context(kind: AttachmentType.PowerPoint);
        Assert.False(AttachmentRenderer.IsUsable(AttachmentRenderer.Map(new AttachmentRenderer.PowerPointAttachmentDto
        {
            Title = "Deck",
            Slides = new() { new() { Title = " ", Bullets = new() { "" } } }
        }, ctx)));
        Assert.True(AttachmentRenderer.IsUsable(AttachmentRenderer.Map(new AttachmentRenderer.PowerPointAttachmentDto
        {
            Title = "Deck",
            Slides = new() { new() { Title = "", Bullets = new() { "On track" } } }
        }, ctx)));
        Assert.False(AttachmentRenderer.IsUsable(AttachmentRenderer.Map(new AttachmentRenderer.FillerPowerPointDto { Title = "Deck" }, ctx)));

        var inviteCtx = Context(AttachmentTestData.InviteThreadId, AttachmentType.CalendarInvite);
        Assert.False(AttachmentRenderer.IsUsable(AttachmentRenderer.Map(new AttachmentRenderer.InviteAttachmentDto { Title = " ", Agenda = "Walk" }, inviteCtx)));
        Assert.True(AttachmentRenderer.IsUsable(AttachmentRenderer.Map(new AttachmentRenderer.InviteAttachmentDto { Title = "Site walk" }, inviteCtx)));
    }

    [Fact]
    public void Map_UnknownDto_Throws()
    {
        Assert.Throws<ArgumentException>(() => AttachmentRenderer.Map(new object(), Context()));
    }
}
