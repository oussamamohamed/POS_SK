using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace RestaurantPos.Infrastructure.Printing;

public static class EscPosSender
{
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(10);

    public static async Task SendAsync(string host, int port, byte[] payload, CancellationToken ct, TimeSpan? connectTimeout = null, TimeSpan? writeTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        using var client = new TcpClient();
        using (var connect = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            connect.CancelAfter(connectTimeout ?? ConnectTimeout);
            await client.ConnectAsync(host, port, connect.Token).ConfigureAwait(false);
        }
        using var write = CancellationTokenSource.CreateLinkedTokenSource(ct);
        write.CancelAfter(writeTimeout ?? WriteTimeout);
        var stream = client.GetStream();
        await stream.WriteAsync(payload, write.Token).ConfigureAwait(false);
        await stream.FlushAsync(write.Token).ConfigureAwait(false);
    }
}
