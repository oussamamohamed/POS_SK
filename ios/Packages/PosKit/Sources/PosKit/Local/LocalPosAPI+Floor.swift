import Foundation

extension LocalPosAPI {
    public func tables() async throws -> [DiningTable] {
        try floorRepository.tables().map { table in
            var withTotal = table
            if let order = try activeOrder(on: table) { withTotal.activeOrderTotalTtc = order.totalTtcAmount ?? .zero }
            return withTotal
        }
    }

    public func createTable(number: String, capacity: Int) async throws {
        try requireAuth()
        guard try !floorRepository.tableExists(number) else { throw APIError.server(status: 400, message: "Table existante") }
        try floorRepository.insert(DiningTable(tableNumber: number, capacity: capacity))
    }

    public func settings() async throws -> RestaurantSettings {
        try requireAuth()
        return try floorRepository.settings()
    }

    public func saveSettings(_ s: RestaurantSettings) async throws -> RestaurantSettings {
        try requireManager()
        guard ["en", "fr", "ar"].contains(s.receiptLanguage), ["en", "fr", "ar"].contains(s.kitchenTicketLanguage) else {
            throw APIError.server(status: 400, message: "Langue non prise en charge.")
        }
        return try floorRepository.save(s)
    }
}
