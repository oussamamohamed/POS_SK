using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

public class FiscalChainVerificationService : IFiscalChainVerificationService
{
    private readonly AppDbContext _dbContext;
    private readonly IFiscalJournal _fiscalJournal;

    public FiscalChainVerificationService(AppDbContext dbContext, IFiscalJournal? fiscalJournal = null)
    {
        _dbContext = dbContext;
        _fiscalJournal = fiscalJournal ?? new FiscalJournalService(dbContext);
    }

    public async Task<FiscalVerificationResultDto> VerifyAllChainsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var chainResults = new List<ChainStatusDto>();

        // 1. Receipts chains (per terminal)
        var receiptTerminals = await _dbContext.FiscalReceipts
            .AsNoTracking()
            .Select(r => r.TerminalId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var terminalId in receiptTerminals)
        {
            var receipts = await _dbContext.FiscalReceipts
                .AsNoTracking()
                .Where(r => r.TerminalId == terminalId)
                .OrderBy(r => r.SequenceNumber)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            chainResults.Add(VerifyReceiptsChain(terminalId, receipts));
        }

        // 2. Z-closures chains (per terminal)
        var zTerminals = await _dbContext.DailyFiscalClosures
            .AsNoTracking()
            .Select(c => c.TerminalId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var terminalId in zTerminals)
        {
            var closures = await _dbContext.DailyFiscalClosures
                .AsNoTracking()
                .Where(c => c.TerminalId == terminalId)
                .OrderBy(c => c.ClosureSequence)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            chainResults.Add(VerifyZClosuresChain(terminalId, closures));
        }

        // 3. Period closures
        var periodTerminals = await _dbContext.PeriodClosures
            .AsNoTracking()
            .Select(c => c.TerminalId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (periodTerminals.Count == 0)
        {
            chainResults.Add(new ChainStatusDto("period_closures", null, 0, true, null));
        }
        else
        {
            foreach (var terminalId in periodTerminals)
            {
                var closures = await _dbContext.PeriodClosures
                    .AsNoTracking()
                    .Where(c => c.TerminalId == terminalId)
                    .OrderBy(c => c.ClosureSequence)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                chainResults.Add(VerifyPeriodClosuresChain(terminalId, closures));
            }
        }

        // 4. Journal chain (transverse)
        var allJournalEntries = await _dbContext.JournalEntries
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var legacyCount = allJournalEntries.Count(j => !j.ChainSequence.HasValue);
        var chainedJournalEntries = allJournalEntries
            .Where(j => j.ChainSequence.HasValue)
            .OrderBy(j => j.ChainSequence!.Value)
            .ToList();

        chainResults.Add(VerifyJournalChain(chainedJournalEntries, legacyCount));

        // 5. Archives
        var allArchives = await _dbContext.FiscalArchives
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        chainResults.Add(VerifyArchivesChain(allArchives));

        var overallValid = chainResults.All(c => c.IsValid);

        if (!overallValid)
        {
            try
            {
                var brokenChain = chainResults.First(c => !c.IsValid);
                await _fiscalJournal.AppendAsync(
                    JournalEventTypes.ChainBreakDetected,
                    new
                    {
                        brokenChain.Chain,
                        brokenChain.TerminalId,
                        brokenChain.Break?.SequenceNumber,
                        brokenChain.Break?.Reference,
                        brokenChain.Break?.Kind
                    },
                    terminalId: brokenChain.TerminalId,
                    cancellationToken: cancellationToken
                ).ConfigureAwait(false);
            }
            catch
            {
                // Never fail verification query if recording break event fails
            }
        }

        return new FiscalVerificationResultDto(overallValid, now, chainResults);
    }

