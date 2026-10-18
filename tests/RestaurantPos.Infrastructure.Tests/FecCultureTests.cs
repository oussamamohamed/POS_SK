using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public sealed class FecCultureTests
{
    private static async Task<string> GenerateAsync(string culture)
    {
        var previous = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(culture);
            var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("FecCulture_" + Guid.NewGuid().ToString("N")).Options;
            using var db = new AppDbContext(options);
            db.FiscalReceipts.Add(new FiscalReceipt
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                TerminalId = "T01",
                ReceiptNumber = "T01-000001",
                OrderId = Guid.Parse("00000000-0000-0000-ffff-000000000001"),
                SequenceNumber = 1,
                TotalTtcAmount = Money.FromCents(1055),
                TotalHtAmount = Money.FromCents(1000),
                TaxBreakdownJson = "{\"5.5\":55}",
                HtBreakdownJson = "{\"5.5\":1000}",
                CreatedAtUtc = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
            });
            await db.SaveChangesAsync();

            var r = await new FecExportService(db).GenerateFecAsync(new FecExportRequest(
                new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 30, 23, 59, 59, TimeSpan.Zero), "123456789"));
            return Encoding.UTF8.GetString(r.FileBytes);
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = previous;
        }
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public async Task Fec_IsIdenticalWhateverTheServerCulture(string other)
    {
        var us = await GenerateAsync("en-US");
        var o = await GenerateAsync(other);

        us.Should().Be(o);
        us.Should().Contain("(HT 5,5%)").And.Contain("TVA Collectee 5,5% T01-000001");
    }
}
