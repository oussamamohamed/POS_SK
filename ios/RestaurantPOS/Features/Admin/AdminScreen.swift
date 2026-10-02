import SwiftUI
import PosKit

/// Back-office (responsables) : navigation par sous-section.
struct AdminScreen: View {
    enum Section: String, CaseIterable, Identifiable {
        case dashboard, catalog, grid, staff, printers, happyHour, establishment, network
        var id: String { rawValue }

        var title: String {
            switch self {
            case .dashboard: String(localized: "admin.section_dashboard")
            case .catalog: String(localized: "admin.section_catalog")
            case .grid: String(localized: "admin.section_grid")
            case .staff: String(localized: "admin.section_staff")
            case .printers: String(localized: "admin.section_printers")
            case .happyHour: String(localized: "admin.section_happy_hour")
            case .establishment: String(localized: "admin.section_establishment")
            case .network: String(localized: "admin.section_network")
            }
        }

        var systemImage: String {
            switch self {
            case .dashboard: "chart.bar.xaxis"
            case .catalog: "menucard"
            case .grid: "square.grid.4x3.fill"
            case .staff: "person.3"
            case .printers: "printer"
            case .happyHour: "wineglass"
            case .establishment: "building.2"
            case .network: "network"
            }
        }
    }

    @State private var section: Section? = .dashboard

