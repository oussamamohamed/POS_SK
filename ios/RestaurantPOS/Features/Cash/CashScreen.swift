import SwiftUI
import PosKit

/// Pavé numérique intégré (pas de clavier système en salle) : écrit dans un texte décimal.
struct AmountKeypad: View {
    @Binding var text: String
    let idPrefix: String

    var body: some View {
        LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 8), count: 3), spacing: 8) {
            ForEach(["7", "8", "9", "4", "5", "6", "1", "2", "3", ".", "0", "⌫"], id: \.self) { key in
                Button { press(key) } label: { Text(key).font(.title2).frame(maxWidth: .infinity, minHeight: 56) }
                    .buttonStyle(.bordered)
                    .accessibilityIdentifier("\(idPrefix).key.\(key)")
            }
        }
    }

    private func press(_ key: String) {
        if key == "⌫" { _ = text.popLast() } else if key != "." || !text.contains(".") { text += key }
    }
}

private func euros(_ text: String) -> Int {
    Decimal(string: text, locale: nil).map { Int(truncating: NSDecimalNumber(decimal: $0 * 100 + (0.5 as Decimal))) } ?? 0
}

/// Tiroir-caisse : session ouverte du poste, sorties et entrées avec motif, comptage de clôture et historique des écarts (responsable).
struct CashScreen: View {
    @Environment(AppModel.self) private var model
    @State private var sheet: CashSheet?

    enum CashSheet: Identifiable {
        case open, out, `in`, count
        var id: Self { self }
    }

    var body: some View {
        let store = model.cash
        ScrollView {
            VStack(alignment: .leading, spacing: Theme.Space.l) {
                Card {
                    if let session = store.session {
                        VStack(alignment: .leading, spacing: Theme.Space.m) {
                            HStack(spacing: Theme.Space.xl) {
                                stat("cash.float", Money(cents: session.openingFloatCents).formatted)
                                stat("cash.expected", Money(cents: session.expectedCents).formatted, id: "cash.expected")
                            }
                            HStack(spacing: Theme.Space.m) {
                                Button("cash.out") { sheet = .out }.buttonStyle(.bordered).accessibilityIdentifier("cash.out")
                                Button("cash.in") { sheet = .in }.buttonStyle(.bordered).accessibilityIdentifier("cash.in")
                                Button("cash.count") { sheet = .count }.buttonStyle(.borderedProminent).accessibilityIdentifier("cash.count")
                            }
                            .controlSize(.large)
                            ForEach(session.movements) { m in
                                HStack {
                                    Text(LocalizedStringKey(m.kind == "in" ? "cash.in" : "cash.out")).bold()
                                    Text(m.reason).foregroundStyle(Theme.inkMuted)
                                    Spacer()
                                    Text(Money(cents: m.amountCents).formatted).monospacedDigit()
                                }
                                .accessibilityIdentifier("cash.movement.\(m.reason)")
                            }
                        }
                    } else {
                        VStack(alignment: .leading, spacing: Theme.Space.m) {
                            Text("cash.no_session").foregroundStyle(Theme.inkMuted)
                            Button("cash.open") { sheet = .open }.buttonStyle(.borderedProminent).controlSize(.large).accessibilityIdentifier("cash.open")
                        }
                    }
                }
                if store.isManagerView {
                    Card {
                        VStack(alignment: .leading, spacing: Theme.Space.s) {
                            Text("cash.history").font(.posHeadline)
                            if store.history.isEmpty { Text("cash.history_empty").foregroundStyle(Theme.inkSubtle) }
                            ForEach(store.history) { s in
                                if let c = s.count {
                                    HStack {
                                        Text(c.closedAtUtc.formatted(date: .abbreviated, time: .shortened)).frame(width: 170, alignment: .leading)
                                        Text(s.terminalId).frame(width: 70, alignment: .leading)
                                        Text(Money(cents: c.expectedCents).formatted).frame(maxWidth: .infinity, alignment: .trailing)
                                        Text(Money(cents: c.countedCents).formatted).frame(maxWidth: .infinity, alignment: .trailing)
                                        Text(Money(cents: c.varianceCents).formatted).foregroundStyle(c.varianceCents == 0 ? Theme.ink : Theme.danger).frame(maxWidth: .infinity, alignment: .trailing)
                                    }
                                    .font(Font.posMono(13))
                                    .accessibilityIdentifier("cash.history.\(c.varianceCents)")
                                    Divider()
                                }
                            }
                        }
                    }
                }
            }
            .padding(Theme.Space.l)
        }
        .task { await store.load() }
        .sheet(item: $sheet) { kind in
            switch kind {
            case .open: CashAmountSheet(title: "cash.open", withReason: false) { amount, _ in await store.open(floatCents: amount) }
            case .out: CashAmountSheet(title: "cash.out", withReason: true) { amount, reason in await store.move(kind: "out", amountCents: amount, reason: reason) }
            case .in: CashAmountSheet(title: "cash.in", withReason: true) { amount, reason in await store.move(kind: "in", amountCents: amount, reason: reason) }
            case .count: CashCountSheet()
            }
        }
    }

