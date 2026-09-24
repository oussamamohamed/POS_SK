import SwiftUI
import PosKit

/// Ticket en cours : lignes, totaux, actions.
struct TicketPanel: View {
    @Environment(AppModel.self) private var model
    @Environment(Router.self) private var router
    @State private var sheet: TicketSheet?
    @State private var counterOutcome: TicketStore.CheckoutOutcome?

    enum TicketSheet: String, Identifiable {
        case payment, discount, transfer, held, hold
        var id: String { rawValue }
    }

    var body: some View {
        let ticket = model.ticket
        VStack(spacing: 0) {
            header
            Divider()
            if ticket.isEmpty {
                EmptyStateView(title: "\(ticket.title) vide", systemImage: "fork.knife", message: "Touchez un article pour démarrer la commande")
                    .frame(maxHeight: .infinity)
            } else {
                List {
                    ForEach(ticket.lines) { line in
                        TicketLineRow(line: line, destination: ticket.destination)
                            .swipeActions(edge: .trailing, allowsFullSwipe: true) {
                                if line.isDraft {
                                    Button(role: .destructive) { ticket.remove(line.id) } label: { Label("Supprimer", systemImage: "trash") }
                                }
                            }
                            .listRowInsets(EdgeInsets(top: 8, leading: 14, bottom: 8, trailing: 14))
                    }
                }
                .listStyle(.plain)
                .accessibilityIdentifier("ticket.lines")
            }
            Divider()
            totals
            actions
        }
        .background(Color(.systemBackground))
        .sheet(item: $sheet) { sheet in
            switch sheet {
            case .payment:
                PaymentSheet { outcome in
                    if ticket.isCounter {
                        counterOutcome = outcome
                    } else if outcome.isComplete {
                        router.section = .floor
                    }
                }
            case .discount: DiscountSheet()
            case .transfer: TransferSheet()
            case .held: HeldOrdersSheet()
            case .hold: HoldSheet()
            }
        }
        .fullScreenCover(item: $counterOutcome) { outcome in
            ChangeOverlay(outcome: outcome) {
                counterOutcome = nil
                Task { await ticket.openCounter(destination: .takeaway) }
            }
        }
    }

    // MARK: En-tête

    private var header: some View {
        let ticket = model.ticket
        return VStack(alignment: .leading, spacing: 10) {
            HStack {
                VStack(alignment: .leading, spacing: 2) {
                    Text(ticket.title).font(.title2.weight(.bold)).accessibilityIdentifier("ticket.title")
                    if !ticket.isCounter {
                        Text(ticket.covers > 0 ? "\(ticket.covers) couvert(s)" : "Sur place").font(.subheadline).foregroundStyle(.secondary)
                    }
                }
                Spacer()
                if ticket.isCounter {
                    Button {
                        Task { await ticket.refreshHeldOrders(); sheet = .held }
                    } label: {
                        Label("\(ticket.heldOrders.count)", systemImage: "pause.circle")
                            .font(.headline)
                    }
                    .buttonStyle(.bordered)
                    .accessibilityIdentifier("ticket.heldQueue")
                    .accessibilityLabel("Commandes en attente : \(ticket.heldOrders.count)")
                } else {
                    Button {
                        Task { await ticket.openCounter(destination: .takeaway) }
                    } label: {
                        Label("Comptoir", systemImage: "bag")
                    }
                    .buttonStyle(.bordered)
                    .accessibilityIdentifier("ticket.toCounter")
                }
                Button {
                    router.section = .floor
                } label: {
                    Image(systemName: "square.grid.3x3.topleft.filled")
                }
                .buttonStyle(.bordered)
                .accessibilityLabel("Plan de salle")
                .accessibilityIdentifier("ticket.toFloor")
            }
            if ticket.isCounter {
                Picker("Destination", selection: Binding(get: { ticket.destination }, set: { value in Task { await ticket.switchDestination(value) } })) {
                    Label("À emporter", systemImage: "bag").tag(OrderDestination.takeaway)
                    Label("Sur place", systemImage: "fork.knife").tag(OrderDestination.eatIn)
                }
                .pickerStyle(.segmented)
                .accessibilityIdentifier("ticket.destination")
            }
        }
        .padding(16)
    }

    // MARK: Totaux

