using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RestaurantPos.Infrastructure.Printing;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class EscPosSenderTests
{
    [Fact]
    public async Task SendAsync_DeliversExactBytes()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var received = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync();
                using var ms = new MemoryStream();
                await client.GetStream().CopyToAsync(ms);
                return ms.ToArray();
            });

            byte[] payload = [0x1B, 0x40, 1, 2, 3, 0x1D, 0x56, 0x42, 0x00];
            await EscPosSender.SendAsync("127.0.0.1", port, payload, CancellationToken.None);

            (await received.WaitAsync(TimeSpan.FromSeconds(5))).Should().Equal(payload);
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task SendAsync_ConnectionRefused_Throws()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var act = () => EscPosSender.SendAsync("127.0.0.1", port, [0x1B, 0x40], CancellationToken.None);
        await act.Should().ThrowAsync<SocketException>();
    }

    [Fact]
    public async Task SendAsync_PeerNeverReads_TimesOut()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            // Garde la connexion acceptée ouverte sans jamais lire, sinon la finalisation du client ferme le socket (Broken pipe).
            var accepted = listener.AcceptTcpClientAsync();
            var act = () => EscPosSender.SendAsync("127.0.0.1", port, new byte[64 * 1024 * 1024], CancellationToken.None, writeTimeout: TimeSpan.FromMilliseconds(300));
            await act.Should().ThrowAsync<OperationCanceledException>();
            GC.KeepAlive(accepted);
        }
        finally { listener.Stop(); }
    }
}