    var body: some View {
        HStack(spacing: 0) {
            List(Section.allCases, selection: $section) { item in
                Label(item.title, systemImage: item.systemImage)
                    .accessibilityElement(children: .combine)
                    .accessibilityIdentifier("admin.\(item.rawValue)")
                    .tag(item)
            }
            .listStyle(.sidebar)
            .frame(width: 240)
            Divider()
            Group {
                switch section ?? .dashboard {
                case .dashboard: DashboardView()
                case .catalog: CatalogAdminView()
                case .grid: GridEditorView()
                case .staff: StaffAdminView()
                case .printers: PrintersAdminView()
                case .happyHour: HappyHourAdminView()
                case .establishment: EstablishmentAdminView()
                case .network: NetworkSettingsView()
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }
}

// MARK: - Tableau de bord

struct DashboardView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        let store = model.dashboard
        ScrollView {
            VStack(alignment: .leading, spacing: 20) {
                HStack {
                    Picker("admin.period_label", selection: Binding(get: { store.range }, set: { store.range = $0; Task { await store.load() } })) {
                        ForEach(DashboardRange.allCases) { Text($0.label).tag($0) }
                    }
                    .pickerStyle(.segmented)
                    .frame(maxWidth: 480)
                    .accessibilityIdentifier("dashboard.range")
                    Spacer()
                    Button { Task { await store.load() } } label: { Label("common.refresh_label", systemImage: "arrow.clockwise") }.buttonStyle(.bordered)
                }
                if let data = store.dashboard {
                    LazyVGrid(columns: [GridItem(.adaptive(minimum: 220), spacing: 16)], spacing: 16) {
                        KPIView(title: String(localized: "admin.kpi_sales_ttc"), value: data.kpis.totalSalesTtc.formatted, subtitle: String(localized: "admin.kpi_sales_ht \(data.kpis.totalSalesHt.formatted)"), systemImage: "eurosign.circle", tint: .green)
                            .accessibilityIdentifier("kpi.sales")
                        KPIView(title: String(localized: "admin.kpi_average_order"), value: data.kpis.averageOrderTtc.formatted, subtitle: String(localized: "admin.kpi_orders_count \(data.kpis.totalOrdersCount)"), systemImage: "receipt", tint: .blue)
                        KPIView(title: String(localized: "admin.kpi_average_cover"), value: data.kpis.averageCoverTtc.formatted, subtitle: String(localized: "admin.kpi_covers_count \(data.kpis.totalCoversCount)"), systemImage: "person.2", tint: .orange)
                    }
                    HStack(alignment: .top, spacing: 16) {
                        Card {
                            VStack(alignment: .leading, spacing: 10) {
                                Text("admin.payment_methods_title").font(.headline)
                                if data.paymentMethods.isEmpty { Text("admin.no_payments").foregroundStyle(Theme.inkMuted) }
                                ForEach(data.paymentMethods) { p in
                                    ShareBar(label: String(localized: "admin.payment_method_row \(PaymentMethod.label(forServerName: p.methodName)) \(p.transactionsCount)"), value: p.totalAmount.formatted, fraction: p.percentageOfTotal / 100, tint: .blue)
                                }
                            }
                        }
                        Card {
                            VStack(alignment: .leading, spacing: 10) {
                                Text("admin.top_sales_title").font(.headline)
                                if data.topProducts.isEmpty { Text("admin.no_sales").foregroundStyle(Theme.inkMuted) }
                                ForEach(data.topProducts.prefix(8)) { p in
                                    ShareBar(label: "\(p.productName) ×\(p.quantitySold)", value: p.totalSalesTtc.formatted, fraction: p.percentageOfTotal / 100, tint: .green)
                                }
                            }
                        }
                    }
                    HStack(alignment: .top, spacing: 16) {
                        Card {
                            VStack(alignment: .leading, spacing: 10) {
                                Text("admin.services_title").font(.headline)
                                ForEach(data.services) { s in
                                    HStack {
                                        VStack(alignment: .leading) {
                                            Text(s.serviceName).font(.subheadline.weight(.semibold))
                                            Text("admin.service_summary \(s.ordersCount) \(s.coversCount)").font(.caption).foregroundStyle(Theme.inkMuted)
                                        }
                                        Spacer()
                                        Text(s.salesTtc.formatted).font(.headline.monospacedDigit()).environment(\.layoutDirection, .leftToRight)
                                    }
                                }
                            }
                        }
                        Card {
                            VStack(alignment: .leading, spacing: 10) {
                                Text("admin.staff_productivity_title").font(.headline)
                                if data.staffPerformance.isEmpty { Text("admin.no_activity").foregroundStyle(Theme.inkMuted) }
                                ForEach(data.staffPerformance) { s in
                                    HStack {
                                        VStack(alignment: .leading) {
                                            Text(s.serverName).font(.subheadline.weight(.semibold))
                                            Text("admin.staff_summary \(s.tablesServedCount) \(s.averageTableTtc.formatted)").font(.caption).foregroundStyle(Theme.inkMuted)
                                        }
                                        Spacer()
                                        Text(s.totalSalesTtc.formatted).font(.headline.monospacedDigit()).environment(\.layoutDirection, .leftToRight)
                                    }
                                }
                            }
                        }
                    }
                } else {
                    ProgressView().frame(maxWidth: .infinity).padding(60)
                }
            }
            .padding(24)
        }
        .task { await store.load() }
    }
}

struct ShareBar: View {
    let label: String
    let value: String
    let fraction: Double
    let tint: Color

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack {
                Text(label).font(.subheadline).lineLimit(1)
                Spacer()
                Text(value).font(.subheadline.monospacedDigit().weight(.semibold)).environment(\.layoutDirection, .leftToRight)
            }
            GeometryReader { proxy in
                Capsule().fill(Theme.raised)
                    .overlay(alignment: .leading) {
                        Capsule().fill(tint).frame(width: proxy.size.width * min(1, max(0, fraction)))
                    }
            }
            .frame(height: 6)
        }
    }
}

// MARK: - Catalogue

struct CatalogAdminView: View {
    @Environment(AppModel.self) private var model
    @State private var editingProduct: ProductEditor?
    @State private var editingCategory: CategoryEditor?
    @State private var filter: String = CatalogStore.allCategoryId
    @State private var search = ""

    struct ProductEditor: Identifiable { let id = UUID(); var productId: UUID?; var draft: ProductDraft }
    struct CategoryEditor: Identifiable { let id = UUID(); var categoryId: String?; var name: String; var color: Color; var station: String = "" }