    private static ChainStatusDto VerifyReceiptsChain(string terminalId, List<FiscalReceipt> receipts)
    {
        if (receipts.Count == 0)
        {
            return new ChainStatusDto("receipts", terminalId, 0, true, null);
        }

        string expectedPrevHash = NF525FiscalAuditService.GenesisHash;
        long expectedSequence = 1;

        foreach (var r in receipts)
        {
            if (r.SequenceNumber != expectedSequence)
            {
                return new ChainStatusDto("receipts", terminalId, receipts.Count, false,
                    new ChainBreakDto(r.SequenceNumber, r.ReceiptNumber, "sequence_gap"));
            }

            if (r.PreviousSignatureHash != expectedPrevHash)
            {
                return new ChainStatusDto("receipts", terminalId, receipts.Count, false,
                    new ChainBreakDto(r.SequenceNumber, r.ReceiptNumber, "previous_hash_mismatch"));
            }

            string computed = FiscalHashing.ComputeReceiptHash(
                r.PreviousSignatureHash,
                r.TerminalId,
                r.SequenceNumber,
                r.TotalTtcAmount.AmountInCents,
                r.CreatedAtUtc,
                r.TaxBreakdownJson
            );

            if (r.SignatureHash != computed)
            {
                return new ChainStatusDto("receipts", terminalId, receipts.Count, false,
                    new ChainBreakDto(r.SequenceNumber, r.ReceiptNumber, "signature_mismatch"));
            }

            expectedPrevHash = r.SignatureHash;
            expectedSequence = r.SequenceNumber + 1;
        }

        return new ChainStatusDto("receipts", terminalId, receipts.Count, true, null);
    }

    private static ChainStatusDto VerifyZClosuresChain(string terminalId, List<DailyFiscalClosure> closures)
    {
        if (closures.Count == 0)
        {
            return new ChainStatusDto("z_closures", terminalId, 0, true, null);
        }

        string expectedPrevHash = NF525FiscalAuditService.GenesisHash;
        long expectedSequence = 1;

        foreach (var c in closures)
        {
            var reference = $"{c.TerminalId}-Z-{c.ClosureSequence:D6}";

            if (c.ClosureSequence != expectedSequence)
            {
                return new ChainStatusDto("z_closures", terminalId, closures.Count, false,
                    new ChainBreakDto(c.ClosureSequence, reference, "sequence_gap"));
            }

            if (c.PreviousSignatureHash != expectedPrevHash)
            {
                return new ChainStatusDto("z_closures", terminalId, closures.Count, false,
                    new ChainBreakDto(c.ClosureSequence, reference, "previous_hash_mismatch"));
            }

            string computed = FiscalHashing.ComputeReceiptHash(
                c.PreviousSignatureHash,
                c.TerminalId,
                c.ClosureSequence,
                c.TotalSalesTtc.AmountInCents,
                c.PeriodEndUtc,
                c.TaxesSummaryJson
            );

            if (c.SignatureHash != computed)
            {
                return new ChainStatusDto("z_closures", terminalId, closures.Count, false,
                    new ChainBreakDto(c.ClosureSequence, reference, "signature_mismatch"));
            }

            expectedPrevHash = c.SignatureHash;
            expectedSequence = c.ClosureSequence + 1;
        }

        return new ChainStatusDto("z_closures", terminalId, closures.Count, true, null);
    }

    private static ChainStatusDto VerifyJournalChain(List<TransactionJournalEntry> chainedEntries, int legacyCount)
    {
        if (chainedEntries.Count == 0)
        {
            return new ChainStatusDto("journal", null, 0, true, null, legacyCount);
        }

        string expectedPrevHash = NF525FiscalAuditService.GenesisHash;
        long expectedSequence = 1;

        foreach (var entry in chainedEntries)
        {
            var seq = entry.ChainSequence!.Value;
            var reference = $"JET-{seq}";

            if (seq != expectedSequence)
            {
                return new ChainStatusDto("journal", null, chainedEntries.Count, false,
                    new ChainBreakDto(seq, reference, "sequence_gap"), legacyCount);
            }

            if (entry.PreviousHash != expectedPrevHash)
            {
                return new ChainStatusDto("journal", null, chainedEntries.Count, false,
                    new ChainBreakDto(seq, reference, "previous_hash_mismatch"), legacyCount);
            }

            string computed = FiscalHashing.ComputeJetHash(
                entry.PreviousHash,
                seq,
                entry.EventType,
                entry.OccurredAtUtc,
                entry.TerminalId,
                entry.OperatorId,
                entry.PayloadJson
            );

            if (entry.EntryHash != computed)
            {
                return new ChainStatusDto("journal", null, chainedEntries.Count, false,
                    new ChainBreakDto(seq, reference, "signature_mismatch"), legacyCount);
            }

            expectedPrevHash = entry.EntryHash;
            expectedSequence = seq + 1;
        }

        return new ChainStatusDto("journal", null, chainedEntries.Count, true, null, legacyCount);
    }

