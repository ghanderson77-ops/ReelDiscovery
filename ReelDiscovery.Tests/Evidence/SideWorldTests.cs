using System.Text.Json;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Tests.Case;
using ReelDiscovery.Tests.Corpus;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Evidence;

public class SideWorldTests
{
    private const string Hint = "Click Retry Generation.";

    private static CaseGuide Guide(int orgCount = 3)
    {
        var guide = new CaseGuide();
        for (var i = 1; i <= orgCount; i++)
        {
            guide.Organizations.Add(new CaseOrganization
            {
                Id = $"ORG-{i:00}",
                Name = $"Org{i} Holdings, Inc.",
                ShortName = $"Org{i}",
                Domain = $"org{i}.com",
                Kind = OrganizationKind.Party,
                Location = "Chicago, IL"
            });
        }

        guide.Facts.Add(new KeyFact { Id = "F-001", Title = "Curtain Wall Failure Review" });
        guide.Issues.Add(new CaseIssue { Id = "ISS-01", Title = "Breach of contract" });
        guide.Values.Add(new CanonicalValue { Id = "V-001", Kind = ValueKind.ChangeOrderNumber, Label = "Change order", Value = "CO-007" });
        guide.Values.Add(new CanonicalValue { Id = "V-002", Kind = ValueKind.Amount, Label = "Unpaid balance", Value = "$412,500" });
        guide.Values.Add(new CanonicalValue { Id = "V-003", Kind = ValueKind.Other, Label = "Notice date", Value = "2024-03-15" });
        guide.Values.Add(new CanonicalValue { Id = "V-004", Kind = ValueKind.ProjectName, Label = "Project", Value = "Lakeview Tower" });
        guide.Cast.Add(new CastMember { Id = "CH-001", FirstName = "Oscar", LastName = "Martinez", OrganizationId = "ORG-01" });
        return guide;
    }

