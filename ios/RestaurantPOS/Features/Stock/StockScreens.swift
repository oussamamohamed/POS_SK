import SwiftUI
import PosKit

private func parseDecimal(_ text: String) -> Decimal? {
    Decimal(string: text.trimmingCharacters(in: .whitespaces).replacingOccurrences(of: ",", with: "."), locale: nil)
}

private func unitLabel(_ unit: String) -> LocalizedStringKey { LocalizedStringKey("stock.unit_short_\(unit)") }

/// Back-office : ingrédients et préparations.
struct StockAdminView: View {
    @Environment(AppModel.self) private var model
    @State private var editing: Editor?
    @State private var operation: Operation?

    struct Editor: Identifiable { let id = UUID(); var ingredient: Ingredient? }
    struct Operation: Identifiable { let id = UUID(); let kind: StockOpSheet.Kind; let ingredient: Ingredient }

    var body: some View {
        let store = model.stock
        List {
            Toggle("stock.auto_unavailable", isOn: Binding(get: { store.settings.autoUnavailableOnStockOut }, set: { on in Task { await store.setAutoUnavailable(on) } }))
                .accessibilityIdentifier("stock.autoUnavailable")
            Toggle("stock.print_allergens", isOn: Binding(get: { store.settings.printAllergensOnKitchenTicket }, set: { on in Task { await store.updateSettings { $0.printAllergensOnKitchenTicket = on } } }))
                .accessibilityIdentifier("stock.printAllergens")
            if store.ingredients.isEmpty {
                Text("stock.empty").foregroundStyle(Theme.inkMuted)
            }
            ForEach(store.ingredients) { ingredient in
                HStack {
                    VStack(alignment: .leading) {
                        Text(ingredient.name).font(.headline)
                        if ingredient.isPreparation {
                            Text("stock.prep_badge").font(.subheadline).foregroundStyle(Theme.inkMuted)
                        } else {
                            Text(priceText(ingredient)).font(.subheadline).foregroundStyle(Theme.inkMuted)
                        }
                    }
                    Spacer()
                    if let stock = ingredient.stockMilli {
                        let isLow = ingredient.lowStockMilli.map { stock <= $0 } ?? false
                        Text("stock.level \(Ingredient.quantity(fromMilli: stock).formatted()) \(String(localized: String.LocalizationValue("stock.unit_short_\(ingredient.unit)")))")
                            .font(.subheadline.monospacedDigit())
                            .foregroundStyle(isLow ? Theme.danger : Theme.inkMuted)
                            .accessibilityIdentifier("stock.level.\(ingredient.name)")
                        Menu {
                            Button("stock.op_receipt") { operation = Operation(kind: .receipt, ingredient: ingredient) }
                            Button("stock.op_count") { operation = Operation(kind: .count, ingredient: ingredient) }
                            Button("stock.op_loss") { operation = Operation(kind: .loss, ingredient: ingredient) }
                            Button("stock.op_history") { operation = Operation(kind: .history, ingredient: ingredient) }
                        } label: { Image(systemName: "ellipsis.circle").font(.title2) }
                        .accessibilityIdentifier("stock.menu.\(ingredient.name)")
                    }
                    if !ingredient.isActive { Badge(text: String(localized: "admin.inactive_badge"), color: .red) }
                }
                .contentShape(Rectangle())
                .onTapGesture { editing = Editor(ingredient: ingredient) }
                .swipeActions {
                    Button("stock.delete_action", role: .destructive) { Task { await store.delete(ingredient) } }
                }
                .accessibilityElement(children: .contain)
                .accessibilityIdentifier("stock.\(ingredient.name)")
            }
        }
        .listStyle(.insetGrouped)
        .overlay(alignment: .bottomTrailing) {
            Button { editing = Editor(ingredient: nil) } label: {
                Label("stock.new", systemImage: "plus").font(.headline).padding(.horizontal, 20).frame(height: 54)
            }
            .buttonStyle(.borderedProminent)
            .clipShape(Capsule())
            .padding(24)
            .accessibilityIdentifier("stock.add")
        }
        .task { await store.load(); await store.loadSettings() }
        .sheet(item: $editing) { IngredientEditorSheet(ingredient: $0.ingredient) }
        .sheet(item: $operation) { StockOpSheet(kind: $0.kind, ingredient: $0.ingredient) }
    }

