import Foundation
@testable import PosKit

func localProduct(_ api: LocalPosAPI, _ name: String) async throws -> Product {
    guard let product = try await api.products().first(where: { $0.name == name }) else { throw APIError.notFound(name) }
    return product
}

/// Ligne de commande envoyée telle que le ferait le client (tarif et taxes de l'article).
func localInput(_ p: Product, quantity: Int = 1, course: CourseType = .direct, modifiers: [String] = [], extra: Money = .zero) -> OrderItemInput {
    OrderItemInput(
        productId: p.id, productName: p.name, quantity: quantity, unitPrice: p.price, taxRatePercent: p.taxRatePercent,
        preparationStationId: p.preparationStationId, modifiers: modifiers, course: course, modifiersPriceExtra: extra,
        taxRateTakeawayPercent: p.taxRateTakeawayPercent, isHappyHourApplied: false, originalUnitPrice: nil, appliedHappyHourScheduleId: nil
    )
}

/// Ouvre une table puis y ajoute les lignes données.
func seatTable(_ api: LocalPosAPI, _ number: String, covers: Int = 2, waiter: String = "Sophie", items: [OrderItemInput] = []) async throws {
    try await api.openTable(number: number, covers: covers, operatorId: nil, waiterName: waiter)
    if !items.isEmpty { _ = try await api.addItems(table: number, items: items) }
}
