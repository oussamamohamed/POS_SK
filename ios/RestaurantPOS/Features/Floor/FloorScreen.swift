import SwiftUI
import PosKit

/// Plan de salle : état des tables, totaux en cours, ouverture et création de tables.
struct FloorScreen: View {
    @Environment(AppModel.self) private var model
    @Environment(Router.self) private var router
    @State private var opening: DiningTable?
    @State private var showsAddTable = false

    var body: some View {
        let floor = model.floor
        VStack(spacing: 0) {
            HStack(spacing: 12) {
                KPIView(title: "Tables occupées", value: "\(floor.occupiedCount)/\(floor.diningTables.count)", systemImage: "person.3", tint: .orange)
                KPIView(title: "En cours", value: floor.openTotal.formatted, systemImage: "eurosign.circle", tint: .green)
                Spacer()
                Button { Task { await floor.load() } } label: { Label("Actualiser", systemImage: "arrow.clockwise") }
                    .buttonStyle(.bordered)
                    .accessibilityIdentifier("floor.refresh")
                Button { showsAddTable = true } label: { Label("Nouvelle table", systemImage: "plus") }
                    .buttonStyle(.borderedProminent)
                    .accessibilityIdentifier("floor.add")
            }
            .fixedSize(horizontal: false, vertical: true)
            .padding(20)

            ScrollView {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 190), spacing: 16)], spacing: 16) {
                    ForEach(floor.diningTables) { table in
                        TableCard(table: table)
                            .onTapGesture { select(table) }
                            .accessibilityIdentifier("table.\(table.tableNumber)")
                    }
                }
                .padding(.horizontal, 20)
                .padding(.bottom, 20)
            }
            .refreshable { await floor.load() }
        }
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

struct TableCard: View {
    let table: DiningTable

    var body: some View {
        let color = Theme.color(for: table.status)
        TimelineView(.periodic(from: .now, by: 30)) { context in
            VStack(alignment: .leading, spacing: 8) {
                HStack {
                    Text(table.tableNumber).font(.system(.largeTitle, design: .rounded).weight(.bold))
                    Spacer()
                    Badge(text: table.status.label, color: color)
                }
                Label("\(table.coversCount > 0 ? "\(table.coversCount)/" : "")\(table.capacity) pers.", systemImage: "person.2")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                if let waiter = table.assignedWaiterName {
                    Label(waiter, systemImage: "person.crop.circle").font(.caption).foregroundStyle(.secondary).lineLimit(1)
                }
                Spacer(minLength: 0)
                HStack {
                    if let opened = table.openedAtUtc, table.status != .free {
                        Label(opened.elapsedDescription(now: context.date), systemImage: "clock").font(.caption.weight(.semibold))
                            .foregroundStyle(context.date.timeIntervalSince(opened) > 90 * 60 ? .red : .secondary)
                    }
                    Spacer()
                    if table.activeOrderTotalTtc.cents > 0 {
                        Text(table.activeOrderTotalTtc.formatted).font(.headline.monospacedDigit())
                    }
                }
            }
            .padding(16)
            .frame(minHeight: 170)
            .background(
                RoundedRectangle(cornerRadius: Theme.cornerRadius, style: .continuous)
                    .fill(Color(.secondarySystemGroupedBackground))
                    .overlay(RoundedRectangle(cornerRadius: Theme.cornerRadius, style: .continuous).strokeBorder(color.opacity(0.8), lineWidth: 2.5))
            )
            .contentShape(RoundedRectangle(cornerRadius: Theme.cornerRadius))
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
            SheetHeader(title: "Ouvrir la table \(table.tableNumber)", subtitle: "Capacité \(table.capacity) personnes") { dismiss() }
            Text("Nombre de couverts").font(.headline)
            LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 10), count: 6), spacing: 10) {
                ForEach(1...12, id: \.self) { count in
                    Button("\(count)") { covers = count }
                        .font(.title2.weight(.semibold))
                        .frame(maxWidth: .infinity, minHeight: 60)
                        .background(RoundedRectangle(cornerRadius: Theme.smallRadius).fill(covers == count ? Color.accentColor : Color(.tertiarySystemFill)))
                        .foregroundStyle(covers == count ? .white : .primary)
                        .buttonStyle(.plain)
                        .accessibilityIdentifier("covers.\(count)")
                }
            }
            ActionButton(title: "Ouvrir avec \(covers) couvert(s)", systemImage: "fork.knife") {
                dismiss()
                onOpen(covers)
            }
            .accessibilityIdentifier("covers.confirm")
        }
        .padding(28)
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
            SheetHeader(title: "Nouvelle table", subtitle: nil) { dismiss() }
            TextField("Numéro ou nom (ex. T9, Terrasse 1)", text: $number)
                .textFieldStyle(.roundedBorder)
                .font(.title3)
                .accessibilityIdentifier("addTable.number")
            Stepper("Capacité : \(capacity) personnes", value: $capacity, in: 1...20)
                .accessibilityIdentifier("addTable.capacity")
            ActionButton(title: "Créer la table", systemImage: "plus") {
                Task { if await model.floor.addTable(number: number, capacity: capacity) { dismiss() } }
            }
            .disabled(number.trimmingCharacters(in: .whitespaces).isEmpty)
            .accessibilityIdentifier("addTable.confirm")
        }
        .padding(28)
        .presentationDetents([.height(320)])
    }
}
