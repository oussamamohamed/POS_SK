using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Client.Maui.Contracts;

namespace RestaurantPos.Client.Maui.Services;

public class CrossPlatformDiscoveryService : ICrossPlatformDiscoveryService
{
    public const int DiscoveryPort = 45454;

    public async Task<IReadOnlyList<DiscoveredPeripheral>> DiscoverPeripheralsAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var discovered = new List<DiscoveredPeripheral>();

        try
        {
            using var udp = new UdpClient();
            udp.EnableBroadcast = true;

            byte[] probe = "DISCOVER_POS_PERIPHERALS"u8.ToArray();
            var endpoint = new IPEndPoint(IPAddress.Broadcast, 9100);

            await udp.SendAsync(probe, endpoint, cancellationToken).ConfigureAwait(false);

        }
        catch (Exception)
        {
            // Fallback gracefully on isolated environments
        }

#if DEBUG
        if (discovered.Count == 0)
        {
            // In local development / debug simulation, provide mock fallback printers for UI testing
            discovered.Add(new DiscoveredPeripheral(
                DeviceName: "Epson TM-T20III (Comptoir)",
                ServiceType: "_printer._tcp",
                IpAddress: "192.168.1.100",
                Port: 9100,
                ModelHint: "Epson 80mm ESC/POS"
            ));

            discovered.Add(new DiscoveredPeripheral(
                DeviceName: "Epson TM-T88 (Cuisine)",
                ServiceType: "_printer._tcp",
                IpAddress: "192.168.1.101",
                Port: 9100,
                ModelHint: "Epson 80mm ESC/POS"
            ));
        }
#endif

        return discovered;
    }

    public async Task<IReadOnlyList<DiscoveredMasterServer>> DiscoverMasterServersAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var servers = new List<DiscoveredMasterServer>();

        try
        {
            using var udp = new UdpClient();
            udp.EnableBroadcast = true;
            udp.Client.ReceiveTimeout = (int)timeout.TotalMilliseconds;

            byte[] probe = "DISCOVER_RESTAURANT_POS_SERVER"u8.ToArray();
            var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);

            await udp.SendAsync(probe, broadcastEndpoint, cancellationToken).ConfigureAwait(false);

            // Give a short listening window
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            while (!cts.IsCancellationRequested)
            {
                try
                {
                    var receiveTask = udp.ReceiveAsync(cts.Token).AsTask();
                    var completedTask = await Task.WhenAny(receiveTask, Task.Delay(timeout, cts.Token)).ConfigureAwait(false);

                    if (completedTask == receiveTask)
                    {
                        var result = await receiveTask.ConfigureAwait(false);
                        string json = Encoding.UTF8.GetString(result.Buffer);
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        if (root.TryGetProperty("service", out var s) && s.GetString() == "RestaurantPosMaster")
                        {
                            string serverName = root.GetProperty("serverName").GetString() ?? "Caisse Principale";
                            string serverUrl = root.GetProperty("serverUrl").GetString() ?? $"http://{result.RemoteEndPoint.Address}:5000";
                            string version = root.TryGetProperty("version", out var v) ? v.GetString() ?? "1.0.0" : "1.0.0";

                            servers.Add(new DiscoveredMasterServer(
                                ServerName: serverName,
                                ServerUrl: serverUrl,
                                IpAddress: result.RemoteEndPoint.Address.ToString(),
                                Port: result.RemoteEndPoint.Port,
                                Version: version,
                                IsActive: true
                            ));
                        }
                    }
                    else
                    {
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    break;
                }
            }
        }
        catch (Exception)
        {
            // Ignore broadcast socket exceptions on restricted mobile profiles
        }

#if DEBUG
        // In DEBUG mode only: If no server responded on broadcast (e.g. offline simulator), add default local fallback
        if (servers.Count == 0)
        {
            servers.Add(new DiscoveredMasterServer(
                ServerName: "Caisse Principale (Serveur Local)",
                ServerUrl: "http://127.0.0.1:5000",
                IpAddress: "127.0.0.1",
                Port: 5000,
                Version: "1.0.0",
                IsActive: true
            ));
        }
#endif

        return servers;
    }
}
