using System;
using System.Collections.Generic;

namespace RestaurantPos.Application.DTOs;

public record GridLayoutDto(
    Guid Id,
    string CategoryId,
    string Name,
    int ColumnsCount,
    int RowsCount,
    int PageIndex,
    int TotalPages,
    int Version,
    DateTimeOffset UpdatedAtUtc,
    List<GridSlotDto> Slots
);

public record GridSlotDto(
    Guid Id,
    Guid GridLayoutId,
    Guid? ProductId,
    int RowIndex,
    int ColumnIndex,
    int SlotIndex,
    string? CustomLabel,
    string? CustomColorHex,
    bool IsDisabled,
    ProductSummaryDto? Product
);

public record ProductSummaryDto(
    Guid Id,
    string Name,
    decimal Price,
    string? PreparationStationId,
    string? ColorHex
);

public record UpdateGridLayoutRequest(
    string CategoryId,
    int ColumnsCount,
    int RowsCount,
    int PageIndex,
    List<UpdateGridSlotItem> Slots
);

public record UpdateGridSlotItem(
    int RowIndex,
    int ColumnIndex,
    Guid? ProductId,
    string? CustomLabel,
    string? CustomColorHex
);

public record SwapGridSlotsRequest(
    Guid LayoutId,
    int SourceRow,
    int SourceCol,
    int TargetRow,
    int TargetCol
);

public record UpdateGridDimensionsRequest(
    string CategoryId,
    int ColumnsCount,
    int RowsCount,
    bool ApplyToAllCategories = false
);