    var body: some View {
        let catalog = model.catalog
        let products = catalog.products(in: filter).filter { search.isEmpty || $0.name.localizedCaseInsensitiveContains(search) }
        VStack(spacing: 0) {
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 8) {
                    ChipButton(title: String(localized: "admin.filter_all"), isSelected: filter == CatalogStore.allCategoryId) { filter = CatalogStore.allCategoryId }
                    ForEach(catalog.categories) { category in
                        ChipButton(title: category.name, isSelected: filter == category.id, tint: Color(hex: category.colorHex) ?? Theme.primary) { filter = category.id }
                            .contextMenu {
                                Button("admin.edit_category", systemImage: "pencil") {
                                    editingCategory = CategoryEditor(categoryId: category.id, name: category.name, color: Color(hex: category.colorHex) ?? .blue, station: category.preparationStationId ?? "")
                                }
                                .accessibilityIdentifier("category.edit")
                            }
                            .accessibilityIdentifier("category.chip.\(category.id)")
                    }
                    Button { editingCategory = CategoryEditor(categoryId: nil, name: "", color: .blue) } label: { Label("admin.category_label", systemImage: "plus") }
                        .buttonStyle(.bordered)
                        .accessibilityIdentifier("catalog.addCategory")
                }
                .padding(20)
            }
            List {
                ForEach(products) { product in
                    HStack(spacing: 12) {
                        RoundedRectangle(cornerRadius: 4).fill(Color(hex: product.colorHex) ?? Theme.primary).frame(width: 6, height: 40)
                        VStack(alignment: .leading, spacing: 2) {
                            HStack(spacing: 6) {
                                Text(product.name).font(.headline)
                                if product.isQuickKey { Image(systemName: "bolt.fill").foregroundStyle(Theme.warning).font(.caption) }
                                if product.hasModifiers { Badge(text: String(localized: "admin.options_count \(product.modifierGroups.count)"), color: .blue) }
                            }
                            Text("admin.station_vat \(product.station ?? "—") \(product.taxRatePercent.formatted())").font(.caption).foregroundStyle(Theme.inkMuted)
                        }
                        Spacer()
                        Text(product.price.formatted).font(.headline.monospacedDigit()).environment(\.layoutDirection, .leftToRight)
                    }
                    .contentShape(Rectangle())
                    .onTapGesture { editingProduct = ProductEditor(productId: product.id, draft: ProductDraft(product: product)) }
                    .swipeActions {
                        Button("admin.deactivate", role: .destructive) { Task { await model.catalogAdmin.archive(product) } }
                    }
                    .accessibilityIdentifier("catalog.product.\(product.name)")
                }
            }
            .listStyle(.insetGrouped)
            .searchable(text: $search, prompt: "admin.search_product_prompt")
        }
        .overlay(alignment: .bottomTrailing) {
            Button {
                editingProduct = ProductEditor(productId: nil, draft: ProductDraft(categoryId: filter == CatalogStore.allCategoryId ? (catalog.categories.first?.id ?? "") : filter))
            } label: {
                Label("admin.new_product", systemImage: "plus").font(.headline).padding(.horizontal, 20).frame(height: 54)
            }
            .buttonStyle(.borderedProminent)
            .clipShape(Capsule())
            .padding(24)
            .accessibilityIdentifier("catalog.addProduct")
        }
        .sheet(item: $editingProduct) { editor in
            ProductEditorSheet(productId: editor.productId, draft: editor.draft)
        }
        .sheet(item: $editingCategory) { editor in
            CategoryEditorSheet(categoryId: editor.categoryId, name: editor.name, color: editor.color, station: editor.station)
        }
    }
}

struct ProductEditorSheet: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    let productId: UUID?
    @State var draft: ProductDraft
    @State private var priceText = ""
    @State private var color: Color = .blue

    var body: some View {
        NavigationStack {
            Form {
                Section("admin.item_section") {
                    TextField("common.field_name", text: $draft.name).accessibilityIdentifier("product.name")
                    Picker("admin.category_label", selection: $draft.categoryId) {
                        ForEach(model.catalog.categories) { Text($0.name).tag($0.id) }
                    }
                    TextField("admin.price_ttc_placeholder", text: $priceText)
                        .keyboardType(.decimalPad)
                        .accessibilityIdentifier("product.price")
                    Picker("admin.vat_label", selection: $draft.taxRatePercent) {
                        ForEach(ProductDraft.vatRates, id: \.self) { Text("\($0.formatted()) %").tag($0) }
                    }
                    .pickerStyle(.segmented)
                }
                Section("admin.preparation_section") {
                    Picker("admin.station_label", selection: $draft.stationId) {
                        Text("admin.station_from_category").tag(String?.none)
                        ForEach(ProductDraft.stations, id: \.self) { Text($0.replacingOccurrences(of: "_", with: " ")).tag(Optional($0)) }
                    }
                    .accessibilityIdentifier("product.station")
                    Toggle("admin.quick_key_toggle", isOn: $draft.isQuickKey).accessibilityIdentifier("product.quickKey")
                    ColorPicker("admin.tile_color_label", selection: $color, supportsOpacity: false)
                    TextField("admin.description_placeholder", text: $draft.description, axis: .vertical)
                }
            }
            .navigationTitle(productId == nil ? "admin.new_product" : "admin.edit_product")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("common.cancel") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("common.save") {
                        draft.price = Money.parse(priceText) ?? .zero
                        draft.colorHex = color.hexString
                        Task { if await model.catalogAdmin.saveProduct(id: productId, draft: draft) { dismiss() } }
                    }
                    .accessibilityIdentifier("product.save")
                }
            }
            .onAppear {
                priceText = draft.price.cents > 0 ? draft.price.plain : ""
                color = Color(hex: draft.colorHex) ?? .blue
            }
        }
    }
}

