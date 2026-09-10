using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class StaffManagementServiceTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"PosTest_Staff_{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task CreateStaffMember_ShouldHashPin_AndPreventDuplicatePin()
    {
        using var context = CreateInMemoryDbContext();
        var service = new StaffManagementService(context);

        var staff1 = await service.CreateStaffMemberAsync("Alexandre", UserRole.FloorManager, "1234");
        staff1.Should().NotBeNull();
        staff1.Name.Should().Be("Alexandre");
        staff1.PinHash.Should().NotBe("1234");
        staff1.PinSalt.Should().NotBeEmpty();

        var act = async () => await service.CreateStaffMemberAsync("Sophie", UserRole.Waiter, "1234");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*déjà utilisé*");
    }

    [Fact]
    public async Task ResetStaffPin_WithValidPin_ShouldUpdateHash()
    {
        using var context = CreateInMemoryDbContext();
        var service = new StaffManagementService(context);

        var staff = await service.CreateStaffMemberAsync("Julien", UserRole.Waiter, "5678");
        string originalHash = staff.PinHash;

        var resetOk = await service.ResetStaffPinAsync(staff.Id, "9999");
        resetOk.Should().BeTrue();

        var updated = await service.GetStaffByIdAsync(staff.Id);
        updated!.PinHash.Should().NotBe(originalHash);
    }

    [Fact]
    public async Task UpdateStaffMember_ShouldUpdateNameAndRole()
    {
        using var context = CreateInMemoryDbContext();
        var service = new StaffManagementService(context);

        var staff = await service.CreateStaffMemberAsync("Alexandre", UserRole.Waiter, "1234");
        var updated = await service.UpdateStaffMemberAsync(staff.Id, "Alexandre Dupont", UserRole.FloorManager, true);

        updated.Name.Should().Be("Alexandre Dupont");
        updated.Role.Should().Be(UserRole.FloorManager);
        updated.IsActive.Should().BeTrue();
    }
}
