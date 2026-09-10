using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Client.Maui.ViewModels;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;
using Xunit;

namespace RestaurantPos.Client.Maui.Tests;

public class PrinterAdminViewModelTests
{
    private readonly Mock<IPrinterConfigurationService> _printerServiceMock = new();
    private readonly Mock<ICrossPlatformDiscoveryService> _discoveryServiceMock = new();
    private readonly Mock<IPlatformEnvironmentService> _environmentMock = new();

    [Fact]
    public async Task RegisterPrinter_WithValidInput_ShouldAddPrinter()
    {
        var created = new PrinterConfiguration
        {
            Id = UuidV7.NewGuid(),
            Name = "Bar Printer",
            IpAddress = "192.168.1.180"
        };

        _printerServiceMock
            .Setup(s => s.RegisterPrinterAsync(It.IsAny<PrinterRegistrationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);

        var vm = new PrinterAdminViewModel(
            _printerServiceMock.Object,
            _discoveryServiceMock.Object,
            _environmentMock.Object)
        {
            NewPrinterName = "Bar Printer",
            NewPrinterIp = "192.168.1.180"
        };

        await vm.RegisterPrinterAsync();

        vm.Printers.Should().Contain(created);
        vm.ErrorMessage.Should().BeEmpty();
        _environmentMock.Verify(e => e.TriggerHapticFeedback(HapticFeedbackType.Success), Times.Once);
    }
}
