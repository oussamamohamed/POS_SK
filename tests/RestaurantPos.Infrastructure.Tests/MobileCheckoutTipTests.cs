using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class MobileCheckoutTipTests
{
    private static AppDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(System.Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public void CalculateEqualSplitPartitions_DistributesCentsEvenlyWithoutDiscrepancy()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var auditMock = new NF525FiscalAuditService(db);
        var service = new CheckoutPaymentService(db, auditMock);

        // 100.00 EUR (10,000 cents) divided by 3 guests
        long totalCents = 10000;
        int guests = 3;

        // Act
        var partitions = service.CalculateEqualSplitPartitions(totalCents, guests);

        // Assert
        partitions.Should().HaveCount(3);
        partitions.Sum().Should().Be(10000);
        partitions[0].Should().Be(3334); // 1 extra cent on first share
        partitions[1].Should().Be(3333);
        partitions[2].Should().Be(3333);
    }

    [Fact]
    public void CalculateEqualSplitPartitions_EvenDivision_ReturnsEqualShares()
    {
        using var db = CreateInMemoryDb();
        var auditMock = new NF525FiscalAuditService(db);
        var service = new CheckoutPaymentService(db, auditMock);

        long totalCents = 8000; // 80.00 EUR
        int guests = 4;

        var partitions = service.CalculateEqualSplitPartitions(totalCents, guests);

        partitions.Should().HaveCount(4);
        partitions.Should().AllBeEquivalentTo(2000);
        partitions.Sum().Should().Be(8000);
    }
}
