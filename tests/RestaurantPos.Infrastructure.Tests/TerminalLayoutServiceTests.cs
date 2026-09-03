using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class TerminalLayoutServiceTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"PosTest_Layout_{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task SaveProfile_ShouldUpdateDefault_AndResetOthers()
    {
        using var context = CreateInMemoryDbContext();
        var service = new TerminalLayoutService(context);

        var p1 = new TerminalLayoutProfile
        {
            Id = UuidV7.NewGuid(),
            ProfileName = "Profile 1",
            IsDefault = true
        };
        await service.SaveProfileAsync(p1);

        var p2 = new TerminalLayoutProfile
        {
            Id = UuidV7.NewGuid(),
            ProfileName = "Profile 2",
            IsDefault = true
        };
        await service.SaveProfileAsync(p2);

        var active = await service.GetActiveProfileAsync("TERM_1");
        active.ProfileName.Should().Be("Profile 2");
    }
}
