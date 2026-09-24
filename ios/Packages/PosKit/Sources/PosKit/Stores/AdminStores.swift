import Foundation
import Observation

/// Back-office : familles et articles.
@MainActor @Observable
public final class CatalogAdminStore {
    private let api: PosAPI
    private let notifier: Notifier
    private let catalog: CatalogStore

    public init(api: PosAPI, notifier: Notifier, catalog: CatalogStore) {
        self.api = api
        self.notifier = notifier
        self.catalog = catalog
    }

    public func saveCategory(id: String?, name: String, colorHex: String) async -> Bool {
        let trimmed = name.trimmingCharacters(in: .whitespaces)
        guard !trimmed.isEmpty else { notifier.warning("Le nom de la famille est requis"); return false }
        do {
            if let id {
                let order = catalog.categories.first { $0.id == id }?.displayOrder ?? 0
                try await api.updateCategory(id: id, name: trimmed, colorHex: colorHex, displayOrder: order)
                notifier.success("Famille « \(trimmed) » mise à jour")
            } else {
                try await api.createCategory(name: trimmed, colorHex: colorHex, displayOrder: catalog.categories.count + 1)
                notifier.success("Famille « \(trimmed) » créée")
            }
            await catalog.load()
            return true
        } catch {
            notifier.error(error)
            return false
        }
    }

    public func saveProduct(id: UUID?, draft: ProductDraft) async -> Bool {
        guard draft.isValid else { notifier.warning("Nom, famille et prix sont obligatoires"); return false }
        do {
            if let id {
                try await api.updateProduct(id: id, draft)
                notifier.success("Article « \(draft.name) » mis à jour")
            } else {
                var d = draft
                d.displayOrder = catalog.products.count + 1
                try await api.createProduct(d)
                notifier.success("Article « \(draft.name) » créé")
            }
            await catalog.load()
            return true
        } catch {
            notifier.error(error)
            return false
        }
    }

    public func archive(_ product: Product) async {
        do {
            try await api.archiveProduct(id: product.id)
            notifier.info("Article « \(product.name) » désactivé")
            await catalog.load()
        } catch {
            notifier.error(error)
        }
    }
}

/// Back-office : équipe.
@MainActor @Observable
public final class StaffStore {
    public private(set) var members: [StaffMember] = []
    private let api: PosAPI
    private let notifier: Notifier

    public init(api: PosAPI, notifier: Notifier) {
        self.api = api
        self.notifier = notifier
    }

    public func load() async {
        do { members = try await api.staff().sorted { $0.name < $1.name } } catch { notifier.error(error) }
    }

    public static func isValidPin(_ pin: String) -> Bool {
        pin.count == SessionStore.pinLength && pin.allSatisfy(\.isNumber)
    }

    public func save(id: UUID?, name: String, role: UserRole, pin: String, isActive: Bool = true) async -> Bool {
        let trimmed = name.trimmingCharacters(in: .whitespaces)
        guard !trimmed.isEmpty else { notifier.warning("Le nom est requis"); return false }
        if id == nil || !pin.isEmpty {
            guard Self.isValidPin(pin) else { notifier.warning("Le PIN doit comporter \(SessionStore.pinLength) chiffres"); return false }
        }
        do {
            if let id {
                try await api.updateStaff(id: id, name: trimmed, role: role, pin: pin.isEmpty ? nil : pin, isActive: isActive)
                notifier.success("Employé « \(trimmed) » mis à jour")
            } else {
                try await api.createStaff(name: trimmed, role: role, pin: pin)
                notifier.success("Employé « \(trimmed) » créé")
            }
            await load()
            return true
        } catch {
            notifier.error(error)
            return false
        }
    }

    public func deactivate(_ member: StaffMember) async {
        do {
            try await api.deactivateStaff(id: member.id)
            notifier.info("Employé « \(member.name) » désactivé")
            await load()
        } catch {
            notifier.error(error)
        }
    }
}

/// Back-office : imprimantes ESC/POS.
@MainActor @Observable
public final class PrinterStore {
    public private(set) var printers: [Printer] = []
    private let api: PosAPI
    private let notifier: Notifier