    private func stat(_ key: LocalizedStringKey, _ value: String, id: String? = nil) -> some View {
        VStack(alignment: .leading) {
            Text(key).font(.posLabel).foregroundStyle(Theme.inkMuted)
            Text(value).font(.posTitle).monospacedDigit().accessibilityIdentifier(id ?? "")
        }
    }
}

/// Montant au pavé numérique, avec un motif (3 à 200 caractères) pour les sorties et entrées.
struct CashAmountSheet: View {
    @Environment(\.dismiss) private var dismiss
    let title: LocalizedStringKey
    let withReason: Bool
    let onSubmit: (Int, String) async -> Bool
    @State private var amount = ""
    @State private var reason = ""

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    HStack { Text("cash.amount"); Spacer(); Text(amount.isEmpty ? "0" : amount).font(.title2.monospacedDigit()).accessibilityIdentifier("cash.amountText") }
                    if withReason { TextField("cash.reason", text: $reason).accessibilityIdentifier("cash.reason") }
                }
                Section { AmountKeypad(text: $amount, idPrefix: "cash") }
            }
            .navigationTitle(title)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("common.cancel") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("common.save") { Task { if await onSubmit(euros(amount), reason) { dismiss() } } }.accessibilityIdentifier("cash.save")
                }
            }
        }
        .presentationDetents([.large])
    }
}

/// Comptage de clôture : coupures (boutons + et −) ou montant total au pavé ; la note est demandée quand l'écart dépasse le seuil.
struct CashCountSheet: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var quantities: [Int: Int] = [:]
    @State private var direct = ""
    @State private var note = ""

    private var denominations: [CashDenomination] { CashDenomination.all.compactMap { d in (quantities[d] ?? 0) > 0 ? CashDenomination(valueCents: d, quantity: quantities[d]!) : nil } }
    private var counted: Int { denominations.isEmpty ? euros(direct) : CashMath.total(denominations) }

    var body: some View {
        NavigationStack {
            Form {
                Section("cash.denominations") {
                    ForEach(CashDenomination.all, id: \.self) { d in
                        HStack {
                            Text(Money(cents: d).formatted).monospacedDigit()
                            Spacer()
                            Button { quantities[d] = max(0, (quantities[d] ?? 0) - 1) } label: { Image(systemName: "minus.circle") }.buttonStyle(.borderless)
                            Text("\(quantities[d] ?? 0)").frame(width: 36).monospacedDigit().accessibilityIdentifier("cash.qty.\(d)")
                            Button { quantities[d, default: 0] += 1 } label: { Image(systemName: "plus.circle") }.buttonStyle(.borderless).accessibilityIdentifier("cash.plus.\(d)")
                        }
                    }
                }
                Section("cash.direct_amount") {
                    HStack { Spacer(); Text(direct.isEmpty ? "0" : direct).font(.title2.monospacedDigit()).accessibilityIdentifier("cash.directText") }
                    AmountKeypad(text: $direct, idPrefix: "cashcount")
                }
                Section {
                    TextField("cash.note", text: $note).accessibilityIdentifier("cash.note")
                }
            }
            // Total épinglé hors de la liste : il reste à l'écran (et dans l'arbre d'accessibilité) quand la liste défile.
            .safeAreaInset(edge: .top) {
                HStack {
                    Text("cash.counted")
                    Spacer()
                    Text(Money(cents: counted).formatted).bold().accessibilityIdentifier("cash.countedTotal")
                }
                .padding(.horizontal, Theme.Space.l)
                .padding(.vertical, Theme.Space.s)
                .background(.bar)
            }
            .navigationTitle("cash.count")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("common.cancel") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("cash.count_confirm") {
                        Task { if await model.cash.count(countedCents: counted, denominations: denominations, note: note) != nil { dismiss() } }
                    }
                    .accessibilityIdentifier("cash.countConfirm")
                }
            }
        }
        .presentationDetents([.large])
    }
}
