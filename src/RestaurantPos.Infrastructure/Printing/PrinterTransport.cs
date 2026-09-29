using System;
using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Infrastructure.Printing;

/// <summary>Envoie un ticket à une imprimante ; lève en cas d'échec (réseau, délai).</summary>
public interface IPrinterTransport
{
    Task SendAsync(PrinterConfiguration printer, TicketDocument document, bool openCashDrawer, CancellationToken ct);
}

public sealed class EscPosPrinterTransport : IPrinterTransport
{
    public Task SendAsync(PrinterConfiguration printer, TicketDocument document, bool openCashDrawer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(printer);
        var images = EscPosRasterRenderer.Render(document, EscPosRasterRenderer.DotsFor(printer.PaperWidthMm));
        return EscPosSender.SendAsync(printer.IpAddress, printer.Port, EscPosCommands.Build(images, openCashDrawer), ct);
    }
}
