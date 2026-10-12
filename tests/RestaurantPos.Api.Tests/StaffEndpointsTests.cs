using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using RestaurantPos.Application.DTOs;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class StaffEndpointsTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public StaffEndpointsTests(PosApiApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClientWithRole(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    [Fact]
    public async Task GetAllStaff_ReturnsStaffList()
    {
        var client = CreateClientWithRole("FloorManager");

        var response = await client.GetAsync("/api/staff");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var staffList = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        staffList.Should().NotBeNull();
        staffList.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetAllStaff_Anonymous_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/staff");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetOperators_ReturnsOperatorsList()
    {
        var client = CreateClientWithRole("FloorManager");

        var response = await client.GetAsync("/api/staff/operators");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var operators = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        operators.Should().NotBeNull();
        operators.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreateStaff_AsManager_Succeeds()
    {
        var managerClient = CreateClientWithRole("FloorManager");
        var pin = "71" + Random.Shared.Next(10, 99);
        var req = new CreateStaffRequest("Nouveau Serveur", "Waiter", pin);

        var response = await managerClient.PostAsJsonAsync("/api/staff", req);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        created.GetProperty("name").GetString().Should().Be("Nouveau Serveur");
        created.GetProperty("role").GetString().Should().Be("Waiter");
        created.GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task CreateStaff_AsWaiter_ReturnsForbidden()
    {
        var waiterClient = CreateClientWithRole("Waiter");
        var req = new CreateStaffRequest("Tentative", "Waiter", "8888");

        var response = await waiterClient.PostAsJsonAsync("/api/staff", req);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateStaff_AsManager_UpdatesDetailsAndResetsPin()
    {
        var managerClient = CreateClientWithRole("FloorManager");
        var pin = "61" + Random.Shared.Next(10, 99);
        var createReq = new CreateStaffRequest("A Modifier", "Waiter", pin);
        var createResp = await managerClient.PostAsJsonAsync("/api/staff", createReq);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();

        var newPin = "62" + Random.Shared.Next(10, 99);
        var updateReq = new UpdateStaffRequest("Modifié", "FloorManager", newPin, true);
        var updateResp = await managerClient.PutAsJsonAsync($"/api/staff/{id}", updateReq);

        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResp.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("name").GetString().Should().Be("Modifié");
        updated.GetProperty("role").GetString().Should().Be("FloorManager");
    }

    [Fact]
    public async Task UpdateStaff_AsWaiter_ReturnsForbidden()
    {
        var waiterClient = CreateClientWithRole("Waiter");
        var updateReq = new UpdateStaffRequest("Modifié", "Waiter", "1234", true);

        var response = await waiterClient.PutAsJsonAsync($"/api/staff/{Guid.NewGuid()}", updateReq);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeactivateStaff_AsAdmin_Succeeds()
    {
        var adminClient = CreateClientWithRole("Admin");
        var pin = "51" + Random.Shared.Next(10, 99);
        var createResp = await adminClient.PostAsJsonAsync("/api/staff", new CreateStaffRequest("A Désactiver", "Waiter", pin));
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();

        var deleteResp = await adminClient.DeleteAsync($"/api/staff/{id}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var deactivateResp = await adminClient.PostAsync($"/api/staff/{id}/deactivate", null);
        deactivateResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
