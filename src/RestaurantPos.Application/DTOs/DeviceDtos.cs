using System;

namespace RestaurantPos.Application.DTOs;

public record CreatePairingCodeRequest(string Name, string Role);

public record PairingCodeResponse(string Code, DateTimeOffset ExpiresAtUtc, string QrPayload, string QrPngBase64);

public record PairRequest(string? Code);

public record PairResponse(Guid DeviceId, string Token, string TerminalId, string Name, string Role, string ServerName);

public record DeviceDto(Guid Id, string Name, string Role, string TerminalId, DateTimeOffset PairedAtUtc, DateTimeOffset? LastSeenUtc, bool IsRevoked, Guid? ReceiptPrinterId);

public record SetReceiptPrinterRequest(Guid? PrinterId);
