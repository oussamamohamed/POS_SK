namespace RestaurantPos.Client.Maui.Contracts;

public interface IPrinterClient
{
    Task<bool> PrintRawEscPosAsync(string ipAddress, int port, byte[] payload, CancellationToken cancellationToken = default);
    Task<bool> KickDrawerAsync(string ipAddress, int port, CancellationToken cancellationToken = default);
}
