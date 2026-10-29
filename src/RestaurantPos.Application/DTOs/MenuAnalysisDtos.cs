using System;
using System.Collections.Generic;

namespace RestaurantPos.Application.DTOs;

/// <summary>Classes de l'ingénierie de menu (Kasavana-Smith) : popularité × marge unitaire.</summary>
public enum MenuClass
{
    Star,
    Plowhorse,
    Puzzle,
    Dog
}

public sealed record MenuAnalysisItemDto(
    Guid ProductId,
    string Name,
    string? CategoryName,
    int Quantity,
    decimal Share,
    decimal RevenueHt,
    decimal? UnitCost,
    decimal? UnitMarginHt,
    decimal? MarginHt,
    bool Popular,
    bool? Profitable,
    MenuClass? Class,
    bool MissingCost);

public sealed record MenuAnalysisReportDto(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    decimal PopularityThreshold,
    decimal? AverageUnitMarginHt,
    int MissingCostCount,
    IReadOnlyList<MenuAnalysisItemDto> Items);
