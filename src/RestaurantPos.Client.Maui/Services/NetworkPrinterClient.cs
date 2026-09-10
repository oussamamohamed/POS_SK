using System.Net.Sockets;
using RestaurantPos.Client.Maui.Contracts;

namespace RestaurantPos.Client.Maui.Services;

public class NetworkPrinterClient : IPrinterClient
{
    public async Task<bool> PrintRawEscPosAsync(string ipAddress, int port, byte[] payload, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ipAddress)) return false;

        try
        {
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(2));

            await client.ConnectAsync(ipAddress, port, cts.Token).ConfigureAwait(false);
            using var stream = client.GetStream();

            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            // Network printer offline or unreachable
            return false;
        }
    }

    public async Task<bool> KickDrawerAsync(string ipAddress, int port, CancellationToken cancellationToken = default)
    {
        // Standard ESC/POS cash drawer pulse sequence: ESC p 0 25 250
        byte[] kickPulse = [0x1B, 0x70, 0x00, 0x19, 0xFA];
        return await PrintRawEscPosAsync(ipAddress, port, kickPulse, cancellationToken).ConfigureAwait(false);
    }
}