    private var totals: some View {
        let ticket = model.ticket
        let totals = ticket.totals
        return VStack(spacing: 6) {
            if let discount = ticket.discount {
                row("Sous-total", totals.subtotalTtc.formatted)
                row("Remise \(discount.label)\(discount.reason.map { " · \($0)" } ?? "")", "−\(totals.discountAmount.formatted)", color: .green)
            }
            row("Total HT", totals.totalHt.formatted)
            ForEach(totals.vatLines, id: \.ratePercent) { vat in
                row("TVA \(vat.ratePercent.formatted()) %", vat.vat.formatted)
            }
            if let remaining = ticket.remainingBalance, remaining != totals.totalTtc {
                row("Déjà réglé", (totals.totalTtc - remaining).formatted, color: .green)
            }
            HStack(alignment: .firstTextBaseline) {
                Text(ticket.remainingBalance == nil ? "Total TTC" : "Reste à payer").font(.headline)
                Spacer()
                Text(ticket.amountDue.formatted)
                    .font(.system(.largeTitle, design: .rounded).weight(.bold))
                    .monospacedDigit()
                    .contentTransition(.numericText())
                    .accessibilityIdentifier("ticket.total")
            }
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 10)
        .animation(.snappy, value: totals.totalTtc)
    }

    private func row(_ label: String, _ value: String, color: Color = .secondary) -> some View {
        HStack {
            Text(label).lineLimit(1)
            Spacer()
            Text(value).monospacedDigit()
        }
        .font(.subheadline)
        .foregroundStyle(color)
    }

    // MARK: Actions

    private var actions: some View {
        let ticket = model.ticket
        return VStack(spacing: 10) {
            HStack(spacing: 8) {
                smallAction("Vider", "trash", tint: .red, id: "ticket.clear", disabled: !ticket.hasDrafts) { ticket.clearDrafts() }
                smallAction("Remise", "percent", tint: .green, id: "ticket.discount", disabled: ticket.isEmpty) {
                    Task { if await ticket.prepareForDiscount() { sheet = .discount } }
                }
                if ticket.isCounter {
                    smallAction("Attente", "pause", tint: .orange, id: "ticket.hold", disabled: ticket.isEmpty) { sheet = .hold }
                } else {
                    smallAction("Transfert", "arrow.left.arrow.right", tint: .indigo, id: "ticket.transfer", disabled: ticket.isEmpty) {
                        Task { await model.floor.load(); sheet = .transfer }
                    }
                    smallAction("Suite", "bell", tint: .purple, id: "ticket.fireSuite", disabled: ticket.isEmpty) {
                        Task { await ticket.fireSuite() }
                    }
                }
            }

            if ticket.isCounter && !ticket.isEmpty {
                FastCashBar { counterOutcome = $0 }
            }

            HStack(spacing: 10) {
                ActionButton(title: "Cuisine", systemImage: "paperplane.fill", tint: .orange, isLoading: ticket.isBusy) {
                    Task {
                        if await ticket.sendToKitchen(), !ticket.isCounter {
                            Haptics.success()
                            router.section = .floor
                        }
                    }
                }
                .disabled(!ticket.hasUndispatched)
                .accessibilityIdentifier("ticket.send")

                ActionButton(title: "Encaisser \(ticket.amountDue.formatted)", systemImage: "creditcard.fill", tint: .green) {
                    sheet = .payment
                }
                .disabled(ticket.amountDue.cents <= 0)
                .accessibilityIdentifier("ticket.pay")
            }
        }
        .padding(16)
        .background(Color(.secondarySystemBackground))
    }

    private func smallAction(_ title: String, _ icon: String, tint: Color, id: String, disabled: Bool, action: @escaping () -> Void) -> some View {
        Button(action: { Haptics.tap(); action() }) {
            VStack(spacing: 4) {
                Image(systemName: icon).font(.headline)
                Text(title).font(.caption.weight(.semibold))
            }
            .frame(maxWidth: .infinity, minHeight: 52)
        }
        .buttonStyle(ActionButtonStyle(tint: tint, prominent: false))
        .disabled(disabled)
        .accessibilityIdentifier(id)
    }
}

extension TicketStore.CheckoutOutcome: @retroactive Identifiable {
    public var id: String { (receiptNumber ?? "") + (pickupNumber ?? "") }
}

