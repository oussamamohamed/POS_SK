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
    private readonly ReportPrintDataService _reports;
    private readonly TimeProvider _time;
    private readonly ILogger<PrintDispatcher> _logger;
    private readonly IFiscalJournal? _journal;

    public PrintDispatcher(AppDbContext db, PrintQueue queue, IRestaurantSettingsService settings, ReportPrintDataService reports, TimeProvider time, ILogger<PrintDispatcher> logger, IFiscalJournal? journal = null)
    {
        _db = db;
        _queue = queue;
        _settings = settings;
        _reports = reports;
        _time = time;
        _logger = logger;
        _journal = journal;
    }

    public async Task<bool> QueueCounterSaleAsync(Guid orderId, string terminalId, string receiptNumber, bool withFiscalReceipt, bool hasCash, CancellationToken ct = default)
    {
        try
        {
            var printer = await ReceiptPrinterAsync(terminalId, ct).ConfigureAwait(false);
            if (printer is null) return false;
            var order = await LoadOrderAsync(orderId, ct).ConfigureAwait(false);
            var settings = await _settings.GetAsync(ct).ConfigureAwait(false);
            var language = settings.ReceiptLanguage;
            var pickup = order.PickupNumber ?? string.Empty;
            var receipt = withFiscalReceipt ? await LoadReceiptAsync(terminalId, receiptNumber, ct).ConfigureAwait(false) : null;
            var (kind, document) = withFiscalReceipt
                ? (PrintJobKind.Receipt, TicketDocumentBuilder.FiscalReceipt(receipt!, order, pickup, order.PickupBuzzer, language, settings))
                : (PrintJobKind.PickupVoucher, TicketDocumentBuilder.PickupCoupon(order, pickup, order.PickupBuzzer, language, _time.GetUtcNow()));
            await _queue.EnqueueAsync(printer.Id, kind, document, hasCash && printer.OpenCashDrawerOnReceipt, receipt?.Id, null, ct).ConfigureAwait(false);
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
            var settings = await _settings.GetAsync(ct).ConfigureAwait(false);
            await _queue.EnqueueAsync(printer.Id, PrintJobKind.Receipt, TicketDocumentBuilder.Receipt(receipt, order, settings.ReceiptLanguage, settings), hasCash && printer.OpenCashDrawerOnReceipt, receipt.Id, null, ct).ConfigureAwait(false);
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

    public Task<bool> QueueXReportAsync(FiscalSummaryDto summary, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return QueueReportAsync(summary.TerminalId, async settings =>
        {
            var data = await _reports.BuildAsync(summary.TerminalId, summary.PeriodStartUtc, summary.PeriodEndUtc, ct).ConfigureAwait(false);
            return TicketDocumentBuilder.XReport(summary, data, settings.ReceiptLanguage, _time.GetUtcNow(), settings);
        }, ct);
    }

    public Task<bool> QueueZClosureAsync(DailyFiscalClosureDto closure, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(closure);
        return QueueReportAsync(closure.TerminalId, async settings =>
        {
            var data = await _reports.BuildAsync(closure.TerminalId, closure.PeriodStartUtc, closure.ClosedAtUtc, ct).ConfigureAwait(false);
            return TicketDocumentBuilder.ZClosure(closure, data, settings.ReceiptLanguage, settings);
        }, closure.ClosureId, ct);
    }

    public async Task<(bool printQueued, int duplicateNumber)> QueueZClosureReprintAsync(DailyFiscalClosureDto closure, Guid? operatorId = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(closure);
        try
        {
            var printer = await ReceiptPrinterAsync(closure.TerminalId, ct).ConfigureAwait(false);
            var settings = await _settings.GetAsync(ct).ConfigureAwait(false);
            var data = await _reports.BuildAsync(closure.TerminalId, closure.PeriodStartUtc, closure.ClosedAtUtc, ct).ConfigureAwait(false);

            return await _db.ExecuteInTransactionAsync(async txCt =>
            {
                var duplicateNumber = await _db.NextAsync(closure.ClosureId, txCt).ConfigureAwait(false);

                var document = TicketDocumentBuilder.ZClosure(closure, data, settings.ReceiptLanguage, settings, duplicateNumber);

                bool queued = false;
                if (printer != null)
                {
                    await _queue.EnqueueAsync(
                        printer.Id,
                        PrintJobKind.Report,
                        document,
                        false,
                        closure.ClosureId,
                        duplicateNumber,
                        txCt).ConfigureAwait(false);
                    queued = true;
                }

                if (_journal != null)
                {
                    await _journal.AppendAsync(
                        JournalEventTypes.DuplicatePrinted,
                        new
                        {
                            documentId = closure.ClosureId,
                            documentType = "closure",
                            closureSequence = closure.ClosureSequence,
                            duplicateNumber,
                            printQueued = queued,
                            terminalId = closure.TerminalId
                        },
                        terminalId: closure.TerminalId,
                        operatorId: operatorId,
                        cancellationToken: txCt).ConfigureAwait(false);
                }

                return (queued, duplicateNumber);
            }, System.Data.IsolationLevel.Serializable, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            PrintLog.QueueFailed(_logger, ex);
            return (false, 0);
        }
    }

    public async Task<(bool printQueued, int duplicateNumber)> QueueReceiptReprintAsync(Guid receiptId, Guid? operatorId = null, CancellationToken ct = default)
    {
        var receipt = await _db.FiscalReceipts.AsNoTracking().FirstOrDefaultAsync(r => r.Id == receiptId, ct).ConfigureAwait(false);
        if (receipt is null) return (false, 0);
        return await QueueReceiptReprintAsync(receipt, operatorId, ct).ConfigureAwait(false);
    }

    public async Task<(bool printQueued, int duplicateNumber)> QueueReceiptReprintAsync(FiscalReceipt receipt, Guid? operatorId = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        try
        {
            var printer = await ReceiptPrinterAsync(receipt.TerminalId, ct).ConfigureAwait(false);
            var settings = await _settings.GetAsync(ct).ConfigureAwait(false);
            var order = await _db.Orders.AsNoTracking().Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == receipt.OrderId, ct).ConfigureAwait(false);

            return await _db.ExecuteInTransactionAsync(async txCt =>
            {
                var duplicateNumber = await _db.NextAsync(receipt.Id, txCt).ConfigureAwait(false);

                var document = order != null
                    ? TicketDocumentBuilder.Receipt(receipt, order, settings.ReceiptLanguage, settings, duplicateNumber)
                    : TicketDocumentBuilder.ReceiptFallback(receipt, settings.ReceiptLanguage, settings, duplicateNumber);

                bool queued = false;
                if (printer != null)
                {
                    await _queue.EnqueueAsync(
                        printer.Id,
                        PrintJobKind.Receipt,
                        document,
                        false,
                        receipt.Id,
                        duplicateNumber,
                        txCt).ConfigureAwait(false);
                    queued = true;
                }

                if (_journal != null)
                {
                    await _journal.AppendAsync(
                        JournalEventTypes.DuplicatePrinted,
                        new
                        {
                            documentId = receipt.Id,
                            documentType = "receipt",
                            receiptNumber = receipt.ReceiptNumber,
                            duplicateNumber,
                            printQueued = queued,
                            terminalId = receipt.TerminalId
                        },
                        terminalId: receipt.TerminalId,
                        operatorId: operatorId,
                        cancellationToken: txCt).ConfigureAwait(false);
                }

                return (queued, duplicateNumber);
            }, System.Data.IsolationLevel.Serializable, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            PrintLog.QueueFailed(_logger, ex);
            return (false, 0);
        }
    }

    public Task<bool> QueuePeriodClosureAsync(PeriodClosureDto closure, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(closure);
        return QueueReportAsync(closure.TerminalId, async settings =>
        {
            var data = await _reports.BuildAsync(closure.TerminalId, closure.PeriodStartUtc, closure.PeriodEndUtc, ct).ConfigureAwait(false);
            return TicketDocumentBuilder.PeriodClosure(closure, data, settings.ReceiptLanguage, settings);
        }, ct);
    }

    private Task<bool> QueueReportAsync(string terminalId, Func<RestaurantSettingsDto, Task<TicketDocument>> build, CancellationToken ct)
        => QueueReportAsync(terminalId, build, null, ct);

    private async Task<bool> QueueReportAsync(string terminalId, Func<RestaurantSettingsDto, Task<TicketDocument>> build, Guid? documentId, CancellationToken ct = default)
    {
        try
        {
            var printer = await ReceiptPrinterAsync(terminalId, ct).ConfigureAwait(false);
            if (printer is null) return false;
            var settings = await _settings.GetAsync(ct).ConfigureAwait(false);
            await _queue.EnqueueAsync(printer.Id, PrintJobKind.Report, await build(settings).ConfigureAwait(false), false, documentId, null, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            PrintLog.QueueFailed(_logger, ex);
            return false;
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