    private static CaseDraft Draft(CaseGuide guide)
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.Guide = guide;
        return draft;
    }

    private static string Json(params (string OrgId, (string Name, string[] Topics)[] Projects)[] orgs) =>
        JsonSerializer.Serialize(new
        {
            organizations = orgs.Select(o => new
            {
                organizationId = o.OrgId,
                projects = o.Projects.Select(p => new { name = p.Name, topics = p.Topics })
            })
        });

    private static (string, string[])[] Clean(string prefix) => new[]
    {
        ($"{prefix} Payroll Migration", new[] { "vendor selection", "go-live date" }),
        ($"{prefix} Holiday Party", new[] { "venue booking", "catering" }),
        ($"{prefix} Fleet Leasing", new[] { "lease renewal", "insurance" })
    };

    private static async Task<(SideWorldOutcome Outcome, FakeLlmProvider Fake, PlanCallRun Run)> Build(CaseGuide guide, LlmProviderResponse response)
    {
        var (gateway, fake) = CorpusTestData.CreateGateway();
        fake.Responses.Enqueue(response);
        var run = new PlanCallRun(CancellationToken.None, 2, Hint);
        var outcome = await SideWorldBuilder.BuildAsync(gateway, Draft(guide), guide, run);
        return (outcome, fake, run);
    }

    [Fact]
    public async Task CleanResponse_BuildsThreeByThree_OneVolumeRequest()
    {
        var guide = Guide();
        var json = Json(("ORG-01", Clean("Alpha")), ("ORG-02", Clean("Beta")), ("ORG-03", Clean("Gamma")));

        var (outcome, fake, run) = await Build(guide, FakeLlmProvider.Ok(json));
        using var _ = run;

        var world = outcome.World!;
        Assert.False(world.FromFallback);
        Assert.Equal(3, world.Organizations.Count);
        Assert.All(world.Organizations, o => Assert.Equal(3, o.Projects.Count));
        Assert.Equal("Alpha Payroll Migration", world.Organizations[0].Projects[0].Name);
        Assert.Single(fake.Requests);
        Assert.Equal(CaseTestData.VolumeModel().ModelId, fake.Requests[0].ModelId);
        Assert.Equal("SideWorldDto", fake.Requests[0].SchemaName);
        Assert.Equal(SideWorldBuilder.SystemPrompt, fake.Requests[0].SystemPrompt);

        var request = SideWorldBuilder.BuildRequest(Draft(guide), guide);
        Assert.Equal(LlmTier.Volume, request.Tier);
        Assert.Equal("Generate Corpus - side projects", request.OperationName);
        Assert.Contains("ORG-02 Org2 Holdings, Inc. (org2.com)", request.UserPrompt);
        Assert.Contains("Give each organization 3 or 4 projects with 2-4 topics each.", request.UserPrompt);
        Assert.Contains("Do not use these names or terms:", request.UserPrompt);
        Assert.Contains("- Lakeview Tower", request.UserPrompt);
        Assert.Contains("Keep every name the document uses exactly as written", request.UserPrompt);

        // Builder-owned wording never says "fictional" (the shared name rules mention it only for applied substitutions)
        var plain = Draft(guide);
        plain.Substitutions.Clear();
        var plainRequest = SideWorldBuilder.BuildRequest(plain, guide);
        Assert.DoesNotContain("fictional", plainRequest.UserPrompt + plainRequest.SystemPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CollidingProjects_Dropped_TopUpToTwo()
    {
        var guide = Guide();
        var json = Json(
            ("ORG-01", new[]
            {
                ("Curtain Wall Failure Review", new[] { "scheduling" }),
                ("Project CO-007", new[] { "budgets" }),
                ("Alpha Payroll Migration", new[] { "vendor selection" })
            }),
            ("ORG-02", Clean("Beta")),
            ("ORG-03", Clean("Gamma")));

        var (outcome, _, run) = await Build(guide, FakeLlmProvider.Ok(json));
        using var __ = run;

        var world = outcome.World!;
        Assert.False(world.FromFallback);
        var org1 = world.Organizations.Single(o => o.OrganizationId == "ORG-01");
        Assert.Equal(2, org1.Projects.Count);
        Assert.Equal("Alpha Payroll Migration", org1.Projects[0].Name);
        Assert.DoesNotContain(org1.Projects, p => p.Name.Contains("Curtain Wall") || p.Name.Contains("CO-007"));
        var template = SideWorldBuilder.TemplatesFor(guide.Organizations[0], SideWorldBuilder.CaseTerms(guide))[0];
        Assert.Equal(template.Name, org1.Projects[1].Name);
        Assert.Equal(3, world.Organizations.Single(o => o.OrganizationId == "ORG-02").Projects.Count);
    }

    [Fact]
    public async Task UnknownOrgDropped_MissingOrgGetsTemplates()
    {
        var guide = Guide();
        var json = Json(("ORG-01", Clean("Alpha")), ("ORG-99", Clean("Ghost")), ("ORG-03", Clean("Gamma")));

        var (outcome, _, run) = await Build(guide, FakeLlmProvider.Ok(json));
        using var _ = run;

        var world = outcome.World!;
        Assert.False(world.FromFallback);
        Assert.Equal(new[] { "ORG-01", "ORG-02", "ORG-03" }, world.Organizations.Select(o => o.OrganizationId));
        var expected = SideWorldBuilder.TemplatesFor(guide.Organizations[1], SideWorldBuilder.CaseTerms(guide)).Select(p => p.Name);
        Assert.Equal(expected, world.Organizations[1].Projects.Select(p => p.Name));
        Assert.DoesNotContain(world.Organizations.SelectMany(o => o.Projects), p => p.Name.StartsWith("Ghost"));
    }

    [Fact]
    public async Task Refusal_AllTemplates_FromFallback()
    {
        var guide = Guide();
        var (outcome, _, run) = await Build(guide, FakeLlmProvider.Refused("No."));
        using var _ = run;

        var world = outcome.World!;
        Assert.True(world.FromFallback);
        Assert.Equal(3, world.Organizations.Count);
        Assert.All(world.Organizations, o => Assert.Equal(3, o.Projects.Count));
        Assert.False(run.IsFatal);
    }

    [Fact]
    public async Task ProviderFailure_AllTemplates_FromFallback()
    {
        var guide = Guide();
        var (outcome, _, run) = await Build(guide, FakeLlmProvider.Failed(LlmFailureKind.BadRequest, "bad"));
        using var _ = run;

        Assert.True(outcome.World!.FromFallback);
        Assert.Equal(3, outcome.World.Organizations.Count);
    }

    [Fact]
    public async Task AuthFailure_WorldNull_RunFatal()
    {
        var guide = Guide();
        var (outcome, _, run) = await Build(guide, FakeLlmProvider.Failed(LlmFailureKind.Auth, "bad key"));
        using var _ = run;

        Assert.Null(outcome.World);
        Assert.True(run.IsFatal);
    }

    [Fact]
    public void Templates_Deterministic_NonColliding_Generic()
    {
        var guide = Guide();
        var terms = SideWorldBuilder.CaseTerms(guide);
        var first = SideWorldBuilder.TemplatesFor(guide.Organizations[0], terms);
        var second = SideWorldBuilder.TemplatesFor(guide.Organizations[0], terms);

        Assert.Equal(3, first.Count);
        Assert.Equal(first.Select(p => p.Name), second.Select(p => p.Name));
        Assert.All(first, p =>
        {
            Assert.False(SideWorldBuilder.Collides(p.Name, terms));
            Assert.All(p.Topics, t => Assert.False(SideWorldBuilder.Collides(t, terms)));
        });

        var fallback = SideWorldBuilder.Fallback(guide);
        Assert.Equal(first.Select(p => p.Name), fallback.Organizations[0].Projects.Select(p => p.Name));
    }

    [Fact]
    public void FactTitledFacilitiesRefresh_TemplateNeverUsed()
    {
        var guide = Guide(10);
        guide.Facts.Add(new KeyFact { Id = "F-002", Title = "Facilities Refresh" });
        var terms = SideWorldBuilder.CaseTerms(guide);

        foreach (var org in guide.Organizations)
        {
            var projects = SideWorldBuilder.TemplatesFor(org, terms);
            Assert.Equal(3, projects.Count);
            Assert.DoesNotContain(projects, p => p.Name == "Facilities Refresh");
        }

        // Without the fact some organization would have drawn it
        var clean = SideWorldBuilder.CaseTerms(Guide(10));
        Assert.Contains(Guide(10).Organizations, o => SideWorldBuilder.TemplatesFor(o, clean).Any(p => p.Name == "Facilities Refresh"));
    }

    [Fact]
    public async Task Result_PassesNormalizeLimits()
    {
        var guide = Guide();
        var longName = new string('x', 120);
        var many = Enumerable.Range(1, 6).Select(i => ($"Alpha Program {i} {longName}", new[] { "a1", "b2", "c3", "d4", "e5" })).ToArray();
        var json = Json(("ORG-01", many), ("ORG-02", Clean("Beta")), ("ORG-03", Clean("Gamma")));

        var (outcome, _, run) = await Build(guide, FakeLlmProvider.Ok(json));
        using var _ = run;

        var org1 = outcome.World!.Organizations[0];
        Assert.Equal(SideWorld.MaxProjects, org1.Projects.Count);
        Assert.All(org1.Projects, p =>
        {
            Assert.True(p.Name.Length <= SideWorld.MaxNameChars);
            Assert.True(p.Topics.Count <= SideWorld.MaxTopics);
        });
    }

    [Fact]
    public void Dto_PassesStrictSchema()
    {
        foreach (var type in SideWorldBuilder.SchemaTypes)
        {
            var schema = JsonSchemaRegistry.GetSchemaJson(type);
            var errors = StrictSchemaValidator.Validate(schema);
            Assert.True(errors.Count == 0, string.Join("; ", errors));
        }
    }

    [Fact]
    public async Task CollidingTopics_Dropped_ProjectKept_CastNameProjectDropped()
    {
        var guide = Guide();
        var json = Json(
            ("ORG-01", new[]
            {
                ("Alpha Payroll Migration", new[] { "CO-007 follow-up", "go-live date" }),
                ("Alpha Holiday Party", new[] { "deposit of $412,500", "catering", "booking for 2024-03-15" }),
                ("Oscar Martinez", new[] { "scheduling" }),
                ("Alpha Fleet Leasing", new[] { "CO-007 follow-up" })
            }),
            ("ORG-02", Clean("Beta")),
            ("ORG-03", Clean("Gamma")));

        var (outcome, _, run) = await Build(guide, FakeLlmProvider.Ok(json));
        using var _ = run;

        var org1 = outcome.World!.Organizations[0];
        Assert.Equal(new[] { "Alpha Payroll Migration", "Alpha Holiday Party" }, org1.Projects.Select(p => p.Name));
        Assert.Equal(new[] { "go-live date" }, org1.Projects[0].Topics);
        Assert.Equal(new[] { "catering" }, org1.Projects[1].Topics);
    }

    [Fact]
    public async Task ThirtyOrganizations_CappedAt24_RestFromTemplates()
    {
        var guide = Guide(30);
        var orgs = guide.Organizations.Select(o => (o.Id, Clean(o.ShortName))).ToArray();
        var json = Json(orgs);

        var request = SideWorldBuilder.BuildRequest(Draft(guide), guide);
        var listed = guide.Organizations.Count(o => request.UserPrompt.Contains(o.Id + " "));
        Assert.Equal(24, listed);
        Assert.DoesNotContain("ORG-25 ", request.UserPrompt);

        var (outcome, _, run) = await Build(guide, FakeLlmProvider.Ok(json));
        using var _ = run;

        var world = outcome.World!;
        Assert.Equal(24, world.Organizations.Count);
        var terms = SideWorldBuilder.CaseTerms(guide);
        foreach (var org in guide.Organizations)
        {
            var projects = SideWorldBuilder.ProjectsFor(world, org, guide);
            if (string.CompareOrdinal(org.Id, "ORG-24") <= 0)
                Assert.Equal($"{org.ShortName} Payroll Migration", projects[0].Name);
            else
                Assert.Equal(SideWorldBuilder.TemplatesFor(org, terms).Select(p => p.Name), projects.Select(p => p.Name));
        }
    }

    [Fact]
    public void FromRecord_ReturnsWorldOnlyWhenCovered()
    {
        var guide = Guide();
        var world = SideWorldBuilder.Fallback(guide);
        var record = new JournalRecord { Type = JournalRecordType.SideWorld, SideWorld = world };

        Assert.Same(world, SideWorldBuilder.FromRecord(record, guide));
        Assert.Null(SideWorldBuilder.FromRecord(null, guide));
        Assert.Null(SideWorldBuilder.FromRecord(new JournalRecord { Type = JournalRecordType.SideWorld }, guide));

        var partial = SideWorldBuilder.Fallback(guide);
        partial.Organizations.RemoveAt(1);
        Assert.Null(SideWorldBuilder.FromRecord(new JournalRecord { Type = JournalRecordType.SideWorld, SideWorld = partial }, guide));

        // 30 organizations: coverage of the first 24 is enough
        var big = Guide(30);
        var bigWorld = SideWorldBuilder.Fallback(big);
        Assert.Equal(24, bigWorld.Organizations.Count);
        Assert.NotNull(SideWorldBuilder.FromRecord(new JournalRecord { Type = JournalRecordType.SideWorld, SideWorld = bigWorld }, big));
    }

    // IN-03: an organization whose templates all collided has no projects; the journaled world must still be reused
    [Fact]
    public void FromRecord_CoveredOrganizationWithNoProjects_WorldReused()
    {
        var guide = Guide();
        var world = SideWorldBuilder.Fallback(guide);
        world.Organizations[1].Projects.Clear();
        var record = new JournalRecord { Type = JournalRecordType.SideWorld, SideWorld = world };

        Assert.Same(world, SideWorldBuilder.FromRecord(record, guide));
    }
}
