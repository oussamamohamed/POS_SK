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
            Theme.line.frame(height: 1)
            if ticket.isEmpty {
                EmptyStateView(title: "\(ticket.title) vide", systemImage: "fork.knife", message: "Touchez un article pour démarrer la commande")
                    .frame(maxHeight: .infinity)
            } else {
                List {
                    ForEach(activeCourses, id: \.self) { course in
                        Section {
                            ForEach(ticket.lines.filter { $0.course == course }) { line in
                                TicketLineRow(line: line, destination: ticket.destination)
                                    .swipeActions(edge: .trailing, allowsFullSwipe: true) {
                                        if line.isDraft {
                                            Button(role: .destructive) { ticket.remove(line.id) } label: { Label("Supprimer", systemImage: "trash") }
                                        }
                                    }
                                    .listRowInsets(EdgeInsets(top: Theme.Space.s, leading: Theme.Space.l, bottom: Theme.Space.s, trailing: Theme.Space.l))
                                    .listRowBackground(Theme.surface)
                                    .listRowSeparatorTint(Theme.line)
                            }
                        } header: {
                            CourseTag(course: course)
                                .padding(.vertical, Theme.Space.xs)
                                .frame(maxWidth: .infinity, alignment: .leading)
                        }
                    }
                }
                .listStyle(.plain)
                .scrollContentBackground(.hidden)
                .accessibilityIdentifier("ticket.lines")
            }
            Theme.line.frame(height: 1)
            totals
            actions
        }
        .background(Theme.surface)
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

    /// Suites présentes dans le ticket, dans l'ordre de service.
    private var activeCourses: [CourseType] {
        let used = Set(model.ticket.lines.map(\.course))
        return CourseType.allCases.filter { used.contains($0) }
    }

    // MARK: En-tête

    private var header: some View {
        let ticket = model.ticket
        return VStack(alignment: .leading, spacing: 10) {
            HStack {
                VStack(alignment: .leading, spacing: 2) {
                    Text(ticket.title).font(.system(size: 22, weight: .bold)).foregroundStyle(Theme.ink).accessibilityIdentifier("ticket.title")
                    if !ticket.isCounter {
                        Text(ticket.covers > 0 ? "\(ticket.covers) couvert(s)" : "Sur place").font(.posLabel).foregroundStyle(Theme.inkMuted)
                    }
                }
                Spacer()
                if ticket.isCounter {
                    Button {
                        Task { await ticket.refreshHeldOrders(); sheet = .held }
                    } label: {
                        Label("\(ticket.heldOrders.count)", systemImage: "pause.circle")
                            .font(.posHeadline)
                            .padding(.horizontal, Theme.Space.m)
                            .frame(minHeight: Theme.touchMin)
                    }
                    .buttonStyle(ActionButtonStyle(kind: .neutral))
                    .accessibilityIdentifier("ticket.heldQueue")
                    .accessibilityLabel("Commandes en attente : \(ticket.heldOrders.count)")
                } else {
                    Button {
                        Task { await ticket.openCounter(destination: .takeaway) }
                    } label: {
                        Label("Comptoir", systemImage: "bag")
                            .font(.posLabel)
                            .padding(.horizontal, Theme.Space.m)
                            .frame(minHeight: Theme.touchMin)
                    }
                    .buttonStyle(ActionButtonStyle(kind: .neutral))
                    .accessibilityIdentifier("ticket.toCounter")
                }
                Button {
                    router.section = .floor
                } label: {
                    Image(systemName: "square.grid.3x3.topleft.filled")
                        .font(.system(size: 17, weight: .semibold))
                        .frame(width: Theme.touchMin, height: Theme.touchMin)
                }
                .buttonStyle(ActionButtonStyle(kind: .neutral))
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
        .padding(Theme.Space.l)
    }

    // MARK: Totaux

    private var totals: some View {
        let ticket = model.ticket
        let totals = ticket.totals
        return VStack(spacing: 6) {
            if let discount = ticket.discount {
                row("Sous-total", totals.subtotalTtc.formatted)
                row("Remise \(discount.label)\(discount.reason.map { " · \($0)" } ?? "")", "−\(totals.discountAmount.formatted)", color: Theme.success)
            }
            row("Total HT", totals.totalHt.formatted)
            ForEach(totals.vatLines, id: \.ratePercent) { vat in
                row("TVA \(vat.ratePercent.formatted()) %", vat.vat.formatted)
            }
            if let remaining = ticket.remainingBalance, remaining != totals.totalTtc {
                row("Déjà réglé", (totals.totalTtc - remaining).formatted, color: Theme.success)
            }
            HStack(alignment: .firstTextBaseline) {
                Text(ticket.remainingBalance == nil ? "Total TTC" : "Reste à payer").font(.posHeadline).foregroundStyle(Theme.ink)
                Spacer()
                Text(ticket.amountDue.formatted)
                    .font(.posAmountXL)
                    .foregroundStyle(Theme.ink)
                    .lineLimit(1)
                    .minimumScaleFactor(0.6)
                    .contentTransition(.numericText())
                    .accessibilityIdentifier("ticket.total")
            }
        }
        .padding(.horizontal, Theme.Space.l)
        .padding(.top, Theme.Space.m)
        .padding(.bottom, Theme.Space.s)
        .animation(.snappy, value: totals.totalTtc)
    }

    private func row(_ label: String, _ value: String, color: Color = Theme.inkMuted) -> some View {
        HStack {
            Text(label).lineLimit(1).font(.posLabel)
            Spacer()
            Text(value).font(.system(size: 13, weight: .medium, design: .monospaced))
        }
        .foregroundStyle(color)
    }

    // MARK: Actions

    private var actions: some View {
        let ticket = model.ticket
        return VStack(spacing: Theme.Space.s) {
            HStack(spacing: Theme.Space.s) {
                smallAction("Remise", "percent", kind: .neutral, id: "ticket.discount", disabled: ticket.isEmpty) {
                    Task { if await ticket.prepareForDiscount() { sheet = .discount } }
                }
                if ticket.isCounter {
                    smallAction("Attente", "pause", kind: .neutral, id: "ticket.hold", disabled: ticket.isEmpty) { sheet = .hold }
                } else {
                    smallAction("Transfert", "arrow.left.arrow.right", kind: .neutral, id: "ticket.transfer", disabled: ticket.isEmpty) {
                        Task { await model.floor.load(); sheet = .transfer }
                    }
                    smallAction("Suite", "bell", kind: .neutral, id: "ticket.fireSuite", disabled: ticket.isEmpty) {
                        Task { await ticket.fireSuite() }
                    }
                }
                smallAction("Vider", "trash", kind: .danger, id: "ticket.clear", disabled: !ticket.hasDrafts) { ticket.clearDrafts() }
            }

            if ticket.isCounter && !ticket.isEmpty {
                FastCashBar { counterOutcome = $0 }
            }

            HStack(spacing: Theme.Space.s) {
                ActionButton(title: "Cuisine", systemImage: "paperplane.fill", isLoading: ticket.isBusy, kind: .tonal, height: Theme.touchLarge) {
                    Task {
                        if await ticket.sendToKitchen(), !ticket.isCounter {
                            Haptics.success()
                            router.section = .floor
                        }
                    }
                }
                .disabled(!ticket.hasUndispatched)
                .accessibilityIdentifier("ticket.send")
                .frame(maxWidth: 150)

                ActionButton(title: "Encaisser \(ticket.amountDue.formatted)", systemImage: "creditcard.fill", kind: .primary, height: Theme.touchLarge) {
                    sheet = .payment
                }
                .disabled(ticket.amountDue.cents <= 0)
                .accessibilityIdentifier("ticket.pay")
            }
        }
        .padding(.horizontal, Theme.Space.l)
        .padding(.top, Theme.Space.m)
        .padding(.bottom, Theme.Space.l)
        .background(Theme.sunken)
    }

    private func smallAction(_ title: String, _ icon: String, kind: ActionButton.Kind, id: String, disabled: Bool, action: @escaping () -> Void) -> some View {
        Button(action: { Haptics.tap(); action() }) {
            VStack(spacing: 2) {
                Image(systemName: icon).font(.system(size: 17, weight: .semibold))
                Text(title).font(.system(size: 12, weight: .semibold)).lineLimit(1).minimumScaleFactor(0.8)
            }
            .frame(maxWidth: .infinity, minHeight: Theme.touchTarget)
        }
        .buttonStyle(ActionButtonStyle(kind: kind))
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
            Image(systemName: "banknote").foregroundStyle(Theme.success)
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
                .font(.system(size: 13, weight: .bold, design: .monospaced))
                .buttonStyle(.bordered)
                .tint(Theme.success)
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
        HStack(alignment: .center, spacing: Theme.Space.m) {
            HStack(spacing: 0) {
                Button { ticket.decrement(line.id) } label: {
                    Image(systemName: "minus").frame(width: 36, height: 40)
                }
                .disabled(!line.isDraft)
                .accessibilityIdentifier("line.minus.\(line.name)")
                Text("\(line.quantity)")
                    .font(.system(size: 15, weight: .semibold, design: .monospaced))
                    .frame(minWidth: 22)
                    .accessibilityIdentifier("line.qty.\(line.name)")
                Button { ticket.increment(line.id) } label: {
                    Image(systemName: "plus").frame(width: 36, height: 40)
                }
                .accessibilityIdentifier("line.plus.\(line.name)")
            }
            .font(.system(size: 15, weight: .bold))
            .foregroundStyle(Theme.ink)
            .buttonStyle(.borderless)
            .background(RoundedRectangle(cornerRadius: Theme.Radius.sm, style: .continuous).fill(Theme.raised))

            VStack(alignment: .leading, spacing: 3) {
                Text(line.name).font(.system(size: 15, weight: .semibold)).foregroundStyle(Theme.ink).strikethrough(line.isComp)
                if !line.modifiers.isEmpty {
                    Text(line.modifiers.joined(separator: " · ")).font(.system(size: 13, weight: .medium)).foregroundStyle(Theme.inkMuted)
                }
                if let comment = line.kitchenComment {
                    Label(comment, systemImage: "text.bubble").font(.system(size: 13, weight: .medium)).foregroundStyle(Theme.inkMuted)
                }
                HStack(spacing: Theme.Space.xs) {
                    Button {
                        ticket.cycleCourse(line.id)
                    } label: {
                        CourseTag(course: line.course)
                    }
                    .buttonStyle(.plain)
                    .disabled(!line.isDraft)
                    .accessibilityLabel(line.course.label)
                    .accessibilityIdentifier("line.course.\(line.name)")
                    if line.isHappyHourApplied { Badge(text: "HH", color: Theme.happyInk, systemImage: "wineglass") }
                    if line.isComp { Badge(text: "Offert", color: Theme.success, systemImage: "gift") }
                    if line.isDispatched {
                        Label("En cuisine", systemImage: "checkmark").font(.system(size: 12, weight: .semibold)).foregroundStyle(Theme.inkSubtle)
                    } else {
                        HStack(spacing: 4) {
                            Circle().fill(Theme.warning).frame(width: 6, height: 6)
                            Text("Non envoyé")
                        }
                        .font(.system(size: 12, weight: .semibold))
                        .foregroundStyle(Theme.warning)
                    }
                }
                Text(unitDescription).font(.system(size: 12, weight: .medium)).foregroundStyle(Theme.inkSubtle)
            }
            Spacer(minLength: 4)
            Text(OrderMath.lineTotal(line).formatted)
                .font(.posAmountSmall)
                .foregroundStyle(Theme.ink)
        }
        .opacity(line.isDispatched ? 0.85 : 1)
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
