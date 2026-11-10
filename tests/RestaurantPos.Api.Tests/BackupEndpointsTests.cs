using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using RestaurantPos.Application.DTOs;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class BackupEndpointsTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;
    public BackupEndpointsTests(PosApiApplicationFactory factory) => _factory = factory;

    private async Task<HttpClient> ClientAsync(string pin)
    {
        var client = _factory.CreateClient();
        var login = await (await client.PostAsJsonAsync("/api/auth/login", new PinLoginRequest(pin))).Content.ReadFromJsonAsync<LoginResultDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return client;
    }

    [Fact]
    public async Task Status_WithoutPassphrase_ReportsNotConfigured_AndRunIsRefused()
    {
        var manager = await ClientAsync("1234");

        var status = await manager.GetFromJsonAsync<JsonElement>("/api/backup/status");
        status.GetProperty("configured").GetBoolean().Should().BeFalse();
        status.GetProperty("overdue").GetBoolean().Should().BeFalse("sans phrase, rien n'est dû");
        status.GetProperty("copies").GetInt32().Should().Be(0);

        var run = await manager.PostAsync("/api/backup/run", null);
        run.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await run.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("backup_not_configured");
    }

    [Fact]
    public async Task Waiter_IsForbidden()
    {
        var waiter = await ClientAsync("2468");

        (await waiter.GetAsync("/api/backup/status")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await waiter.PostAsync("/api/backup/run", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
