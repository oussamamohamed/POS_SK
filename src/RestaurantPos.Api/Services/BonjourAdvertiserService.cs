using System;
using Microsoft.Extensions.Configuration;

namespace RestaurantPos.Api.Services;

public sealed partial class BonjourAdvertiserService
{
    /// <summary>Nom annoncé en Bonjour et renvoyé à l'appairage (l'iPad s'en sert pour retrouver le serveur).</summary>
    public static string ServerName(IConfiguration config) =>
        string.IsNullOrWhiteSpace(config["Discovery:ServerName"]) ? Environment.MachineName : config["Discovery:ServerName"]!;
}
