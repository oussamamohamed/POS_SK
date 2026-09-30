using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Printing;
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

    private sealed class FakeTransport : IPrinterTransport
    {
        public Exception? Failure { get; set; }
        public List<(PrinterConfiguration Printer, TicketDocument Document, bool Drawer)> Sent { get; } = [];
        public Task SendAsync(PrinterConfiguration printer, TicketDocument document, bool openCashDrawer, CancellationToken ct)
        {
            if (Failure is not null) throw Failure;
            Sent.Add((printer, document, openCashDrawer));
            return Task.CompletedTask;
        }
    }

    private static (AppDbContext Db, FakeTransport Transport, PrinterConfigurationService Service) Create()
    {
        var db = CreateInMemoryDbContext();
        var transport = new FakeTransport();
        return (db, transport, new PrinterConfigurationService(db, transport, new RestaurantSettingsService(db)));
    }

    [Fact]
    public async Task SendTestPrint_RendersTestPage_InReceiptLanguage()
    {
        var (db, transport, service) = Create();
        using var _ = db;
        await new RestaurantSettingsService(db).UpdateAsync(new UpdateRestaurantSettingsRequest("ar"));
        var printer = await service.RegisterPrinterAsync(new PrinterRegistrationRequest("Caisse", "10.0.0.3", 9100, 80, true, ["RECEIPT"]));

        var result = await service.SendTestPrintAsync(printer.Id);

        result.Success.Should().BeTrue();
        transport.Sent.Single().Document.Language.Should().Be("ar");
        transport.Sent.Single().Drawer.Should().BeTrue();
    }

    [Fact]
    public async Task SendTestPrint_TransportFailure_ReturnsFailure()
    {
        var (db, transport, service) = Create();
        using var _ = db;
        var printer = await service.RegisterPrinterAsync(new PrinterRegistrationRequest("Caisse", "10.0.0.3", 9100, 80, false, ["RECEIPT"]));
        transport.Failure = new SocketException((int)SocketError.ConnectionRefused);

        (await service.SendTestPrintAsync(printer.Id)).Success.Should().BeFalse();
    }

    [Fact]
    public async Task RegisterPrinter_ShouldPersist_AndRetrieve()
    {
        using var context = CreateInMemoryDbContext();
        var service = new PrinterConfigurationService(context, new EscPosPrinterTransport(), new RestaurantSettingsService(context));

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
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
        using var context = CreateInMemoryDbContext();
        var service = new PrinterConfigurationService(context, new EscPosPrinterTransport(), new RestaurantSettingsService(context));

        var request = new PrinterRegistrationRequest("Test Unreachable", "127.0.0.1", 59999, 80, true, []);
        var printer = await service.RegisterPrinterAsync(request);

        var testResult = await service.SendTestPrintAsync(printer.Id);
        testResult.Should().NotBeNull();
        testResult.Success.Should().BeFalse();
        testResult.Message.Should().Contain("Printer connection failed");
    }

    [Fact]
    public async Task UpdatePrinter_ShouldModifySettingsCorrectly()
    {
        using var context = CreateInMemoryDbContext();
        var service = new PrinterConfigurationService(context, new EscPosPrinterTransport(), new RestaurantSettingsService(context));

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

    [Fact]
    public async Task TextMode_DefaultsFalse_SetOnRegister_KeptWhenNullOnUpdate()
    {
        var (_, _, service) = Create();
        var plain = await service.RegisterPrinterAsync(new PrinterRegistrationRequest("Caisse", "10.0.0.3", 9100, 80, false, ["RECEIPT"]));
        plain.TextMode.Should().BeFalse();

        var text = await service.RegisterPrinterAsync(new PrinterRegistrationRequest("Cuisine", "10.0.0.4", 9100, 58, false, ["HOT_KITCHEN"], TextMode: true));
        text.TextMode.Should().BeTrue();

        var updated = await service.UpdatePrinterAsync(text.Id, new PrinterRegistrationRequest("Cuisine 2", "10.0.0.4", 9100, 58, false, ["HOT_KITCHEN"]), isActive: true);
        updated.TextMode.Should().BeTrue();

        updated = await service.UpdatePrinterAsync(text.Id, new PrinterRegistrationRequest("Cuisine 2", "10.0.0.4", 9100, 58, false, ["HOT_KITCHEN"], TextMode: false), isActive: true);
        updated.TextMode.Should().BeFalse();
    }
}
