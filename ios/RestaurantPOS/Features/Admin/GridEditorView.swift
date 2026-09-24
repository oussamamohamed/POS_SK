import SwiftUI
import PosKit

/// Éditeur de grille tactile : glisser-déposer, personnalisation des cases, formats et pages.
struct GridEditorView: View {
    @Environment(AppModel.self) private var model
    @State private var editingSlot: SlotEditor?
    @State private var columns = 4
    @State private var rows = 4
    @State private var applyToAll = false

    struct SlotEditor: Identifiable {
        let position: GridPosition
        var slot: GridSlot?
        var id: GridPosition { position }
    }

    var body: some View {
        let editor = model.gridEditor
        HStack(spacing: 0) {
            VStack(alignment: .leading, spacing: 14) {
                toolbar
                if let layout = editor.layout {
                    pageTabs(layout)
                    matrix(layout)
                } else {
                    ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)
                }
            }
            .padding(20)
            Divider()
            productList
                .frame(width: 280)
        }
        .task {
            if editor.layout == nil { await editor.select(category: editor.categoryId) }
            syncDimensions()
        }
        .onChange(of: editor.layout?.id) { syncDimensions() }
        .sheet(item: $editingSlot) { slot in
            SlotEditorSheet(position: slot.position, slot: slot.slot)
        }
    }

    private func syncDimensions() {
        columns = model.gridEditor.layout?.columnsCount ?? 4
        rows = model.gridEditor.layout?.rowsCount ?? 4
    }

    private var toolbar: some View {
        let editor = model.gridEditor
        return HStack(spacing: 12) {
            Picker("Famille", selection: Binding(get: { editor.categoryId }, set: { id in Task { await editor.select(category: id) } })) {
                Text("Tout le menu").tag(CatalogStore.allCategoryId)
                ForEach(model.catalog.categories) { Text($0.name).tag($0.id) }
            }
            .pickerStyle(.menu)
            .accessibilityIdentifier("grid.category")

            Menu {
                ForEach(GridEditorStore.presets.indices, id: \.self) { index in
                    let preset = GridEditorStore.presets[index]
                    Button("\(preset.columns) × \(preset.rows)") { columns = preset.columns; rows = preset.rows }
                }
            } label: {
                Label("\(columns) × \(rows)", systemImage: "square.grid.3x3")
            }
            .accessibilityIdentifier("grid.presets")
            Stepper("Colonnes \(columns)", value: $columns, in: 2...8).labelsHidden()
            Stepper("Lignes \(rows)", value: $rows, in: 2...8).labelsHidden()
            Toggle("Toutes les familles", isOn: $applyToAll).fixedSize()
            Button("Appliquer") { Task { await editor.applyDimensions(columns: columns, rows: rows, applyToAll: applyToAll) } }
                .buttonStyle(.borderedProminent)
                .accessibilityIdentifier("grid.applyDimensions")
            Spacer()
            Button("Réinitialiser", role: .destructive) { Task { await editor.resetWithCatalogOrder() } }
                .buttonStyle(.bordered)
                .accessibilityIdentifier("grid.reset")
        }
    }

    private func pageTabs(_ layout: TouchGridLayout) -> some View {
        let editor = model.gridEditor
        return HStack(spacing: 8) {
            ForEach(0..<max(1, layout.totalPages), id: \.self) { page in
                ChipButton(title: "Page \(page + 1)", isSelected: page == editor.pageIndex) {
                    Task { await editor.select(category: editor.categoryId, page: page) }
                }
                .accessibilityIdentifier("grid.page.\(page + 1)")
            }
            Button { Task { await editor.addPage() } } label: { Label("Nouvelle page", systemImage: "plus") }
                .buttonStyle(.bordered)
                .accessibilityIdentifier("grid.addPage")
            Spacer()
            Text("Format \(layout.columnsCount)×\(layout.rowsCount) · v\(layout.version ?? 1)").font(.caption).foregroundStyle(.secondary)
        }
    }

    private func matrix(_ layout: TouchGridLayout) -> some View {
        let columnsCount = max(1, layout.columnsCount)
        return GeometryReader { proxy in
            let spacing: CGFloat = 8
            let height = max(60, (proxy.size.height - spacing * CGFloat(layout.rowsCount - 1)) / CGFloat(max(1, layout.rowsCount)))
            LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: spacing), count: columnsCount), spacing: spacing) {
                ForEach(layout.positions, id: \.self) { position in
                    slotView(layout.slot(at: position), position: position)
                        .frame(height: height)
                }
            }
        }
    }

    @ViewBuilder
    private func slotView(_ slot: GridSlot?, position: GridPosition) -> some View {
        let editor = model.gridEditor
        let product = model.catalog.product(id: slot?.productId)
        let label = "L\(position.row + 1)·C\(position.column + 1)"
        Group {
            if let product {
                VStack(alignment: .leading, spacing: 4) {
                    HStack {
                        Text(label).font(.caption2.monospaced()).foregroundStyle(.secondary)
                        Spacer()
                        Button { Task { await editor.clear(at: position) } } label: { Image(systemName: "xmark.circle.fill") }
                            .buttonStyle(.plain)
                            .foregroundStyle(.secondary)
                            .accessibilityIdentifier("grid.clear.\(position.row).\(position.column)")
                    }
                    Spacer(minLength: 0)
                    Text(slot?.customLabel ?? product.name).font(.subheadline.weight(.semibold)).lineLimit(2)
                }
                .padding(8)
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
                .background(RoundedRectangle(cornerRadius: 10).fill(Color(.secondarySystemGroupedBackground)))
                .overlay(alignment: .top) {
                    UnevenRoundedRectangle(topLeadingRadius: 10, topTrailingRadius: 10).fill(Color(hex: slot?.customColorHex ?? product.colorHex) ?? .accentColor).frame(height: 4)
                }
                .draggable("slot:\(position.row),\(position.column)")
            } else {
                VStack(spacing: 4) {
                    Image(systemName: "plus").font(.title3)
                    Text(label).font(.caption2.monospaced())
                }
                .foregroundStyle(.secondary)
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .background(RoundedRectangle(cornerRadius: 10).strokeBorder(Color(.separator), style: StrokeStyle(lineWidth: 1.5, dash: [5, 4])))
            }
        }
        .contentShape(Rectangle())
        .onTapGesture { editingSlot = SlotEditor(position: position, slot: slot) }
        .dropDestination(for: String.self) { items, _ in
            guard let payload = items.first else { return false }
            Task { await handleDrop(payload, on: position) }
            return true
        }
        .accessibilityIdentifier("grid.slot.\(position.row).\(position.column)")
        .accessibilityLabel(product.map { "\(label) : \($0.name)" } ?? "\(label) : vide")
    }

    private func handleDrop(_ payload: String, on target: GridPosition) async {
        let editor = model.gridEditor
        if payload.hasPrefix("slot:") {
            let parts = payload.dropFirst(5).split(separator: ",").compactMap { Int($0) }
            guard parts.count == 2 else { return }
            await editor.move(from: GridPosition(row: parts[0], column: parts[1]), to: target)
        } else if payload.hasPrefix("product:"), let id = UUID(uuidString: String(payload.dropFirst(8))), let product = model.catalog.product(id: id) {
            await editor.assign(product: product, at: target)
        }
    }

    private var productList: some View {
        let editor = model.gridEditor
        return List {
            Section("Articles — glissez sur une case") {
                ForEach(editor.candidateProducts) { product in
                    HStack {
                        VStack(alignment: .leading) {
                            Text(product.name).font(.subheadline.weight(.semibold))
                            Text(product.price.formatted).font(.caption).foregroundStyle(.secondary)
                        }
                        Spacer()
                        Image(systemName: editor.placedProductIds.contains(product.id) ? "checkmark.circle.fill" : "line.3.horizontal")
                            .foregroundStyle(editor.placedProductIds.contains(product.id) ? .green : .secondary)
                    }
                    .draggable("product:\(product.id.uuidString)")
                }
            }
        }
        .listStyle(.insetGrouped)
    }
}

