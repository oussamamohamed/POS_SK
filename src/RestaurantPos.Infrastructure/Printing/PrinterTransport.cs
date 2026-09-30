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
        return EscPosSender.SendAsync(printer.IpAddress, printer.Port, BuildPayload(printer, document, openCashDrawer), ct);
    }

    /// <summary>Texte si l'imprimante est en mode texte et le document LTR ; sinon image (l'arabe exige la mise en forme HarfBuzz).</summary>
    public static byte[] BuildPayload(PrinterConfiguration printer, TicketDocument document, bool openCashDrawer)
    {
        ArgumentNullException.ThrowIfNull(printer);
        ArgumentNullException.ThrowIfNull(document);
        return printer.TextMode && !document.RightToLeft
            ? EscPosTextRenderer.Render(document, printer.PaperWidthMm, openCashDrawer)
            : EscPosCommands.Build(EscPosRasterRenderer.Render(document, EscPosRasterRenderer.DotsFor(printer.PaperWidthMm)), openCashDrawer);
    }
}