struct CategoryEditorSheet: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    let categoryId: String?
    @State var name: String
    @State var color: Color
    /// "" = cuisine chaude (poste vide côté serveur).
    @State var station: String
    private let initialStation: String

    init(categoryId: String?, name: String, color: Color, station: String) {
        self.categoryId = categoryId
        _name = State(initialValue: name)
        _color = State(initialValue: color)
        _station = State(initialValue: station)
        initialStation = station
    }

    var body: some View {
        NavigationStack {
            Form {
                TextField("admin.category_name_placeholder", text: $name).accessibilityIdentifier("category.name")
                ColorPicker("admin.color_label", selection: $color, supportsOpacity: false)
                if categoryId != nil {
                    Picker("admin.category_station_label", selection: $station) {
                        Text("admin.station_default_hot").tag("")
                        ForEach(ProductDraft.stations, id: \.self) { Text($0.replacingOccurrences(of: "_", with: " ")).tag($0) }
                    }
                    .accessibilityIdentifier("category.station")
                }
            }
            .navigationTitle(categoryId == nil ? "admin.new_category" : "admin.edit_category")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("common.cancel") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("common.save") {
                        Task { if await model.catalogAdmin.saveCategory(id: categoryId, name: name, colorHex: color.hexString, preparationStationId: station == initialStation ? nil : station) { dismiss() } }
                    }
                    .accessibilityIdentifier("category.save")
                }
            }
        }
        .presentationDetents([.medium])
    }
}

// MARK: - Équipe

struct StaffAdminView: View {
    @Environment(AppModel.self) private var model
    @State private var editing: StaffEditor?

    struct StaffEditor: Identifiable { let id = UUID(); var member: StaffMember? }

    var body: some View {
        let store = model.staff
        List {
            ForEach(store.members) { member in
                HStack {
                    Image(systemName: "person.crop.circle.fill").font(.title).foregroundStyle(member.isActive ? Theme.primary : .secondary)
                    VStack(alignment: .leading) {
                        Text(member.name).font(.headline)
                        Text(member.role.label).font(.subheadline).foregroundStyle(Theme.inkMuted)
                    }
                    Spacer()
                    if !member.isActive { Badge(text: String(localized: "admin.inactive_badge"), color: .red) }
                }
                .contentShape(Rectangle())
                .onTapGesture { editing = StaffEditor(member: member) }
                .swipeActions {
                    if member.isActive {
                        Button("admin.deactivate", role: .destructive) { Task { await store.deactivate(member) } }
                    }
                }
                .accessibilityIdentifier("staff.\(member.name)")
            }
        }
        .listStyle(.insetGrouped)
        .overlay(alignment: .bottomTrailing) {
            Button { editing = StaffEditor(member: nil) } label: {
                Label("admin.new_staff", systemImage: "person.badge.plus").font(.headline).padding(.horizontal, 20).frame(height: 54)
            }
            .buttonStyle(.borderedProminent)
            .clipShape(Capsule())
            .padding(24)
            .accessibilityIdentifier("staff.add")
        }
        .task { await store.load() }
        .sheet(item: $editing) { editor in
            StaffEditorSheet(member: editor.member)
        }
    }
}

