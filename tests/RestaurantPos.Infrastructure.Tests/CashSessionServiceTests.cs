using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public sealed class CashSessionServiceTests
{
    private static readonly Guid Op = Guid.NewGuid();

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("CashDb_" + Guid.NewGuid().ToString("N")).Options);

    private static CashSessionService Service(AppDbContext db) => new(db, new AlertService(db));

    /// <summary>Reçu du poste avec ses règlements (montant imputé, remis, rendu) ; avoir si <paramref name="credit"/>.</summary>
    private static void Receipt(AppDbContext db, string terminal, bool credit, params (PaymentMethod Method, long Amount, long Tendered)[] tenders)
    {
        var receipt = new FiscalReceipt { TerminalId = terminal, ReceiptNumber = Guid.NewGuid().ToString("N")[..8], OrderId = Guid.NewGuid(), CreatedAtUtc = DateTimeOffset.UtcNow, VoidedReceiptId = credit ? Guid.NewGuid() : null };
        foreach (var (method, amount, tendered) in tenders)
            receipt.Tenders.Add(new PaymentTender { Method = method, Amount = Money.FromCents(amount), Tendered = Money.FromCents(tendered), ChangeGiven = Money.FromCents(Math.Max(0, tendered - amount)) });
        db.FiscalReceipts.Add(receipt);
        db.SaveChanges();
    }

    [Fact]
    public async Task Expected_IsFloatPlusKeptCash_NetOfChange_Credits_AndMovements()
    {
        using var db = NewDb();
        var svc = Service(db);
        var session = await svc.OpenAsync("T01", Op, 10_000);

        Receipt(db, "T01", false, (PaymentMethod.Cash, 5_000, 6_000));                                  // 50 € réglés avec un billet de 60 € : 10 € rendus
        Receipt(db, "T01", false, (PaymentMethod.CreditCard, 3_000, 3_000), (PaymentMethod.Cash, 1_000, 1_000));
        Receipt(db, "T01", true, (PaymentMethod.Cash, -2_000, -2_000));                                  // avoir espèces : l'attendu baisse
        Receipt(db, "T02", false, (PaymentMethod.Cash, 9_999, 9_999));                                   // autre poste : ignoré
        await svc.AddMovementAsync(session.Id, "out", 1_200, "Achat de glace", Op);
        await svc.AddMovementAsync(session.Id, "in", 500, "Fond complémentaire", Op);

        var current = (await svc.CurrentAsync("T01"))!;

        current.ExpectedCents.Should().Be(10_000 + 5_000 + 1_000 - 2_000 - 1_200 + 500);
    }

    [Fact]
    public async Task OneOpenSession_PerTerminal_AndNoFloatMeansStartingFromZero()
    {
        using var db = NewDb();
        var svc = Service(db);
        await svc.OpenAsync("T01", Op, 0);

        await FluentActions.Awaiting(() => svc.OpenAsync("T01", Op, 100)).Should().ThrowAsync<CashException>().Where(e => e.Code == "cash_session_already_open");
        (await svc.OpenAsync("T02", Op, 0)).ExpectedCents.Should().Be(0);
        (await svc.CurrentAsync("T01"))!.ExpectedCents.Should().Be(0);
    }

    [Fact]
    public async Task Count_FreezesExpectedAndVariance_AndIsIdempotent()
    {
        using var db = NewDb();
        var svc = Service(db);
        var session = await svc.OpenAsync("T01", Op, 10_000);
        Receipt(db, "T01", false, (PaymentMethod.Cash, 5_000, 5_000));
        await svc.AddMovementAsync(session.Id, "out", 1_200, "Achat de glace", Op);

        var first = (await svc.CountAsync(session.Id, 13_800, null, null, Op))!;
        Receipt(db, "T01", false, (PaymentMethod.Cash, 700, 700)); // vente après le comptage : sans effet sur le comptage figé
        var again = (await svc.CountAsync(session.Id, 1, null, null, Op))!;

        first.Should().Be(new CashCountResult(13_800, 13_800, 0, false));
        again.Should().Be(first);
        db.CashCounts.Should().ContainSingle();
        (await svc.CurrentAsync("T01")).Should().BeNull(); // la session comptée n'est plus ouverte
        (await svc.OpenAsync("T01", Op, 5_000)).Should().NotBeNull();
    }

    [Fact]
    public async Task VarianceNote_IsRequiredAboveTheActiveThreshold()
    {
        using var db = NewDb();
        var alerts = new AlertService(db);
        await alerts.SetRuleAsync(AlertRuleTypes.CashVariance, 1_000, true, null);
        var svc = new CashSessionService(db, alerts);
        var session = await svc.OpenAsync("T01", Op, 10_000);

        await FluentActions.Awaiting(() => svc.CountAsync(session.Id, 8_000, null, "  ", Op)).Should().ThrowAsync<CashException>().Where(e => e.Code == "variance_note_required");
        var small = await svc.CountAsync(session.Id, 9_500, null, null, Op); // −5 € : sous le seuil, pas de note
        small!.VarianceCents.Should().Be(-500);
    }

    [Fact]
    public async Task Movements_NeedAReasonOf3To200Chars_AMeaningfulAmount_AndAnOpenSession()
    {
        using var db = NewDb();
        var svc = Service(db);
        var session = await svc.OpenAsync("T01", Op, 0);

        foreach (var (kind, amount, reason) in new[] { ("out", 100L, "ab"), ("out", 100L, new string('x', 201)), ("out", 0L, "Motif valable"), ("autre", 100L, "Motif valable") })
            await FluentActions.Awaiting(() => svc.AddMovementAsync(session.Id, kind, amount, reason, Op)).Should().ThrowAsync<CashException>().Where(e => e.Code == "movement_invalid");

        (await svc.AddMovementAsync(Guid.NewGuid(), "out", 100, "Motif valable", Op)).Should().BeNull();
        await svc.CountAsync(session.Id, 0, null, null, Op);
        await FluentActions.Awaiting(() => svc.AddMovementAsync(session.Id, "out", 100, "Motif valable", Op)).Should().ThrowAsync<CashException>().Where(e => e.Code == "cash_session_closed");
    }

    [Fact]
    public async Task EveryStep_WritesItsJournalEvent()
    {
        using var db = NewDb();
        var svc = Service(db);
        var session = await svc.OpenAsync("T01", Op, 1_000);
        await svc.AddMovementAsync(session.Id, "out", 100, "Achat de glace", Op);
        await svc.CountAsync(session.Id, 900, null, null, Op);

        db.JournalEntries.Select(j => j.EventType).Should().Contain([JournalEventTypes.CashSessionOpened, JournalEventTypes.CashMovement, JournalEventTypes.CashCounted]);
    }

    [Fact]
    public async Task JournalChain_StaysValid_WithTheCashAndAlertEvents()
    {
        using var db = NewDb();
        var alerts = new AlertService(db);
        await alerts.SetRuleAsync(AlertRuleTypes.CashVariance, 100, true, null);
        var svc = new CashSessionService(db, alerts);
        var session = await svc.OpenAsync("T01", Op, 1_000);
        await svc.AddMovementAsync(session.Id, "out", 100, "Achat de glace", Op);
        var result = await svc.CountAsync(session.Id, 400, null, "Erreur de rendu", Op); // écart de −500 : alerte
        result!.AlertCreated.Should().BeTrue();
        var alert = (await alerts.ListAsync("open", null, null)).Single();
        await alerts.ResolveAsync(alert.Id, "Vérifié", Op);

        (await new FiscalChainVerificationService(db).VerifyAllChainsAsync()).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task CashForPeriod_ReadsTheLastCountOfTheWindow_AndIsNullOutside()
    {
        using var db = NewDb();
        var svc = Service(db);
        var session = await svc.OpenAsync("T01", Op, 5_000);
        await svc.CountAsync(session.Id, 5_200, null, null, Op);

        (await svc.CashForPeriodAsync("T01", DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1))).Should().Be(new CashZSummary(5_000, 5_200, 200));
        (await svc.CashForPeriodAsync("T01", DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow.AddHours(2))).Should().BeNull();
        (await svc.CashForPeriodAsync("T02", DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1))).Should().BeNull();
    }

    [Fact]
    public async Task CashForPeriod_AddsEveryCountOfThePeriod_AndTheMainTerminalCoversAllTerminals()
    {
        using var db = NewDb();
        var svc = Service(db);
        var noon = await svc.OpenAsync("T01", Op, 5_000);          // service de midi : écart −2,00 €
        await svc.CountAsync(noon.Id, 4_800, "Rendu erroné", null, Op);
        var evening = await svc.OpenAsync("T01", Op, 3_000);       // service du soir : écart +1,00 €
        await svc.CountAsync(evening.Id, 3_100, null, null, Op);
        var other = await svc.OpenAsync("T02", Op, 1_000);
        await svc.CountAsync(other.Id, 1_000, null, null, Op);
        var (from, to) = (DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1));

        (await svc.CashForPeriodAsync("T01", from, to)).Should().Be(new CashZSummary(8_000, 7_900, -100));
        (await svc.CashForPeriodAsync("POS_MAIN_TERM", from, to)).Should().Be(new CashZSummary(9_000, 8_900, -100));
    }
}
