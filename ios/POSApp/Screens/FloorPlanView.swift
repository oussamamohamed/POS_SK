import POSKit
import SwiftUI

/// Plan de salle : état des tables en temps réel, ouverture en un geste.
struct FloorPlanView: View {
    @Environment(AppModel.self) private var app
    @State private var tableToOpen: DiningTable?

    private let columns = [GridItem(.adaptive(minimum: 180, maximum: 260), spacing: 16)]

    var body: some View {
        @Bindable var floor = app.floor

        VStack(alignment: .leading, spacing: 20) {
            header

            HStack(spacing: 12) {
                StatTile(title: "Tables libres", value: "\(floor.freeCount)", systemImage: "checkmark.circle", tint: .green)
                StatTile(title: "Tables occupées", value: "\(floor.occupiedCount)", systemImage: "person.2.fill", tint: .orange)
                StatTile(title: "Couverts en salle", value: "\(floor.coversInRoom)", systemImage: "fork.knife", tint: .blue)
                StatTile(title: "Notes ouvertes", value: floor.openAmount.formatted, systemImage: "eurosign.circle", tint: .purple)
            }

            Picker("Filtre", selection: $floor.filter) {
                ForEach(FloorPlanModel.Filter.allCases) { filter in
                    Text(filter.label).tag(filter)
                }
            }
            .pickerStyle(.segmented)
            .frame(maxWidth: 420)
            .accessibilityIdentifier("floor.filter")

            ScrollView {
                if floor.tables.isEmpty && floor.isLoading {
                    ProgressView().padding(60)
                } else {
                    LazyVGrid(columns: columns, spacing: 16) {
                        ForEach(floor.visibleTables) { table in
                            TableCard(table: table) {
                                select(table)
                            }
                        }
                    }
                    .padding(.bottom, 24)
                }
            }
            .refreshable { await floor.load() }
        }
        .padding(24)
        .task {
            // Rafraîchissement périodique : les autres iPads modifient aussi la salle.
            while !Task.isCancelled {
                await floor.load()
                try? await Task.sleep(for: .seconds(15))
            }
        }
        .sheet(item: $tableToOpen) { table in
            CoversSheet(table: table) { covers in
                Task {
                    if await floor.open(table, covers: covers) {
                        tableToOpen = nil
                        await app.openTable(table.tableNumber)
                    }
                }
            }
            .noticeOverlay()
        }
    }

    private var header: some View {
        HStack(alignment: .firstTextBaseline) {
            VStack(alignment: .leading, spacing: 4) {
                Text("Salle").font(.largeTitle.weight(.bold))
                if let refresh = app.floor.lastRefresh {
                    Text("Mis à jour à \(refresh.shortTime)")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                }
            }
            Spacer()
            Button {
                Task { await app.floor.load() }
            } label: {
                Label("Actualiser", systemImage: "arrow.clockwise")
            }
            .buttonStyle(.bordered)
            .accessibilityIdentifier("floor.refresh")

            Button {
                Task { await app.openCounter() }
            } label: {
                Label("Vente comptoir", systemImage: "bag.fill")
            }
            .buttonStyle(.borderedProminent)
            .accessibilityIdentifier("floor.counter")
        }
    }

    private func select(_ table: DiningTable) {
        if table.status == .free {
            tableToOpen = table
        } else {
            Task { await app.openTable(table.tableNumber) }
        }
    }
}

struct TableCard: View {
    var table: DiningTable
    var action: () -> Void

    var body: some View {
        Button(action: action) {
            VStack(alignment: .leading, spacing: 10) {
                HStack {
                    Text(table.tableNumber)
                        .font(.system(size: 30, weight: .bold, design: .rounded))
                    Spacer()
                    StatusPill(text: table.status.label, color: table.status.color)
                }

                if table.status == .free {
                    Label("\(table.capacity) places", systemImage: "chair.lounge")
                        .foregroundStyle(.secondary)
                    Spacer(minLength: 0)
                    Text("Toucher pour ouvrir")
                        .font(.footnote)
                        .foregroundStyle(.tertiary)
                } else {
                    HStack {
                        Label("\(table.coversCount) couv.", systemImage: "person.2")
                        Spacer()
                        if let opened = table.openedAtUtc {
                            TimelineView(.periodic(from: .now, by: 30)) { context in
                                Label(opened.elapsedLabel(now: context.date), systemImage: "clock")
                            }
                        }
                    }
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                    if let waiter = table.assignedWaiterName {
                        Text(waiter.components(separatedBy: " (").first ?? waiter)
                            .font(.footnote)
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                    }
                    Spacer(minLength: 0)
                    Text(table.activeOrderTotalTtc.formatted)
                        .font(.title3.weight(.semibold))
                        .monospacedDigit()
                }
            }
            .padding(16)
            .frame(maxWidth: .infinity, minHeight: 150, alignment: .topLeading)
            .background(
                RoundedRectangle(cornerRadius: 20, style: .continuous)
                    .fill(Color.posSurface)
            )
            .overlay(alignment: .leading) {
                UnevenRoundedRectangle(topLeadingRadius: 20, bottomLeadingRadius: 20, style: .continuous)
                    .fill(table.status.color)
                    .frame(width: 6)
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityElement(children: .combine)
        .accessibilityIdentifier("table.\(table.tableNumber)")
        .accessibilityValue(table.status.label)
    }
}

/// Choix du nombre de couverts à l'ouverture d'une table.
struct CoversSheet: View {
    var table: DiningTable
    var onConfirm: (Int) -> Void
    @State private var covers: Int
    @Environment(\.dismiss) private var dismiss

    init(table: DiningTable, onConfirm: @escaping (Int) -> Void) {
        self.table = table
        self.onConfirm = onConfirm
        _covers = State(initialValue: max(1, min(table.capacity, 2)))
    }

    private let columns = Array(repeating: GridItem(.flexible(), spacing: 12), count: 6)

    var body: some View {
        VStack(spacing: 20) {
            HStack {
                Text("Table \(table.tableNumber)").font(.title2.weight(.bold))
                Spacer()
                Button("Annuler") { dismiss() }
                    .accessibilityIdentifier("covers.cancel")
            }
            Text("Nombre de couverts")
                .font(.headline)
                .frame(maxWidth: .infinity, alignment: .leading)
            LazyVGrid(columns: columns, spacing: 12) {
                ForEach(1...12, id: \.self) { value in
                    Button {
                        covers = value
                    } label: {
                        Text("\(value)")
                            .font(.title2.weight(.semibold))
                            .frame(maxWidth: .infinity, minHeight: 56)
                            .foregroundStyle(covers == value ? Color.white : Color.primary)
                            .background(
                                RoundedRectangle(cornerRadius: 12, style: .continuous)
                                    .fill(covers == value ? Color.accentColor : Color.posElevated)
                            )
                    }
                    .buttonStyle(.plain)
                    .accessibilityIdentifier("covers.\(value)")
                }
            }
            Button {
                onConfirm(covers)
            } label: {
                Text("Ouvrir la table — \(covers) couvert\(covers > 1 ? "s" : "")")
            }
            .buttonStyle(ActionButtonStyle())
            .accessibilityIdentifier("covers.confirm")
        }
        .padding(28)
    }
}
