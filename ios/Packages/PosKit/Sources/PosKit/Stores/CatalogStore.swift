import Foundation
import Observation

/// Case affichée dans la grille de vente.
public struct GridCell: Identifiable, Hashable, Sendable {
    public let position: GridPosition
    public let product: Product?
    public let label: String?
    public let colorHex: String?

    public var id: GridPosition { position }
    public var isEmpty: Bool { product == nil }
}

/// Catalogue + grilles tactiles paginées de l'écran de vente.
@MainActor @Observable
public final class CatalogStore {
    public static let allCategoryId = "ALL"

    public private(set) var categories: [MenuCategory] = []
    public private(set) var products: [Product] = []
    public private(set) var isLoaded = false
    public var selectedCategoryId: String = CatalogStore.allCategoryId
    public private(set) var pageIndex = 0
    private var layouts: [String: TouchGridLayout] = [:]
    private var missingLayouts: Set<String> = []

    private let api: PosAPI
    private let notifier: Notifier

    public init(api: PosAPI, notifier: Notifier) {
        self.api = api
        self.notifier = notifier
    }

    public func load() async {
        do {
            async let cats = api.categories()
            async let prods = api.products()
            categories = try await cats.sorted { ($0.displayOrder ?? 0) < ($1.displayOrder ?? 0) }
            products = try await prods
            isLoaded = true
            if selectedCategoryId != Self.allCategoryId, !categories.contains(where: { $0.id == selectedCategoryId }) {
                selectedCategoryId = Self.allCategoryId
            }
            layouts.removeAll()
            missingLayouts.removeAll()
            await loadCurrentLayout()
        } catch {
            notifier.error("Erreur de chargement du catalogue")
        }
    }

    public func product(id: UUID?) -> Product? {
        guard let id else { return nil }
        return products.first { $0.id == id }
    }

    public var quickKeys: [Product] { products.filter(\.isQuickKey) }

    public func products(in categoryId: String) -> [Product] {
        categoryId == Self.allCategoryId ? products : products.filter { $0.categoryId == categoryId }
    }

    // MARK: Grille paginée

    private func key(_ category: String, _ page: Int) -> String { "\(category)#\(page)" }

    public var currentLayout: TouchGridLayout? { layouts[key(selectedCategoryId, pageIndex)] }

    public func selectCategory(_ id: String) async {
        selectedCategoryId = id
        pageIndex = 0
        await loadCurrentLayout()
    }

    public var totalPages: Int {
        if let layout = currentLayout, !layout.slots.isEmpty { return max(1, layout.totalPages) }
        let count = products(in: selectedCategoryId).count
        return max(1, Int(ceil(Double(count) / Double(Self.fallbackColumns * Self.fallbackRows))))
    }

    public func goToPage(_ page: Int) async {
        let clamped = min(max(0, page), totalPages - 1)
        guard clamped != pageIndex else { return }
        pageIndex = clamped
        await loadCurrentLayout()
    }

    public func nextPage() async { await goToPage(pageIndex + 1) }
    public func previousPage() async { await goToPage(pageIndex - 1) }

    public func loadCurrentLayout() async {
        let k = key(selectedCategoryId, pageIndex)
        guard layouts[k] == nil, !missingLayouts.contains(k) else { return }
        do {
            layouts[k] = try await api.gridLayout(categoryId: selectedCategoryId, page: pageIndex)
        } catch {
            missingLayouts.insert(k)
        }
    }

    /// Mise à jour temps réel (SignalR) ou après édition dans le back-office.
    public func apply(layout: TouchGridLayout) {
        layouts[key(layout.categoryId, layout.pageIndex)] = layout
        missingLayouts.remove(key(layout.categoryId, layout.pageIndex))
    }

    public func invalidateLayouts() {
        layouts.removeAll()
        missingLayouts.removeAll()
    }

    static let fallbackColumns = 4
    static let fallbackRows = 4

    public var columns: Int {
        if let layout = currentLayout, !layout.slots.isEmpty { return max(1, layout.columnsCount) }
        return Self.fallbackColumns
    }

    public var rows: Int {
        if let layout = currentLayout, !layout.slots.isEmpty { return max(1, layout.rowsCount) }
        return Self.fallbackRows
    }

