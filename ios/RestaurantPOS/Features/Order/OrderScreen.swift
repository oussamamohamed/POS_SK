import SwiftUI
import PosKit

/// Écran de vente : catalogue à gauche, ticket à droite.
struct OrderScreen: View {
    @Environment(AppModel.self) private var model
    @State private var modifierProduct: ModifierRequest?

    struct ModifierRequest: Identifiable {
        let product: Product
        var course: CourseType = .direct
        var id: UUID { product.id }
    }

    var body: some View {
        GeometryReader { proxy in
            HStack(spacing: 0) {
                CatalogPane { product, course in
                    if product.hasModifiers {
                        modifierProduct = ModifierRequest(product: product, course: course)
                    } else {
                        model.ticket.add(product, course: course)
                        Haptics.tap()
                    }
                } onCustomize: { product, course in
                    modifierProduct = ModifierRequest(product: product, course: course)
                }
                Divider()
                TicketPanel()
                    .frame(width: min(Theme.ticketWidth, proxy.size.width * 0.42))
            }
        }
        .sheet(item: $modifierProduct) { request in
            ModifierSheet(product: request.product, course: request.course)
        }
    }
}

// MARK: - Catalogue

struct CatalogPane: View {
    @Environment(AppModel.self) private var model
    let onSelect: (Product, CourseType) -> Void
    let onCustomize: (Product, CourseType) -> Void

    var body: some View {
        let catalog = model.catalog
        VStack(spacing: 12) {
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 8) {
                    ChipButton(title: "Tout le menu", systemImage: "fork.knife", isSelected: catalog.selectedCategoryId == CatalogStore.allCategoryId) {
                        Task { await catalog.selectCategory(CatalogStore.allCategoryId) }
                    }
                    .accessibilityIdentifier("category.ALL")
                    ForEach(catalog.categories) { category in
                        ChipButton(title: category.name, systemImage: category.symbolName, isSelected: catalog.selectedCategoryId == category.id, tint: Color(hex: category.colorHex) ?? .accentColor) {
                            Task { await catalog.selectCategory(category.id) }
                        }
                        .accessibilityIdentifier("category.\(category.id)")
                    }
                }
                .padding(.horizontal, 16)
            }
            .padding(.top, 12)

            if !catalog.quickKeys.isEmpty {
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: 8) {
                        Label("Rapide", systemImage: "bolt.fill").font(.caption.weight(.bold)).foregroundStyle(.orange)
                        ForEach(catalog.quickKeys) { product in
                            Button {
                                onSelect(product, .direct)
                            } label: {
                                Text("\(product.name) · \(effectivePrice(product).formatted)")
                                    .font(.footnote.weight(.semibold))
                                    .lineLimit(1)
                                    .padding(.horizontal, 12)
                                    .frame(minHeight: 40)
                                    .background(Capsule().fill(Color.orange.opacity(0.14)))
                            }
                            .buttonStyle(.plain)
                            .accessibilityIdentifier("quickkey.\(product.name)")
                        }
                    }
                    .padding(.horizontal, 16)
                }
            }

            ProductGrid(onSelect: onSelect, onCustomize: onCustomize)
                .padding(.horizontal, 16)

            if catalog.totalPages > 1 {
                PageControl(page: catalog.pageIndex, count: catalog.totalPages) { page in
                    Task { await catalog.goToPage(page) }
                }
                .padding(.bottom, 8)
            }
        }
        .padding(.bottom, 12)
    }

    private func effectivePrice(_ product: Product) -> Money {
        model.happyHour.displayPrice(for: product)?.happyHourPrice ?? product.price
    }
}

struct ProductGrid: View {
    @Environment(AppModel.self) private var model
    let onSelect: (Product, CourseType) -> Void
    let onCustomize: (Product, CourseType) -> Void

