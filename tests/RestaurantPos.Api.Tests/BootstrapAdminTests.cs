using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Security;
using RestaurantPos.Infrastructure.Services;

namespace RestaurantPos.Api.Tests;

public class BootstrapAdminTests
{
    private static readonly string[] DemoPins = ["0000", "1234", "2468", "5678", "9999"];

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("Bootstrap_" + Guid.NewGuid()).Options);

    private static IConfiguration Config(string? pin) =>
        new ConfigurationBuilder().AddInMemoryCollection(pin is null
            ? []
            : new Dictionary<string, string?> { ["Bootstrap:AdminPin"] = pin }).Build();

    private sealed class CaptureLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    [Fact]
    public async Task Empty_db_creates_single_admin_with_configured_pin()
    {
        await using var db = NewDb();
        await Program.BootstrapAdminAsync(db, new StaffManagementService(db), Config("4829"), new CaptureLogger());

        var auth = new OperatorAuthenticationService(db);
        (await auth.AuthenticatePinAsync("4829")).IsSuccess.Should().BeTrue();
        foreach (var known in new[] { "1234", "2468", "9999" })
        {
            (await auth.AuthenticatePinAsync(known)).IsSuccess.Should().BeFalse();
        }

        var users = await db.Users.ToListAsync();
        users.Should().ContainSingle().Which.Role.Should().Be(UserRole.Admin);
    }

    // 4 chiffres : les claviers web et iPad envoient le PIN au 4e chiffre.
    [Fact]
    public async Task Missing_pin_generates_one()
    {
        await using var db = NewDb();
        var log = new CaptureLogger();
        await Program.BootstrapAdminAsync(db, new StaffManagementService(db), Config(null), log);

        var warnings = log.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        warnings.Should().ContainSingle();
        var match = System.Text.RegularExpressions.Regex.Match(warnings[0].Message, @"\b\d{4}\b");
        match.Success.Should().BeTrue();
        DemoPins.Should().NotContain(match.Value);
        (await new OperatorAuthenticationService(db).AuthenticatePinAsync(match.Value)).IsSuccess.Should().BeTrue();
        (await db.Users.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData("482915")]
    [InlineData("123")]
    [InlineData("12ab")]
    public async Task Invalid_configured_pin_fails_startup(string pin)
    {
        await using var db = NewDb();
        var act = () => Program.BootstrapAdminAsync(db, new StaffManagementService(db), Config(pin), new CaptureLogger());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Bootstrap:AdminPin*");
        (await db.Users.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Non_empty_db_is_untouched()
    {
        await using var db = NewDb();
        var staff = new StaffManagementService(db);
        await staff.CreateStaffMemberAsync("Existant", UserRole.Admin, "1357");
        var log = new CaptureLogger();

        await Program.BootstrapAdminAsync(db, staff, Config("4829"), log);

        (await db.Users.CountAsync()).Should().Be(1);
        log.Entries.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    [InlineData("Testing", false)]
    public void Seed_cli_is_allowed_only_in_development(string env, bool expected)
    {
        var host = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = env };
        Program.IsSeedCliAllowed(host).Should().Be(expected);
    }
}
