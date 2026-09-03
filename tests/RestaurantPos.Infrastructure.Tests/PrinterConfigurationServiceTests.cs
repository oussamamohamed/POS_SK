using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class PrinterConfigurationServiceTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"PosTest_Printer_{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task RegisterPrinter_ShouldPersist_AndRetrieve()
    {
        using var context = CreateInMemoryDbContext();
        var service = new PrinterConfigurationService(context);

        var request = new PrinterRegistrationRequest(
            Name: "Imprimante Cuisine Chaud",
            IpAddress: "192.168.1.150",
            Port: 9100,
            PaperWidthMm: 80,
            OpenCashDrawerOnReceipt: false,
            AssignedStationIds: ["HOT_KITCHEN", "GRILL"]
        );

        var printer = await service.RegisterPrinterAsync(request);
        printer.Should().NotBeNull();
        printer.Name.Should().Be("Imprimante Cuisine Chaud");
        printer.AssignedStationIds.Should().Contain("HOT_KITCHEN");

        var all = await service.GetAllPrintersAsync();
        all.Should().ContainSingle(p => p.Id == printer.Id);
    }

    [Fact]
    public async Task SendTestPrint_ToUnreachableIp_ShouldReturnFailedTestResult()
    {
        using var context = CreateInMemoryDbContext();
        var service = new PrinterConfigurationService(context);

        var request = new PrinterRegistrationRequest("Test Unreachable", "127.0.0.1", 59999, 80, true, []);
        var printer = await service.RegisterPrinterAsync(request);

        var testResult = await service.SendTestPrintAsync(printer.Id);
        testResult.Should().NotBeNull();
        testResult.Success.Should().BeFalse();
        testResult.Message.Should().Contain("Échec");
    }

    [Fact]
    public async Task UpdatePrinter_ShouldModifySettingsCorrectly()
    {
        using var context = CreateInMemoryDbContext();
        var service = new PrinterConfigurationService(context);

        var reg = new PrinterRegistrationRequest("Old Name", "192.168.1.100", 9100, 80, false, ["BAR"]);
        var printer = await service.RegisterPrinterAsync(reg);

        var updateReq = new PrinterRegistrationRequest("New Name", "192.168.1.200", 9100, 58, true, ["BAR", "DESSERT"]);
        var updated = await service.UpdatePrinterAsync(printer.Id, updateReq, true);

        updated.Name.Should().Be("New Name");
        updated.IpAddress.Should().Be("192.168.1.200");
        updated.PaperWidthMm.Should().Be(58);
        updated.OpenCashDrawerOnReceipt.Should().BeTrue();
        updated.AssignedStationIds.Should().Contain("DESSERT");
    }
}