    var body: some View {
        let catalog = model.catalog
        GeometryReader { proxy in
            let spacing: CGFloat = 10
            let columns = CGFloat(catalog.columns)
            let rows = CGFloat(catalog.rows)
            let width = (proxy.size.width - spacing * (columns - 1)) / columns
            let height = max(Theme.touchTarget + 20, (proxy.size.height - spacing * (rows - 1)) / rows)
            LazyVGrid(columns: Array(repeating: GridItem(.fixed(width), spacing: spacing), count: catalog.columns), spacing: spacing) {
                ForEach(catalog.cells) { cell in
                    if let product = cell.product {
                        ProductTile(product: product, label: cell.label ?? product.name, colorHex: cell.colorHex)
                            .frame(height: height)
                            .onTapGesture { onSelect(product, .direct) }
                            .contextMenu {
                                Button("Ajouter en Suite", systemImage: "arrow.turn.down.right") { onSelect(product, .suite) }
                                Button("Ajouter en Dessert", systemImage: "birthday.cake") { onSelect(product, .dessert) }
                                Button("Personnaliser…", systemImage: "slider.horizontal.3") { onCustomize(product, .direct) }
                            }
                    } else {
                        RoundedRectangle(cornerRadius: Theme.smallRadius, style: .continuous)
                            .strokeBorder(Color(.separator), style: StrokeStyle(lineWidth: 1, dash: [4, 4]))
                            .frame(height: height)
                            .accessibilityHidden(true)
                    }
                }
            }
        }
        .contentShape(Rectangle())
        .gesture(
            DragGesture(minimumDistance: 40)
                .onEnded { value in
                    guard abs(value.translation.width) > 60, abs(value.translation.height) < 60 else { return }
                    Task {
                        if value.translation.width < 0 { await catalog.nextPage() } else { await catalog.previousPage() }
                    }
                }
        )
        .animation(.snappy, value: catalog.pageIndex)
        .animation(.snappy, value: catalog.selectedCategoryId)
    }
}

struct ProductTile: View {
    @Environment(AppModel.self) private var model
    let product: Product
    let label: String
    let colorHex: String?

    var body: some View {
        let tint = Color(hex: colorHex) ?? .accentColor
        let hh = model.happyHour.displayPrice(for: product)
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 4) {
                Badge(text: product.station.replacingOccurrences(of: "_", with: " "), color: .secondary)
                Spacer(minLength: 0)
                if product.hasModifiers { Image(systemName: "slider.horizontal.3").font(.caption).foregroundStyle(tint) }
            }
            Spacer(minLength: 0)
            Text(label)
                .font(.headline)
                .lineLimit(3)
                .minimumScaleFactor(0.75)
                .multilineTextAlignment(.leading)
            if let hh {
                HStack(spacing: 6) {
                    Text(hh.standardPrice.formatted).strikethrough().font(.caption).foregroundStyle(.secondary)
                    Text(hh.happyHourPrice.formatted).font(.subheadline.weight(.bold)).foregroundStyle(Theme.happyHour)
                }
            } else {
                Text(product.price.formatted).font(.subheadline.weight(.semibold)).foregroundStyle(.secondary)
            }
        }
        .padding(12)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .background(
            RoundedRectangle(cornerRadius: Theme.smallRadius, style: .continuous)
                .fill(Color(.secondarySystemGroupedBackground))
                .overlay(alignment: .top) {
                    UnevenRoundedRectangle(topLeadingRadius: Theme.smallRadius, topTrailingRadius: Theme.smallRadius)
                        .fill(tint)
                        .frame(height: 5)
                }
        )
        .contentShape(RoundedRectangle(cornerRadius: Theme.smallRadius))
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("\(label), \((hh?.happyHourPrice ?? product.price).formatted)")
        .accessibilityHint(product.hasModifiers ? "Ouvre les options" : "Ajoute au ticket")
        .accessibilityAddTraits(.isButton)
        .accessibilityIdentifier("product.\(product.name)")
    }
}

struct PageControl: View {
    let page: Int
    let count: Int
    let onSelect: (Int) -> Void

    var body: some View {
        HStack(spacing: 14) {
            Button { onSelect(page - 1) } label: { Image(systemName: "chevron.left") }
                .disabled(page == 0)
                .accessibilityIdentifier("grid.previous")
            ForEach(0..<count, id: \.self) { index in
                Circle()
                    .fill(index == page ? Color.accentColor : Color(.tertiaryLabel))
                    .frame(width: 8, height: 8)
                    .onTapGesture { onSelect(index) }
            }
            Text("Page \(page + 1)/\(count)").font(.caption).foregroundStyle(.secondary).accessibilityIdentifier("grid.pageLabel")
            Button { onSelect(page + 1) } label: { Image(systemName: "chevron.right") }
                .disabled(page >= count - 1)
                .accessibilityIdentifier("grid.next")
        }
        .font(.headline)
    }
}
