using System.Security.Cryptography;
using System.Text;

namespace RestaurantPos.Infrastructure.Services;

public static class FiscalHashing
{
    public static string ComputeSha256(string rawData, bool lowerCase = false)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(rawData);
        byte[] hashBytes = SHA256.HashData(bytes);
        return lowerCase ? Convert.ToHexStringLower(hashBytes) : Convert.ToHexString(hashBytes);
    }

    public static string ComputeReceiptHash(
        string previousHash,
        string terminalId,
        long sequenceNumber,
        long amountCents,
        DateTimeOffset timestampUtc,
        string taxBreakdownJson)
    {
        string rawData = $"{previousHash}|{terminalId}|{sequenceNumber}|{amountCents}|{timestampUtc:O}|{taxBreakdownJson}";
        return ComputeSha256(rawData, lowerCase: false);
    }

    public static string ComputeJetHash(
        string previousHash,
        long chainSequence,
        string eventType,
        DateTimeOffset occurredAtUtc,
        string? terminalId,
        Guid? operatorId,
        string payloadJson)
    {
        string rawData = $"{previousHash}|{chainSequence}|{eventType}|{occurredAtUtc:O}|{terminalId ?? string.Empty}|{operatorId?.ToString() ?? string.Empty}|{payloadJson}";
        return ComputeSha256(rawData, lowerCase: true);
    }

    public static string ComputePeriodClosureHash(
        string previousHash,
        string terminalId,
        int periodType,
        string periodKey,
        long totalTtcCents,
        long totalHtCents,
        string taxesJson,
        string tendersJson,
        long perpetualGrandTotalCents,
        DateTimeOffset periodEndUtc)
    {
        string rawData = $"{previousHash}|{terminalId}|{periodType}|{periodKey}|{totalTtcCents}|{totalHtCents}|{taxesJson}|{tendersJson}|{perpetualGrandTotalCents}|{periodEndUtc:O}";
        return ComputeSha256(rawData, lowerCase: true);
    }

    public static string ComputeArchiveHash(
        string previousHash,
        int periodType,
        string periodKey,
        string fileSha256,
        DateTimeOffset createdAtUtc)
    {
        string rawData = $"{previousHash}|{periodType}|{periodKey}|{fileSha256}|{createdAtUtc:O}";
        return ComputeSha256(rawData, lowerCase: true);
    }
}
