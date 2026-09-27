using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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
}
