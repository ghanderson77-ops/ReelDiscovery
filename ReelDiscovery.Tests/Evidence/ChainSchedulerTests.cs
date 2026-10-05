using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Tests.Evidence;

/// <summary>
/// ChainScheduler (06-18): deterministic D-16 predecessor text and deadlock-free waits. A thread waits only on in-step
/// threads of strictly lower rank, so every wait edge points to a lower rank (checker 06-18).
/// </summary>
public class ChainSchedulerTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(100);

    // ---------- fixture helpers ----------

    private static AttachmentChainLink Link(string chain, int version, int count, string previous) => new()
    {
        ChainId = chain,
        Version = version,
        Count = count,
        PreviousDocId = previous,
        BaseTitle = chain
    };

    /// <summary>
    /// Chains as (docId, threadId) lists in version order; ranks by the given thread order.
    /// </summary>
    private static ChainScheduler Scheduler(IReadOnlyDictionary<string, string>? committed, IEnumerable<string> rankOrder,
        params (string Chain, (string DocId, string ThreadId)[] Versions)[] chains)
    {
        var links = new Dictionary<string, AttachmentChainLink>(StringComparer.Ordinal);
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (chain, versions) in chains)
        {
            for (var i = 0; i < versions.Length; i++)
            {
                links[versions[i].DocId] = Link(chain, i + 1, versions.Length, i > 0 ? versions[i - 1].DocId : string.Empty);
                owners[versions[i].DocId] = versions[i].ThreadId;
            }
        }

        var ranks = rankOrder.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i, StringComparer.Ordinal);
        return new ChainScheduler(links, committed ?? new Dictionary<string, string>(), owners, ranks);
    }

    private static Dictionary<string, string> Texts(params (string DocId, string Text)[] items) =>
        items.ToDictionary(i => i.DocId, i => i.Text, StringComparer.Ordinal);

    /// <summary>
    /// A simulated thread unit exactly like ThreadUnitOfWork: attachments in order; a same-thread predecessor comes from
    /// the unit's own texts, any other from PriorTextAsync; Complete only after every own attachment. Returns each
    /// attachment's prior text.
    /// </summary>
    private static async Task<Dictionary<string, string?>> SimulateUnit(ChainScheduler scheduler, string threadId,
        IReadOnlyList<string> docIds, Random? random = null)
    {
        var priors = new Dictionary<string, string?>(StringComparer.Ordinal);
        var own = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var docId in docIds)
            {
                if (random != null)
                    await Task.Delay(random.Next(0, 4));

                var link = scheduler.Links.TryGetValue(docId, out var l) ? l : null;
                string? prior = null;
                if (link != null && link.PreviousDocId.Length > 0)
                {
                    prior = docIds.Contains(link.PreviousDocId)
                        ? own.TryGetValue(link.PreviousDocId, out var text) ? text : null
                        : await scheduler.PriorTextAsync(docId, CancellationToken.None);
                }

                priors[docId] = prior;
                own[docId] = $"{docId} written after [{prior ?? "none"}]";
            }
        }
        finally
        {
            scheduler.Complete(threadId, own);
        }

        return priors;
    }

    private static async Task<List<Dictionary<string, string?>>> RunUnits(ChainScheduler scheduler,
        IReadOnlyList<(string ThreadId, string[] DocIds)> units, Random? random = null)
    {
        scheduler.BeginStep(units.Select(u => u.ThreadId));
        var tasks = units.Select(u => Task.Run(() => SimulateUnit(scheduler, u.ThreadId, u.DocIds, random))).ToList();
        var results = await Task.WhenAll(tasks).WaitAsync(Guard);
        return results.ToList();
    }

    private static string Flatten(IEnumerable<Dictionary<string, string?>> results) =>
        string.Join("\n", results.SelectMany(r => r).OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value ?? "<null>"}"));

    private static PlannedThread Thread(string id, DateTimeOffset firstSent) => new()
    {
        Id = id,
        Messages = new List<PlannedMessage> { new() { Seq = 1, SentAt = firstSent, DocId = id + "-M1" } }
    };

    // ---------- basic lookups ----------

    [Fact]
    public async Task Seeded_PriorTextReturnedImmediately()
    {
        var scheduler = Scheduler(Texts(("v1", "draft one")), new[] { "A", "B" }, ("c", new[] { ("v1", "A"), ("v2", "B") }));
        scheduler.BeginStep(new[] { "B" });

        var text = await scheduler.PriorTextAsync("v2", CancellationToken.None).WaitAsync(Short);

        Assert.Equal("draft one", text);
    }

    [Fact]
    public async Task InStepLowerRanked_WaitsForComplete_WhicheverStartsFirst()
    {
        for (var i = 0; i < 50; i++)
        {
            var random = new Random(i);
            var scheduler = Scheduler(null, new[] { "A", "B" }, ("c", new[] { ("v1", "A"), ("v2", "B") }));
            scheduler.BeginStep(new[] { "A", "B" });

            var waiter = Task.Run(async () =>
            {
                await Task.Delay(random.Next(0, 5));
                return await scheduler.PriorTextAsync("v2", CancellationToken.None);
            });
            var completer = Task.Run(async () =>
            {
                await Task.Delay(random.Next(0, 5));
                scheduler.Complete("A", Texts(("v1", "v1 text")));
            });

            await Task.WhenAll(waiter, completer).WaitAsync(Guard);
            Assert.Equal("v1 text", await waiter);
        }
    }

    [Fact]
    public async Task InStep_WaitDoesNotFinishBeforeComplete()
    {
        var scheduler = Scheduler(null, new[] { "A", "B" }, ("c", new[] { ("v1", "A"), ("v2", "B") }));
        scheduler.BeginStep(new[] { "A", "B" });

        var waiter = scheduler.PriorTextAsync("v2", CancellationToken.None);
        await Task.Delay(50);
        Assert.False(waiter.IsCompleted);

        scheduler.Complete("A", Texts(("v1", "v1 text")));
        Assert.Equal("v1 text", await waiter.WaitAsync(Guard));
    }

    [Fact]
    public async Task CompleteWithoutText_NothingSeeded_ReturnsNull()
    {
        var scheduler = Scheduler(null, new[] { "A", "B" }, ("c", new[] { ("v1", "A"), ("v2", "B") }));
        scheduler.BeginStep(new[] { "A", "B" });

        var waiter = scheduler.PriorTextAsync("v2", CancellationToken.None);
        scheduler.Complete("A", new Dictionary<string, string>());

        Assert.Null(await waiter.WaitAsync(Guard));
    }

    [Fact]
    public async Task PredecessorNotCommittedNotInStep_ReturnsNullImmediately()
    {
        var scheduler = Scheduler(null, new[] { "A", "B" }, ("c", new[] { ("v1", "A"), ("v2", "B") }));
        scheduler.BeginStep(new[] { "B" });

        Assert.Null(await scheduler.PriorTextAsync("v2", CancellationToken.None).WaitAsync(Short));
        Assert.Null(await scheduler.PriorTextAsync("v1", CancellationToken.None).WaitAsync(Short));
        Assert.Null(await scheduler.PriorTextAsync("unknown", CancellationToken.None).WaitAsync(Short));
    }

    [Fact]
    public async Task CancelledToken_EndsWait()
    {
        var scheduler = Scheduler(null, new[] { "A", "B" }, ("c", new[] { ("v1", "A"), ("v2", "B") }));
        scheduler.BeginStep(new[] { "A", "B" });
        using var cts = new CancellationTokenSource();

        var waiter = scheduler.PriorTextAsync("v2", cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter.WaitAsync(Guard));
    }

    [Fact]
    public async Task Complete_IsIdempotent_NeverRemovesText()
    {
        var scheduler = Scheduler(null, new[] { "A", "B" }, ("c", new[] { ("v1", "A"), ("v2", "B") }));
        scheduler.BeginStep(new[] { "A", "B" });

        scheduler.Complete("A", Texts(("v1", "v1 text")));
        scheduler.Complete("A", new Dictionary<string, string>());
        scheduler.Complete("A", new Dictionary<string, string>());

        Assert.Equal("v1 text", await scheduler.PriorTextAsync("v2", CancellationToken.None).WaitAsync(Short));
    }

    [Fact]
    public async Task CrossStep_CompleteInStep1_VisibleInStep2()
    {
        var scheduler = Scheduler(null, new[] { "A", "B", "C" },
            ("c", new[] { ("v1", "A"), ("v2", "B"), ("v3", "C") }));

        scheduler.BeginStep(new[] { "A" });
        scheduler.Complete("A", Texts(("v1", "draft one")));

        scheduler.BeginStep(new[] { "B" });
        Assert.Equal("draft one", await scheduler.PriorTextAsync("v2", CancellationToken.None).WaitAsync(Short));

        // Step 3: v2 never committed, v1's text is still in the run's store
        scheduler.BeginStep(new[] { "C" });
        Assert.Null(await scheduler.PriorTextAsync("v3", CancellationToken.None).WaitAsync(Short));
        Assert.Equal("draft one", await scheduler.PriorTextAsync("v2", CancellationToken.None).WaitAsync(Short));
    }

    [Fact]
    public async Task Repair_InStepPredecessorWithOldText_WaitsThenNewest()
    {
        foreach (var regenerated in new[] { true, false })
        {
            var scheduler = Scheduler(Texts(("v1", "old")), new[] { "A", "B" }, ("c", new[] { ("v1", "A"), ("v2", "B") }));
            scheduler.BeginStep(new[] { "A", "B" });

            var waiter = scheduler.PriorTextAsync("v2", CancellationToken.None);
            await Task.Delay(30);
            Assert.False(waiter.IsCompleted);

            scheduler.Complete("A", regenerated ? Texts(("v1", "new")) : new Dictionary<string, string>());

            Assert.Equal(regenerated ? "new" : "old", await waiter.WaitAsync(Guard));
        }
    }

    [Fact]
    public async Task SameThread_PredecessorNeverWaits()
    {
        foreach (var seeded in new[] { true, false })
        {
            var scheduler = Scheduler(seeded ? Texts(("v1", "committed")) : null, new[] { "A" },
                ("c", new[] { ("v1", "A"), ("v2", "A") }));
            scheduler.BeginStep(new[] { "A" });

            var text = await scheduler.PriorTextAsync("v2", CancellationToken.None).WaitAsync(Short);

            Assert.Equal(seeded ? "committed" : null, text);
            Assert.Empty(scheduler.WaitEdges("A"));
        }
    }

    [Fact]
    public async Task WaitForPredecessors_WaitsOnlyForOtherInStepThreads()
    {
        var scheduler = Scheduler(null, new[] { "A", "B", "C" }, ("c", new[] { ("v1", "A"), ("v2", "B") }));
        scheduler.BeginStep(new[] { "A", "B", "C" });

        var waitB = scheduler.WaitForPredecessorsAsync("B", CancellationToken.None);
        await scheduler.WaitForPredecessorsAsync("A", CancellationToken.None).WaitAsync(Short);
        await scheduler.WaitForPredecessorsAsync("C", CancellationToken.None).WaitAsync(Short);
        await Task.Delay(30);
        Assert.False(waitB.IsCompleted);
        Assert.Equal(new[] { ("B", "A") }, scheduler.WaitEdges("B"));

        scheduler.Complete("A", Texts(("v1", "v1 text")));
        await waitB.WaitAsync(Guard);

        // After the wait, PriorTextAsync answers without blocking
        Assert.Equal("v1 text", await scheduler.PriorTextAsync("v2", CancellationToken.None).WaitAsync(Short));

        scheduler.BeginStep(new[] { "A", "B" });
        using var cts = new CancellationTokenSource();
        var cancelled = scheduler.WaitForPredecessorsAsync("B", cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.WaitAsync(Guard));

        using var already = new CancellationTokenSource();
        already.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scheduler.WaitForPredecessorsAsync("B", already.Token));
    }

    // ---------- rank ----------

    [Fact]
    public void RankThreads_OrderIsFirstSentThenId()
    {
        var t0 = new DateTimeOffset(2024, 3, 1, 9, 0, 0, TimeSpan.Zero);
        var plan = new CorpusPlan
        {
            Threads = new List<PlannedThread>
            {
                Thread("T-0003", t0.AddHours(1)),
                Thread("T-0002", t0),
                Thread("T-0001", t0),
                Thread("T-0004", t0.AddHours(-1))
            }
        };

        var ranks = ChainScheduler.RankThreads(plan);

        Assert.Equal(0, ranks["T-0004"]);
        Assert.Equal(1, ranks["T-0001"]);
        Assert.Equal(2, ranks["T-0002"]);
        Assert.Equal(3, ranks["T-0003"]);

        var reversed = new CorpusPlan { Threads = Enumerable.Reverse(plan.Threads).ToList() };
        Assert.Equal(ranks.OrderBy(p => p.Key), ChainScheduler.RankThreads(reversed).OrderBy(p => p.Key));

        // A thread missing from the plan ranks after every planned one
        var scheduler = new ChainScheduler(new Dictionary<string, AttachmentChainLink>(), new Dictionary<string, string>(),
            new Dictionary<string, string>(), ranks);
        Assert.True(scheduler.Compare("T-0003", "T-0000") < 0);
        Assert.True(scheduler.Compare("T-0000", "T-0004") > 0);
        Assert.True(scheduler.Compare("T-0000", "T-9999") < 0);
        Assert.Equal(0, scheduler.Compare("T-0001", "T-0001"));
    }

    [Fact]
    public async Task Rank_HigherRankedInStepPredecessor_NeverWaits_UsesStepStartText()
    {
        foreach (var seeded in new[] { true, false })
        {
            foreach (var completeFirst in new[] { true, false })
            {
                // v1 in B, v2 in A, rank(A) < rank(B)
                var scheduler = Scheduler(seeded ? Texts(("v1", "old")) : null, new[] { "A", "B" },
                    ("c", new[] { ("v1", "B"), ("v2", "A") }));
                scheduler.BeginStep(new[] { "A", "B" });

                if (completeFirst)
                    scheduler.Complete("B", Texts(("v1", "new")));
                var text = await scheduler.PriorTextAsync("v2", CancellationToken.None).WaitAsync(Short);
                if (!completeFirst)
                    scheduler.Complete("B", Texts(("v1", "new")));

                Assert.Equal(seeded ? "old" : null, text);
                await scheduler.WaitForPredecessorsAsync("A", CancellationToken.None).WaitAsync(Short);
                Assert.Empty(scheduler.WaitEdges("A"));
            }
        }
    }

    [Fact]
    public async Task Chain_InterleavedAcrossTwoThreads_SchedulerNoDeadlock()
    {
        // Chain v1 (A msg 1) -> v2 (B) -> v3 (A msg 3), rank(A) < rank(B)
        var units = new[] { ("A", new[] { "v1", "v3" }), ("B", new[] { "v2" }) };
        string? expected = null;
        for (var i = 0; i < 52; i++)
        {
            var scheduler = Scheduler(null, new[] { "A", "B" }, ("c", new[] { ("v1", "A"), ("v2", "B"), ("v3", "A") }));
            var order = i % 2 == 0 ? units : units.Reverse().ToArray();
            var results = await RunUnits(scheduler, order, i < 2 ? null : new Random(i));
            var flat = Flatten(results);
            expected ??= flat;
            Assert.Equal(expected, flat);
        }

        // B's v2 got A's v1 text; A's v3 got the step-start text of v2 (none)
        Assert.Contains("v2=v1 written after [none]", expected);
        Assert.Contains("v3=<null>", expected);
    }

    [Fact]
    public async Task Chain_CrossingChainsTwoThreads_SchedulerNoDeadlock()
    {
        // Chain X: x1 (A) -> x2 (B); chain Y: y1 (B) -> y2 (A); rank(A) < rank(B)
        var units = new[] { ("A", new[] { "x1", "y2" }), ("B", new[] { "y1", "x2" }) };
        foreach (var seeded in new[] { true, false })
        {
            string? expected = null;
            for (var i = 0; i < 52; i++)
            {
                var scheduler = Scheduler(seeded ? Texts(("y1", "old")) : null, new[] { "A", "B" },
                    ("x", new[] { ("x1", "A"), ("x2", "B") }), ("y", new[] { ("y1", "B"), ("y2", "A") }));
                var order = i % 2 == 0 ? units : units.Reverse().ToArray();
                var flat = Flatten(await RunUnits(scheduler, order, i < 2 ? null : new Random(i)));
                expected ??= flat;
                Assert.Equal(expected, flat);
            }

            Assert.Contains("x2=x1 written after [none]", expected);
            Assert.Contains(seeded ? "y2=old" : "y2=<null>", expected);
        }
    }

    [Fact]
    public async Task Rank_WaitGraphIsAcyclic()
    {
        var threads = Enumerable.Range(0, 8).Select(i => $"T-{i:0000}").ToArray();
        foreach (var seed in new[] { 1, 7, 42, 99, 2024 })
        {
            var random = new Random(seed);

            // Random chains over the 8 threads, mapped in any order (cycles in the chain-to-thread mapping included)
            var chains = new List<(string Chain, (string DocId, string ThreadId)[] Versions)>();
            var doc = 0;
            for (var c = 0; c < 6; c++)
            {
                var length = random.Next(2, 6);
                var versions = Enumerable.Range(0, length)
                    .Select(_ => ($"d{doc++}", threads[random.Next(threads.Length)]))
                    .ToArray();
                chains.Add(($"chain{c}", versions));
            }

            // Deliberate cycle: T-0001 -> T-0002 -> T-0001 through two chains
            chains.Add(("cycleA", new[] { ($"d{doc++}", "T-0001"), ($"d{doc++}", "T-0002") }));
            chains.Add(("cycleB", new[] { ($"d{doc++}", "T-0002"), ($"d{doc++}", "T-0001") }));

            var rankOrder = threads.OrderBy(_ => random.Next()).ToArray();
            var scheduler = Scheduler(null, rankOrder, chains.ToArray());
            scheduler.BeginStep(threads);

            var rank = rankOrder.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
            foreach (var thread in threads)
            {
                foreach (var (from, to) in scheduler.WaitEdges(thread))
                {
                    Assert.Equal(thread, from);
                    Assert.True(rank[to] < rank[from], $"{from} (rank {rank[from]}) waits on {to} (rank {rank[to]})");
                }
            }

            // Each unit's attachments in chain-version order within the thread
            var units = threads
                .Select(t => (t, chains.SelectMany(c => c.Versions).Where(v => v.ThreadId == t).Select(v => v.DocId).ToArray()))
                .ToList();
            var tasks = units.Select(u => Task.Run(() => SimulateUnit(scheduler, u.t, u.Item2, random: new Random(seed)))).ToList();
            await Task.WhenAll(tasks).WaitAsync(Guard);
        }
    }
}
