import Foundation

extension LocalPosAPI {
    public func gridLayout(categoryId: String, page: Int) async throws -> TouchGridLayout {
        if let layout = try gridRepository.layout(categoryId: categoryId, page: page) { return layout }
        guard page == 0 else { throw APIError.notFound("Aucune grille") }
        // Comme le serveur : génération automatique d'une grille 4×4 à la première lecture.
        let products = try catalogRepository.activeProducts().filter { categoryId == "ALL" || $0.categoryId == categoryId }
        let slots = (0..<16).map { GridSlot(productId: products[safe: $0]?.id, rowIndex: $0 / 4, columnIndex: $0 % 4) }
        let layout = TouchGridLayout(categoryId: categoryId, name: categoryId, slots: slots)
        try db.transaction { try gridRepository.store(layout) }
        return try gridRepository.layout(id: layout.id) ?? layout
    }

    public func saveGridLayout(_ request: UpdateGridLayoutRequest) async throws -> TouchGridLayout {
        try requireAuth()
        var layout = try gridRepository.layout(categoryId: request.categoryId, page: request.pageIndex)
            ?? TouchGridLayout(categoryId: request.categoryId, pageIndex: request.pageIndex, version: 0)
        layout.columnsCount = request.columnsCount
        layout.rowsCount = request.rowsCount
        layout.version = (layout.version ?? 0) + 1
        layout.slots = request.slots.map {
            GridSlot(productId: $0.productId, rowIndex: $0.rowIndex, columnIndex: $0.columnIndex, customLabel: $0.customLabel, customColorHex: $0.customColorHex)
        }
        try db.transaction { try gridRepository.store(layout) }
        return try gridRepository.layout(id: layout.id) ?? layout
    }

    public func swapGridSlots(layoutId: UUID, from: GridPosition, to: GridPosition) async throws -> TouchGridLayout {
        try requireAuth()
        guard var layout = try gridRepository.layout(id: layoutId) else { throw APIError.notFound("Grille introuvable") }
        let a = layout.slots.firstIndex { $0.rowIndex == from.row && $0.columnIndex == from.column }
        let b = layout.slots.firstIndex { $0.rowIndex == to.row && $0.columnIndex == to.column }
        switch (a, b) {
        case let (a?, b?):
            (layout.slots[a].rowIndex, layout.slots[b].rowIndex) = (layout.slots[b].rowIndex, layout.slots[a].rowIndex)
            (layout.slots[a].columnIndex, layout.slots[b].columnIndex) = (layout.slots[b].columnIndex, layout.slots[a].columnIndex)
        case let (a?, nil):
            layout.slots[a].rowIndex = to.row
            layout.slots[a].columnIndex = to.column
        default:
            break
        }
        layout.version = (layout.version ?? 0) + 1
        try db.transaction { try gridRepository.store(layout) }
        return try gridRepository.layout(id: layoutId) ?? layout
    }

    public func updateGridDimensions(categoryId: String, columns: Int, rows: Int, applyToAll: Bool) async throws -> [TouchGridLayout] {
        try requireAuth()
        let targets = try gridRepository.layouts().filter { applyToAll || $0.categoryId == categoryId }
        try db.transaction {
            for var layout in targets {
                layout.columnsCount = columns
                layout.rowsCount = rows
                layout.slots.removeAll { $0.rowIndex >= rows || $0.columnIndex >= columns }
                try gridRepository.store(layout)
            }
        }
        return try targets.compactMap { try gridRepository.layout(id: $0.id) }
    }
}