struct StaffEditorSheet: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    let member: StaffMember?
    @State private var name = ""
    @State private var role: UserRole = .waiter
    @State private var pin = ""
    @State private var isActive = true

    var body: some View {
        NavigationStack {
            Form {
                TextField("admin.full_name_placeholder", text: $name).accessibilityIdentifier("staff.name")
                Picker("admin.role_label", selection: $role) {
                    ForEach(UserRole.allCases, id: \.self) { Text($0.label).tag($0) }
                }
                SecureField(member == nil ? String(localized: "admin.pin_code_placeholder \(SessionStore.pinLength)") : String(localized: "admin.new_pin_placeholder"), text: $pin)
                    .keyboardType(.numberPad)
                    .accessibilityIdentifier("staff.pin")
                if member != nil { Toggle("admin.active_toggle", isOn: $isActive) }
            }
            .navigationTitle(member == nil ? "admin.new_staff" : "admin.edit_staff")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("common.cancel") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("common.save") {
                        Task { if await model.staff.save(id: member?.id, name: name, role: role, pin: pin, isActive: isActive) { dismiss() } }
                    }
                    .accessibilityIdentifier("staff.save")
                }
            }
            .onAppear {
                name = member?.name ?? ""
                role = member?.role ?? .waiter
                isActive = member?.isActive ?? true
            }
        }
        .presentationDetents([.medium])
    }
}

// MARK: - Imprimantes

struct PrintersAdminView: View {
    @Environment(AppModel.self) private var model
    @State private var editing: PrinterEditor?

    struct PrinterEditor: Identifiable { let id = UUID(); var printer: Printer; var isNew: Bool }

    var body: some View {
        let store = model.printers
        List {
            ForEach(store.printers) { printer in
                HStack(spacing: 14) {
                    Image(systemName: "printer.fill").font(.title2).foregroundStyle(printer.isActive ? Theme.primary : .secondary)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(printer.name).font(.headline)
                        Text("\(printer.ipAddress):\(printer.port) · \(printer.paperWidthMm) mm · \(printer.assignedStationIds.joined(separator: ", "))")
                            .font(.caption).foregroundStyle(Theme.inkMuted)
                        let status = store.statuses.first { $0.printerId == printer.id }
                        Text(Self.statusText(status))
                            .font(.caption.weight(.semibold))
                            .foregroundStyle(status?.isOnline == false ? Theme.danger : Theme.inkMuted)
                            .accessibilityIdentifier("printer.status")
                        if let status, status.pendingCount + status.failedCount > 0 {
                            PrinterJobsList(printerId: printer.id)
                        }
                    }
                    Spacer()
                    if printer.openCashDrawerOnReceipt { Badge(text: String(localized: "admin.cash_drawer_badge"), color: .green, systemImage: "tray") }
                    if printer.textMode { Badge(text: String(localized: "admin.printer_text_mode_badge"), color: .blue, systemImage: "textformat") }
                    if !printer.isActive { Badge(text: String(localized: "admin.deactivated_badge"), color: .red) }
                    Button("admin.test_button") { Task { await store.test(printer) } }
                        .buttonStyle(.bordered)
                        .accessibilityIdentifier("printer.test.\(printer.name)")
                }
                .contentShape(Rectangle())
                .onTapGesture { editing = PrinterEditor(printer: printer, isNew: false) }
                .swipeActions {
                    Button(printer.isActive ? "admin.deactivate" : "admin.activate", role: printer.isActive ? .destructive : nil) {
                        Task { await store.setActive(printer, !printer.isActive) }
                    }
                }
            }
        }
        .listStyle(.insetGrouped)
        .overlay(alignment: .bottomTrailing) {
            Button { editing = PrinterEditor(printer: Printer(name: "", ipAddress: "192.168.1.", assignedStationIds: ["HOT_KITCHEN"]), isNew: true) } label: {
                Label("admin.new_printer", systemImage: "plus").font(.headline).padding(.horizontal, 20).frame(height: 54)
            }
            .buttonStyle(.borderedProminent)
            .clipShape(Capsule())
            .padding(24)
            .accessibilityIdentifier("printer.add")
        }
        .task { await store.load(); await store.loadStatuses() }
        .sheet(item: $editing) { editor in
            PrinterEditorSheet(printer: editor.printer, isNew: editor.isNew)
        }
    }
}

