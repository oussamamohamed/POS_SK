using System;
using System.Threading.Tasks;
using FluentAssertions;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class PickupNumberingTests
{
    [Fact]
    public async Task TakeawayCounterService_GeneratesMonotonicSequencesWithTerminalPrefix()
    {
        var service = new TakeawayCounterService();
        await service.ResetDailySequencesAsync();

        // Terminal 1: letter 'A'
        var numA1 = await service.GetNextPickupNumberAsync("POS_TERM_A");
        var numA2 = await service.GetNextPickupNumberAsync("POS_TERM_A");
        var numA3 = await service.GetNextPickupNumberAsync("POS_TERM_A");

        numA1.Should().Be("#A-01");
        numA2.Should().Be("#A-02");
        numA3.Should().Be("#A-03");

        // Terminal 2: letter 'B'
        var numB1 = await service.GetNextPickupNumberAsync("POS_TERM_B");
        numB1.Should().Be("#B-01");
    }

    [Fact]
    public async Task TakeawayCounterService_RolloversAfter99()
    {
        var service = new TakeawayCounterService();
        await service.ResetDailySequencesAsync();

        string lastNum = "";
        for (int i = 0; i < 99; i++)
        {
            lastNum = await service.GetNextPickupNumberAsync("POS_ROLLOVER");
        }

        lastNum.Should().Be("#A-99");

        // 100th call should rollover to #A-01
        var rolloverNum = await service.GetNextPickupNumberAsync("POS_ROLLOVER");
        rolloverNum.Should().Be("#A-01");
    }
}
