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

public class StaffAdminViewModelTests
{
    private readonly Mock<IStaffManagementService> _staffServiceMock = new();
    private readonly Mock<IPlatformEnvironmentService> _environmentMock = new();

    [Fact]
    public async Task CreateStaff_WithValidInputs_ShouldAddUser_AndTriggerHaptic()
    {
        var createdUser = new User
        {
            Id = UuidV7.NewGuid(),
            Name = "Lucas",
            Role = UserRole.Waiter,
            PinHash = "hash123",
            PinSalt = "salt123"
        };

        _staffServiceMock
            .Setup(s => s.CreateStaffMemberAsync("Lucas", UserRole.Waiter, "2468", It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdUser);

        var vm = new StaffAdminViewModel(_staffServiceMock.Object, _environmentMock.Object)
        {
            NewStaffName = "Lucas",
            NewStaffRole = UserRole.Waiter,
            NewStaffPin = "2468"
        };

        await vm.CreateStaffAsync();

        vm.StaffMembers.Should().Contain(createdUser);
        vm.ErrorMessage.Should().BeEmpty();
        vm.StatusMessage.Should().Contain("Lucas");
        _environmentMock.Verify(e => e.TriggerHapticFeedback(HapticFeedbackType.Success), Times.Once);
    }

    [Fact]
    public async Task CreateStaff_WithShortPin_ShouldSetErrorMessage()
    {
        var vm = new StaffAdminViewModel(_staffServiceMock.Object, _environmentMock.Object)
        {
            NewStaffName = "Lucas",
            NewStaffPin = "12" // too short
        };

        await vm.CreateStaffAsync();

        vm.ErrorMessage.Should().Contain("4 à 6 chiffres");
        _environmentMock.Verify(e => e.TriggerHapticFeedback(HapticFeedbackType.Error), Times.Once);
    }
}