struct SlotEditorSheet: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    let position: GridPosition
    let slot: GridSlot?
    @State private var productId: UUID?
    @State private var label = ""
    @State private var color: Color = .blue

    var body: some View {
        let editor = model.gridEditor
        NavigationStack {
            Form {
                Picker("Article", selection: $productId) {
                    Text("(Case vide)").tag(UUID?.none)
                    ForEach(editor.candidateProducts) { product in
                        Text("\(product.name) — \(product.price.formatted)").tag(Optional(product.id))
                    }
                }
                .accessibilityIdentifier("slot.product")
                TextField("Libellé personnalisé (facultatif)", text: $label).accessibilityIdentifier("slot.label")
                ColorPicker("Couleur", selection: $color, supportsOpacity: false)
            }
            .navigationTitle("Case L\(position.row + 1) · C\(position.column + 1)")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Annuler") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Enregistrer") {
                        Task {
                            await editor.assign(product: model.catalog.product(id: productId), at: position, label: label, colorHex: color.hexString)
                            dismiss()
                        }
                    }
                    .accessibilityIdentifier("slot.save")
                }
            }
            .onAppear {
                productId = slot?.productId
                label = slot?.customLabel ?? ""
                color = Color(hex: slot?.customColorHex ?? model.catalog.product(id: slot?.productId)?.colorHex) ?? .blue
            }
        }
        .presentationDetents([.medium])
    }
}
