using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Makaretu.Dns;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RestaurantPos.Api.Services;

/// <summary>
/// Annonce le serveur en Bonjour (<c>_restaurantpos._tcp</c>) pour que les iPads le trouvent sans saisir d'adresse.
/// Un échec (port 5353 pris, pas d'interface multicast) est journalisé : l'appairage par QR reste possible.
/// </summary>
public sealed partial class BonjourAdvertiserService : BackgroundService
{
    public const string ServiceType = "_restaurantpos._tcp";

    private readonly ILogger<BonjourAdvertiserService> _logger;
    private readonly IServer _server;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IConfiguration _config;

    public BonjourAdvertiserService(ILogger<BonjourAdvertiserService> logger, IServer server, IHostApplicationLifetime lifetime, IConfiguration config)
    {
        _logger = logger;
        _server = server;
        _lifetime = lifetime;
        _config = config;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Annonce Bonjour « {Name} » ({ServiceType}) sur le port {Port}")]
    private static partial void LogAdvertising(ILogger logger, string name, string serviceType, int port);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Annonce Bonjour impossible : {Message}. Appairage par QR ou adresse manuelle uniquement.")]
    private static partial void LogAdvertiseFailed(ILogger logger, string message);

    /// <summary>Nom annoncé en Bonjour et renvoyé à l'appairage (l'iPad s'en sert pour retrouver le serveur).</summary>
    public static string ServerName(IConfiguration config) =>
        string.IsNullOrWhiteSpace(config["Discovery:ServerName"]) ? Environment.MachineName : config["Discovery:ServerName"]!;

    /// <summary>Port HTTP réel lu dans les adresses Kestrel (« http://+:5080 », « http://[::]:5080 »…).</summary>
    public static ushort? PortFrom(IEnumerable<string> addresses)
    {
        var parsed = addresses
            .Select(a => Uri.TryCreate(a.Replace("://+:", "://localhost:", StringComparison.Ordinal).Replace("://*:", "://localhost:", StringComparison.Ordinal), UriKind.Absolute, out var uri) ? uri : null)
            .Where(u => u is not null)
            .OrderBy(u => u!.Scheme == Uri.UriSchemeHttp ? 0 : 1)
            .FirstOrDefault();
        return parsed is null ? null : (ushort)parsed.Port;
    }

    private static ServiceProfile BuildProfile(string name, ushort port)
    {
        var profile = new ServiceProfile(name, ServiceType, port);
        profile.AddProperty("name", name);
        profile.AddProperty("version", Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0");
        return profile;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = _lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        try
        {
            // Le port n'est connu qu'une fois Kestrel démarré.
            await started.Task.WaitAsync(stoppingToken);

            var port = PortFrom(_server.Features.Get<IServerAddressesFeature>()?.Addresses ?? []);
            if (port is null)
            {
                LogAdvertiseFailed(_logger, "port HTTP introuvable");
                return;
            }

            var name = ServerName(_config);
            using var mdns = new MulticastService();
            using var discovery = new ServiceDiscovery(mdns);
            var gate = new object();
            ServiceProfile? profile = null;

            // Le profil fige les adresses IP à sa création : on le remplace pour ne pas annoncer une IP périmée.
            // Ignoré tant que la première annonce n'est pas faite (profile null) : l'événement mDNS se déclenche aussi au Start.
            void Readvertise()
            {
                try
                {
                    lock (gate)
                    {
                        if (profile is null)
                        {
                            return;
                        }

                        discovery.Unadvertise(profile);
                        profile = BuildProfile(name, port.Value);
                        discovery.Advertise(profile);
                        discovery.Announce(profile, 2);
                    }
                }
                catch (Exception ex)
                {
                    LogAdvertiseFailed(_logger, ex.Message);
                }
            }

            // Nouvelle interface (câble branché, Wi-Fi reconnecté) côté mDNS ; nouveau bail DHCP sur la même
            // interface côté système : Makaretu ne signale que les nouvelles interfaces, pas les changements d'adresse.
            void OnAddressChanged(object? sender, EventArgs e) => Readvertise();
            mdns.NetworkInterfaceDiscovered += (_, _) => Readvertise();
            NetworkChange.NetworkAddressChanged += OnAddressChanged;
            try
            {
                mdns.Start();
                lock (gate)
                {
                    profile = BuildProfile(name, port.Value);
                    discovery.Advertise(profile);
                    discovery.Announce(profile, 2);
                }
                LogAdvertising(_logger, name, ServiceType, port.Value);

                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            finally
            {
                // Avant la libération de mdns/discovery : plus de réannonce sur des objets détruits.
                NetworkChange.NetworkAddressChanged -= OnAddressChanged;
            }
        }
        catch (OperationCanceledException)
        {
            // Arrêt normal du serveur.
        }
        catch (Exception ex)
        {
            LogAdvertiseFailed(_logger, ex.Message);
        }
    }
}