extension PrintersAdminView {
    /// « En ligne » / « Hors ligne depuis HH:mm » / « État inconnu », suivi du nombre de bons en attente.
    static func statusText(_ status: PrinterStatus?) -> String {
        var text: String
        switch status?.isOnline {
        case .none: text = String(localized: "admin.printer_status_unknown")
        case .some(true): text = String(localized: "admin.printer_status_online")
        case .some(false):
            let time = status?.sinceUtc?.formatted(date: .omitted, time: .shortened) ?? "—"
            text = String(localized: "admin.printer_status_offline_since \(time)")
        }
        if let pending = status?.pendingCount, pending > 0 { text += " · " + String(localized: "admin.printer_pending \(pending)") }
        return text
    }
}

/// Jobs `Failed` (Relancer) et `Pending` (Annuler) d'une imprimante, chargés à l'ouverture.
struct PrinterJobsList: View {
    @Environment(AppModel.self) private var model
    let printerId: UUID
    @State private var isExpanded = false

    /// Types serveur connus ; un type inconnu s'affiche tel quel.
    static func kindLabel(_ kind: String) -> String {
        switch kind {
        case "PickupVoucher": String(localized: "admin.print_job_kind_pickupvoucher")
        case "Receipt": String(localized: "admin.print_job_kind_receipt")
        case "KitchenTicket": String(localized: "admin.print_job_kind_kitchenticket")
        case "Report": String(localized: "admin.print_job_kind_report")
        default: kind
        }
    }

    var body: some View {
        DisclosureGroup(isExpanded: $isExpanded) {
            ForEach(model.printers.jobs[printerId] ?? []) { job in
                HStack(spacing: 8) {
                    VStack(alignment: .leading, spacing: 2) {
                        Text(Self.kindLabel(job.kind)).font(.subheadline.weight(.semibold))
                        Text(job.createdAtUtc.formatted(date: .omitted, time: .shortened)).font(.caption).foregroundStyle(Theme.inkMuted)
                        if let error = job.lastError { Text(error).font(.caption).foregroundStyle(Theme.danger) }
                    }
                    Spacer()
                    if job.status == "Failed" {
                        Button("admin.print_job_retry") { Task { await model.printers.retry(job) } }
                            .buttonStyle(.bordered)
                            .accessibilityIdentifier("printjob.retry")
                    }
                    if job.status == "Pending" {
                        Button("admin.print_job_cancel", role: .destructive) { Task { await model.printers.cancel(job) } }
                            .buttonStyle(.bordered)
                            .accessibilityIdentifier("printjob.cancel")
                    }
                }
            }
        } label: {
            Text("admin.printer_jobs_btn").font(.caption.weight(.semibold))
        }
        .accessibilityIdentifier("printer.jobs")
        .onChange(of: isExpanded) { _, open in if open { Task { await model.printers.loadJobs(printerId: printerId) } } }
    }
}

struct PrinterEditorSheet: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State var printer: Printer
    let isNew: Bool

    static let stations = ProductDraft.stations + ["RECEIPT"]

    var body: some View {
        NavigationStack {
            Form {
                Section("admin.printer_section") {
                    TextField("common.field_name", text: $printer.name).accessibilityIdentifier("printer.name")
                    TextField("admin.ip_address_placeholder", text: $printer.ipAddress).keyboardType(.decimalPad).accessibilityIdentifier("printer.ip")
                    Stepper("admin.port_stepper \(printer.port)", value: $printer.port, in: 1...65535)
                    Picker("admin.paper_width_label", selection: $printer.paperWidthMm) {
                        Text(verbatim: "58 mm").tag(58)
                        Text(verbatim: "80 mm").tag(80)
                    }
                    .pickerStyle(.segmented)
                    Toggle("admin.open_cash_drawer_toggle", isOn: $printer.openCashDrawerOnReceipt)
                    Toggle("admin.printer_text_mode", isOn: $printer.textMode).accessibilityIdentifier("printer.textMode")
                }
                Section("admin.served_stations_section") {
                    ForEach(Self.stations, id: \.self) { station in
                        Toggle(station.replacingOccurrences(of: "_", with: " "), isOn: Binding(
                            get: { printer.assignedStationIds.contains(station) },
                            set: { on in
                                if on { printer.assignedStationIds.append(station) } else { printer.assignedStationIds.removeAll { $0 == station } }
                            }
                        ))
                    }
                }
            }
            .navigationTitle(isNew ? "admin.new_printer" : "admin.edit_printer")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("common.cancel") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("common.save") { Task { if await model.printers.save(printer, isNew: isNew) { dismiss() } } }
                        .accessibilityIdentifier("printer.save")
                }
            }
        }
    }
}

