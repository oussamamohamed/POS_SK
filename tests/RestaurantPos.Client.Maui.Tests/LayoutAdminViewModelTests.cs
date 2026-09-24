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

public class LayoutAdminViewModelTests
{
    private readonly Mock<ITerminalLayoutService> _layoutServiceMock = new();
    private readonly Mock<IPlatformEnvironmentService> _environmentMock = new();

    [Fact]
    public async Task SaveProfile_ShouldCallService_AndTriggerHaptic()
    {
        var saved = new TerminalLayoutProfile
        {
            Id = UuidV7.NewGuid(),
            ProfileName = "Bar Layout",
            GridColumnCount = 5,
            IsDefault = true
        };

        _layoutServiceMock
            .Setup(s => s.SaveProfileAsync(It.IsAny<TerminalLayoutProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(saved);

        _layoutServiceMock
            .Setup(s => s.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([saved]);

        var vm = new LayoutAdminViewModel(_layoutServiceMock.Object, _environmentMock.Object)
        {
            ProfileName = "Bar Layout",
            GridColumnCount = 5,
            IsDefault = true
        };

        await vm.SaveCurrentProfileAsync();

        vm.StatusMessage.Should().Contain("Bar Layout");
        _environmentMock.Verify(e => e.TriggerHapticFeedback(HapticFeedbackType.Success), Times.Once);
    }
}
