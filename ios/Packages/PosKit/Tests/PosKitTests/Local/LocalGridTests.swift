import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : grille tactile")
struct LocalGridTests {
    @Test func firstReadGeneratesA4x4GridFromTheCategory() async throws {
        let api = try await makeLocalAPI()
        let layout = try await api.gridLayout(categoryId: "CAT_DRINKS", page: 0)
        #expect(layout.columnsCount == 4 && layout.rowsCount == 4 && layout.totalPages == 1)
        #expect(layout.slots.count == 16)
        let names = layout.slots.compactMap { $0.product?.name }
        #expect(names == ["Bière Artisanale IPA 33cl", "Verre Bordeaux AOP 12cl", "Eau Pétillante 50cl"])
        // Relecture : même grille, pas de régénération.
        #expect(try await api.gridLayout(categoryId: "CAT_DRINKS", page: 0).id == layout.id)
    }

    @Test func unknownPageIsNotFound() async throws {
        let api = try await makeLocalAPI()
        await #expect(throws: APIError.notFound("Aucune grille")) { try await api.gridLayout(categoryId: "CAT_DRINKS", page: 3) }
    }

    @Test func saveCreatesAnotherPageAndBumpsVersion() async throws {
        let api = try await makeLocalAPI()
        let product = try #require(try await api.products().first)
        let request = UpdateGridLayoutRequest(categoryId: "CAT_STARTERS", columnsCount: 3, rowsCount: 2, pageIndex: 1,
                                              slots: [UpdateGridSlotItem(rowIndex: 1, columnIndex: 2, productId: product.id, customLabel: "Star")])
        let page = try await api.saveGridLayout(request)
        #expect(page.pageIndex == 1 && page.totalPages == 2 && page.version == 1)
        #expect(page.slots.count == 1 && page.slots[0].customLabel == "Star" && page.slots[0].slotIndex == 5)
        #expect(page.slots[0].product?.id == product.id)
        let again = try await api.saveGridLayout(request)
        #expect(again.id == page.id && again.version == 2)
    }

    @Test func failedSaveKeepsThePreviousGrid() async throws {
        let api = try await makeLocalAPI()
        let before = try await api.gridLayout(categoryId: "CAT_DRINKS", page: 0)
        let bad = UpdateGridLayoutRequest(categoryId: "CAT_DRINKS", columnsCount: 4, rowsCount: 4, pageIndex: 0,
                                          slots: [UpdateGridSlotItem(rowIndex: 0, columnIndex: 0, productId: UUID())])
        await #expect(throws: SQLiteError.self) { try await api.saveGridLayout(bad) }
        let after = try await api.gridLayout(categoryId: "CAT_DRINKS", page: 0)
        #expect(after.version == before.version && after.slots.map(\.productId) == before.slots.map(\.productId))
    }

    @Test func swapMovesAndExchangesSlots() async throws {
        let api = try await makeLocalAPI()
        let layout = try await api.gridLayout(categoryId: "CAT_DRINKS", page: 0)
        let first = layout.slots[0].productId, second = layout.slots[1].productId
        let swapped = try await api.swapGridSlots(layoutId: layout.id, from: GridPosition(row: 0, column: 0), to: GridPosition(row: 0, column: 1))
        #expect(swapped.slot(at: GridPosition(row: 0, column: 0))?.productId == second)
        #expect(swapped.slot(at: GridPosition(row: 0, column: 1))?.productId == first)
        #expect(swapped.version == 2)
        await #expect(throws: APIError.notFound("Grille introuvable")) {
            try await api.swapGridSlots(layoutId: UUID(), from: GridPosition(row: 0, column: 0), to: GridPosition(row: 0, column: 1))
        }
    }

    @Test func shrinkingDimensionsDropsOutOfRangeSlots() async throws {
        let api = try await makeLocalAPI()
        _ = try await api.gridLayout(categoryId: "CAT_DRINKS", page: 0)
        _ = try await api.gridLayout(categoryId: "CAT_MAINS", page: 0)
        let updated = try await api.updateGridDimensions(categoryId: "CAT_DRINKS", columns: 2, rows: 2, applyToAll: false)
        #expect(updated.count == 1 && updated[0].columnsCount == 2 && updated[0].slots.count == 4)
        #expect(try await api.gridLayout(categoryId: "CAT_MAINS", page: 0).columnsCount == 4)
        #expect(try await api.updateGridDimensions(categoryId: "x", columns: 3, rows: 3, applyToAll: true).count == 2)
    }

    @Test func archivedProductStaysInExistingSlots() async throws {
        let api = try await makeLocalAPI()
        let layout = try await api.gridLayout(categoryId: "CAT_DRINKS", page: 0)
        let productId = try #require(layout.slots[0].productId)
        try await api.archiveProduct(id: productId)
        // Archivage logique : l'article reste référencé, comme sur le serveur.
        #expect(try await api.gridLayout(categoryId: "CAT_DRINKS", page: 0).slots[0].productId == productId)
    }
}
