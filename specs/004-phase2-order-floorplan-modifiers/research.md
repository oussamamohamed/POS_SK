# Research & Architecture Decisions: Phase 2 - Tactile Order Entry, 2D Floor Plan & Modifiers

**Feature**: `004-phase2-order-floorplan-modifiers`
**Date**: 2026-08-15
**Status**: Completed

## 1. 2D Floor Plan Rendering & Zero-Latency Table Transitions

### Decision
Render restaurant tables using high-performance responsive MAUI layout templates (`AbsoluteLayout` / `FlexLayout`) backed by reactive ViewModel bindings (`FloorPlanViewModel`). Table state transitions (`Free` $\to$ `Occupied` $\to$ `BillRequested` $\to$ `Paid`) execute purely in-memory with background local database persistence.

### Rationale
- **Sub-100ms Transitions**: Direct in-memory table lookup by `TableNumber` allows instant opening of active table notes without screen flickers or query latency.
- **Visual Contrast**: Status color tokens (Green `#10B981`, Blue `#3B82F6`, Amber `#F59E0B`, Slate `#64748B`) provide high-contrast legibility across bright daylight and dim restaurant lighting.

### Alternatives Considered
- **SkiaSharp Custom Canvas (`SKCanvasView`)**: Useful for complex polygon tables, but MAUI XAML native templates provide faster data-binding, native accessibility, and touch handling with less overhead.
- **Embedded HTML5 Canvas**: Rejected due to Webview startup latency and memory overhead.

---

## 2. In-Memory Reactive Basket & Fast Calculation Engine

### Decision
Maintain the active order in memory as an `ObservableCollection<OrderItem>` with cached tax summaries. Any item addition, quantity modification, or modifier selection triggers an atomic in-memory recalculation in $< 1\text{ms}$, updating the UI at 60 FPS ($< 16\text{ms}$).

### Rationale
- Cashiers and waitstaff can tap product tiles in rapid succession without encountering UI lags.
- Each modification atomically updates the append-only local journal (`TransactionJournalEntry`) asynchronously in the background.

---

## 3. Modal Modifier Architecture (Cuissons & Suppléments)

### Decision
Implement `ModifiersModal` as a tactile modal popup with distinct sections:
1. **Mandatory Single-Choice** (e.g. Cuisson: Bleu, Saignant, À point, Bien cuit) $\to$ `MinSelections=1, MaxSelections=1`.
2. **Optional Multi-Choice** (e.g. Suppléments: Double Fromage +1.50 €, Bacon +2.00 €) $\to$ `MinSelections=0, MaxSelections=5`.
3. **Special Instructions / Kitchen Notes** (e.g. "Sans sel / Allergie arachides").

### Rationale
- Guarantees kitchen tickets receive clear preparation instructions while billing customers accurately for paid extras.
