using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Printing;

/// <summary>
/// Met en file les tickets après le succès d'une opération métier. Ne lève jamais :
/// toute erreur est journalisée et donne false (printQueued), la vente reste valide.
/// </summary>
public sealed class PrintDispatcher
{
    private readonly AppDbContext _db;
    private readonly PrintQueue _queue;
    private readonly IRestaurantSettingsService _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<PrintDispatcher> _logger;

    public PrintDispatcher(AppDbContext db, PrintQueue queue, IRestaurantSettingsService settings, TimeProvider time, ILogger<PrintDispatcher> logger)
    {
        _db = db;
        _queue = queue;
        _settings = settings;
        _time = time;
        _logger = logger;
    }

    public async Task<bool> QueueCounterSaleAsync(Guid orderId, string terminalId, string receiptNumber, bool withFiscalReceipt, bool hasCash, CancellationToken ct = default)
    {
        try
        {
            var printer = await ReceiptPrinterAsync(terminalId, ct).ConfigureAwait(false);
            if (printer is null) return false;
            var order = await LoadOrderAsync(orderId, ct).ConfigureAwait(false);
            var language = (await _settings.GetAsync(ct).ConfigureAwait(false)).ReceiptLanguage;
            var pickup = order.PickupNumber ?? string.Empty;
            var (kind, document) = withFiscalReceipt
                ? (PrintJobKind.Receipt, TicketDocumentBuilder.FiscalReceipt(await LoadReceiptAsync(terminalId, receiptNumber, ct).ConfigureAwait(false), order, pickup, order.PickupBuzzer, language))
                : (PrintJobKind.PickupVoucher, TicketDocumentBuilder.PickupCoupon(order, pickup, order.PickupBuzzer, language, _time.GetUtcNow()));
            await _queue.EnqueueAsync(printer.Id, kind, document, hasCash && printer.OpenCashDrawerOnReceipt, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            PrintLog.QueueFailed(_logger, ex);
            return false;
        }
    }

    public async Task<bool> QueueTableReceiptAsync(Guid orderId, string terminalId, string receiptNumber, bool hasCash, CancellationToken ct = default)
    {
        try
        {
            var printer = await ReceiptPrinterAsync(terminalId, ct).ConfigureAwait(false);
            if (printer is null) return false;
            var order = await LoadOrderAsync(orderId, ct).ConfigureAwait(false);
            var receipt = await LoadReceiptAsync(terminalId, receiptNumber, ct).ConfigureAwait(false);
            var language = (await _settings.GetAsync(ct).ConfigureAwait(false)).ReceiptLanguage;
            await _queue.EnqueueAsync(printer.Id, PrintJobKind.Receipt, TicketDocumentBuilder.Receipt(receipt, order, language), hasCash && printer.OpenCashDrawerOnReceipt, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            PrintLog.QueueFailed(_logger, ex);
            return false;
        }
    }

    public async Task QueueKitchenTicketsAsync(IReadOnlyCollection<Guid> ticketIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ticketIds);
        if (ticketIds.Count == 0) return;
        try
        {
            var ids = ticketIds.ToList();
            var tickets = await _db.KitchenTickets.AsNoTracking().Include(t => t.Items).Where(t => ids.Contains(t.Id)).ToListAsync(ct).ConfigureAwait(false);
            var printers = await _db.PrinterConfigurations.AsNoTracking().Where(p => p.IsActive).ToListAsync(ct).ConfigureAwait(false);
            var language = (await _settings.GetAsync(ct).ConfigureAwait(false)).KitchenTicketLanguage;
            foreach (var ticket in tickets)
            {
                var targets = printers.Where(p => p.AssignedStationIds.Contains(ticket.StationId)).ToList();
                if (targets.Count == 0)
                {
                    PrintLog.NoPrinter(_logger, ticket.StationId);
                    continue;
                }
                var document = TicketDocumentBuilder.KitchenTicket(ticket, await LoadOrderAsync(ticket.OrderId, ct).ConfigureAwait(false), language);
                foreach (var printer in targets)
                {
                    try
                    {
                        await _queue.EnqueueAsync(printer.Id, PrintJobKind.KitchenTicket, document, false, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        PrintLog.QueueFailed(_logger, ex);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            PrintLog.QueueFailed(_logger, ex);
        }
    }

    /// <summary>Imprimante propre au poste (active) sinon première imprimante active du poste RECEIPT.</summary>
    private async Task<PrinterConfiguration?> ReceiptPrinterAsync(string terminalId, CancellationToken ct)
    {
        var printers = await _db.PrinterConfigurations.AsNoTracking().Where(p => p.IsActive).ToListAsync(ct).ConfigureAwait(false);
        var ownId = await _db.Devices.AsNoTracking().Where(d => d.TerminalId == terminalId).Select(d => d.ReceiptPrinterId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var printer = printers.FirstOrDefault(p => p.Id == ownId)
            ?? printers.Where(p => p.AssignedStationIds.Contains(PreparationStations.Receipt)).OrderBy(p => p.Name, StringComparer.Ordinal).FirstOrDefault();
        if (printer is null) PrintLog.NoPrinter(_logger, terminalId);
        return printer;
    }

    private Task<Order> LoadOrderAsync(Guid orderId, CancellationToken ct) =>
        _db.Orders.AsNoTracking().Include(o => o.Items).FirstAsync(o => o.Id == orderId, ct);

    private Task<FiscalReceipt> LoadReceiptAsync(string terminalId, string receiptNumber, CancellationToken ct) =>
        _db.FiscalReceipts.AsNoTracking().FirstAsync(r => r.TerminalId == terminalId && r.ReceiptNumber == receiptNumber, ct);
}
