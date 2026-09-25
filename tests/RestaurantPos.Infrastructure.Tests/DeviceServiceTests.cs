using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class DeviceServiceTests
{
    private sealed class MutableTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static DbContextOptions<AppDbContext> NewOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"PosTest_Devices_{Guid.NewGuid()}")
            .Options;

    private static (DeviceService Service, AppDbContext Db, MutableTimeProvider Clock) Create()
    {
        var db = new AppDbContext(NewOptions());
        var clock = new MutableTimeProvider();
        return (new DeviceService(db, clock), db, clock);
    }

    [Fact]
    public async Task CreatePairingCode_ReturnsEightCharCode_AndStoresOnlyItsHash()
    {
        var (service, db, clock) = Create();

        var result = await service.CreatePairingCodeAsync("Caisse comptoir", DeviceRole.Caisse, Guid.NewGuid());

        result.Code.Should().HaveLength(DeviceService.CodeLength);
        result.Code.Should().MatchRegex("^[ABCDEFGHJKMNPQRSTUVWXYZ23456789]{8}$");
        result.ExpiresAtUtc.Should().Be(clock.Now + DeviceService.CodeLifetime);
        var stored = await db.DevicePairingCodes.SingleAsync();
        stored.CodeHash.Should().Be(DeviceService.Sha256Hex(result.Code));
        stored.CodeHash.Should().NotContain(result.Code);
    }

    [Fact]
    public async Task Pair_WithValidCode_AssignsSequentialTerminalIds_AndStoresOnlyTokenHash()
    {
        var (service, db, _) = Create();
        var first = await service.CreatePairingCodeAsync("Caisse comptoir", DeviceRole.Caisse, Guid.NewGuid());
        var second = await service.CreatePairingCodeAsync("iPad terrasse", DeviceRole.Serveur, Guid.NewGuid());

        var d1 = await service.PairAsync(first.Code);
        var d2 = await service.PairAsync(second.Code);

        d1!.TerminalId.Should().Be("T01");
        d2!.TerminalId.Should().Be("T02");
        d2.Role.Should().Be(DeviceRole.Serveur);
        d1.Token.Should().NotBe(d2.Token);
        var stored = await db.Devices.SingleAsync(d => d.Id == d1.DeviceId);
        stored.TokenHash.Should().Be(DeviceService.Sha256Hex(d1.Token));
        stored.Name.Should().Be("Caisse comptoir");
    }

    [Fact]
    public async Task Pair_IsCaseAndWhitespaceInsensitive()
    {
        var (service, _, _) = Create();
        var created = await service.CreatePairingCodeAsync("Caisse", DeviceRole.Caisse, Guid.NewGuid());
        var typed = "  " + string.Concat(created.Code.Select(char.ToLowerInvariant)) + " ";

        var paired = await service.PairAsync(typed);

        paired.Should().NotBeNull();
    }

    [Fact]
    public async Task Pair_WithUsedCode_ReturnsNull()
    {
        var (service, _, _) = Create();
        var created = await service.CreatePairingCodeAsync("Caisse", DeviceRole.Caisse, Guid.NewGuid());
        (await service.PairAsync(created.Code)).Should().NotBeNull();

        (await service.PairAsync(created.Code)).Should().BeNull();
    }

    [Fact]
    public async Task Pair_WithExpiredCode_ReturnsNull()
    {
        var (service, _, clock) = Create();
        var created = await service.CreatePairingCodeAsync("Caisse", DeviceRole.Caisse, Guid.NewGuid());
        clock.Now += DeviceService.CodeLifetime + TimeSpan.FromSeconds(1);

        (await service.PairAsync(created.Code)).Should().BeNull();
    }

    [Fact]
    public async Task Pair_WithUnknownCode_ReturnsNull()
    {
        var (service, _, _) = Create();
        (await service.PairAsync("ZZZZZZZZ")).Should().BeNull();
        (await service.PairAsync("")).Should().BeNull();
    }

    [Fact]
    public async Task Pair_SameCodeInParallel_OnlyOneSucceeds()
    {
        var options = NewOptions();
        var clock = new MutableTimeProvider();
        await using var setupDb = new AppDbContext(options);
        var created = await new DeviceService(setupDb, clock).CreatePairingCodeAsync("Caisse", DeviceRole.Caisse, Guid.NewGuid());

        await using var dbA = new AppDbContext(options);
        await using var dbB = new AppDbContext(options);
        var results = await Task.WhenAll(
            new DeviceService(dbA, clock).PairAsync(created.Code),
            new DeviceService(dbB, clock).PairAsync(created.Code));

        results.Count(r => r is not null).Should().Be(1);
    }

    [Fact]
    public async Task Authenticate_WithValidToken_ReturnsDevice_AndUpdatesLastSeen()
    {
        var (service, db, clock) = Create();
        var created = await service.CreatePairingCodeAsync("Caisse", DeviceRole.Caisse, Guid.NewGuid());
        var paired = await service.PairAsync(created.Code);
        clock.Now += TimeSpan.FromMinutes(5);

        var device = await service.AuthenticateAsync(paired!.Token);

        device!.TerminalId.Should().Be("T01");
        (await db.Devices.SingleAsync()).LastSeenUtc.Should().Be(clock.Now);
    }

    [Fact]
    public async Task Authenticate_WithMissingUnknownOrRevokedToken_ReturnsNull()
    {
        var (service, _, _) = Create();
        var created = await service.CreatePairingCodeAsync("Caisse", DeviceRole.Caisse, Guid.NewGuid());
        var paired = await service.PairAsync(created.Code);

        (await service.AuthenticateAsync(null)).Should().BeNull();
        (await service.AuthenticateAsync("")).Should().BeNull();
        (await service.AuthenticateAsync("not-a-token")).Should().BeNull();

        (await service.RevokeAsync(paired!.DeviceId)).Should().BeTrue();
        (await service.AuthenticateAsync(paired.Token)).Should().BeNull();
    }

    [Fact]
    public async Task Revoke_UnknownDevice_ReturnsFalse_AndRevokedDevicesStayListed()
    {
        var (service, _, _) = Create();
        (await service.RevokeAsync(Guid.NewGuid())).Should().BeFalse();

        var created = await service.CreatePairingCodeAsync("Caisse", DeviceRole.Caisse, Guid.NewGuid());
        var paired = await service.PairAsync(created.Code);
        await service.RevokeAsync(paired!.DeviceId);

        var list = await service.ListAsync();
        list.Should().ContainSingle().Which.RevokedAtUtc.Should().NotBeNull();
    }
}
