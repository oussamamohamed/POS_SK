using System;
using System.Linq;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class SplitBillCalculationTests
{
    [Theory]
    [InlineData(10000, 3, new long[] { 3334, 3333, 3333 })] // 100.00 EUR / 3
    [InlineData(1000, 3, new long[] { 334, 333, 333 })]      // 10.00 EUR / 3
    [InlineData(5000, 4, new long[] { 1250, 1250, 1250, 1250 })] // 50.00 EUR / 4
    [InlineData(777, 5, new long[] { 156, 156, 155, 155, 155 })] // 7.77 EUR / 5
    public void CalculateEqualSplitPartitionsDistributesRemainderCentsZeroLoss(
        long totalCents,
        int guests,
        long[] expectedParts)
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "SplitDb_" + Guid.NewGuid().ToString("N"))
            .Options;

        using var dbContext = new AppDbContext(options);
        var fiscalService = new NF525FiscalAuditService(dbContext);
        var checkoutService = new CheckoutPaymentService(dbContext, fiscalService);

        // Act
        var parts = checkoutService.CalculateEqualSplitPartitions(totalCents, guests);

        // Assert
        parts.Should().Equal(expectedParts);
        parts.Sum().Should().Be(totalCents);
    }
}
