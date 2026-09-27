import POSKit
import SwiftUI

/// Prise de commande : carte tactile à gauche, note en cours à droite.
struct OrderEntryView: View {
    @Environment(AppModel.self) private var app

    var body: some View {
        if app.order.context == nil {
            EmptyStateView(
                title: "Aucune table sélectionnée",
                message: "Choisissez une table dans la salle ou démarrez une vente directe au comptoir.",
                systemImage: "fork.knife"
            ) {
                VStack(spacing: 12) {
                    Button {
                        app.section = .floor
                    } label: {
                        Label("Voir la salle", systemImage: "square.grid.3x3.topleft.filled")
                    }
                    .buttonStyle(ActionButtonStyle())
                    .accessibilityIdentifier("order.goFloor")

                    Button {
                        Task { await app.openCounter() }
                    } label: {
                        Label("Vente comptoir", systemImage: "bag")
                    }
                    .buttonStyle(ActionButtonStyle(prominent: false))
                    .accessibilityIdentifier("order.goCounter")
                }
            }
        } else {
            HStack(spacing: 0) {
                CatalogPane()
                    .frame(maxWidth: .infinity)
                Divider()
                CartPanel()
                    .frame(width: 400)
                    .background(Color.posSurface)
            }
        }
    }
}

// MARK: - Carte

struct CatalogPane: View {
    @Environment(AppModel.self) private var app
    @State private var productForOptions: Product?

    private let columns = [GridItem(.adaptive(minimum: 150, maximum: 220), spacing: 12)]

    var body: some View {
        @Bindable var catalog = app.catalog

        VStack(spacing: 14) {
            HStack(spacing: 10) {
                Image(systemName: "magnifyingglass").foregroundStyle(.secondary)
                TextField("Rechercher un article", text: $catalog.searchText)
                    .textInputAutocapitalization(.never)
                    .autocorrectionDisabled()
                    .submitLabel(.search)
                    .accessibilityIdentifier("catalog.search")
                if !catalog.searchText.isEmpty {
                    Button {
                        catalog.searchText = ""
                    } label: {
                        Image(systemName: "xmark.circle.fill").foregroundStyle(.secondary)
                    }
                    .accessibilityIdentifier("catalog.search.clear")
                }
            }
            .padding(12)
            .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(Color.posSurface))

            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 10) {
                    if catalog.hasFavorites {
                        categoryChip(id: CatalogModel.favoritesID, title: "Favoris", color: .yellow, systemImage: "star.fill")
                    }
                    ForEach(catalog.categories) { category in
                        categoryChip(id: category.id, title: category.name, color: Color(hex: category.colorHex) ?? .accentColor, systemImage: nil)
                    }
                }
                .padding(.vertical, 2)
            }

            ScrollView {
                if catalog.isLoading && catalog.products.isEmpty {
                    ProgressView().padding(60)
                } else if catalog.visibleProducts.isEmpty {
                    ContentUnavailableView.search(text: catalog.searchText)
                } else {
                    LazyVGrid(columns: columns, spacing: 12) {
                        ForEach(catalog.visibleProducts) { product in
                            ProductTile(product: product, accent: accent(for: product)) {
                                tap(product)
                            }
                            .contextMenu {
                                Button {
                                    productForOptions = product
                                } label: {
                                    Label("Options et note cuisine…", systemImage: "text.badge.plus")
                                }
                            }
                        }
                    }
                    .padding(.bottom, 20)
                }
            }
            .scrollDismissesKeyboard(.immediately)
        }
        .padding(20)
        .sensoryFeedback(.impact(weight: .light), trigger: app.order.addCounter)
        .sheet(item: $productForOptions) { product in
            ModifierSheet(product: product) { options, note in
                app.order.add(product, modifiers: options, note: note)
            }
        }
    }

    private func categoryChip(id: String, title: String, color: Color, systemImage: String?) -> some View {
        let selected = app.catalog.selectedCategoryID == id && app.catalog.searchText.isEmpty
        return Button {
            app.catalog.searchText = ""
            app.catalog.selectedCategoryID = id
        } label: {
            HStack(spacing: 8) {
                if let systemImage {
                    Image(systemName: systemImage)
                } else {
                    Circle().fill(color).frame(width: 10, height: 10)
                }
                Text(title).lineLimit(1)
            }
            .font(.subheadline.weight(.semibold))
            .padding(.horizontal, 16)
            .frame(minHeight: 44)
            .foregroundStyle(selected ? Color.white : Color.primary)
            .background(Capsule().fill(selected ? color : Color.posSurface))
        }
        .buttonStyle(.plain)
        .accessibilityIdentifier("category.\(id == CatalogModel.favoritesID ? "favorites" : id)")
        .accessibilityAddTraits(selected ? .isSelected : [])
    }

    private func accent(for product: Product) -> Color {
        Color(hex: product.colorHex) ?? Color(hex: app.catalog.category(for: product)?.colorHex) ?? .accentColor
    }

    private func tap(_ product: Product) {
        if product.hasModifiers {
            productForOptions = product
        } else {
            app.order.add(product)
        }
    }
}