    private static ChainStatusDto VerifyPeriodClosuresChain(string terminalId, List<FiscalPeriodClosure> closures)
    {
        if (closures.Count == 0)
        {
            return new ChainStatusDto("period_closures", terminalId, 0, true, null);
        }

        foreach (var group in closures.GroupBy(c => c.PeriodType))
        {
            string expectedPrevHash = NF525FiscalAuditService.GenesisHash;
            long expectedSequence = 1;
            var typeClosures = group.OrderBy(c => c.ClosureSequence).ToList();

            foreach (var c in typeClosures)
            {
                var refCode = $"{(c.PeriodType == FiscalPeriodType.Monthly ? "M" : "A")}-{c.PeriodKey}";

                if (c.ClosureSequence != expectedSequence)
                {
                    return new ChainStatusDto("period_closures", terminalId, closures.Count, false,
                        new ChainBreakDto(c.ClosureSequence, refCode, "sequence_gap"));
                }

                if (c.PreviousSignatureHash != expectedPrevHash)
                {
                    return new ChainStatusDto("period_closures", terminalId, closures.Count, false,
                        new ChainBreakDto(c.ClosureSequence, refCode, "previous_hash_mismatch"));
                }

                var calculatedHash = FiscalHashing.ComputePeriodClosureHash(
                    expectedPrevHash,
                    c.TerminalId,
                    (int)c.PeriodType,
                    c.PeriodKey,
                    c.TotalTtcAmount.AmountInCents,
                    c.TotalHtAmount.AmountInCents,
                    c.TaxesSummaryJson,
                    c.TenderTotalsJson,
                    c.PerpetualGrandTotalCents,
                    c.PeriodEndUtc);

                if (c.SignatureHash != calculatedHash)
                {
                    return new ChainStatusDto("period_closures", terminalId, closures.Count, false,
                        new ChainBreakDto(c.ClosureSequence, refCode, "signature_mismatch"));
                }

                expectedPrevHash = c.SignatureHash;
                expectedSequence++;
            }
        }

        return new ChainStatusDto("period_closures", terminalId, closures.Count, true, null);
    }

    public static ChainStatusDto VerifyArchivesChain(List<FiscalArchive> archives)
    {
        if (archives.Count == 0)
        {
            return new ChainStatusDto("archives", null, 0, true, null);
        }

        string expectedPrevHash = NF525FiscalAuditService.GenesisHash;
        long expectedSequence = 1;
        var ordered = archives.OrderBy(a => a.ArchiveSequence).ToList();

        foreach (var a in ordered)
        {
            var refCode = $"ARC-{a.ArchiveSequence}";

            if (a.ArchiveSequence != expectedSequence)
            {
                return new ChainStatusDto("archives", null, archives.Count, false,
                    new ChainBreakDto(a.ArchiveSequence, refCode, "sequence_gap"));
            }

            if (a.PreviousSignatureHash != expectedPrevHash)
            {
                return new ChainStatusDto("archives", null, archives.Count, false,
                    new ChainBreakDto(a.ArchiveSequence, refCode, "previous_hash_mismatch"));
            }

            string computedSig = FiscalHashing.ComputeArchiveHash(
                expectedPrevHash,
                (int)a.PeriodType,
                a.PeriodKey,
                a.FileSha256,
                a.CreatedAtUtc);

            if (a.SignatureHash != computedSig)
            {
                return new ChainStatusDto("archives", null, archives.Count, false,
                    new ChainBreakDto(a.ArchiveSequence, refCode, "signature_mismatch"));
            }

            expectedPrevHash = a.SignatureHash;
            expectedSequence++;
        }

        return new ChainStatusDto("archives", null, archives.Count, true, null);
    }
}
