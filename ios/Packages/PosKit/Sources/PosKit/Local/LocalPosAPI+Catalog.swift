import Foundation

extension LocalPosAPI {
    public func categories() async throws -> [MenuCategory] { try catalogRepository.activeCategories() }
    public func products() async throws -> [Product] { try catalogRepository.activeProducts() }

    public func createCategory(name: String, colorHex: String, displayOrder: Int) async throws {
        try requireManager()
        try catalogRepository.insert(MenuCategory(id: "CAT_\(UUID().uuidString.prefix(8))", name: name, iconName: "utensils", colorHex: colorHex, displayOrder: displayOrder))
    }

    public func updateCategory(id: String, name: String, colorHex: String, displayOrder: Int, preparationStationId: String?) async throws {
        try requireManager()
        guard try catalogRepository.updateCategory(id: id, name: name, colorHex: colorHex, displayOrder: displayOrder, stationId: preparationStationId) else {
            throw APIError.notFound(nil)
        }
    }

    public func createProduct(_ d: ProductDraft) async throws {
        try requireManager()
        try catalogRepository.insert(Product(
            name: d.name, categoryId: d.categoryId, description: d.description, price: d.price, taxRatePercent: d.taxRatePercent,
            colorHex: d.colorHex, displayOrder: d.displayOrder, isQuickKey: d.isQuickKey, preparationStationId: d.stationId
        ))
    }

    public func updateProduct(id: UUID, _ d: ProductDraft) async throws {
        try requireManager()
        guard try catalogRepository.update(productId: id, d) else { throw APIError.notFound(nil) }
    }

    public func archiveProduct(id: UUID) async throws {
        try requireManager()
        guard try catalogRepository.archive(productId: id) else { throw APIError.notFound(nil) }
    }
}
