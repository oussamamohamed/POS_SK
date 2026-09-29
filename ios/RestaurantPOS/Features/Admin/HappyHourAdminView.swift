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

    enum Tab: String, CaseIterable {
        case products = "Articles", families = "Familles", rules = "Règles actives"

        var label: String {
            switch self {
            case .products: String(localized: "admin.hh_tab_products")
            case .families: String(localized: "admin.hh_tab_families")
            case .rules: String(localized: "admin.hh_tab_rules")
            }
        }
    }

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
                            Text("\(schedule.daysLabel) · \(schedule.startTime)–\(schedule.endTime) · \(schedule.appliesToTakeaway ? String(localized: "admin.hh_takeaway_included") : String(localized: "admin.hh_dine_in_only"))")
                                .font(.subheadline).foregroundStyle(Theme.inkMuted)
                        }
                        Spacer()
                    }
                    Picker("admin.hh_tab_label", selection: $tab) {
                        ForEach(Tab.allCases, id: \.self) { Text($0.label) }
                    }
                    .pickerStyle(.segmented)
                    .accessibilityIdentifier("hh.tab")
                    switch tab {
                    case .products: productsTab
                    case .families: familiesTab
                    case .rules: rulesTab(schedule)
                    }
                } else {
                    EmptyStateView(title: String(localized: "admin.hh_no_schedule_title"), systemImage: "wineglass", message: String(localized: "admin.hh_no_schedule_message"))
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
                        Text("\(schedule.startTime)–\(schedule.endTime) · \(schedule.daysLabel)").font(.caption).foregroundStyle(Theme.inkMuted)
                        Text("admin.hh_rules_priority \(schedule.priceRules.count) \(schedule.priority)").font(.caption2).foregroundStyle(.blue)
                    }
                    .tag(schedule.id)
                    .swipeActions {
                        Button("common.delete", role: .destructive) { Task { await store.delete(schedule) } }
                    }
                    .accessibilityIdentifier("hh.schedule.\(schedule.name)")
                }
            } header: {
                HStack {
                    Text("admin.hh_schedules_header")
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
                TextField("admin.search_placeholder", text: $search).textFieldStyle(.roundedBorder).frame(maxWidth: 260)
                Button("admin.select_all_button") { selectedProducts.formUnion(products.map(\.id)) }
                Button("admin.select_none_button") { selectedProducts.removeAll() }
                Spacer()
                Text("admin.hh_selected_count \(selectedProducts.count)").foregroundStyle(Theme.inkMuted).accessibilityIdentifier("hh.selectedCount")
            }
            ScrollView {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 200), spacing: 10)], spacing: 10) {
                    ForEach(products) { product in
                        SelectableCard(title: product.name, subtitle: String(localized: "admin.hh_normal_price \(product.price.formatted)"), isSelected: selectedProducts.contains(product.id)) {
                            if selectedProducts.contains(product.id) { selectedProducts.remove(product.id) } else { selectedProducts.insert(product.id) }
                        }
                        .accessibilityIdentifier("hh.product.\(product.name)")
                    }
                }
            }
            HStack(spacing: 12) {
                Picker("admin.hh_price_mode_label", selection: $priceMode) {
                    Text("admin.hh_fixed_price_option").tag(HappyHourPricingMode.fixedPrice)
                    Text("admin.hh_discount_percent_option").tag(HappyHourPricingMode.percentageDiscount)
                }
                .pickerStyle(.segmented)
                .frame(width: 280)
                TextField("order.discount_value_placeholder", text: $valueText).keyboardType(.decimalPad).textFieldStyle(.roundedBorder).frame(width: 100)
                    .accessibilityIdentifier("hh.value")
                ActionButton(title: String(localized: "admin.hh_apply_products \(selectedProducts.count)"), systemImage: "checkmark", kind: .primary) {
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
                        SelectableCard(title: category.name, subtitle: String(localized: "admin.hh_family_item_count \(model.catalog.products(in: category.id).count)"), isSelected: selectedCategories.contains(category.id)) {
                            if selectedCategories.contains(category.id) { selectedCategories.remove(category.id) } else { selectedCategories.insert(category.id) }
                        }
                        .accessibilityIdentifier("hh.family.\(category.id)")
                    }
                }
            }
            HStack(spacing: 12) {
                Text("admin.hh_discount_label")
                TextField("%", text: $familyPercent).keyboardType(.decimalPad).textFieldStyle(.roundedBorder).frame(width: 80)
                    .accessibilityIdentifier("hh.familyPercent")
                Text(verbatim: "%")
                ActionButton(title: String(localized: "admin.hh_apply_families \(selectedCategories.count)"), systemImage: "checkmark", kind: .primary) {
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
                Button("admin.hh_select_all_rules") { selectedRules = Set(schedule.priceRules.compactMap(\.id)) }
                Spacer()
                ActionButton(title: String(localized: "admin.hh_delete_rules \(selectedRules.count)"), systemImage: "trash", kind: .danger) {
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
                Image(systemName: isSelected ? "checkmark.circle.fill" : "circle").font(.title3).foregroundStyle(isSelected ? Theme.primary : .secondary)
                VStack(alignment: .leading) {
                    Text(title).font(.subheadline.weight(.semibold)).lineLimit(1)
                    Text(subtitle).font(.caption).foregroundStyle(Theme.inkMuted)
                }
                Spacer(minLength: 0)
            }
            .padding(12)
            .background(RoundedRectangle(cornerRadius: Theme.smallRadius).fill(isSelected ? Theme.primary.opacity(0.12) : Theme.surface))
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
                TextField("admin.hh_schedule_name_placeholder", text: $name).accessibilityIdentifier("schedule.name")
                Section("admin.hh_days_section") {
                    HStack {
                        ForEach([1, 2, 3, 4, 5, 6, 0], id: \.self) { day in
                            ChipButton(title: HappyHourSchedule.dayLabels[day], isSelected: days.contains(day)) {
                                if days.contains(day) { days.remove(day) } else { days.insert(day) }
                            }
                        }
                    }
                }
                Section("admin.hh_hours_section") {
                    DatePicker("admin.hh_start_label", selection: $start, displayedComponents: .hourAndMinute)
                    DatePicker("admin.hh_end_label", selection: $end, displayedComponents: .hourAndMinute)
                }
                Toggle("admin.hh_applies_takeaway_toggle", isOn: $takeaway)
                Stepper("admin.hh_priority_stepper \(priority)", value: $priority, in: 1...10)
            }
            .navigationTitle("admin.hh_new_schedule_title")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("common.cancel") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("admin.hh_create_button") {
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
