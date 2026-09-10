using FluentAssertions;
using RestaurantPos.Domain.Entities;
using Xunit;

namespace RestaurantPos.Domain.Tests;

public class UserRoleAuthorizationTests
{
    [Theory]
    [InlineData(UserRole.Waiter, false, false, false)]
    [InlineData(UserRole.Cashier, false, false, false)]
    [InlineData(UserRole.KitchenStaff, false, false, false)]
    [InlineData(UserRole.FloorManager, true, true, false)]
    [InlineData(UserRole.Admin, true, true, true)]
    public void RolePermissionsEnforceAppropriateAuthority(
        UserRole role,
        bool canVoidItems,
        bool canPrintZReports,
        bool canAccessBackOffice)
    {
        // Arrange
        var user = new User
        {
            Name = "Test User",
            Role = role,
            PinHash = "dummy_hash",
            PinSalt = "dummy_salt"
        };

        // Assert
        user.CanVoidItems().Should().Be(canVoidItems);
        user.CanPrintZReports().Should().Be(canPrintZReports);
        user.CanAccessBackOffice().Should().Be(canAccessBackOffice);
    }
}
