using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using RestaurantPos.Api.Hubs;
using RestaurantPos.Infrastructure.Printing;

namespace RestaurantPos.Api.Services;

public sealed class SignalRPrinterStatusNotifier : IPrinterStatusNotifier
{
    private readonly IHubContext<PosHub, IPosHubClient> _hub;
    public SignalRPrinterStatusNotifier(IHubContext<PosHub, IPosHubClient> hub) => _hub = hub;

    public Task PrinterStatusChangedAsync(Guid printerId, string printerName, bool isOnline, int pendingCount) =>
        _hub.Clients.All.OnPrinterStatusChanged(printerId, printerName, isOnline, pendingCount);
}
