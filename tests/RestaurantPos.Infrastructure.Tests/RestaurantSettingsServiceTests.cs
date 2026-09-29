using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class RestaurantSettingsServiceTests
{
    private static AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase($"PosTest_Settings_{Guid.NewGuid()}").Options);

    [Fact]
    public async Task Get_OnEmptyDatabase_InitializesEnglish()
    {
        using var db = NewDb();
        var dto = await new RestaurantSettingsService(db).GetAsync();
        dto.ReceiptLanguage.Should().Be("en");
        (await db.RestaurantSettings.SingleAsync()).ReceiptLanguage.Should().Be("en");
    }

    [Fact]
    public async Task Get_WithExistingOrders_InitializesFrench()
    {
        using var db = NewDb();
        db.Orders.Add(new Order());
        await db.SaveChangesAsync();
        (await new RestaurantSettingsService(db).GetAsync()).ReceiptLanguage.Should().Be("fr");
    }

    [Fact]
    public async Task Get_IsStable_AfterOrdersAppear()
    {
        using var db = NewDb();
        var service = new RestaurantSettingsService(db);
        await service.GetAsync();
        db.Orders.Add(new Order());
        await db.SaveChangesAsync();
        (await service.GetAsync()).ReceiptLanguage.Should().Be("en");
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("fr")]
    [InlineData("en")]
    public async Task Update_AcceptsSupportedLanguage(string lang)
    {
        using var db = NewDb();
        var service = new RestaurantSettingsService(db);
        (await service.UpdateAsync(new UpdateRestaurantSettingsRequest(lang)))!.ReceiptLanguage.Should().Be(lang);
        (await service.GetAsync()).ReceiptLanguage.Should().Be(lang);
    }

    [Theory]
    [InlineData("de")]
    [InlineData("")]
    [InlineData("FR")]
    [InlineData("fr-FR")]
    public async Task Update_RejectsOtherValues(string lang)
    {
        using var db = NewDb();
        (await new RestaurantSettingsService(db).UpdateAsync(new UpdateRestaurantSettingsRequest(lang))).Should().BeNull();
    }

    [Fact]
    public async Task Get_ConcurrentFirstCall_RecoversFromDuplicateKeyInsert()
    {
        // Simule deux terminaux appelant GetAsync en même temps sur une base fraîche : les deux
        // voient FindAsync == null puis tentent d'insérer la ligne singleton (Id = 1).
        //
        // Le provider InMemory ne lève pas DbUpdateException sur un conflit de clé primaire : il
        // laisse remonter un ArgumentException brut du Dictionary interne (vérifié empiriquement ;
        // voir le rapport de correction). Seuls les providers relationnels (SQLite en production)
        // enveloppent ce genre de conflit dans DbUpdateException, ce qui est le comportement que le
        // correctif cible. Pour exercer réellement le bloc catch (DbUpdateException) du service, on
        // utilise un intercepteur EF (test uniquement, aucun code de prod modifié) qui, juste avant
        // la persistance du premier terminal, insère la ligne gagnante via un second contexte partageant
        // la même base InMemory puis lève une vraie DbUpdateException pour simuler le conflit.
        var dbName = $"PosTest_Settings_Race_{Guid.NewGuid()}";
        var interceptor = new ThrowDbUpdateExceptionOnFirstSaveInterceptor(() =>
        {
            using var winner = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);
            winner.RestaurantSettings.Add(new RestaurantSettings { ReceiptLanguage = "ar", KitchenTicketLanguage = "ar" });
            winner.SaveChanges();
        });
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(interceptor)
            .Options);

        var dto = await new RestaurantSettingsService(db).GetAsync();

        dto.ReceiptLanguage.Should().Be("ar");
        (await db.RestaurantSettings.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Get_DbUpdateExceptionUnrelatedToSingletonConflict_Rethrows()
    {
        // Une DbUpdateException qui n'est pas un conflit sur la ligne singleton (ex. base verrouillée) :
        // après détachement, la relecture ne trouve toujours rien. Le service doit laisser remonter
        // l'exception d'origine plutôt que de la masquer derrière un InvalidOperationException générique.
        var interceptor = new ThrowDbUpdateExceptionOnFirstSaveInterceptor(() => { });
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"PosTest_Settings_Rethrow_{Guid.NewGuid()}")
            .AddInterceptors(interceptor)
            .Options);

        var act = () => new RestaurantSettingsService(db).GetAsync();

        await act.Should().ThrowAsync<DbUpdateException>()
            .WithMessage("Conflit de clé primaire simulé (course sur le premier appel).");
    }

    /// <summary>Simule, une seule fois, le conflit de clé primaire (DbUpdateException) qu'un provider
    /// relationnel lèverait réellement sur une course entre deux premiers appels.</summary>
    private sealed class ThrowDbUpdateExceptionOnFirstSaveInterceptor : SaveChangesInterceptor
    {
        private readonly Action _beforeThrow;
        private bool _thrown;

        public ThrowDbUpdateExceptionOnFirstSaveInterceptor(Action beforeThrow) => _beforeThrow = beforeThrow;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            if (_thrown) return result;
            _thrown = true;
            _beforeThrow();
            throw new DbUpdateException("Conflit de clé primaire simulé (course sur le premier appel).");
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (_thrown) return ValueTask.FromResult(result);
            _thrown = true;
            _beforeThrow();
            throw new DbUpdateException("Conflit de clé primaire simulé (course sur le premier appel).");
        }
    }
}
