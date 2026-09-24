import Foundation
import Observation

// MARK: - Carte

@MainActor
@Observable
public final class CatalogModel {
    public private(set) var categories: [Category] = []
    public private(set) var products: [Product] = []
    public var selectedCategoryID: String?
    public var searchText = ""
    public private(set) var isLoading = false
    public private(set) var hasLoaded = false

    /// Identifiant réservé à l'onglet « Favoris » (touches rapides).
    public static let favoritesID = "__favorites__"

    private let ctx: POSContext

    public init(context: POSContext) {
        self.ctx = context
    }

    public func load(force: Bool = false) async {
        guard force || !hasLoaded else { return }
        isLoading = true
        defer { isLoading = false }
        do {
            async let categoriesTask = ctx.api.categories()
            async let productsTask = ctx.api.products()
            let (loadedCategories, loadedProducts) = try await (categoriesTask, productsTask)
            categories = loadedCategories
                .filter(\.isActive)
                .sorted { ($0.displayOrder, $0.name) < ($1.displayOrder, $1.name) }
            products = loadedProducts.filter(\.isActive)
            hasLoaded = true
            if selectedCategoryID == nil {
                selectedCategoryID = hasFavorites ? Self.favoritesID : categories.first?.id
            }
        } catch {
            ctx.report(error)
        }
    }

    public var hasFavorites: Bool { products.contains(where: \.isQuickKey) }

    public func category(for product: Product) -> Category? {
        categories.first { $0.id == product.categoryId }
    }

    /// Articles affichés : la recherche porte sur toute la carte, sinon filtre par onglet.
    public var visibleProducts: [Product] {
        let query = Self.normalize(searchText)
        let filtered: [Product]
        if !query.isEmpty {
            filtered = products.filter { Self.normalize($0.name).contains(query) }
        } else if selectedCategoryID == Self.favoritesID {
            filtered = products.filter(\.isQuickKey)
        } else if let selectedCategoryID {
            filtered = products.filter { $0.categoryId == selectedCategoryID }
        } else {
            filtered = products
        }
        return filtered.sorted { ($0.displayOrder, $0.name) < ($1.displayOrder, $1.name) }
    }

    static func normalize(_ text: String) -> String {
        text.folding(options: [.caseInsensitive, .diacriticInsensitive], locale: Locale(identifier: "fr_FR"))
            .trimmingCharacters(in: .whitespaces)
    }
}

// MARK: - Plan de salle

@MainActor
@Observable
public final class FloorPlanModel {
    public enum Filter: String, CaseIterable, Identifiable, Sendable {
        case all, free, occupied
        public var id: String { rawValue }
        public var label: String {
            switch self {
            case .all: "Toutes"
            case .free: "Libres"
            case .occupied: "Occupées"
            }
        }
    }

    public private(set) var tables: [DiningTable] = []
    public var filter: Filter = .all
    public private(set) var isLoading = false
    public private(set) var lastRefresh: Date?

    private let ctx: POSContext

    public init(context: POSContext) {
        self.ctx = context
    }

    public func load() async {
        isLoading = true
        defer { isLoading = false }
        do {
            tables = try await ctx.api.tables()
                .filter { !$0.isCounter }
                .sorted { $0.tableNumber.compare($1.tableNumber, options: [.numeric, .caseInsensitive]) == .orderedAscending }
            lastRefresh = Date()
        } catch {
            ctx.report(error)
        }
    }

    public var visibleTables: [DiningTable] {
        switch filter {
        case .all: tables
        case .free: tables.filter { $0.status == .free }
        case .occupied: tables.filter { $0.status != .free }
        }
    }

    public var freeCount: Int { tables.filter { $0.status == .free }.count }
    public var occupiedCount: Int { tables.count - freeCount }
    public var coversInRoom: Int { tables.filter { $0.status != .free }.reduce(0) { $0 + $1.coversCount } }
    public var openAmount: Money { tables.reduce(.zero) { $0 + $1.activeOrderTotalTtc } }

    /// Ouvre une table libre pour `covers` couverts. Renvoie `true` en cas de succès.
    public func open(_ table: DiningTable, covers: Int) async -> Bool {
        guard let op = ctx.currentOperator else { return false }
        do {
            let opened = try await ctx.api.openTable(table.tableNumber, covers: max(1, covers), operator: op)
            if let index = tables.firstIndex(where: { $0.tableNumber == opened.tableNumber }) {
                tables[index] = opened
            }
            return true
        } catch {
            ctx.report(error)
            return false
        }
    }
}

// MARK: - Cuisine

@MainActor
@Observable
public final class KitchenModel {
    public private(set) var tickets: [KitchenTicket] = []
    public var stationFilter: String?
    public private(set) var isLoading = false
    /// Délai (minutes) au-delà duquel un bon est signalé en retard.
    public var lateThresholdMinutes = 15

