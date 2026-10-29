using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
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

    // PIN à 6 chiffres, distincts : jamais ceux semés (4 chiffres) ni ceux des autres classes.
    private static int _pinCounter = 900000;

    private static string NewPin() => Interlocked.Increment(ref _pinCounter).ToString(System.Globalization.CultureInfo.InvariantCulture);

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
    public async Task Admin_can_edit_admin()
    {
        var admin = ClientWithRole("Admin");
        var id = await CreateAsync(admin, "Admin");

        var response = await admin.PutAsJsonAsync($"/api/staff/{id}", new UpdateStaffRequest("Renommé", "Admin", null, true));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Admin_can_promote_to_admin()
    {
        var admin = ClientWithRole("Admin");
        var id = await CreateAsync(admin, "Waiter");

        var response = await admin.PutAsJsonAsync($"/api/staff/{id}", new UpdateStaffRequest("Promu", "Admin", null, true));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Admin_can_reset_admin_pin()
    {
        var admin = ClientWithRole("Admin");
        var id = await CreateAsync(admin, "Admin");

        var response = await admin.PutAsJsonAsync($"/api/staff/{id}", new UpdateStaffRequest("Admin", "Admin", NewPin(), true));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("Root")]
    [InlineData("99")]
    [InlineData("")]
    public async Task Unknown_role_is_rejected(string badRole)
    {
        var client = ClientWithRole("FloorManager");

        var create = await client.PostAsJsonAsync("/api/staff", new CreateStaffRequest("Root", badRole, NewPin()));
        create.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var id = await CreateAsync(client, "Waiter");
        var update = await client.PutAsJsonAsync($"/api/staff/{id}", new UpdateStaffRequest("Root", badRole, null, true));
        update.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Manager_cannot_create_admin_with_lowercase_role()
    {
        var response = await ClientWithRole("FloorManager")
            .PostAsJsonAsync("/api/staff", new CreateStaffRequest("Pirate", "admin", NewPin()));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static Task<HttpResponseMessage> Deactivate(HttpClient client, int route, Guid id) => route switch
    {
        0 => client.DeleteAsync($"/api/staff/{id}"),
        1 => client.DeleteAsync($"/api/staff/operators/{id}"),
        _ => client.PostAsync($"/api/staff/{id}/deactivate", null),
    };

    private static async Task<bool> IsActiveAsync(HttpClient client, Guid id)
    {
        var list = await client.GetFromJsonAsync<JsonElement>("/api/staff");
        foreach (var u in list.EnumerateArray())
        {
            if (u.GetProperty("id").GetGuid() == id)
            {
                return u.GetProperty("isActive").GetBoolean();
            }
        }
        throw new InvalidOperationException("Membre introuvable.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Manager_cannot_deactivate_admin(int route)
    {
        var id = await CreateAsync(ClientWithRole("Admin"), "Admin");
        var manager = ClientWithRole("FloorManager");

        (await Deactivate(manager, route, id)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IsActiveAsync(manager, id)).Should().BeTrue();
    }

    [Fact]
    public async Task Admin_can_deactivate_admin()
    {
        var admin = ClientWithRole("Admin");
        var id = await CreateAsync(admin, "Admin");

        (await Deactivate(admin, 2, id)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await IsActiveAsync(admin, id)).Should().BeFalse();
    }

    [Fact]
    public async Task Manager_can_deactivate_waiter()
    {
        var manager = ClientWithRole("FloorManager");
        var id = await CreateAsync(manager, "Waiter");

        (await Deactivate(manager, 0, id)).StatusCode.Should().Be(HttpStatusCode.OK);
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
