using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using RestaurantPos.Application.DTOs;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class StaffRolesTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public StaffRolesTests(PosApiApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientWithRole(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private static string NewPin() => "63" + Random.Shared.Next(10, 99);

    private static async Task<Guid> CreateAsync(HttpClient client, string role)
    {
        var resp = await client.PostAsJsonAsync("/api/staff", new CreateStaffRequest("Membre " + role, role, NewPin()));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Manager_cannot_create_admin()
    {
        var response = await ClientWithRole("FloorManager")
            .PostAsJsonAsync("/api/staff", new CreateStaffRequest("Pirate", "Admin", NewPin()));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_can_create_admin()
    {
        var response = await ClientWithRole("Admin")
            .PostAsJsonAsync("/api/staff", new CreateStaffRequest("Second Admin", "Admin", NewPin()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Manager_cannot_promote_to_admin()
    {
        var id = await CreateAsync(ClientWithRole("FloorManager"), "Waiter");

        var response = await ClientWithRole("FloorManager")
            .PutAsJsonAsync($"/api/staff/{id}", new UpdateStaffRequest("Promu", "Admin", null, true));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Manager_cannot_edit_existing_admin()
    {
        var id = await CreateAsync(ClientWithRole("Admin"), "Admin");

        var response = await ClientWithRole("FloorManager")
            .PutAsJsonAsync($"/api/staff/{id}", new UpdateStaffRequest("Rétrogradé", "Waiter", null, true));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Unknown_role_is_rejected()
    {
        var client = ClientWithRole("FloorManager");

        var create = await client.PostAsJsonAsync("/api/staff", new CreateStaffRequest("Root", "Root", NewPin()));
        create.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var id = await CreateAsync(client, "Waiter");
        var update = await client.PutAsJsonAsync($"/api/staff/{id}", new UpdateStaffRequest("Root", "Root", null, true));
        update.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Seed_test_data_is_admin_only()
    {
        (await ClientWithRole("FloorManager").PostAsync("/api/admin/seed-test-data", null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await ClientWithRole("Admin").PostAsync("/api/admin/seed-test-data", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