    public init(api: PosAPI, notifier: Notifier) {
        self.api = api
        self.notifier = notifier
    }

    public func load() async {
        do { printers = try await api.printers().sorted { $0.name < $1.name } } catch { notifier.error(error) }
    }

    public static func isValidIPv4(_ ip: String) -> Bool {
        let parts = ip.split(separator: ".")
        return parts.count == 4 && parts.allSatisfy { UInt8($0) != nil }
    }

    public func save(_ printer: Printer, isNew: Bool) async -> Bool {
        guard !printer.name.trimmingCharacters(in: .whitespaces).isEmpty else { notifier.warning("Le nom est requis"); return false }
        guard Self.isValidIPv4(printer.ipAddress) else { notifier.warning("Adresse IP invalide"); return false }
        do {
            try await api.savePrinter(printer, isNew: isNew)
            notifier.success("Imprimante « \(printer.name) » enregistrée")
            await load()
            return true
        } catch {
            notifier.error(error)
            return false
        }
    }

    public func setActive(_ printer: Printer, _ active: Bool) async {
        var p = printer
        p.isActive = active
        _ = await save(p, isNew: false)
    }

    public func test(_ printer: Printer) async {
        do {
            let result = try await api.testPrinter(id: printer.id)
            result.success ? notifier.success(result.message) : notifier.error(result.message)
        } catch {
            notifier.error(error)
        }
    }
}

/// Back-office : éditeur de grille tactile (placement, permutation, format, pages).
@MainActor @Observable
public final class GridEditorStore {
    public private(set) var layout: TouchGridLayout?
    public private(set) var categoryId: String = CatalogStore.allCategoryId
    public private(set) var pageIndex = 0

    public static let presets: [(columns: Int, rows: Int)] = [(3, 3), (4, 4), (5, 4), (5, 5), (6, 4), (6, 5)]

    private let api: PosAPI
    private let notifier: Notifier
    private let catalog: CatalogStore

    public init(api: PosAPI, notifier: Notifier, catalog: CatalogStore) {
        self.api = api
        self.notifier = notifier
        self.catalog = catalog
    }

    public var candidateProducts: [Product] { catalog.products(in: categoryId) }

    public var placedProductIds: Set<UUID> { Set(layout?.slots.compactMap(\.productId) ?? []) }

    public func select(category: String, page: Int = 0) async {
        categoryId = category
        pageIndex = page
        await reload()
    }

    public func reload() async {
        do {
            layout = try await api.gridLayout(categoryId: categoryId, page: pageIndex)
        } catch {
            // Aucune grille : on repart d'une matrice vide 4×4 qui sera créée à la première sauvegarde.
            layout = TouchGridLayout(categoryId: categoryId, pageIndex: pageIndex, totalPages: max(1, pageIndex + 1), slots: [])
        }
    }

    private func save(slots: [UpdateGridSlotItem], columns: Int? = nil, rows: Int? = nil, page: Int? = nil) async -> Bool {
        guard let layout else { return false }
        do {
            let saved = try await api.saveGridLayout(UpdateGridLayoutRequest(
                categoryId: categoryId, columnsCount: columns ?? layout.columnsCount, rowsCount: rows ?? layout.rowsCount,
                pageIndex: page ?? pageIndex, slots: slots
            ))
            if (page ?? pageIndex) == pageIndex { self.layout = saved }
            catalog.apply(layout: saved)
            return true
        } catch {
            notifier.error(error)
            return false
        }
    }

    private var currentSlotItems: [UpdateGridSlotItem] { layout?.slots.map(UpdateGridSlotItem.init) ?? [] }

    public func assign(product: Product?, at position: GridPosition, label: String? = nil, colorHex: String? = nil) async {
        var items = currentSlotItems.filter { !($0.rowIndex == position.row && $0.columnIndex == position.column) }
        items.append(UpdateGridSlotItem(rowIndex: position.row, columnIndex: position.column, productId: product?.id,
                                        customLabel: product == nil ? nil : (label?.isEmpty == false ? label : nil),
                                        customColorHex: product == nil ? nil : colorHex))
        if await save(slots: items) {
            notifier.success(product == nil ? "Emplacement libéré" : "« \(product!.name) » placé")
        }
    }

