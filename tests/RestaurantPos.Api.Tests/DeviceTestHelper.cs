using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Api.Endpoints;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Enums;

namespace RestaurantPos.Api.Tests;

/// <summary>Appaire un poste via le service et pose son jeton sur le client HTTP.</summary>
internal static class DeviceTestHelper
{
    public static async Task<PairedDevice> PairAsync(PosApiApplicationFactory factory, HttpClient client, string name = "Caisse test")
    {
        using var scope = factory.Services.CreateScope();
        var devices = scope.ServiceProvider.GetRequiredService<IDeviceService>();
        var code = await devices.CreatePairingCodeAsync(name, DeviceRole.Caisse, Guid.NewGuid());
        var paired = await devices.PairAsync(code.Code);
        client.DefaultRequestHeaders.Remove(RequireDeviceFilter.HeaderName);
        client.DefaultRequestHeaders.Add(RequireDeviceFilter.HeaderName, paired!.Token);
        return paired;
    }
}
