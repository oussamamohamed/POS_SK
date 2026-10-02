using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class FiscalHashingTests
{
    private static string LegacyCompute(
        string previousHash,
        string terminalId,
        long sequenceNumber,
        long amountCents,
        DateTimeOffset timestampUtc,
        string taxBreakdownJson)
    {
        string rawData = $"{previousHash}|{terminalId}|{sequenceNumber}|{amountCents}|{timestampUtc:O}|{taxBreakdownJson}";
        byte[] bytes = Encoding.UTF8.GetBytes(rawData);
        byte[] hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes);
    }

    [Fact]
    public void ComputeReceiptHash_MatchesLegacyImplementation_Exactly()
    {
        var timestamp = new DateTimeOffset(2026, 9, 30, 14, 30, 0, TimeSpan.Zero);
        string prevHash = NF525FiscalAuditService.GenesisHash;
        string terminalId = "POS_MAIN_TERM";
        long seq = 42;
        long amountCents = 12500;
        string taxJson = "{\"10\":1250,\"20\":2500}";

        string expected = LegacyCompute(prevHash, terminalId, seq, amountCents, timestamp, taxJson);
        string actual = FiscalHashing.ComputeReceiptHash(prevHash, terminalId, seq, amountCents, timestamp, taxJson);

        actual.Should().Be(expected);
    }

    [Fact]
    public void ComputeClosureHash_MatchesLegacyZCalculation()
    {
        var timestamp = new DateTimeOffset(2026, 9, 30, 23, 59, 59, TimeSpan.Zero);
        string prevHash = NF525FiscalAuditService.GenesisHash;
        string terminalId = "T01";
        long seq = 1;
        long totalSalesTtc = 450000;
        string taxJson = JsonSerializer.Serialize(new Dictionary<string, long> { ["10"] = 45000 });

        string expected = LegacyCompute(prevHash, terminalId, seq, totalSalesTtc, timestamp, taxJson);
        string actual = FiscalHashing.ComputeReceiptHash(prevHash, terminalId, seq, totalSalesTtc, timestamp, taxJson);

        actual.Should().Be(expected);
    }

    [Fact]
    public void ComputeSha256_WithLowercase_ReturnsValidLowercaseHex()
    {
        string raw = "hello|world";
        string hash = FiscalHashing.ComputeSha256(raw, lowerCase: true);

        hash.Should().MatchRegex("^[0-9a-f]{64}$");
        hash.Should().Be(FiscalHashing.ComputeSha256(raw, lowerCase: false).ToLowerInvariant());
    }
}