// MARK: - Réseau & terminal

struct NetworkSettingsView: View {
    @Environment(AppModel.self) private var model
    @Environment(AppEnvironment.self) private var environment
    @State private var showsServer = false
    @AppStorage(AppearancePreference.storageKey) private var appearance = AppearancePreference.dark.rawValue

    var body: some View {
        let network = model.network
        Form {
            Section("common.master_server_section") {
                LabeledContent("admin.address_label", value: environment.launch.isUITest ? String(localized: "admin.demo_data_label") : model.settings.serverURL)
                LabeledContent("admin.status_label", value: network.isOnline ? String(localized: "common.status_online_simple") : String(localized: "common.status_offline"))
                LabeledContent("admin.realtime_label", value: model.isRealtimeConnected ? String(localized: "admin.connected_label") : String(localized: "admin.disconnected_label"))
                if let info = network.info {
                    LabeledContent("common.field_name", value: info.serverName ?? "—")
                    LabeledContent("admin.host_label", value: "\(info.hostName ?? "—") (\(info.ipAddresses?.joined(separator: ", ") ?? "—"))")
                    LabeledContent("admin.version_label", value: info.version ?? "—")
                }
                if let latency = network.lastLatency { LabeledContent("admin.latency_label", value: "\(Int(latency * 1000)) ms") }
                Button("common.test_connection") { Task { await network.testConnection() } }
                    .accessibilityIdentifier("network.test")
                Button("admin.change_server_button") { showsServer = true }
            }
            Section("admin.sync_section") {
                if let sync = network.sync {
                    LabeledContent("admin.pending_label", value: String(localized: "admin.pending_messages \(sync.pendingMessages)"))
                    LabeledContent("admin.synced_label", value: String(localized: "admin.synced_transactions \(sync.completedMessages)"))
                    if let last = sync.lastSyncUtc { LabeledContent("admin.last_sync_label", value: last.formatted(date: .omitted, time: .standard)) }
                }
                Button("admin.force_sync_button") { Task { await network.forceSync() } }
                    .accessibilityIdentifier("network.forceSync")
            }
            Section("admin.terminal_section") {
                LabeledContent("admin.identifier_label", value: model.settings.terminalId)
                Picker("admin.voucher_overflow_label", selection: Binding(get: { model.settings.mealVoucherPolicy }, set: { model.settings.mealVoucherPolicy = $0 })) {
                    ForEach(MealVoucherPolicy.allCases, id: \.self) { Text($0.label).tag($0) }
                }
                .accessibilityIdentifier("settings.voucherPolicy")
                ReceiptLanguagePicker()
                KitchenTicketLanguagePicker()
            }
            Section("admin.appearance_section") {
                Picker("admin.theme_label", selection: $appearance) {
                    ForEach(AppearancePreference.allCases) { Text($0.label).tag($0.rawValue) }
                }
                .accessibilityIdentifier("settings.appearance")
            }
        }
        .task { await network.refresh() }
        .sheet(isPresented: $showsServer) { ServerSettingsSheet() }
    }
}

/// Réglage back-office : langue des tickets imprimés (indépendante de la langue de l'app).
struct ReceiptLanguagePicker: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Picker("admin.receipt_language", selection: Binding(
                get: { model.settingsStore.settings?.receiptLanguage ?? "en" },
                set: { lang in Task { await model.settingsStore.setReceiptLanguage(lang) } }
            )) {
                Text(verbatim: "English").tag("en")
                Text(verbatim: "Français").tag("fr")
                Text(verbatim: "العربية").tag("ar")
            }
            .accessibilityIdentifier("admin.receiptLanguage")
            Text("admin.receipt_language_hint")
                .font(.caption)
                .foregroundStyle(Theme.inkMuted)
        }
        .task { await model.settingsStore.load() }
    }
}