    public func clear(at position: GridPosition) async { await assign(product: nil, at: position) }

    public func move(from source: GridPosition, to target: GridPosition) async {
        guard source != target, let layout else { return }
        do {
            let swapped = try await api.swapGridSlots(layoutId: layout.id, from: source, to: target)
            self.layout = swapped
            catalog.apply(layout: swapped)
            notifier.success("Positions permutées")
        } catch {
            notifier.error(error)
        }
    }

    public func applyDimensions(columns: Int, rows: Int, applyToAll: Bool) async {
        let c = min(max(columns, 2), 8), r = min(max(rows, 2), 8)
        do {
            let updated = try await api.updateGridDimensions(categoryId: categoryId, columns: c, rows: r, applyToAll: applyToAll)
            updated.forEach(catalog.apply(layout:))
            if applyToAll { catalog.invalidateLayouts() }
            notifier.success("Format \(c) × \(r) appliqué")
            await reload()
        } catch {
            notifier.error(error)
        }
    }

    public func addPage() async {
        guard let layout else { return }
        let newPage = layout.totalPages
        let emptySlots = layout.positions.map { UpdateGridSlotItem(rowIndex: $0.row, columnIndex: $0.column, productId: nil) }
        if await save(slots: emptySlots, page: newPage) {
            pageIndex = newPage
            await reload()
            notifier.success("Page \(newPage + 1) ajoutée")
        }
    }

    /// Remplit la page avec les articles de la famille dans l'ordre du catalogue.
    public func resetWithCatalogOrder() async {
        let columns = layout?.columnsCount ?? 4, rows = layout?.rowsCount ?? 4
        let products = candidateProducts
        let items = (0..<(columns * rows)).map { index in
            UpdateGridSlotItem(rowIndex: index / columns, columnIndex: index % columns, productId: products[safe: index]?.id)
        }
        if await save(slots: items) { notifier.success("Grille réinitialisée") }
    }
}

/// Back-office : plages Happy Hour et règles tarifaires groupées.
@MainActor @Observable
public final class HappyHourAdminStore {
    public private(set) var schedules: [HappyHourSchedule] = []
    public var selectedScheduleId: UUID?

    private let api: PosAPI
    private let notifier: Notifier
    private let happyHour: HappyHourStore

    public init(api: PosAPI, notifier: Notifier, happyHour: HappyHourStore) {
        self.api = api
        self.notifier = notifier
        self.happyHour = happyHour
    }

    public var selectedSchedule: HappyHourSchedule? { schedules.first { $0.id == selectedScheduleId } }

    public func load() async {
        do {
            schedules = try await api.happyHourSchedules()
            if selectedScheduleId == nil || !schedules.contains(where: { $0.id == selectedScheduleId }) {
                selectedScheduleId = schedules.first?.id
            }
        } catch {
            notifier.error(error)
        }
    }

    public func create(_ schedule: HappyHourSchedule) async -> Bool {
        guard !schedule.name.trimmingCharacters(in: .whitespaces).isEmpty else { notifier.warning("Nom de plage requis"); return false }
        guard !schedule.daysOfWeek.isEmpty else { notifier.warning("Sélectionnez au moins un jour"); return false }
        do {
            let id = try await api.createHappyHourSchedule(schedule)
            notifier.success("Plage « \(schedule.name) » créée")
            await load()
            if let id { selectedScheduleId = id }
            await happyHour.refresh()
            return true
        } catch {
            notifier.error(error)
            return false
        }
    }

    public func delete(_ schedule: HappyHourSchedule) async {
        guard let id = schedule.id else { return }
        do {
            try await api.deleteHappyHourSchedule(id: id)
            notifier.info("Plage « \(schedule.name) » supprimée")
            if selectedScheduleId == id { selectedScheduleId = nil }
            await load()
            await happyHour.refresh()
        } catch {
            notifier.error(error)
        }
    }

