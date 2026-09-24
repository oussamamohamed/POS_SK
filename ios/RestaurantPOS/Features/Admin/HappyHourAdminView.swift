import SwiftUI
import PosKit

/// Gestion des plages Happy Hour et des règles tarifaires groupées (articles / familles).
struct HappyHourAdminView: View {
    @Environment(AppModel.self) private var model
    @State private var tab: Tab = .products
    @State private var showsNewSchedule = false
    @State private var selectedProducts: Set<UUID> = []
    @State private var selectedCategories: Set<String> = []
    @State private var selectedRules: Set<UUID> = []
    @State private var priceMode: HappyHourPricingMode = .fixedPrice
    @State private var valueText = "5"
    @State private var familyPercent = "20"
    @State private var search = ""

    enum Tab: String, CaseIterable { case products = "Articles", families = "Familles", rules = "Règles actives" }

    var body: some View {
        let store = model.happyHourAdmin
        HStack(spacing: 0) {
            schedulesList
                .frame(width: 320)
            Divider()
            VStack(alignment: .leading, spacing: 16) {
                if let schedule = store.selectedSchedule {
                    HStack {
                        VStack(alignment: .leading) {
                            Text(schedule.name).font(.title2.weight(.bold))
                            Text("\(schedule.daysLabel) · \(schedule.startTime)–\(schedule.endTime) · \(schedule.appliesToTakeaway ? "emporté inclus" : "sur place uniquement")")
                                .font(.subheadline).foregroundStyle(.secondary)
                        }
                        Spacer()
                    }
                    Picker("Onglet", selection: $tab) {
                        ForEach(Tab.allCases, id: \.self) { Text($0.rawValue) }
                    }
                    .pickerStyle(.segmented)
                    .accessibilityIdentifier("hh.tab")
                    switch tab {
                    case .products: productsTab
                    case .families: familiesTab
                    case .rules: rulesTab(schedule)
                    }
                } else {
                    EmptyStateView(title: "Aucune plage sélectionnée", systemImage: "wineglass", message: "Créez une plage Happy Hour pour configurer des tarifs.")
                }
            }
            .padding(20)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        }
        .task { await store.load() }
        .sheet(isPresented: $showsNewSchedule) { NewScheduleSheet() }
    }

    private var schedulesList: some View {
        let store = model.happyHourAdmin
        return List(selection: Binding(get: { store.selectedScheduleId }, set: { store.selectedScheduleId = $0; selectedRules = [] })) {
            Section {
                ForEach(store.schedules) { schedule in
                    VStack(alignment: .leading, spacing: 4) {
                        Text(schedule.name).font(.headline)
                        Text("\(schedule.startTime)–\(schedule.endTime) · \(schedule.daysLabel)").font(.caption).foregroundStyle(.secondary)
                        Text("\(schedule.priceRules.count) règle(s) · priorité \(schedule.priority)").font(.caption2).foregroundStyle(.blue)
                    }
                    .tag(schedule.id)
                    .swipeActions {
                        Button("Supprimer", role: .destructive) { Task { await store.delete(schedule) } }
                    }
                    .accessibilityIdentifier("hh.schedule.\(schedule.name)")
                }
            } header: {
                HStack {
                    Text("Plages horaires")
                    Spacer()
                    Button { showsNewSchedule = true } label: { Image(systemName: "plus.circle.fill") }
                        .accessibilityIdentifier("hh.newSchedule")
                }
            }
        }
        .listStyle(.insetGrouped)
    }

