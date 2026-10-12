using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class HappyHourEndpointsTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public HappyHourEndpointsTests(PosApiApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetStatus_ReturnsStatusDto()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/happy-hour/status?terminalId=T01");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = await response.Content.ReadFromJsonAsync<HappyHourStatusDto>();
        status.Should().NotBeNull();
    }

    [Fact]
    public async Task GetPricingTable_ReturnsPricingTableDto()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/happy-hour/pricing-table?terminalId=T01");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var table = await response.Content.ReadFromJsonAsync<HappyHourPricingTableDto>();
        table.Should().NotBeNull();
    }

    [Fact]
    public async Task GetSchedules_ReturnsList()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/happy-hour/schedules");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var schedules = await response.Content.ReadFromJsonAsync<List<HappyHourScheduleDto>>();
        schedules.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateSchedule_WithValidTimes_CreatesSuccessfully()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "FloorManager");
        var scheduleDto = new HappyHourScheduleDto(
            Guid.Empty,
            "Afterwork Spécial",
            [DayOfWeek.Thursday, DayOfWeek.Friday],
            "17:00",
            "19:00",
            true,
            false,
            1,
            []
        );

        var response = await client.PostAsJsonAsync("/api/happy-hour/schedules", scheduleDto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();
        id.Should().NotBeEmpty();

        // Cleanup
        var delResp = await client.DeleteAsync($"/api/happy-hour/schedules/{id}");
        delResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateSchedule_Anonymous_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var scheduleDto = new HappyHourScheduleDto(
            Guid.Empty,
            "Anonyme",
            [DayOfWeek.Monday],
            "17:00",
            "19:00",
            true,
            false,
            1,
            []
        );

        var response = await client.PostAsJsonAsync("/api/happy-hour/schedules", scheduleDto);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateSchedule_WithInvalidTimeFormat_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "FloorManager");
        var scheduleDto = new HappyHourScheduleDto(
            Guid.Empty,
            "Format Invalide",
            [DayOfWeek.Monday],
            "25:00",
            "19:00",
            true,
            false,
            1,
            []
        );

        var response = await client.PostAsJsonAsync("/api/happy-hour/schedules", scheduleDto);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteSchedule_NonExistent_ReturnsNotFound()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "FloorManager");

        var response = await client.DeleteAsync($"/api/happy-hour/schedules/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Override_ActivateAndStop_WithValidAndInvalidPins()
    {
        var client = _factory.CreateClient();

        // 1. Invalid PIN -> 403
        var invalidReq = new ActivateOverrideRequest("T01", "0000", 60, "Raison test");
        var invalidResp = await client.PostAsJsonAsync("/api/happy-hour/override/activate", invalidReq);
        invalidResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 2. Valid Manager PIN (1234 seeded) -> 200
        var validReq = new ActivateOverrideRequest("T01", "1234", 30, "Raison test");
        var validResp = await client.PostAsJsonAsync("/api/happy-hour/override/activate", validReq);
        validResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Stop override with valid manager PIN -> 200
        var stopReq = new StopOverrideRequest("T01", "1234", "Fin test");
        var stopResp = await client.PostAsJsonAsync("/api/happy-hour/override/stop", stopReq);
        stopResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