    private func priceText(_ i: Ingredient) -> String {
        let price = NSDecimalNumber(decimal: Ingredient.price(fromMilliCents: i.unitCostMilliCents, unit: i.unit)).doubleValue
        let per = i.unit == "g" ? "kg" : i.unit == "ml" ? "l" : String(localized: "stock.unit_short_piece")
        return String(format: "%.2f € / %@", price, per)
    }
}

struct IngredientEditorSheet: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    let ingredient: Ingredient?
    @State private var name = ""
    @State private var unit = "g"
    @State private var priceText = ""
    @State private var lowText = ""
    @State private var isPreparation = false
    @State private var yieldText = ""
    @State private var allergenMask = 0

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("common.field_name", text: $name).accessibilityIdentifier("ingredient.name")
                    Picker("stock.unit", selection: $unit) {
                        ForEach(Ingredient.units, id: \.self) { Text(LocalizedStringKey("stock.unit_\($0)")).tag($0) }
                    }
                    .pickerStyle(.segmented)
                    .accessibilityIdentifier("ingredient.unit")
                    if !isPreparation {
                        TextField("stock.price_placeholder", text: $priceText)
                            .keyboardType(.decimalPad)
                            .accessibilityIdentifier("ingredient.price")
                    }
                    TextField("stock.low_placeholder", text: $lowText).keyboardType(.decimalPad)
                    Toggle("stock.is_preparation", isOn: $isPreparation).accessibilityIdentifier("ingredient.prep")
                    if isPreparation {
                        TextField("stock.yield_placeholder", text: $yieldText).keyboardType(.decimalPad)
                    }
                }
                Section("admin.allergens_title") { AllergenGrid(mask: $allergenMask) }
            }
            .navigationTitle(ingredient == nil ? "stock.new" : "stock.edit")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("common.cancel") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("common.save") {
                        Task { if await model.stock.save(id: ingredient?.id, draft: draft()) { dismiss() } }
                    }
                    .accessibilityIdentifier("ingredient.save")
                }
            }
            .onAppear(perform: load)
        }
    }

    private func load() {
        guard let i = ingredient else { return }
        name = i.name
        unit = i.unit
        priceText = i.isPreparation ? "" : "\(Ingredient.price(fromMilliCents: i.unitCostMilliCents, unit: i.unit))"
        lowText = i.lowStockMilli.map { "\(Ingredient.quantity(fromMilli: $0))" } ?? ""
        isPreparation = i.isPreparation
        yieldText = i.yieldMilli.map { "\(Ingredient.quantity(fromMilli: $0))" } ?? ""
        allergenMask = i.allergenMask
    }

    private func draft() -> IngredientDraft {
        // Le prix d'une préparation est recalculé depuis sa fiche : on garde celui du serveur.
        let price = isPreparation ? (ingredient?.unitCostMilliCents ?? 0) : Ingredient.milliCents(fromPrice: parseDecimal(priceText) ?? 0, unit: unit)
        return IngredientDraft(
            name: name, unit: unit, unitCostMilliCents: price, isPreparation: isPreparation,
            yieldMilli: isPreparation ? parseDecimal(yieldText).map(Ingredient.milli(fromQuantity:)) : nil, allergenMask: allergenMask,
            lowStockMilli: parseDecimal(lowText).map(Ingredient.milli(fromQuantity:)), isActive: ingredient?.isActive ?? true
        )
    }
}

/// Fiche technique d'un produit dans l'éditeur d'article : lignes, ajout, coût matière en direct.
struct RecipeEditorSection: View {
    @Environment(AppModel.self) private var model
    @Binding var lines: [RecipeLine]
    @Binding var isDirty: Bool
    @State private var picked: UUID?
    @State private var qtyText = ""