/// Encaissement espèces express au comptoir (montant exact ou billet), sans écran de paiement.
struct FastCashBar: View {
    @Environment(AppModel.self) private var model
    let onPaid: (TicketStore.CheckoutOutcome) -> Void

    var body: some View {
        let due = model.ticket.amountDue
        HStack(spacing: 6) {
            Image(systemName: "banknote").foregroundStyle(.green)
            ForEach(OrderMath.suggestedCashAmounts(for: due).prefix(4), id: \.self) { amount in
                Button(amount == due ? "Exact" : amount.formatted) {
                    Task {
                        let outcome = await model.ticket.counterCheckout(method: .cash, amount: due, tendered: amount, tip: .zero, buzzer: nil, printReceipt: false, policy: model.settings.mealVoucherPolicy)
                        if let outcome {
                            Haptics.success()
                            onPaid(outcome)
                        }
                    }
                }
                .font(.footnote.weight(.bold))
                .buttonStyle(.bordered)
                .tint(.green)
                .accessibilityIdentifier(amount == due ? "fastcash.exact" : "fastcash.\(amount.cents / 100)")
            }
        }
    }
}

struct TicketLineRow: View {
    @Environment(AppModel.self) private var model
    let line: CartLine
    let destination: OrderDestination

    var body: some View {
        let ticket = model.ticket
        HStack(alignment: .top, spacing: 10) {
            VStack(alignment: .leading, spacing: 4) {
                Text(line.name).font(.body.weight(.semibold)).strikethrough(line.isComp)
                HStack(spacing: 4) {
                    Button {
                        ticket.cycleCourse(line.id)
                    } label: {
                        Badge(text: line.course.label, color: Theme.color(for: line.course))
                    }
                    .buttonStyle(.plain)
                    .disabled(!line.isDraft)
                    .accessibilityIdentifier("line.course.\(line.name)")
                    if line.isHappyHourApplied { Badge(text: "HH", color: Theme.happyHour, systemImage: "wineglass") }
                    if line.isComp { Badge(text: "Offert", color: .green, systemImage: "gift") }
                    if line.isDispatched {
                        Badge(text: "Cuisine", color: .orange, systemImage: "flame")
                    } else if line.isDraft {
                        Badge(text: "Nouveau", color: .blue, systemImage: "plus")
                    } else {
                        Badge(text: "Enregistré", color: .secondary)
                    }
                }
                if !line.modifiers.isEmpty {
                    Text(line.modifiers.joined(separator: " · ")).font(.caption).foregroundStyle(.orange)
                }
                if let comment = line.kitchenComment {
                    Label(comment, systemImage: "text.bubble").font(.caption).foregroundStyle(.secondary)
                }
                Text(unitDescription).font(.caption).foregroundStyle(.secondary)
            }
            Spacer(minLength: 4)
            VStack(alignment: .trailing, spacing: 8) {
                Text(OrderMath.lineTotal(line).formatted).font(.body.weight(.bold)).monospacedDigit()
                HStack(spacing: 0) {
                    Button { ticket.decrement(line.id) } label: {
                        Image(systemName: "minus").frame(width: 36, height: 36)
                    }
                    .disabled(!line.isDraft)
                    .accessibilityIdentifier("line.minus.\(line.name)")
                    Text("\(line.quantity)").font(.headline.monospacedDigit()).frame(minWidth: 26)
                        .accessibilityIdentifier("line.qty.\(line.name)")
                    Button { ticket.increment(line.id) } label: {
                        Image(systemName: "plus").frame(width: 36, height: 36)
                    }
                    .accessibilityIdentifier("line.plus.\(line.name)")
                }
                .buttonStyle(.borderless)
                .background(Capsule().fill(Color(.tertiarySystemFill)))
            }
        }
        .opacity(line.isDispatched ? 0.8 : 1)
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier("line.\(line.name)")
    }

    private var unitDescription: String {
        var text = "\(line.effectiveUnitPrice.formatted) × \(line.quantity)"
        if let original = line.originalUnitPrice { text += " (au lieu de \(original.formatted))" }
        text += " · TVA \(OrderMath.effectiveTaxRate(line, destination: destination).formatted()) %"
        return text
    }
}