    public func applyToProducts(_ ids: Set<UUID>, mode: HappyHourPricingMode, value: Decimal) async -> Bool {
        guard let scheduleId = selectedScheduleId else { notifier.warning("Choisissez d'abord une plage"); return false }
        guard !ids.isEmpty else { notifier.warning("Sélectionnez au moins un article"); return false }
        guard value > 0, mode == .fixedPrice || value <= 100 else { notifier.error("Tarif ou remise invalide"); return false }
        let request = BatchPriceRulesRequest(
            targetType: .product, targetIds: ids.map { $0.uuidString.lowercased() }.sorted(), pricingMode: mode,
            fixedPrice: mode == .fixedPrice ? Money(euros: value) : nil,
            discountPercent: mode == .percentageDiscount ? value : nil
        )
        return await apply(request, scheduleId: scheduleId)
    }

    public func applyToCategories(_ ids: Set<String>, percent: Decimal) async -> Bool {
        guard let scheduleId = selectedScheduleId else { notifier.warning("Choisissez d'abord une plage"); return false }
        guard !ids.isEmpty else { notifier.warning("Sélectionnez au moins une famille"); return false }
        guard percent > 0, percent <= 100 else { notifier.error("La remise doit être comprise entre 1 et 100 %"); return false }
        return await apply(BatchPriceRulesRequest(targetType: .category, targetIds: ids.sorted(), pricingMode: .percentageDiscount, fixedPrice: nil, discountPercent: percent), scheduleId: scheduleId)
    }

    private func apply(_ request: BatchPriceRulesRequest, scheduleId: UUID) async -> Bool {
        do {
            let count = try await api.applyHappyHourRules(scheduleId: scheduleId, request)
            notifier.success("\(count) règle(s) appliquée(s)")
            await load()
            await happyHour.refresh()
            return true
        } catch {
            notifier.error(error)
            return false
        }
    }

    public func deleteRules(_ ids: Set<UUID>) async {
        guard let scheduleId = selectedScheduleId, !ids.isEmpty else { return }
        do {
            try await api.deleteHappyHourRules(scheduleId: scheduleId, ruleIds: Array(ids))
            notifier.info("\(ids.count) règle(s) supprimée(s)")
            await load()
            await happyHour.refresh()
        } catch {
            notifier.error(error)
        }
    }
}

/// Back-office : tableau de bord financier.
@MainActor @Observable
public final class DashboardStore {
    public private(set) var dashboard: FinancialDashboard?
    public var range: DashboardRange = .today
    private let api: PosAPI
    private let notifier: Notifier

    public init(api: PosAPI, notifier: Notifier) {
        self.api = api
        self.notifier = notifier
    }

    public func load() async {
        let interval = range.interval()
        do { dashboard = try await api.dashboard(from: interval.from, to: interval.to) } catch { notifier.error(error) }
    }
}

/// Réglages réseau : serveur maître, test de connexion, file de synchronisation.
@MainActor @Observable
public final class NetworkStore {
    public private(set) var info: NetworkInfo?
    public private(set) var sync: SyncStatus?
    public private(set) var isOnline = false
    public private(set) var lastLatency: TimeInterval?

    private let api: PosAPI
    private let notifier: Notifier

    public init(api: PosAPI, notifier: Notifier) {
        self.api = api
        self.notifier = notifier
    }

    public func refresh() async {
        do {
            async let i = api.networkInfo()
            async let s = api.syncStatus()
            info = try await i
            sync = try await s
            isOnline = true
        } catch {
            isOnline = false
        }
    }

    @discardableResult
    public func testConnection() async -> Bool {
        do {
            let latency = try await api.health()
            lastLatency = latency
            isOnline = true
            notifier.success("Connexion au serveur établie (\(Int(latency * 1000)) ms)")
            return true
        } catch {
            isOnline = false
            notifier.error(error)
            return false
        }
    }

    public func forceSync() async {
        do {
            try await api.forceSync()
            notifier.success("Synchronisation réussie")
            await refresh()
        } catch {
            notifier.error(error)
        }
    }
}
