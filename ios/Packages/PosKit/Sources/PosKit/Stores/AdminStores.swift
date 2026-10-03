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

    public func saveCategory(id: String?, name: String, colorHex: String, preparationStationId: String? = nil) async -> Bool {
        let trimmed = name.trimmingCharacters(in: .whitespaces)
        guard !trimmed.isEmpty else { notifier.warning(L10n.string("admin.category_name_required")); return false }
        do {
            if let id {
                let order = catalog.categories.first { $0.id == id }?.displayOrder ?? 0
                try await api.updateCategory(id: id, name: trimmed, colorHex: colorHex, displayOrder: order, preparationStationId: preparationStationId)
                notifier.success(String(format: L10n.string("admin.category_updated"), trimmed))
            } else {
                try await api.createCategory(name: trimmed, colorHex: colorHex, displayOrder: catalog.categories.count + 1)
                notifier.success(String(format: L10n.string("admin.category_created"), trimmed))
            }
            await catalog.load()
            return true
        } catch {
            notifier.error(error)
            return false
        }
    }

    public func saveProduct(id: UUID?, draft: ProductDraft) async -> Bool {
        guard draft.isValid else { notifier.warning(L10n.string("admin.product_fields_required")); return false }
        do {
            if let id {
                try await api.updateProduct(id: id, draft)
                notifier.success(String(format: L10n.string("admin.product_updated"), draft.name))
            } else {
                var d = draft
                d.displayOrder = catalog.products.count + 1
                try await api.createProduct(d)
                notifier.success(String(format: L10n.string("admin.product_created"), draft.name))
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
            notifier.info(String(format: L10n.string("admin.product_archived"), product.name))
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
        guard !trimmed.isEmpty else { notifier.warning(L10n.string("admin.name_required")); return false }
        if id == nil || !pin.isEmpty {
            guard Self.isValidPin(pin) else { notifier.warning(String(format: L10n.string("admin.pin_length"), SessionStore.pinLength)); return false }
        }
        do {
            if let id {
                try await api.updateStaff(id: id, name: trimmed, role: role, pin: pin.isEmpty ? nil : pin, isActive: isActive)
                notifier.success(String(format: L10n.string("admin.staff_updated"), trimmed))
            } else {
                try await api.createStaff(name: trimmed, role: role, pin: pin)
                notifier.success(String(format: L10n.string("admin.staff_created"), trimmed))
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
            notifier.info(String(format: L10n.string("admin.staff_deactivated"), member.name))
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
    public private(set) var statuses: [PrinterStatus] = []
    public private(set) var jobs: [UUID: [PrintJobInfo]] = [:]
    private let api: PosAPI
    private let notifier: Notifier

    public init(api: PosAPI, notifier: Notifier) {
        self.api = api
        self.notifier = notifier
    }

    public func load() async {
        do { printers = try await api.printers().sorted { $0.name < $1.name } } catch { notifier.error(error) }
    }

    public func loadStatuses() async {
        do { statuses = try await api.printerStatuses() } catch { notifier.error(error) }
    }

    public func loadJobs(printerId: UUID) async {
        do { jobs[printerId] = try await api.printJobs(printerId: printerId) } catch { notifier.error(error) }
    }

    public func retry(_ job: PrintJobInfo) async {
        await act(job) { try await self.api.retryPrintJob(id: job.id) }
    }

    public func cancel(_ job: PrintJobInfo) async {
        await act(job) { try await self.api.cancelPrintJob(id: job.id) }
    }

    private func act(_ job: PrintJobInfo, _ action: () async throws -> Void) async {
        do { try await action() } catch { notifier.error(error) }
        await loadJobs(printerId: job.printerId)
        await loadStatuses()
    }

    public static func isValidIPv4(_ ip: String) -> Bool {
        let parts = ip.split(separator: ".")
        return parts.count == 4 && parts.allSatisfy { UInt8($0) != nil }
    }

    public func save(_ printer: Printer, isNew: Bool) async -> Bool {
        guard !printer.name.trimmingCharacters(in: .whitespaces).isEmpty else { notifier.warning(L10n.string("admin.name_required")); return false }
        guard Self.isValidIPv4(printer.ipAddress) else { notifier.warning(L10n.string("admin.ip_invalid")); return false }
        do {
            try await api.savePrinter(printer, isNew: isNew)
            notifier.success(String(format: L10n.string("admin.printer_saved"), printer.name))
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
            notifier.success(product == nil ? L10n.string("admin.slot_cleared") : String(format: L10n.string("admin.slot_placed"), product!.name))
        }
    }

    public func clear(at position: GridPosition) async { await assign(product: nil, at: position) }

    public func move(from source: GridPosition, to target: GridPosition) async {
        guard source != target, let layout else { return }
        do {
            let swapped = try await api.swapGridSlots(layoutId: layout.id, from: source, to: target)
            self.layout = swapped
            catalog.apply(layout: swapped)
            notifier.success(L10n.string("admin.positions_swapped"))
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
            notifier.success(String(format: L10n.string("admin.grid_size_applied"), c, r))
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
            notifier.success(String(format: L10n.string("admin.page_added"), newPage + 1))
        }
    }

    /// Remplit la page avec les articles de la famille dans l'ordre du catalogue.
    public func resetWithCatalogOrder() async {
        let columns = layout?.columnsCount ?? 4, rows = layout?.rowsCount ?? 4
        let products = candidateProducts
        let items = (0..<(columns * rows)).map { index in
            UpdateGridSlotItem(rowIndex: index / columns, columnIndex: index % columns, productId: products[safe: index]?.id)
        }
        if await save(slots: items) { notifier.success(L10n.string("admin.grid_reset")) }
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
        guard !schedule.name.trimmingCharacters(in: .whitespaces).isEmpty else { notifier.warning(L10n.string("admin.schedule_name_required")); return false }
        guard !schedule.daysOfWeek.isEmpty else { notifier.warning(L10n.string("admin.schedule_days_required")); return false }
        do {
            let id = try await api.createHappyHourSchedule(schedule)
            notifier.success(String(format: L10n.string("admin.schedule_created"), schedule.name))
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
            notifier.info(String(format: L10n.string("admin.schedule_deleted"), schedule.name))
            if selectedScheduleId == id { selectedScheduleId = nil }
            await load()
            await happyHour.refresh()
        } catch {
            notifier.error(error)
        }
    }

    public func applyToProducts(_ ids: Set<UUID>, mode: HappyHourPricingMode, value: Decimal) async -> Bool {
        guard let scheduleId = selectedScheduleId else { notifier.warning(L10n.string("admin.schedule_select_first")); return false }
        guard !ids.isEmpty else { notifier.warning(L10n.string("admin.products_required")); return false }
        guard value > 0, mode == .fixedPrice || value <= 100 else { notifier.error(L10n.string("admin.price_or_discount_invalid")); return false }
        let request = BatchPriceRulesRequest(
            targetType: .product, targetIds: ids.map { $0.uuidString.lowercased() }.sorted(), pricingMode: mode,
            fixedPrice: mode == .fixedPrice ? Money(euros: value) : nil,
            discountPercent: mode == .percentageDiscount ? value : nil
        )
        return await apply(request, scheduleId: scheduleId)
    }

    public func applyToCategories(_ ids: Set<String>, percent: Decimal) async -> Bool {
        guard let scheduleId = selectedScheduleId else { notifier.warning(L10n.string("admin.schedule_select_first")); return false }
        guard !ids.isEmpty else { notifier.warning(L10n.string("admin.categories_required")); return false }
        guard percent > 0, percent <= 100 else { notifier.error(L10n.string("admin.discount_range")); return false }
        return await apply(BatchPriceRulesRequest(targetType: .category, targetIds: ids.sorted(), pricingMode: .percentageDiscount, fixedPrice: nil, discountPercent: percent), scheduleId: scheduleId)
    }

    private func apply(_ request: BatchPriceRulesRequest, scheduleId: UUID) async -> Bool {
        do {
            let count = try await api.applyHappyHourRules(scheduleId: scheduleId, request)
            notifier.success(String(format: L10n.string("admin.rules_applied"), count))
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
            notifier.info(String(format: L10n.string("admin.rules_deleted"), ids.count))
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
            notifier.success(String(format: L10n.string("admin.connection_established"), Int(latency * 1000)))
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
            notifier.success(L10n.string("admin.sync_success"))
            await refresh()
        } catch {
            notifier.error(error)
        }
    }
}

/// Back-office : réglages restaurant (langue des tickets).
@MainActor @Observable
public final class SettingsStore {
    public private(set) var settings: RestaurantSettings?

    private let api: PosAPI
    private let notifier: Notifier

    public init(api: PosAPI, notifier: Notifier) {
        self.api = api
        self.notifier = notifier
    }

    public func load() async {
        do { settings = try await api.settings() } catch { notifier.error(error) }
    }

    public func setReceiptLanguage(_ lang: String) async {
        do {
            var updated = settings ?? RestaurantSettings(receiptLanguage: lang)
            updated.receiptLanguage = lang
            settings = try await api.saveSettings(updated)
            notifier.success(L10n.string("admin.receipt_language_saved"))
        } catch {
            notifier.error(error)
        }
    }

    public func setKitchenTicketLanguage(_ lang: String) async {
        do {
            var updated = settings ?? RestaurantSettings(receiptLanguage: "en", kitchenTicketLanguage: lang)
            updated.kitchenTicketLanguage = lang
            settings = try await api.saveSettings(updated)
            notifier.success(L10n.string("admin.kitchen_language_saved"))
        } catch {
            notifier.error(error)
        }
    }

    public func updateEstablishment(
        companyName: String?,
        addressLines: String?,
        siret: String?,
        vatNumber: String?,
        certificateNumber: String?,
        fiscalYearStartMonth: Int?,
        fiscalYearStartDay: Int?
    ) async -> Bool {
        do {
            var updated = settings ?? RestaurantSettings(receiptLanguage: "fr")
            updated.companyName = companyName
            updated.addressLines = addressLines
            updated.siret = siret
            updated.vatNumber = vatNumber
            updated.certificateNumber = certificateNumber
            updated.fiscalYearStartMonth = fiscalYearStartMonth
            updated.fiscalYearStartDay = fiscalYearStartDay
            settings = try await api.saveSettings(updated)
            notifier.success(L10n.string("admin.settings_saved"))
            return true
        } catch {
            notifier.error(error)
            return false
        }
    }
}
