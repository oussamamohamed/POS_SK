import SwiftUI
import PosKit

/// Plan de salle : état des tables, totaux en cours, ouverture et création de tables.
struct FloorScreen: View {
    @Environment(AppModel.self) private var model
    @Environment(Router.self) private var router
    @State private var opening: DiningTable?
    @State private var showsAddTable = false
    /// Filtre par statut (`nil` : toutes les tables).
    @State private var statusFilter: TableStatus?

    var body: some View {
        let floor = model.floor
        VStack(alignment: .leading, spacing: Theme.Space.l) {
            HStack(spacing: Theme.Space.m) {
                KPIView(title: String(localized: "floor.kpi_occupied_tables"), value: "\(floor.occupiedCount)/\(floor.diningTables.count)", systemImage: "person.2")
                    .frame(maxWidth: 240)
                KPIView(title: String(localized: "floor.kpi_open_total"), value: floor.openTotal.formatted, systemImage: "eurosign.circle")
                    .frame(maxWidth: 260)
                Spacer()
                Button { Task { await floor.load() } } label: {
                    Image(systemName: "arrow.clockwise")
                        .font(.system(size: 17, weight: .semibold))
                        .frame(width: Theme.touchTarget, height: Theme.touchTarget)
                }
                .buttonStyle(ActionButtonStyle(kind: .neutral))
                .accessibilityLabel("common.refresh_label")
                .accessibilityIdentifier("floor.refresh")
                Button { showsAddTable = true } label: {
                    Label("floor.add_table", systemImage: "plus")
                        .font(.posHeadline)
                        .padding(.horizontal, Theme.Space.xl)
                        .frame(minHeight: Theme.touchTarget)
                }
                .buttonStyle(ActionButtonStyle(kind: .primary))
                .accessibilityIdentifier("floor.add")
            }
            .fixedSize(horizontal: false, vertical: true)

            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: Theme.Space.s) {
                    filterChip(String(localized: "floor.filter_all"), nil, id: "all")
                    filterChip(String(localized: "floor.filter_occupied"), .occupied, id: "occupied")
                    filterChip(String(localized: "floor.filter_bill"), .billRequested, id: "bill")
                    filterChip(String(localized: "floor.filter_free"), .free, id: "free")
                }
            }

            ScrollView {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 200), spacing: Theme.Space.l)], spacing: Theme.Space.l) {
                    ForEach(visibleTables) { table in
                        TableCard(table: table)
                            .onTapGesture { select(table) }
                            .accessibilityIdentifier("table.\(table.tableNumber)")
                    }
                }
                .padding(.bottom, Theme.Space.xl)
            }
            .refreshable { await floor.load() }
        }
        .padding([.horizontal, .top], Theme.Space.xl)
        .task { await floor.load() }
        .sheet(item: $opening) { table in
            OpenTableSheet(table: table) { covers in
                Task {
                    if await floor.open(table, covers: covers) {
                        await model.ticket.load(table: table.tableNumber)
                        router.section = .order
                    }
                }
            }
        }
        .sheet(isPresented: $showsAddTable) { AddTableSheet() }
    }

    private var visibleTables: [DiningTable] {
        guard let statusFilter else { return model.floor.diningTables }
        return model.floor.diningTables.filter { $0.status == statusFilter }
    }

    private func filterChip(_ title: String, _ status: TableStatus?, id: String) -> some View {
        let tables = model.floor.diningTables
        let count = status.map { status in tables.filter { $0.status == status }.count } ?? tables.count
        return ChipButton(title: title, isSelected: statusFilter == status, count: count) {
            statusFilter = status
        }
        .accessibilityIdentifier("floor.filter.\(id)")
    }

    private func select(_ table: DiningTable) {
        Haptics.tap()
        if table.status == .free {
            opening = table
        } else {
            Task {
                await model.ticket.load(table: table.tableNumber)
                router.section = .order
            }
        }
    }
}

/// Carte de table : les tables libres s'effacent (pointillés), les tables actives sont pleines.
struct TableCard: View {
    let table: DiningTable

    private var capacityLabel: String {
        let coversPrefix = table.coversCount > 0 ? "\(table.coversCount)/" : ""
        let waiterSuffix = table.assignedWaiterName.map { " · \($0)" } ?? ""
        return String(localized: "floor.table_capacity \(coversPrefix)\(table.capacity)\(waiterSuffix)")
    }