/// Réglage back-office : langue des bons cuisine (indépendante de celle des tickets clients).
struct KitchenTicketLanguagePicker: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        Picker("admin.kitchen_ticket_language", selection: Binding(
            get: { model.settingsStore.settings?.kitchenTicketLanguage ?? "en" },
            set: { lang in Task { await model.settingsStore.setKitchenTicketLanguage(lang) } }
        )) {
            Text(verbatim: "English").tag("en")
            Text(verbatim: "Français").tag("fr")
            Text(verbatim: "العربية").tag("ar")
        }
        .accessibilityIdentifier("admin.kitchenTicketLanguage")
    }
}

/// Back-office : identité de l'établissement et exercice fiscal (NF525).
struct EstablishmentAdminView: View {
    @Environment(AppModel.self) private var model
    @State private var companyName = ""
    @State private var addressLines = ""
    @State private var siret = ""
    @State private var vatNumber = ""
    @State private var certificateNumber = ""
    @State private var fiscalYearStartMonth = 1
    @State private var fiscalYearStartDay = 1
    @State private var isSaving = false

    var body: some View {
        Form {
            Section {
                VStack(alignment: .leading, spacing: 4) {
                    Text("admin.establishment_title").font(.headline)
                    Text("admin.establishment_subtitle")
                        .font(.caption)
                        .foregroundStyle(Theme.inkMuted)
                }
            }
            Section("admin.company_name") {
                TextField("admin.company_name", text: $companyName)
                    .accessibilityIdentifier("establishment.companyName")
            }
            Section("admin.address_lines") {
                TextField("admin.address_lines", text: $addressLines, axis: .vertical)
                    .lineLimit(2...4)
                    .accessibilityIdentifier("establishment.addressLines")
            }
            Section("admin.siret") {
                TextField("admin.siret", text: $siret)
                    .keyboardType(.numberPad)
                    .accessibilityIdentifier("establishment.siret")
            }
            Section("admin.vat_number") {
                TextField("admin.vat_number", text: $vatNumber)
                    .autocorrectionDisabled()
                    .accessibilityIdentifier("establishment.vatNumber")
            }
            Section("admin.certificate_number") {
                TextField("admin.certificate_number", text: $certificateNumber)
                    .autocorrectionDisabled()
                    .accessibilityIdentifier("establishment.certificateNumber")
            }
            Section("admin.fiscal_year_start") {
                Stepper(value: $fiscalYearStartMonth, in: 1...12) {
                    LabeledContent("admin.fiscal_year_month", value: "\(fiscalYearStartMonth)")
                }
                .accessibilityIdentifier("establishment.fiscalYearMonth")
                Stepper(value: $fiscalYearStartDay, in: 1...28) {
                    LabeledContent("admin.fiscal_year_day", value: "\(fiscalYearStartDay)")
                }
                .accessibilityIdentifier("establishment.fiscalYearDay")
            }
            Section {
                Button {
                    Task {
                        isSaving = true
                        _ = await model.settingsStore.updateEstablishment(
                            companyName: companyName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? nil : companyName,
                            addressLines: addressLines.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? nil : addressLines,
                            siret: siret.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? nil : siret,
                            vatNumber: vatNumber.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? nil : vatNumber,
                            certificateNumber: certificateNumber.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? nil : certificateNumber,
                            fiscalYearStartMonth: fiscalYearStartMonth,
                            fiscalYearStartDay: fiscalYearStartDay
                        )
                        isSaving = false
                    }
                } label: {
                    if isSaving {
                        ProgressView()
                    } else {
                        Label("admin.save_establishment", systemImage: "checkmark.circle")
                    }
                }
                .accessibilityIdentifier("establishment.save")
            }
        }
        .task {
            await model.settingsStore.load()
            if let s = model.settingsStore.settings {
                companyName = s.companyName ?? ""
                addressLines = s.addressLines ?? ""
                siret = s.siret ?? ""
                vatNumber = s.vatNumber ?? ""
                certificateNumber = s.certificateNumber ?? ""
                fiscalYearStartMonth = s.fiscalYearStartMonth ?? 1
                fiscalYearStartDay = s.fiscalYearStartDay ?? 1
            }
        }
    }
}