    var body: some View {
        let store = model.stock
        Section("stock.recipe_title") {
            if lines.isEmpty { Text("stock.recipe_none").foregroundStyle(Theme.inkMuted) }
            ForEach(lines, id: \.ingredientId) { line in
                let ingredient = store.ingredient(id: line.ingredientId)
                HStack {
                    Text(ingredient?.name ?? "?")
                    Spacer()
                    Text("\(Ingredient.quantity(fromMilli: line.quantityMilli).formatted())")
                    if let unit = ingredient?.unit { Text(unitLabel(unit)).foregroundStyle(Theme.inkMuted) }
                }
                .swipeActions {
                    Button("stock.delete_action", role: .destructive) {
                        lines.removeAll { $0.ingredientId == line.ingredientId }
                        isDirty = true
                    }
                }
            }
            let available = store.ingredients.filter { ing in ing.isActive && !lines.contains { $0.ingredientId == ing.id } }
            if !available.isEmpty {
                Picker("stock.recipe_pick", selection: $picked) {
                    Text("—").tag(UUID?.none)
                    ForEach(available) { Text($0.name).tag(Optional($0.id)) }
                }
                .accessibilityIdentifier("recipe.pick")
                TextField("stock.recipe_qty", text: $qtyText).keyboardType(.decimalPad).accessibilityIdentifier("recipe.qty")
                Button("stock.recipe_add") {
                    guard let id = picked, let qty = parseDecimal(qtyText), qty > 0 else { return }
                    lines.append(RecipeLine(ingredientId: id, quantityMilli: Ingredient.milli(fromQuantity: qty)))
                    isDirty = true
                    picked = nil
                    qtyText = ""
                }
                .accessibilityIdentifier("recipe.add")
            }
            HStack {
                Text("stock.recipe_cost").bold()
                Spacer()
                Text(lines.isEmpty ? "—" : Money(cents: Int(store.liveCostCents(lines))).formatted).bold().accessibilityIdentifier("recipe.cost")
            }
        }
    }
}

/// Réception, inventaire, perte (pavé numérique intégré : pas de clavier système en salle) et historique des mouvements.
struct StockOpSheet: View {
    enum Kind { case receipt, count, loss, history }

    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    let kind: Kind
    let ingredient: Ingredient
    @State private var quantity = ""
    @State private var price = ""
    @State private var note = ""
    @State private var movements: [StockMovement] = []
    @State private var field = Field.quantity

    enum Field { case quantity, price }

    private var title: LocalizedStringKey {
        switch kind {
        case .receipt: "stock.op_receipt"
        case .count: "stock.op_count"
        case .loss: "stock.op_loss"
        case .history: "stock.op_history"
        }
    }

    var body: some View {
        NavigationStack {
            Group {
                if kind == .history { history } else { form }
            }
            .navigationTitle(title)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("common.cancel") { dismiss() } }
                if kind != .history {
                    ToolbarItem(placement: .confirmationAction) {
                        Button("common.save") { Task { if await save() { dismiss() } } }.accessibilityIdentifier("stockop.save")
                    }
                }
            }
            .task {
                if kind == .history { movements = await model.stock.movements(of: ingredient) }
                if kind == .receipt { price = "\(Ingredient.price(fromMilliCents: ingredient.unitCostMilliCents, unit: ingredient.unit))" }
            }
        }
        .presentationDetents([.large])
    }

    private var form: some View {
        Form {
            Section {
                LabeledContent(ingredient.name) { Text(LocalizedStringKey("stock.unit_short_\(ingredient.unit)")) }
                valueRow(kind == .count ? "stock.counted" : "stock.qty", text: quantity, field: .quantity, id: "stockop.qty")
                if kind == .receipt { valueRow("stock.price_placeholder", text: price, field: .price, id: "stockop.price") }
                TextField("stock.note", text: $note)
            }
            Section { keypad }
        }
    }

    private func valueRow(_ label: LocalizedStringKey, text: String, field target: Field, id: String) -> some View {
        Button { field = target } label: {
            HStack {
                Text(label).foregroundStyle(Theme.ink)
                Spacer()
                Text(text.isEmpty ? "0" : text).font(.title3.monospacedDigit()).foregroundStyle(field == target ? Theme.primary : Theme.ink)
            }
        }
        .accessibilityIdentifier(id)
    }

    private var keypad: some View {
        LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 8), count: 3), spacing: 8) {
            ForEach(["7", "8", "9", "4", "5", "6", "1", "2", "3", ".", "0", "⌫"], id: \.self) { key in
                Button { press(key) } label: { Text(key).font(.title2).frame(maxWidth: .infinity, minHeight: 56) }
                    .buttonStyle(.bordered)
                    .accessibilityIdentifier("stockop.key.\(key)")
            }
        }
    }

    private func press(_ key: String) {
        func apply(_ text: inout String) {
            if key == "⌫" { _ = text.popLast() } else if key != "." || !text.contains(".") { text += key }
        }
        if field == .quantity { apply(&quantity) } else { apply(&price) }
    }

    private var history: some View {
        List {
            if movements.isEmpty { Text("stock.history_empty").foregroundStyle(Theme.inkMuted) }
            ForEach(movements) { m in
                HStack {
                    VStack(alignment: .leading) {
                        Text(LocalizedStringKey("stock.kind_\(m.kind)"))
                        Text(m.createdAtUtc.formatted(date: .abbreviated, time: .shortened) + (m.note.map { " · \($0)" } ?? "")).font(.caption).foregroundStyle(Theme.inkMuted)
                    }
                    Spacer()
                    Text("\(m.quantityMilli > 0 ? "+" : "")\(Ingredient.quantity(fromMilli: m.quantityMilli).formatted()) \(String(localized: String.LocalizationValue("stock.unit_short_\(ingredient.unit)")))")
                        .monospacedDigit()
                }
                .accessibilityIdentifier("stock.movement.\(m.kind)")
            }
        }
    }

    private func save() async -> Bool {
        guard let qty = Decimal(string: quantity, locale: nil), kind == .count ? qty >= 0 : qty > 0 else { return false }
        let note = note.trimmingCharacters(in: .whitespaces).isEmpty ? nil : note
        switch kind {
        case .receipt: return await model.stock.receive(ingredient, quantity: qty, price: Decimal(string: price, locale: nil) ?? 0, note: note)
        case .count: return await model.stock.count(ingredient, counted: qty, note: note)
        case .loss: return await model.stock.loss(ingredient, quantity: qty, note: note)
        case .history: return true
        }
    }
}