    var body: some View {
        let color = Theme.color(for: table.status)
        let isFree = table.status == .free
        let shape = RoundedRectangle(cornerRadius: Theme.cornerRadius, style: .continuous)
        TimelineView(.periodic(from: .now, by: 30)) { context in
            VStack(alignment: .leading, spacing: Theme.Space.s) {
                HStack(alignment: .top) {
                    Text(table.tableNumber)
                        .font(.posDisplay)
                        .foregroundStyle(isFree ? Theme.inkMuted : Theme.ink)
                        .lineLimit(1)
                        .minimumScaleFactor(0.5)
                    Spacer(minLength: Theme.Space.s)
                    Badge(text: table.status.label, color: color)
                }
                Label(capacityLabel, systemImage: "person.2")
                    .font(.posLabel)
                    .foregroundStyle(Theme.inkMuted)
                    .lineLimit(1)
                Spacer(minLength: 0)
                HStack(alignment: .firstTextBaseline, spacing: Theme.Space.s) {
                    if isFree {
                        Text("floor.tap_to_open").font(.posLabel).foregroundStyle(Theme.inkSubtle)
                    } else if let opened = table.openedAtUtc {
                        let isLate = context.date.timeIntervalSince(opened) > 90 * 60
                        Label(opened.elapsedDescription(now: context.date), systemImage: "clock")
                            .font(.posLabel.monospacedDigit())
                            .foregroundStyle(isLate ? Theme.danger : Theme.inkMuted)
                    }
                    Spacer(minLength: 0)
                    if table.activeOrderTotalTtc.cents > 0 {
                        Text(table.activeOrderTotalTtc.formatted).font(.posAmount).foregroundStyle(Theme.ink)
                    }
                }
                .lineLimit(1)
            }
            .padding(Theme.Space.l)
            .frame(minHeight: 168)
            .background(shape.fill(isFree ? Theme.canvas : Theme.surface))
            .overlay(shape.strokeBorder(color, style: StrokeStyle(lineWidth: 2, dash: isFree ? [6, 5] : [])))
            .contentShape(shape)
        }
        .accessibilityElement(children: .combine)
        .accessibilityAddTraits(.isButton)
    }
}

struct OpenTableSheet: View {
    @Environment(\.dismiss) private var dismiss
    let table: DiningTable
    let onOpen: (Int) -> Void
    @State private var covers = 2

    var body: some View {
        VStack(spacing: 24) {
            SheetHeader(title: String(localized: "floor.open_table_title \(table.tableNumber)"), subtitle: String(localized: "floor.open_table_subtitle \(table.capacity)")) { dismiss() }
            Text("floor.covers_count").font(.posHeadline).foregroundStyle(Theme.ink)
            LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 10), count: 6), spacing: 10) {
                ForEach(1...12, id: \.self) { count in
                    Button { covers = count } label: {
                        Text("\(count)")
                            .font(.system(size: 22, weight: .semibold, design: .monospaced))
                            .frame(maxWidth: .infinity, minHeight: 60)
                            .background(RoundedRectangle(cornerRadius: Theme.smallRadius, style: .continuous).fill(covers == count ? Theme.primary : Theme.raised))
                            .foregroundStyle(covers == count ? Theme.onPrimary : Theme.ink)
                            .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                        .accessibilityIdentifier("covers.\(count)")
                }
            }
            ActionButton(title: String(localized: "floor.open_with_covers \(covers)"), systemImage: "fork.knife", kind: .primary, height: Theme.touchLarge) {
                dismiss()
                onOpen(covers)
            }
            .accessibilityIdentifier("covers.confirm")
        }
        .padding(Theme.Space.xxl)
        .presentationDetents([.medium])
        .onAppear { covers = min(table.capacity, 2) }
    }
}

struct AddTableSheet: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var number = ""
    @State private var capacity = 4

    var body: some View {
        VStack(alignment: .leading, spacing: 20) {
            SheetHeader(title: String(localized: "floor.new_table_title"), subtitle: nil) { dismiss() }
            TextField("floor.number_placeholder", text: $number)
                .textFieldStyle(.roundedBorder)
                .font(.title3)
                .accessibilityIdentifier("addTable.number")
            Stepper("floor.capacity_stepper \(capacity)", value: $capacity, in: 1...20)
                .accessibilityIdentifier("addTable.capacity")
            ActionButton(title: String(localized: "floor.create_table"), systemImage: "plus", kind: .primary) {
                Task { if await model.floor.addTable(number: number, capacity: capacity) { dismiss() } }
            }
            .disabled(number.trimmingCharacters(in: .whitespaces).isEmpty)
            .accessibilityIdentifier("addTable.confirm")
        }
        .padding(Theme.Space.xxl)
        .presentationDetents([.height(320)])
    }
}
