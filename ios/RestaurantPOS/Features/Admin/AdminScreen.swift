import SwiftUI
import PosKit

/// Back-office (responsables) : navigation par sous-section.
struct AdminScreen: View {
    enum Section: String, CaseIterable, Identifiable {
        case dashboard, catalog, grid, staff, printers, happyHour, network
        var id: String { rawValue }

        var title: String {
            switch self {
            case .dashboard: "Tableau de bord"
            case .catalog: "Catalogue"
            case .grid: "Grille tactile"
            case .staff: "Équipe"
            case .printers: "Imprimantes"
            case .happyHour: "Happy Hour"
            case .network: "Réseau & terminal"
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
                    Picker("Période", selection: Binding(get: { store.range }, set: { store.range = $0; Task { await store.load() } })) {
                        ForEach(DashboardRange.allCases) { Text($0.label).tag($0) }
                    }
                    .pickerStyle(.segmented)
                    .frame(maxWidth: 480)
                    .accessibilityIdentifier("dashboard.range")
                    Spacer()
                    Button { Task { await store.load() } } label: { Label("Actualiser", systemImage: "arrow.clockwise") }.buttonStyle(.bordered)
                }
                if let data = store.dashboard {
                    LazyVGrid(columns: [GridItem(.adaptive(minimum: 220), spacing: 16)], spacing: 16) {
                        KPIView(title: "Chiffre d'affaires TTC", value: data.kpis.totalSalesTtc.formatted, subtitle: "HT \(data.kpis.totalSalesHt.formatted)", systemImage: "eurosign.circle", tint: .green)
                            .accessibilityIdentifier("kpi.sales")
                        KPIView(title: "Ticket moyen", value: data.kpis.averageOrderTtc.formatted, subtitle: "\(data.kpis.totalOrdersCount) commande(s)", systemImage: "receipt", tint: .blue)
                        KPIView(title: "Moyenne / couvert", value: data.kpis.averageCoverTtc.formatted, subtitle: "\(data.kpis.totalCoversCount) couvert(s)", systemImage: "person.2", tint: .orange)
                    }
                    HStack(alignment: .top, spacing: 16) {
                        Card {
                            VStack(alignment: .leading, spacing: 10) {
                                Text("Moyens de paiement").font(.headline)
                                if data.paymentMethods.isEmpty { Text("Aucun encaissement").foregroundStyle(.secondary) }
                                ForEach(data.paymentMethods) { p in
                                    ShareBar(label: "\(PaymentMethod.label(forServerName: p.methodName)) (\(p.transactionsCount))", value: p.totalAmount.formatted, fraction: p.percentageOfTotal / 100, tint: .blue)
                                }
                            }
                        }
                        Card {
                            VStack(alignment: .leading, spacing: 10) {
                                Text("Meilleures ventes").font(.headline)
                                if data.topProducts.isEmpty { Text("Aucune vente").foregroundStyle(.secondary) }
                                ForEach(data.topProducts.prefix(8)) { p in
                                    ShareBar(label: "\(p.productName) ×\(p.quantitySold)", value: p.totalSalesTtc.formatted, fraction: p.percentageOfTotal / 100, tint: .green)
                                }
                            }
                        }
                    }
                    HStack(alignment: .top, spacing: 16) {
                        Card {
                            VStack(alignment: .leading, spacing: 10) {
                                Text("Services").font(.headline)
                                ForEach(data.services) { s in
                                    HStack {
                                        VStack(alignment: .leading) {
                                            Text(s.serviceName).font(.subheadline.weight(.semibold))
                                            Text("\(s.ordersCount) commande(s) · \(s.coversCount) couvert(s)").font(.caption).foregroundStyle(.secondary)
                                        }
                                        Spacer()
                                        Text(s.salesTtc.formatted).font(.headline.monospacedDigit())
                                    }
                                }
                            }
                        }
                        Card {
                            VStack(alignment: .leading, spacing: 10) {
                                Text("Productivité serveurs").font(.headline)
                                if data.staffPerformance.isEmpty { Text("Aucune activité").foregroundStyle(.secondary) }
                                ForEach(data.staffPerformance) { s in
                                    HStack {
                                        VStack(alignment: .leading) {
                                            Text(s.serverName).font(.subheadline.weight(.semibold))
                                            Text("\(s.tablesServedCount) table(s) · moy. \(s.averageTableTtc.formatted)").font(.caption).foregroundStyle(.secondary)
                                        }
                                        Spacer()
                                        Text(s.totalSalesTtc.formatted).font(.headline.monospacedDigit())
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
                Text(value).font(.subheadline.monospacedDigit().weight(.semibold))
            }
            GeometryReader { proxy in
                Capsule().fill(Color(.tertiarySystemFill))
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
    struct CategoryEditor: Identifiable { let id = UUID(); var categoryId: String?; var name: String; var color: Color }

    var body: some View {
        let catalog = model.catalog
        let products = catalog.products(in: filter).filter { search.isEmpty || $0.name.localizedCaseInsensitiveContains(search) }
        VStack(spacing: 0) {
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 8) {
                    ChipButton(title: "Tout", isSelected: filter == CatalogStore.allCategoryId) { filter = CatalogStore.allCategoryId }
                    ForEach(catalog.categories) { category in
                        ChipButton(title: category.name, isSelected: filter == category.id, tint: Color(hex: category.colorHex) ?? .accentColor) { filter = category.id }
                            .contextMenu {
                                Button("Modifier la famille", systemImage: "pencil") {
                                    editingCategory = CategoryEditor(categoryId: category.id, name: category.name, color: Color(hex: category.colorHex) ?? .blue)
                                }
                            }
                    }
                    Button { editingCategory = CategoryEditor(categoryId: nil, name: "", color: .blue) } label: { Label("Famille", systemImage: "plus") }
                        .buttonStyle(.bordered)
                        .accessibilityIdentifier("catalog.addCategory")
                }
                .padding(20)
            }
            List {
                ForEach(products) { product in
                    HStack(spacing: 12) {
                        RoundedRectangle(cornerRadius: 4).fill(Color(hex: product.colorHex) ?? .accentColor).frame(width: 6, height: 40)
                        VStack(alignment: .leading, spacing: 2) {
                            HStack(spacing: 6) {
                                Text(product.name).font(.headline)
                                if product.isQuickKey { Image(systemName: "bolt.fill").foregroundStyle(.orange).font(.caption) }
                                if product.hasModifiers { Badge(text: "\(product.modifierGroups.count) option(s)", color: .blue) }
                            }
                            Text("\(product.station) · TVA \(product.taxRatePercent.formatted()) %").font(.caption).foregroundStyle(.secondary)
                        }
                        Spacer()
                        Text(product.price.formatted).font(.headline.monospacedDigit())
                    }
                    .contentShape(Rectangle())
                    .onTapGesture { editingProduct = ProductEditor(productId: product.id, draft: ProductDraft(product: product)) }
                    .swipeActions {
                        Button("Désactiver", role: .destructive) { Task { await model.catalogAdmin.archive(product) } }
                    }
                    .accessibilityIdentifier("catalog.product.\(product.name)")
                }
            }
            .listStyle(.insetGrouped)
            .searchable(text: $search, prompt: "Rechercher un article")
        }
        .overlay(alignment: .bottomTrailing) {
            Button {
                editingProduct = ProductEditor(productId: nil, draft: ProductDraft(categoryId: filter == CatalogStore.allCategoryId ? (catalog.categories.first?.id ?? "") : filter))
            } label: {
                Label("Nouvel article", systemImage: "plus").font(.headline).padding(.horizontal, 20).frame(height: 54)
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
            CategoryEditorSheet(categoryId: editor.categoryId, name: editor.name, color: editor.color)
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
                Section("Article") {
                    TextField("Nom", text: $draft.name).accessibilityIdentifier("product.name")
                    Picker("Famille", selection: $draft.categoryId) {
                        ForEach(model.catalog.categories) { Text($0.name).tag($0.id) }
                    }
                    TextField("Prix TTC (€)", text: $priceText)
                        .keyboardType(.decimalPad)
                        .accessibilityIdentifier("product.price")
                    Picker("TVA", selection: $draft.taxRatePercent) {
                        ForEach(ProductDraft.vatRates, id: \.self) { Text("\($0.formatted()) %").tag($0) }
                    }
                    .pickerStyle(.segmented)
                }
                Section("Préparation") {
                    Picker("Poste", selection: $draft.stationId) {
                        ForEach(ProductDraft.stations, id: \.self) { Text($0.replacingOccurrences(of: "_", with: " ")).tag($0) }
                    }
                    Toggle("Touche rapide", isOn: $draft.isQuickKey).accessibilityIdentifier("product.quickKey")
                    ColorPicker("Couleur de la tuile", selection: $color, supportsOpacity: false)
                    TextField("Description", text: $draft.description, axis: .vertical)
                }
            }
            .navigationTitle(productId == nil ? "Nouvel article" : "Modifier l'article")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Annuler") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Enregistrer") {
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

    var body: some View {
        NavigationStack {
            Form {
                TextField("Nom de la famille", text: $name).accessibilityIdentifier("category.name")
                ColorPicker("Couleur", selection: $color, supportsOpacity: false)
            }
            .navigationTitle(categoryId == nil ? "Nouvelle famille" : "Modifier la famille")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Annuler") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Enregistrer") {
                        Task { if await model.catalogAdmin.saveCategory(id: categoryId, name: name, colorHex: color.hexString) { dismiss() } }
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
                    Image(systemName: "person.crop.circle.fill").font(.title).foregroundStyle(member.isActive ? Color.accentColor : .secondary)
                    VStack(alignment: .leading) {
                        Text(member.name).font(.headline)
                        Text(member.role.label).font(.subheadline).foregroundStyle(.secondary)
                    }
                    Spacer()
                    if !member.isActive { Badge(text: "Inactif", color: .red) }
                }
                .contentShape(Rectangle())
                .onTapGesture { editing = StaffEditor(member: member) }
                .swipeActions {
                    if member.isActive {
                        Button("Désactiver", role: .destructive) { Task { await store.deactivate(member) } }
                    }
                }
                .accessibilityIdentifier("staff.\(member.name)")
            }
        }
        .listStyle(.insetGrouped)
        .overlay(alignment: .bottomTrailing) {
            Button { editing = StaffEditor(member: nil) } label: {
                Label("Nouvel employé", systemImage: "person.badge.plus").font(.headline).padding(.horizontal, 20).frame(height: 54)
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
                TextField("Nom complet", text: $name).accessibilityIdentifier("staff.name")
                Picker("Rôle", selection: $role) {
                    ForEach(UserRole.allCases, id: \.self) { Text($0.label).tag($0) }
                }
                SecureField(member == nil ? "Code PIN (\(SessionStore.pinLength) chiffres)" : "Nouveau PIN (laisser vide pour conserver)", text: $pin)
                    .keyboardType(.numberPad)
                    .accessibilityIdentifier("staff.pin")
                if member != nil { Toggle("Actif", isOn: $isActive) }
            }
            .navigationTitle(member == nil ? "Nouvel employé" : "Modifier l'employé")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Annuler") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Enregistrer") {
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
                    Image(systemName: "printer.fill").font(.title2).foregroundStyle(printer.isActive ? Color.accentColor : .secondary)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(printer.name).font(.headline)
                        Text("\(printer.ipAddress):\(printer.port) · \(printer.paperWidthMm) mm · \(printer.assignedStationIds.joined(separator: ", "))")
                            .font(.caption).foregroundStyle(.secondary)
                    }
                    Spacer()
                    if printer.openCashDrawerOnReceipt { Badge(text: "Tiroir", color: .green, systemImage: "tray") }
                    if !printer.isActive { Badge(text: "Désactivée", color: .red) }
                    Button("Test") { Task { await store.test(printer) } }
                        .buttonStyle(.bordered)
                        .accessibilityIdentifier("printer.test.\(printer.name)")
                }
                .contentShape(Rectangle())
                .onTapGesture { editing = PrinterEditor(printer: printer, isNew: false) }
                .swipeActions {
                    Button(printer.isActive ? "Désactiver" : "Activer", role: printer.isActive ? .destructive : nil) {
                        Task { await store.setActive(printer, !printer.isActive) }
                    }
                }
            }
        }
        .listStyle(.insetGrouped)
        .overlay(alignment: .bottomTrailing) {
            Button { editing = PrinterEditor(printer: Printer(name: "", ipAddress: "192.168.1.", assignedStationIds: ["HOT_KITCHEN"]), isNew: true) } label: {
                Label("Nouvelle imprimante", systemImage: "plus").font(.headline).padding(.horizontal, 20).frame(height: 54)
            }
            .buttonStyle(.borderedProminent)
            .clipShape(Capsule())
            .padding(24)
            .accessibilityIdentifier("printer.add")
        }
        .task { await store.load() }
        .sheet(item: $editing) { editor in
            PrinterEditorSheet(printer: editor.printer, isNew: editor.isNew)
        }
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
                Section("Imprimante") {
                    TextField("Nom", text: $printer.name).accessibilityIdentifier("printer.name")
                    TextField("Adresse IP", text: $printer.ipAddress).keyboardType(.decimalPad).accessibilityIdentifier("printer.ip")
                    Stepper("Port \(printer.port)", value: $printer.port, in: 1...65535)
                    Picker("Largeur papier", selection: $printer.paperWidthMm) {
                        Text("58 mm").tag(58)
                        Text("80 mm").tag(80)
                    }
                    .pickerStyle(.segmented)
                    Toggle("Ouvre le tiroir-caisse", isOn: $printer.openCashDrawerOnReceipt)
                }
                Section("Postes desservis") {
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
            .navigationTitle(isNew ? "Nouvelle imprimante" : "Modifier l'imprimante")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Annuler") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Enregistrer") { Task { if await model.printers.save(printer, isNew: isNew) { dismiss() } } }
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

    var body: some View {
        let network = model.network
        Form {
            Section("Serveur maître") {
                LabeledContent("Adresse", value: environment.launch.isUITest ? "Données de démonstration" : model.settings.serverURL)
                LabeledContent("État", value: network.isOnline ? "En ligne" : "Hors ligne")
                LabeledContent("Temps réel (SignalR)", value: model.isRealtimeConnected ? "Connecté" : "Déconnecté")
                if let info = network.info {
                    LabeledContent("Nom", value: info.serverName ?? "—")
                    LabeledContent("Hôte", value: "\(info.hostName ?? "—") (\(info.ipAddresses?.joined(separator: ", ") ?? "—"))")
                    LabeledContent("Version", value: info.version ?? "—")
                }
                if let latency = network.lastLatency { LabeledContent("Latence", value: "\(Int(latency * 1000)) ms") }
                Button("Tester la connexion") { Task { await network.testConnection() } }
                    .accessibilityIdentifier("network.test")
                Button("Changer de serveur…") { showsServer = true }
            }
            Section("Synchronisation (file Outbox)") {
                if let sync = network.sync {
                    LabeledContent("En attente", value: "\(sync.pendingMessages) message(s)")
                    LabeledContent("Synchronisés", value: "\(sync.completedMessages) transaction(s)")
                    if let last = sync.lastSyncUtc { LabeledContent("Dernière synchro", value: last.formatted(date: .omitted, time: .standard)) }
                }
                Button("Forcer la synchronisation") { Task { await network.forceSync() } }
                    .accessibilityIdentifier("network.forceSync")
            }
            Section("Terminal") {
                LabeledContent("Identifiant", value: model.settings.terminalId)
                Picker("Titres-restaurant : dépassement", selection: Binding(get: { model.settings.mealVoucherPolicy }, set: { model.settings.mealVoucherPolicy = $0 })) {
                    ForEach(MealVoucherPolicy.allCases, id: \.self) { Text($0.label).tag($0) }
                }
                .accessibilityIdentifier("settings.voucherPolicy")
            }
        }
        .task { await network.refresh() }
        .sheet(isPresented: $showsServer) { ServerSettingsSheet() }
    }
}
