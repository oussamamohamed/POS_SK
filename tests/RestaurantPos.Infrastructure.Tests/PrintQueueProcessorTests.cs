using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Printing;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class PrintQueueProcessorTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeTransport : IPrinterTransport
    {
        public HashSet<Guid> Offline { get; } = [];
        public List<(Guid PrinterId, string FirstLine)> Sent { get; } = [];
        public Task SendAsync(PrinterConfiguration printer, TicketDocument document, bool openCashDrawer, CancellationToken ct)
        {
            if (Offline.Contains(printer.Id)) throw new SocketException((int)SocketError.ConnectionRefused);
            Sent.Add((printer.Id, ((TicketText)document.Lines[0]).Text));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeNotifier : IPrinterStatusNotifier
    {
        public List<(Guid PrinterId, bool IsOnline, int Pending)> Events { get; } = [];
        public Task PrinterStatusChangedAsync(Guid printerId, string printerName, bool isOnline, int pendingCount)
        {
            Events.Add((printerId, isOnline, pendingCount));
            return Task.CompletedTask;
        }
    }

    private sealed class Harness
    {
        public ServiceProvider Services { get; }
        public Clock Clock { get; } = new();
        public FakeTransport Transport { get; } = new();
        public FakeNotifier Notifier { get; } = new();
        public PrintQueueProcessor Processor { get; private set; } = null!;

        public Harness()
        {
            var name = "PrintQueue_" + Guid.NewGuid().ToString("N");
            Services = new ServiceCollection()
                .AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(name))
                .BuildServiceProvider();
            Restart();
        }

        /// <summary>Nouvelle instance (état mémoire vierge), même base : simule un redémarrage du serveur.</summary>
        public void Restart() => Processor = new PrintQueueProcessor(
            Services.GetRequiredService<IServiceScopeFactory>(), Transport, new PrinterStatusTracker(), Notifier, Clock, NullLogger<PrintQueueProcessor>.Instance);

        public T Db<T>(Func<AppDbContext, T> use)
        {
            using var scope = Services.CreateScope();
            return use(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        public PrinterConfiguration AddPrinter(string name, bool active = true) => Db(db =>
        {
            var printer = new PrinterConfiguration { Name = name, IpAddress = "10.0.0.1", IsActive = active };
            db.PrinterConfigurations.Add(printer);
            db.SaveChanges();
            return printer;
        });

        public async Task EnqueueAsync(Guid printerId, string text)
        {
            using var scope = Services.CreateScope();
            var queue = new PrintQueue(scope.ServiceProvider.GetRequiredService<AppDbContext>(), new PrintSignal(), Clock);
            await queue.EnqueueAsync(printerId, PrintJobKind.KitchenTicket, new TicketDocument("fr", false, [new TicketText(text)]), false);
            Clock.Now = Clock.Now.AddMilliseconds(1);
        }

        public async Task CycleAsync()
        {
            foreach (var id in await Processor.PrepareAsync(CancellationToken.None))
                await Processor.ProcessPrinterAsync(id, CancellationToken.None);
        }

        public List<PrintJob> Jobs() => Db(db => db.PrintJobs.AsNoTracking().ToList().OrderBy(j => j.CreatedAtUtc).ToList());
    }

    [Fact]
    public async Task Enqueue_SetsDeadlineThirtyMinutes()
    {
        var h = new Harness();
        var p = h.AddPrinter("Cuisine");
        var start = h.Clock.Now;
        await h.EnqueueAsync(p.Id, "A");
        var job = h.Jobs().Single();
        job.Status.Should().Be(PrintJobStatus.Pending);
        job.DeadlineAtUtc.Should().Be(start + TimeSpan.FromMinutes(30));
        job.NextAttemptAtUtc.Should().Be(start);
    }

    [Fact]
    public async Task Fifo_SendsJobsInCreationOrder()
    {
        var h = new Harness();
        var p = h.AddPrinter("Cuisine");
        foreach (var t in new[] { "1", "2", "3" }) await h.EnqueueAsync(p.Id, t);
        await h.CycleAsync();
        h.Transport.Sent.Select(s => s.FirstLine).Should().Equal("1", "2", "3");
        h.Jobs().Should().OnlyContain(j => j.Status == PrintJobStatus.Sent && j.SentAtUtc != null);
    }

    [Fact]
    public async Task HeadFailure_BlocksFollowingJobs_OfSamePrinterOnly()
    {
        var h = new Harness();
        var down = h.AddPrinter("Cuisine");
        var up = h.AddPrinter("Bar");
        h.Transport.Offline.Add(down.Id);
        await h.EnqueueAsync(down.Id, "d1");
        await h.EnqueueAsync(down.Id, "d2");
        await h.EnqueueAsync(up.Id, "u1");

        await h.CycleAsync();

        h.Transport.Sent.Select(s => s.FirstLine).Should().Equal("u1");
        var downJobs = h.Jobs().Where(j => j.PrinterId == down.Id).ToList();
        downJobs[0].Attempts.Should().Be(1);
        downJobs[0].NextAttemptAtUtc.Should().Be(h.Clock.Now + TimeSpan.FromSeconds(5));
        downJobs[1].Attempts.Should().Be(0);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 15)]
    [InlineData(3, 30)]
    [InlineData(4, 60)]
    [InlineData(9, 60)]
    public void RetryDelay_Schedule(int attempts, int seconds) =>
        PrintQueueProcessor.RetryDelay(attempts).Should().Be(TimeSpan.FromSeconds(seconds));

    [Fact]
    public async Task FailedJob_NotRetriedBeforeDelay()
    {
        var h = new Harness();
        var p = h.AddPrinter("Cuisine");
        h.Transport.Offline.Add(p.Id);
        await h.EnqueueAsync(p.Id, "A");
        await h.CycleAsync();
        h.Clock.Now += TimeSpan.FromSeconds(2);
        await h.CycleAsync();
        h.Jobs().Single().Attempts.Should().Be(1);
        h.Clock.Now += TimeSpan.FromSeconds(4);
        await h.CycleAsync();
        h.Jobs().Single().Attempts.Should().Be(2);
    }

    [Fact]
    public async Task Recovery_PrintsBacklogInOrder_AndNotifiesOnline()
    {
        var h = new Harness();
        var p = h.AddPrinter("Cuisine");
        h.Transport.Offline.Add(p.Id);
        await h.EnqueueAsync(p.Id, "1");
        await h.EnqueueAsync(p.Id, "2");
        await h.CycleAsync();
        h.Notifier.Events.Should().ContainSingle().Which.Should().Be((p.Id, false, 2));

        h.Transport.Offline.Clear();
        h.Clock.Now += TimeSpan.FromSeconds(6);
        await h.CycleAsync();

        h.Transport.Sent.Select(s => s.FirstLine).Should().Equal("1", "2");
        h.Notifier.Events.Last().IsOnline.Should().BeTrue();
    }

    [Fact]
    public async Task FirstSuccessFromUnknown_DoesNotNotify()
    {
        var h = new Harness();
        var p = h.AddPrinter("Cuisine");
        await h.EnqueueAsync(p.Id, "A");
        await h.CycleAsync();
        h.Notifier.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task DeadlinePassed_MarksFailed_AndNotifies()
    {
        var h = new Harness();
        var p = h.AddPrinter("Cuisine");
        h.Transport.Offline.Add(p.Id);
        await h.EnqueueAsync(p.Id, "A");
        await h.EnqueueAsync(p.Id, "B");
        await h.CycleAsync();
        h.Notifier.Events.Clear();

        h.Clock.Now += TimeSpan.FromMinutes(31);
        await h.CycleAsync();

        h.Jobs().Should().OnlyContain(j => j.Status == PrintJobStatus.Failed);
        h.Notifier.Events.Should().NotBeEmpty();
        h.Notifier.Events.Last().Should().Be((p.Id, false, 0));
    }

    [Fact]
    public async Task PendingJobs_AreResumedAfterRestart()
    {
        var h = new Harness();
        var p = h.AddPrinter("Cuisine");
        await h.EnqueueAsync(p.Id, "A");
        h.Restart();
        await h.CycleAsync();
        h.Transport.Sent.Should().ContainSingle();
    }

    [Fact]
    public async Task InactiveOrDeletedPrinter_CancelsPendingJobs()
    {
        var h = new Harness();
        var inactive = h.AddPrinter("Désactivée", active: false);
        await h.EnqueueAsync(inactive.Id, "A");
        await h.EnqueueAsync(Guid.NewGuid(), "B");
        await h.CycleAsync();
        h.Jobs().Should().OnlyContain(j => j.Status == PrintJobStatus.Cancelled && j.LastError != null);
        h.Transport.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Purge_RemovesOldSentAndCancelled_KeepsFailedAndRecent()
    {
        var h = new Harness();
        var p = h.AddPrinter("Cuisine");
        var old = h.Clock.Now.AddDays(-8);
        h.Db(db =>
        {
            foreach (var status in new[] { PrintJobStatus.Sent, PrintJobStatus.Cancelled, PrintJobStatus.Failed })
                db.PrintJobs.Add(new PrintJob { PrinterId = p.Id, DocumentJson = "{}", Status = status, CreatedAtUtc = old, NextAttemptAtUtc = old, DeadlineAtUtc = old });
            db.PrintJobs.Add(new PrintJob { PrinterId = p.Id, DocumentJson = "{}", Status = PrintJobStatus.Sent, CreatedAtUtc = h.Clock.Now.AddDays(-1), NextAttemptAtUtc = old, DeadlineAtUtc = old });
            return db.SaveChanges();
        });

        await h.Processor.PurgeAsync(CancellationToken.None);

        h.Jobs().Select(j => j.Status).Should().BeEquivalentTo([PrintJobStatus.Failed, PrintJobStatus.Sent]);
    }
}