    private let ctx: POSContext

    public init(context: POSContext) {
        self.ctx = context
    }

    public func load() async {
        isLoading = true
        defer { isLoading = false }
        do {
            tickets = try await ctx.api.kitchenTickets()
        } catch {
            ctx.report(error)
        }
    }

    public var stations: [String] {
        Array(Set(tickets.map(\.stationId))).sorted()
    }

    /// Bons d'une colonne, du plus ancien au plus récent.
    public func tickets(in status: TicketStatus) -> [KitchenTicket] {
        tickets
            .filter { $0.status == status && (stationFilter == nil || $0.stationId == stationFilter) }
            .sorted { ($0.dispatchedAtUtc ?? .distantPast) < ($1.dispatchedAtUtc ?? .distantPast) }
    }

    public func isLate(_ ticket: KitchenTicket, now: Date = Date()) -> Bool {
        ticket.status != .ready && ticket.elapsedMinutes(now: now) >= lateThresholdMinutes
    }

    /// Fait avancer un bon (À préparer → En préparation → Prêt → Servi), avec mise à jour immédiate.
    public func bump(_ ticket: KitchenTicket) async {
        guard let index = tickets.firstIndex(where: { $0.id == ticket.id }) else { return }
        let previous = tickets[index]
        tickets[index].status = previous.status.next
        do {
            let updated = try await ctx.api.bumpTicket(id: ticket.id)
            if let current = tickets.firstIndex(where: { $0.id == ticket.id }) {
                tickets[current].status = updated.status
            }
        } catch {
            if let current = tickets.firstIndex(where: { $0.id == ticket.id }) {
                tickets[current] = previous
            }
            ctx.report(error)
        }
    }
}

// MARK: - Fiscal

@MainActor
@Observable
public final class FiscalModel {
    public private(set) var xReport: FiscalReport?
    public private(set) var lastClosure: FiscalReport?
    public private(set) var isLoading = false
    public private(set) var isClosing = false

    private let ctx: POSContext

    public init(context: POSContext) {
        self.ctx = context
    }

    /// Les rapports fiscaux sont réservés aux responsables (politique `RequireManagerOrAdmin`).
    public var isAllowed: Bool { ctx.currentOperator?.role.isManager ?? false }

    public func load() async {
        guard isAllowed else { return }
        isLoading = true
        defer { isLoading = false }
        do {
            async let x = ctx.api.xReport(terminalId: ctx.terminalId)
            async let z = ctx.api.latestClosure(terminalId: ctx.terminalId)
            (xReport, lastClosure) = try await (x, z)
        } catch {
            ctx.report(error)
        }
    }

    /// Clôture journalière Z : scelle définitivement les ventes de la période (irréversible).
    public func closeDay() async -> Bool {
        guard isAllowed, let manager = ctx.currentOperator else {
            ctx.notify("Clôture Z réservée aux responsables.", style: .warning)
            return false
        }
        isClosing = true
        defer { isClosing = false }
        do {
            lastClosure = try await ctx.api.zClosure(terminalId: ctx.terminalId, manager: manager)
            xReport = try await ctx.api.xReport(terminalId: ctx.terminalId)
            ctx.notify("Clôture Z n°\(lastClosure?.closureSequence ?? 0) scellée", style: .success)
            return true
        } catch {
            ctx.report(error)
            return false
        }
    }
}

// MARK: - Commandes en attente (comptoir)

@MainActor
@Observable
public final class HeldOrdersModel {
    public private(set) var orders: [HeldOrder] = []
    public private(set) var isLoading = false

    private let ctx: POSContext

    public init(context: POSContext) {
        self.ctx = context
    }

    public func load() async {
        isLoading = true
        defer { isLoading = false }
        do {
            orders = try await ctx.api.heldOrders(terminalId: ctx.terminalId)
                .sorted { ($0.heldAtUtc ?? .distantPast) < ($1.heldAtUtc ?? .distantPast) }
        } catch {
            ctx.report(error)
        }
    }

    /// Annulation protégée par PIN responsable (journalisée côté serveur, JET NF525).
    public func void(_ order: HeldOrder, supervisorPin: String, reason: String = "Annulation au comptoir (iPad)") async -> Bool {
        do {
            try await ctx.api.voidHeldOrder(holdId: order.holdId, supervisorPin: supervisorPin, reason: reason, terminalId: ctx.terminalId)
            orders.removeAll { $0.holdId == order.holdId }
            ctx.notify("Commande en attente annulée", style: .success)
            return true
        } catch {
            ctx.report(error)
            return false
        }
    }

    public func remove(_ holdId: UUID) {
        orders.removeAll { $0.holdId == holdId }
    }
}