    private var productsTab: some View {
        let products = model.catalog.products.filter { search.isEmpty || $0.name.localizedCaseInsensitiveContains(search) }
        return VStack(alignment: .leading, spacing: 12) {
            HStack {
                TextField("Rechercher", text: $search).textFieldStyle(.roundedBorder).frame(maxWidth: 260)
                Button("Tout") { selectedProducts.formUnion(products.map(\.id)) }
                Button("Aucun") { selectedProducts.removeAll() }
                Spacer()
                Text("\(selectedProducts.count) sélectionné(s)").foregroundStyle(.secondary).accessibilityIdentifier("hh.selectedCount")
            }
            ScrollView {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 200), spacing: 10)], spacing: 10) {
                    ForEach(products) { product in
                        SelectableCard(title: product.name, subtitle: "Prix normal \(product.price.formatted)", isSelected: selectedProducts.contains(product.id)) {
                            if selectedProducts.contains(product.id) { selectedProducts.remove(product.id) } else { selectedProducts.insert(product.id) }
                        }
                        .accessibilityIdentifier("hh.product.\(product.name)")
                    }
                }
            }
            HStack(spacing: 12) {
                Picker("Mode", selection: $priceMode) {
                    Text("Prix fixe (€)").tag(HappyHourPricingMode.fixedPrice)
                    Text("Remise (%)").tag(HappyHourPricingMode.percentageDiscount)
                }
                .pickerStyle(.segmented)
                .frame(width: 280)
                TextField("Valeur", text: $valueText).keyboardType(.decimalPad).textFieldStyle(.roundedBorder).frame(width: 100)
                    .accessibilityIdentifier("hh.value")
                ActionButton(title: "Appliquer aux \(selectedProducts.count) article(s)", systemImage: "checkmark", tint: Theme.happyHour) {
                    Task {
                        if await model.happyHourAdmin.applyToProducts(selectedProducts, mode: priceMode, value: decimal(valueText)) { selectedProducts.removeAll() }
                    }
                }
                .accessibilityIdentifier("hh.applyProducts")
            }
        }
    }

    private var familiesTab: some View {
        VStack(alignment: .leading, spacing: 12) {
            ScrollView {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 220), spacing: 10)], spacing: 10) {
                    ForEach(model.catalog.categories) { category in
                        SelectableCard(title: category.name, subtitle: "\(model.catalog.products(in: category.id).count) article(s)", isSelected: selectedCategories.contains(category.id)) {
                            if selectedCategories.contains(category.id) { selectedCategories.remove(category.id) } else { selectedCategories.insert(category.id) }
                        }
                        .accessibilityIdentifier("hh.family.\(category.id)")
                    }
                }
            }
            HStack(spacing: 12) {
                Text("Remise")
                TextField("%", text: $familyPercent).keyboardType(.decimalPad).textFieldStyle(.roundedBorder).frame(width: 80)
                    .accessibilityIdentifier("hh.familyPercent")
                Text("%")
                ActionButton(title: "Appliquer aux \(selectedCategories.count) famille(s)", systemImage: "checkmark", tint: Theme.happyHour) {
                    Task {
                        if await model.happyHourAdmin.applyToCategories(selectedCategories, percent: decimal(familyPercent)) { selectedCategories.removeAll() }
                    }
                }
                .accessibilityIdentifier("hh.applyFamilies")
            }
        }
    }

    private func rulesTab(_ schedule: HappyHourSchedule) -> some View {
        VStack(alignment: .leading, spacing: 12) {
            List(schedule.priceRules.filter { $0.id != nil }, id: \.id, selection: $selectedRules) { rule in
                HStack {
                    Image(systemName: rule.targetType == .category ? "tag" : "wineglass")
                    Text(rule.targetName)
                    Spacer()
                    Text(rule.valueLabel).font(.headline).foregroundStyle(Theme.happyHour)
                }
                .tag(rule.id!)
            }
            .environment(\.editMode, .constant(.active))
            .listStyle(.plain)
            .accessibilityIdentifier("hh.rules")
            HStack {
                Button("Tout sélectionner") { selectedRules = Set(schedule.priceRules.compactMap(\.id)) }
                Spacer()
                ActionButton(title: "Supprimer \(selectedRules.count) règle(s)", systemImage: "trash", tint: .red) {
                    Task { await model.happyHourAdmin.deleteRules(selectedRules); selectedRules.removeAll() }
                }
                .frame(maxWidth: 320)
                .disabled(selectedRules.isEmpty)
                .accessibilityIdentifier("hh.deleteRules")
            }
        }
    }

    private func decimal(_ text: String) -> Decimal {
        Decimal(string: text.replacingOccurrences(of: ",", with: "."), locale: Locale(identifier: "en_US_POSIX")) ?? 0
    }
}

struct SelectableCard: View {
    let title: String
    let subtitle: String
    let isSelected: Bool
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(spacing: 10) {
                Image(systemName: isSelected ? "checkmark.circle.fill" : "circle").font(.title3).foregroundStyle(isSelected ? Color.accentColor : .secondary)
                VStack(alignment: .leading) {
                    Text(title).font(.subheadline.weight(.semibold)).lineLimit(1)
                    Text(subtitle).font(.caption).foregroundStyle(.secondary)
                }
                Spacer(minLength: 0)
            }
            .padding(12)
            .background(RoundedRectangle(cornerRadius: Theme.smallRadius).fill(isSelected ? Color.accentColor.opacity(0.12) : Color(.secondarySystemGroupedBackground)))
        }
        .buttonStyle(.plain)
        .accessibilityAddTraits(isSelected ? .isSelected : [])
    }
}

struct NewScheduleSheet: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var name = ""
    @State private var days: Set<Int> = [1, 2, 3, 4, 5]
    @State private var start = Calendar.current.date(bySettingHour: 17, minute: 0, second: 0, of: Date())!
    @State private var end = Calendar.current.date(bySettingHour: 20, minute: 0, second: 0, of: Date())!
    @State private var takeaway = false
    @State private var priority = 1

    var body: some View {
        NavigationStack {
            Form {
                TextField("Nom de la plage", text: $name).accessibilityIdentifier("schedule.name")
                Section("Jours") {
                    HStack {
                        ForEach([1, 2, 3, 4, 5, 6, 0], id: \.self) { day in
                            ChipButton(title: HappyHourSchedule.dayLabels[day], isSelected: days.contains(day)) {
                                if days.contains(day) { days.remove(day) } else { days.insert(day) }
                            }
                        }
                    }
                }
                Section("Horaires") {
                    DatePicker("Début", selection: $start, displayedComponents: .hourAndMinute)
                    DatePicker("Fin", selection: $end, displayedComponents: .hourAndMinute)
                }
                Toggle("S'applique aussi à la vente à emporter", isOn: $takeaway)
                Stepper("Priorité \(priority)", value: $priority, in: 1...10)
            }
            .navigationTitle("Nouvelle plage Happy Hour")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Annuler") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Créer") {
                        let schedule = HappyHourSchedule(name: name, daysOfWeek: days.sorted(), startTime: format(start), endTime: format(end), appliesToTakeaway: takeaway, priority: priority)
                        Task { if await model.happyHourAdmin.create(schedule) { dismiss() } }
                    }
                    .accessibilityIdentifier("schedule.create")
                }
            }
        }
    }

    private func format(_ date: Date) -> String {
        let c = Calendar.current.dateComponents([.hour, .minute], from: date)
        return String(format: "%02d:%02d", c.hour ?? 0, c.minute ?? 0)
    }
}
