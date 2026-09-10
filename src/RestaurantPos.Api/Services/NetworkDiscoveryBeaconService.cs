using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RestaurantPos.Api.Services;

public partial class NetworkDiscoveryBeaconService : BackgroundService
{
    public const int DiscoveryPort = 45454;
    private readonly ILogger<NetworkDiscoveryBeaconService> _logger;

    public NetworkDiscoveryBeaconService(ILogger<NetworkDiscoveryBeaconService> logger)
    {
        _logger = logger;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Service de découverte automatique démarré sur le port UDP {Port}")]
    private static partial void LogServiceStarted(ILogger logger, int port);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Impossible de lier le port UDP {Port} pour la découverte: {Message}")]
    private static partial void LogBindFailed(ILogger logger, int port, string message);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Requête de découverte reçue depuis {RemoteEndPoint}")]
    private static partial void LogDiscoveryRequestReceived(ILogger logger, IPEndPoint remoteEndPoint);

    [LoggerMessage(EventId = 4, Level = LogLevel.Debug, Message = "Erreur d'écoute UDP découverte: {Message}")]
    private static partial void LogUdpError(ILogger logger, string message);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogServiceStarted(_logger, DiscoveryPort);

        using var udpListener = new UdpClient();
        udpListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        
        try
        {
            udpListener.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
        }
        catch (Exception ex)
        {
            LogBindFailed(_logger, DiscoveryPort, ex.Message);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await udpListener.ReceiveAsync(stoppingToken).ConfigureAwait(false);
                string message = Encoding.UTF8.GetString(result.Buffer);

                if (message.Contains("DISCOVER_RESTAURANT_POS_SERVER", StringComparison.OrdinalIgnoreCase))
                {
                    LogDiscoveryRequestReceived(_logger, result.RemoteEndPoint);

                    var responsePayload = new
                    {
                        service = "RestaurantPosMaster",
                        version = "1.0.0",
                        serverName = "Caisse Principale (Master POS)",
                        serverUrl = "http://localhost:5000",
                        apiEndpoint = "http://localhost:5000/api",
                        hubEndpoint = "http://localhost:5000/hubs/kitchen",
                        timestamp = DateTimeOffset.UtcNow
                    };

                    byte[] responseBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(responsePayload));
                    await udpListener.SendAsync(responseBytes, responseBytes.Length, result.RemoteEndPoint).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogUdpError(_logger, ex.Message);
            }
        }
    }
}