struct ProductTile: View {
    var product: Product
    var accent: Color
    var action: () -> Void

    var body: some View {
        Button(action: action) {
            VStack(alignment: .leading, spacing: 8) {
                Text(product.name)
                    .font(.headline)
                    .foregroundStyle(.primary)
                    .multilineTextAlignment(.leading)
                    .lineLimit(2)
                    .frame(maxWidth: .infinity, alignment: .leading)
                Spacer(minLength: 0)
                HStack {
                    Text(product.price.formatted)
                        .font(.subheadline.weight(.semibold))
                        .monospacedDigit()
                        .foregroundStyle(.secondary)
                    Spacer()
                    if product.hasModifiers {
                        Image(systemName: "slider.horizontal.3")
                            .foregroundStyle(accent)
                            .accessibilityLabel("Options")
                    }
                }
            }
            .padding(14)
            .frame(maxWidth: .infinity, minHeight: 104, alignment: .topLeading)
            .background(
                RoundedRectangle(cornerRadius: 16, style: .continuous)
                    .fill(Color.posSurface)
            )
            .overlay(alignment: .top) {
                UnevenRoundedRectangle(topLeadingRadius: 16, topTrailingRadius: 16, style: .continuous)
                    .fill(accent)
                    .frame(height: 6)
            }
            .overlay {
                if !product.isSellable {
                    RoundedRectangle(cornerRadius: 16, style: .continuous)
                        .fill(Color.posBackground.opacity(0.7))
                        .overlay(Text("Indisponible").font(.caption.weight(.bold)).foregroundStyle(.red))
                }
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityElement(children: .combine)
        .accessibilityIdentifier("product.\(product.name)")
    }
}

// MARK: - Modificateurs

/// Choix des options d'un article (cuisson, suppléments) et note pour la cuisine.
struct ModifierSheet: View {
    var product: Product
    var onAdd: ([ModifierOption], String?) -> Void
    @State private var selection: ModifierSelection
    @Environment(\.dismiss) private var dismiss

    init(product: Product, onAdd: @escaping ([ModifierOption], String?) -> Void) {
        self.product = product
        self.onAdd = onAdd
        _selection = State(initialValue: ModifierSelection(product: product))
    }

    var body: some View {
        NavigationStack {
            Form {
                if let description = product.description, !description.isEmpty {
                    Section {
                        Text(description).foregroundStyle(.secondary)
                    }
                }
                ForEach(product.modifierGroups) { group in
                    Section {
                        ForEach(group.options) { option in
                            optionRow(option, in: group)
                        }
                    } header: {
                        HStack {
                            Text(group.groupName)
                            Spacer()
                            Text(rule(for: group))
                                .foregroundStyle(selection.missingGroups.contains(group) ? Color.red : Color.secondary)
                        }
                    }
                }
                Section("Note pour la cuisine") {
                    TextField("Ex. sans oignons, allergie arachide…", text: $selection.note, axis: .vertical)
                        .lineLimit(1...3)
                        .accessibilityIdentifier("modifiers.note")
                }
            }
            .navigationTitle(product.name)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Annuler") { dismiss() }
                        .accessibilityIdentifier("modifiers.cancel")
                }
            }
            .safeAreaInset(edge: .bottom) {
                Button {
                    onAdd(selection.selectedOptions, selection.note)
                    dismiss()
                } label: {
                    Text("Ajouter — \(selection.unitTotal.formatted)")
                }
                .buttonStyle(ActionButtonStyle())
                .disabled(!selection.isValid)
                .padding()
                .background(.bar)
                .accessibilityIdentifier("modifiers.add")
            }
        }
    }

    private func optionRow(_ option: ModifierOption, in group: ModifierGroup) -> some View {
        let isSelected = selection.isSelected(option, in: group)
        return Button {
            selection.toggle(option, in: group)
        } label: {
            HStack {
                Image(systemName: isSelected ? (group.isSingleChoice ? "largecircle.fill.circle" : "checkmark.square.fill") : (group.isSingleChoice ? "circle" : "square"))
                    .foregroundStyle(isSelected ? Color.accentColor : Color.secondary)
                    .font(.title3)
                Text(option.name).foregroundStyle(.primary)
                Spacer()
                if option.extraPrice.isPositive {
                    Text("+\(option.extraPrice.formatted)")
                        .foregroundStyle(.secondary)
                        .monospacedDigit()
                }
            }
            .frame(minHeight: 44)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityIdentifier("modifier.\(option.name)")
        .accessibilityAddTraits(isSelected ? .isSelected : [])
    }

    private func rule(for group: ModifierGroup) -> String {
        if group.requiredSelections > 0 && group.isSingleChoice { return "Obligatoire" }
        if group.requiredSelections > 0 { return "Min. \(group.requiredSelections)" }
        if group.isSingleChoice { return "Facultatif" }
        return "Jusqu'à \(group.allowedSelections)"
    }
}