/// Ajustements de consommation des options de modificateur d'un produit : quantité ajoutée (ou retirée si négative) par portion vendue.
struct OptionConsumptionSection: View {
    @Environment(AppModel.self) private var model
    let groups: [ModifierGroup]
    @Binding var items: [UUID: [ModifierConsumptionItem]]
    @Binding var dirty: Set<UUID>
    @State private var picked: [UUID: UUID] = [:]
    @State private var qty: [UUID: String] = [:]

    var body: some View {
        let store = model.stock
        let raw = store.ingredients.filter { $0.isActive && !$0.isPreparation }
        Section("stock.options_title") {
            ForEach(groups.flatMap { g in g.options.map { (group: g, option: $0) } }, id: \.option.id) { entry in
                VStack(alignment: .leading, spacing: 6) {
                    Text("\(entry.group.groupName) · \(entry.option.name)").font(.headline)
                    ForEach(items[entry.option.id] ?? [], id: \.ingredientId) { item in
                        let ingredient = store.ingredient(id: item.ingredientId)
                        HStack {
                            Text(ingredient?.name ?? "?")
                            Spacer()
                            Text("\(item.deltaQuantityMilli > 0 ? "+" : "")\(Ingredient.quantity(fromMilli: item.deltaQuantityMilli).formatted())").monospacedDigit()
                            if let unit = ingredient?.unit { Text(LocalizedStringKey("stock.unit_short_\(unit)")).foregroundStyle(Theme.inkMuted) }
                            Button { remove(item, from: entry.option.id) } label: { Image(systemName: "xmark.circle") }
                                .buttonStyle(.borderless)
                        }
                    }
                    HStack {
                        Picker("stock.recipe_pick", selection: Binding(get: { picked[entry.option.id] }, set: { picked[entry.option.id] = $0 })) {
                            Text("—").tag(UUID?.none)
                            ForEach(raw) { Text($0.name).tag(Optional($0.id)) }
                        }
                        .accessibilityIdentifier("option.pick.\(entry.option.name)")
                        TextField("stock.option_qty", text: Binding(get: { qty[entry.option.id] ?? "" }, set: { qty[entry.option.id] = $0 }))
                            .keyboardType(.numbersAndPunctuation).frame(width: 90)
                            .accessibilityIdentifier("option.qty.\(entry.option.name)")
                        Button("stock.recipe_add") { add(to: entry.option.id) }
                            .buttonStyle(.borderless) // sans cela, toute la ligne du formulaire déclenche le bouton
                            .accessibilityIdentifier("option.add.\(entry.option.name)")
                    }
                }
            }
        }
        .task {
            for option in groups.flatMap(\.options) where items[option.id] == nil { items[option.id] = await store.consumption(of: option.id) }
        }
    }

    private func add(to option: UUID) {
        guard let ingredient = picked[option], let value = Decimal(string: (qty[option] ?? "").replacingOccurrences(of: ",", with: "."), locale: nil), value != 0,
              !(items[option] ?? []).contains(where: { $0.ingredientId == ingredient }) else { return }
        items[option, default: []].append(ModifierConsumptionItem(ingredientId: ingredient, deltaQuantityMilli: Ingredient.milli(fromQuantity: value)))
        dirty.insert(option)
        picked[option] = nil
        qty[option] = ""
    }

    private func remove(_ item: ModifierConsumptionItem, from option: UUID) {
        items[option]?.removeAll { $0.ingredientId == item.ingredientId }
        dirty.insert(option)
    }
}