    /// Cases de la page courante : grille configurée si elle existe, sinon remplissage automatique.
    public var cells: [GridCell] {
        if let layout = currentLayout, !layout.slots.isEmpty {
            return layout.positions.map { position in
                guard let slot = layout.slot(at: position), slot.isDisabled != true,
                      let product = product(id: slot.productId) else {
                    return GridCell(position: position, product: nil, label: nil, colorHex: nil)
                }
                return GridCell(position: position, product: product, label: slot.customLabel ?? product.name, colorHex: slot.customColorHex ?? product.colorHex)
            }
        }
        let perPage = Self.fallbackColumns * Self.fallbackRows
        let pageProducts = Array(products(in: selectedCategoryId).dropFirst(pageIndex * perPage).prefix(perPage))
        return (0..<perPage).map { index in
            let position = GridPosition(row: index / Self.fallbackColumns, column: index % Self.fallbackColumns)
            guard let product = pageProducts[safe: index] else {
                return GridCell(position: position, product: nil, label: nil, colorHex: nil)
            }
            return GridCell(position: position, product: product, label: product.name, colorHex: product.colorHex)
        }
    }
}

/// État Happy Hour (bandeau, compte à rebours, grille tarifaire, dérogations superviseur).
@MainActor @Observable
public final class HappyHourStore {
    public private(set) var status: HappyHourStatus = .inactive
    public private(set) var prices: [UUID: HappyHourPrice] = [:]
    /// Secondes restantes, décrémentées par `tick()`.
    public private(set) var remainingSeconds = 0

    private let api: PosAPI
    private let notifier: Notifier
    private let terminalId: @MainActor () -> String

    public init(api: PosAPI, notifier: Notifier, terminalId: @escaping @MainActor () -> String) {
        self.api = api
        self.notifier = notifier
        self.terminalId = terminalId
    }

    public var isActive: Bool { status.isActive }

    public func refresh() async {
        do {
            apply(try await api.happyHourStatus(terminalId: terminalId()))
            if status.isActive {
                let table = try await api.happyHourPricing(terminalId: terminalId())
                prices = Dictionary(table.items.map { ($0.productId, $0) }, uniquingKeysWith: { first, _ in first })
            } else {
                prices = [:]
            }
        } catch {
            // Happy Hour non bloquant : on garde l'état précédent.
        }
    }

    public func apply(_ newStatus: HappyHourStatus) {
        status = newStatus
        remainingSeconds = newStatus.isActive ? max(0, (newStatus.currentWindow?.remainingMinutes ?? 0) * 60) : 0
        if !newStatus.isActive { prices = [:] }
    }

    /// À appeler chaque seconde par la vue ; renvoie `true` quand la fenêtre expire.
    @discardableResult
    public func tick() -> Bool {
        guard status.isActive, remainingSeconds > 0 else { return false }
        remainingSeconds -= 1
        return remainingSeconds == 0
    }

    public var countdownText: String {
        String(format: "%02d:%02d", remainingSeconds / 60, remainingSeconds % 60)
    }

    public var bannerTitle: String {
        let name = (status.activeScheduleName ?? "Happy Hour").uppercased()
        return status.isOverride ? "Dérogation : \(name)" : "Happy Hour : \(name)"
    }

    /// Prix réduit applicable, `nil` si l'article est au tarif normal.
    public func discountedPrice(for product: Product, destination: OrderDestination) -> HappyHourPrice? {
        guard status.isActive else { return nil }
        if destination == .takeaway && !status.appliesToTakeaway { return nil }
        guard let price = prices[product.id], price.happyHourPrice < price.standardPrice else { return nil }
        return price
    }

    public func displayPrice(for product: Product) -> HappyHourPrice? {
        guard status.isActive, let price = prices[product.id], price.happyHourPrice < price.standardPrice else { return nil }
        return price
    }

    public func activateOverride(pin: String, minutes: Int, reason: String) async -> Bool {
        guard !pin.isEmpty else { notifier.warning("Saisissez le code PIN superviseur"); return false }
        do {
            let result = try await api.activateHappyHourOverride(terminalId: terminalId(), pin: pin, minutes: minutes, reason: reason.isEmpty ? "Dérogation responsable" : reason)
            notifier.success(result.message ?? "Happy Hour prolongé de \(minutes) min")
            await refresh()
            return true
        } catch {
            notifier.error(error)
            return false
        }
    }

    public func stopOverride(pin: String, reason: String) async -> Bool {
        guard !pin.isEmpty else { notifier.warning("Saisissez le code PIN superviseur"); return false }
        do {
            _ = try await api.stopHappyHourOverride(terminalId: terminalId(), pin: pin, reason: reason.isEmpty ? "Arrêt anticipé" : reason)
            notifier.info("Happy Hour arrêté")
            await refresh()
            return true
        } catch {
            notifier.error(error)
            return false
        }
    }
}
