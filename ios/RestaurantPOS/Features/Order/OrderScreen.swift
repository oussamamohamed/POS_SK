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
                TicketPanel()
                    .frame(width: min(Theme.ticketWidth, proxy.size.width * 0.42))
                    .overlay(alignment: .leading) { Theme.line.frame(width: 1) }
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
                    ChipButton(title: String(localized: "order.all_menu"), systemImage: "fork.knife", isSelected: catalog.selectedCategoryId == CatalogStore.allCategoryId) {
                        Task { await catalog.selectCategory(CatalogStore.allCategoryId) }
                    }
                    .accessibilityIdentifier("category.ALL")
                    ForEach(catalog.categories) { category in
                        ChipButton(title: category.name, isSelected: catalog.selectedCategoryId == category.id, tint: Color(hex: category.colorHex) ?? Theme.primary) {
                            Task { await catalog.selectCategory(category.id) }
                        }
                        .accessibilityIdentifier("category.\(category.id)")
                    }
                }
                .padding(.horizontal, Theme.Space.l)
            }
            .padding(.top, Theme.Space.l)

            if !catalog.quickKeys.isEmpty {
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: 8) {
                        Label("order.quick_products", systemImage: "bolt.fill").font(.posCaption).foregroundStyle(Theme.warning)
                        ForEach(catalog.quickKeys) { product in
                            Button {
                                onSelect(product, .direct)
                            } label: {
                                Text("\(product.name) · \u{2066}\(effectivePrice(product).formatted)\u{2069}")
                                    .font(.posLabel)
                                    .foregroundStyle(Theme.ink)
                                    .lineLimit(1)
                                    .padding(.horizontal, Theme.Space.m)
                                    .frame(minHeight: Theme.touchMin)
                                    .background(Capsule().fill(Theme.raised))
                            }
                            .buttonStyle(.plain)
                            .accessibilityIdentifier("quickkey.\(product.name)")
                        }
                    }
                    .padding(.horizontal, 16)
                }
            }

            ProductGrid(onSelect: onSelect, onCustomize: onCustomize)
                .padding(.horizontal, Theme.Space.l)

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
            let spacing: CGFloat = Theme.Space.m
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
                                Button("order.add_as_suite", systemImage: "arrow.turn.down.right") { onSelect(product, .suite) }
                                Button("order.add_as_dessert", systemImage: "birthday.cake") { onSelect(product, .dessert) }
                                Button("order.customize", systemImage: "slider.horizontal.3") { onCustomize(product, .direct) }
                            }
                    } else {
                        RoundedRectangle(cornerRadius: Theme.smallRadius, style: .continuous)
                            .strokeBorder(Theme.lineStrong.opacity(0.6), style: StrokeStyle(lineWidth: 1.5, dash: [4, 4]))
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

/// Tuile d'article : fond teinté par la catégorie, quantité déjà au ticket en pastille.
struct ProductTile: View {
    @Environment(AppModel.self) private var model
    let product: Product
    let label: String
    let colorHex: String?

    var body: some View {
        let tint = Color(hex: colorHex) ?? Theme.primary
        let hh = model.happyHour.displayPrice(for: product)
        let quantity = model.ticket.lines.filter { $0.productId == product.id }.reduce(0) { $0 + $1.quantity }
        let shape = RoundedRectangle(cornerRadius: Theme.smallRadius, style: .continuous)
        VStack(alignment: .leading, spacing: Theme.Space.xs) {
            HStack(spacing: 6) {
                Circle().fill(tint).frame(width: 10, height: 10)
                Text((product.station ?? "").replacingOccurrences(of: "_", with: " ").uppercased())
                    .font(.system(size: 11, weight: .bold))
                    .tracking(0.4)
                    .foregroundStyle(Theme.inkMuted)
                    .lineLimit(1)
                Spacer(minLength: 0)
                if quantity > 0 {
                    Text("×\(quantity)")
                        .font(.system(size: 14, weight: .semibold, design: .monospaced))
                        .foregroundStyle(Theme.onPrimary)
                        .padding(.horizontal, 8)
                        .frame(minWidth: 28, minHeight: 28)
                        .background(Capsule().fill(Theme.primary))
                } else if product.hasModifiers {
                    Image(systemName: "slider.horizontal.3").font(.caption.weight(.semibold)).foregroundStyle(Theme.inkMuted)
                }
            }
            Spacer(minLength: 0)
            Text(label)
                .font(.posHeadline)
                .foregroundStyle(Theme.ink)
                .lineLimit(2)
                .minimumScaleFactor(0.75)
                .multilineTextAlignment(.leading)
            if let hh {
                HStack(spacing: 6) {
                    Text(hh.standardPrice.formatted).strikethrough().font(.system(size: 13, design: .monospaced)).foregroundStyle(Theme.inkSubtle).environment(\.layoutDirection, .leftToRight)
                    Text(hh.happyHourPrice.formatted).font(.system(size: 15, weight: .semibold, design: .monospaced)).foregroundStyle(Theme.happyInk).environment(\.layoutDirection, .leftToRight)
                }
            } else {
                Text(product.price.formatted).font(.posAmountSmall).foregroundStyle(Theme.inkMuted).environment(\.layoutDirection, .leftToRight)
            }
        }
        .padding(Theme.Space.m)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .background(
            shape.fill(Theme.surface)
                .overlay(shape.fill(tint.opacity(0.14)))
        )
        .cardShadow(radius: Theme.smallRadius)
        .contentShape(shape)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("\(label), \((hh?.happyHourPrice ?? product.price).formatted)")
        .accessibilityValue(quantity > 0 ? "order.product_tile_quantity_value \(quantity)" : "")
        .accessibilityHint(product.hasModifiers ? "order.product_tile_hint_options" : "order.product_tile_hint_add")
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
                    .fill(index == page ? Theme.primary : Theme.lineStrong)
                    .frame(width: 8, height: 8)
                    .onTapGesture { onSelect(index) }
            }
            Text("order.page_indicator \(page + 1)/\(count)").font(.posLabel).foregroundStyle(Theme.inkMuted).accessibilityIdentifier("grid.pageLabel")
            Button { onSelect(page + 1) } label: { Image(systemName: "chevron.right") }
                .disabled(page >= count - 1)
                .accessibilityIdentifier("grid.next")
        }
        .font(.headline)
    }
}
