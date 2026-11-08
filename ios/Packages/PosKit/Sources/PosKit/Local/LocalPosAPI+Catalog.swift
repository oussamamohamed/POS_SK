import Foundation

extension LocalPosAPI {
    public func categories() async throws -> [MenuCategory] { try catalogRepository.activeCategories() }
    /// Le coût de revient n'est servi qu'aux responsables (comme `GET /api/catalog/products` du .NET).
    public func products() async throws -> [Product] {
        let isManager = (try? requireAuth())?.role.isManager == true
        return try catalogRepository.activeProducts().map { product in
            var p = product
            if !isManager { p.cost = nil }
            return p
        }
    }

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

    static let negativeProductPrice = APIError.server(status: 400, message: "Le prix d'un article ne peut pas être négatif.")
    static let negativeProductCost = APIError.server(status: 400, message: "Le coût de revient d'un article ne peut pas être négatif.")

    public func createProduct(_ d: ProductDraft) async throws {
        try requireManager()
        guard d.price >= .zero else { throw Self.negativeProductPrice }
        guard d.cost.map({ $0 >= .zero }) ?? true else { throw Self.negativeProductCost }
        try catalogRepository.insert(Product(
            name: d.name, categoryId: d.categoryId, description: d.description, price: d.price, taxRatePercent: d.taxRatePercent,
            colorHex: d.colorHex, displayOrder: d.displayOrder, isQuickKey: d.isQuickKey, preparationStationId: d.stationId, cost: d.cost
        ))
    }

    public func updateProduct(id: UUID, _ d: ProductDraft) async throws {
        try requireManager()
        guard d.price >= .zero else { throw Self.negativeProductPrice }
        guard d.cost.map({ $0 >= .zero }) ?? true else { throw Self.negativeProductCost }
        guard try catalogRepository.update(productId: id, d) else { throw APIError.notFound(nil) }
    }

    public func archiveProduct(id: UUID) async throws {
        try requireManager()
        guard try catalogRepository.archive(productId: id) else { throw APIError.notFound(nil) }
    }

    public func setAvailability(productId: UUID, isAvailable: Bool) async throws {
        try requireAuth()
        guard try catalogRepository.setAvailability(productId: productId, isAvailable: isAvailable) else { throw APIError.notFound(nil) }
    }

    static let invalidAllergenMask = APIError.server(status: 400, message: "Liste d'allergènes invalide.")
    /// Même texte que `errors.product_unavailable` du .NET (français).
    static let productUnavailable = APIError.server(status: 409, message: "Produit en rupture : il ne peut pas être ajouté à la commande.")

    public func setAllergens(productId: UUID, declared: Bool, mask: Int, overridden: Bool) async throws {
        try requireManager()
        guard Allergens.isValid(mask: mask) else { throw Self.invalidAllergenMask }
        guard try catalogRepository.setAllergens(productId: productId, declared: declared, mask: mask, overridden: overridden) else { throw APIError.notFound(nil) }
    }
}
