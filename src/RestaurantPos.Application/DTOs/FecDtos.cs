using System;

namespace RestaurantPos.Application.DTOs;

public sealed record FecExportRequest(
    DateTimeOffset StartDateUtc,
    DateTimeOffset EndDateUtc,
    string? SirenNumber = null,
    string? CompanyName = null);

public sealed record FecExportResult(
    string FileName,
    byte[] FileBytes,
    int TotalRecords,
    decimal TotalDebit,
    decimal TotalCredit);
