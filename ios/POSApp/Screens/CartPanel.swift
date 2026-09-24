import POSKit
import SwiftUI

/// Note en cours : lignes envoyées, lignes à envoyer, totaux et actions.
struct CartPanel: View {
    @Environment(AppModel.self) private var app
    @State private var checkout: CheckoutModel?
    @State private var showHeldOrders = false
    @State private var showHoldPrompt = false
    @State private var holdLabel = ""

    private var order: OrderModel { app.order }
    private var isCounter: Bool { order.context == .counter }

    var body: some View {
        VStack(spacing: 0) {
            header
            Divider()
            lines
            Divider()
            footer
        }
        .overlay {
            if order.isBusy {
                ProgressView()
                    .padding(20)
                    .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 12))
            }
        }
        .fullScreenCover(item: $checkout) { model in
            CheckoutView(model: model)
        }
        .sheet(isPresented: $showHeldOrders) {
            HeldOrdersSheet()
        }
        .alert("Mettre en attente", isPresented: $showHoldPrompt) {
            TextField("Nom ou repère du client", text: $holdLabel)
                .accessibilityIdentifier("hold.label")
            Button("Annuler", role: .cancel) {}
            Button("Mettre en attente") {
                let label = holdLabel.trimmingCharacters(in: .whitespaces)
                Task { _ = await order.hold(label: label.isEmpty ? nil : label) }
            }
            .accessibilityIdentifier("hold.confirm")
        } message: {
            Text("La caisse est libérée pour le client suivant.")
        }
    }

    // MARK: En-tête

    private var header: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(alignment: .firstTextBaseline) {
                VStack(alignment: .leading, spacing: 2) {
                    Text(order.context?.title ?? "")
                        .font(.title2.weight(.bold))
                        .accessibilityIdentifier("cart.title")
                    if let current = order.order, !isCounter {
                        Text("\(current.coversCount) couvert\(current.coversCount > 1 ? "s" : "") · \(current.waiterName?.components(separatedBy: " (").first ?? "")")
                            .font(.subheadline)
                            .foregroundStyle(.secondary)
                    }
                }
                Spacer()
                if isCounter {
                    Button {
                        showHeldOrders = true
                    } label: {
                        Label("En attente", systemImage: "tray.full")
                    }
                    .buttonStyle(.bordered)
                    .accessibilityIdentifier("cart.heldOrders")
                } else {
                    Button {
                        Task {
                            await order.close()
                            app.section = .floor
                        }
                    } label: {
                        Label("Salle", systemImage: "chevron.backward")
                    }
                    .buttonStyle(.bordered)
                    .accessibilityIdentifier("cart.backToFloor")
                }
            }

            if isCounter {
                Picker("Destination", selection: Binding(
                    get: { order.destination },
                    set: { newValue in Task { await order.setDestination(newValue) } }
                )) {
                    Text(OrderDestination.takeaway.label).tag(OrderDestination.takeaway)
                    Text(OrderDestination.eatIn.label).tag(OrderDestination.eatIn)
                }
                .pickerStyle(.segmented)
                .accessibilityIdentifier("cart.destination")
            } else {
                Picker("Envoi", selection: Binding(
                    get: { order.currentCourse },
                    set: { order.currentCourse = $0 }
                )) {
                    ForEach(CourseType.allCases) { course in
                        Text(course.label).tag(course)
                    }
                }
                .pickerStyle(.segmented)
                .accessibilityIdentifier("cart.course")
            }
        }
        .padding(16)
    }

    // MARK: Lignes

    private var lines: some View {
        Group {
            if order.isEmpty {
                ContentUnavailableView(
                    "Note vide",
                    systemImage: "hand.tap",
                    description: Text("Touchez un article pour l'ajouter.")
                )
            } else {
                List {
                    if !order.sentLines.isEmpty {
                        Section("Envoyé en cuisine") {
                            ForEach(order.sentLines) { line in
                                ServerLineRow(line: line, sent: true)
                            }
                        }
                    }
                    if !order.savedUnsentLines.isEmpty {
                        Section("Enregistré, non envoyé") {
                            ForEach(order.savedUnsentLines) { line in
                                ServerLineRow(line: line, sent: false)
                            }
                        }
                    }
                    if order.hasPending {
                        Section("Nouveau") {
                            ForEach(order.pending) { line in
                                PendingLineRow(line: line)
                                    .swipeActions(edge: .trailing, allowsFullSwipe: true) {
                                        Button(role: .destructive) {
                                            order.remove(line.id)
                                        } label: {
                                            Label("Supprimer", systemImage: "trash")
                                        }
                                    }
                            }
                        }
                    }
                }
                .listStyle(.plain)
                .accessibilityIdentifier("cart.lines")
            }
        }
        .frame(maxHeight: .infinity)
    }

    // MARK: Totaux et actions

    private var footer: some View {
        VStack(spacing: 12) {
            VStack(spacing: 6) {
                totalRow("Total HT", order.totals.ht)
                totalRow("TVA", order.totals.vat)
                HStack(alignment: .firstTextBaseline) {
                    Text("Total TTC").font(.title3.weight(.semibold))
                    Spacer()
                    Text(order.totalDue.formatted)
                        .font(.system(size: 30, weight: .bold, design: .rounded))
                        .monospacedDigit()
                        .accessibilityIdentifier("cart.total")
                }
            }

            HStack(spacing: 10) {
                Button {
                    Task { await order.sendToKitchen() }
                } label: {
                    Label("Envoyer", systemImage: "paperplane.fill")
                }
                .buttonStyle(ActionButtonStyle(tint: .orange, prominent: order.hasUnsentItems))
                .disabled(!order.hasUnsentItems || order.isBusy)
                .accessibilityIdentifier("cart.send")

                if isCounter {
                    Button {
                        holdLabel = ""
                        showHoldPrompt = true
                    } label: {
                        Label("Attente", systemImage: "pause.fill")
                    }
                    .buttonStyle(ActionButtonStyle(tint: .purple, prominent: false))
                    .disabled(order.isEmpty || order.isBusy)
                    .accessibilityIdentifier("cart.hold")
                }
            }

            Button {
                Task { await startCheckout() }
            } label: {
                Label("Encaisser \(order.totalDue.formatted)", systemImage: "creditcard.fill")
            }
            .buttonStyle(ActionButtonStyle(tint: .green))
            .disabled(order.isEmpty || order.isBusy)
            .accessibilityIdentifier("cart.checkout")
        }
        .padding(16)
    }

    private func totalRow(_ title: String, _ amount: Money) -> some View {
        HStack {
            Text(title)
            Spacer()
            Text(amount.formatted).monospacedDigit()
        }
        .font(.subheadline)
        .foregroundStyle(.secondary)
    }

    private func startCheckout() async {
        guard let context = order.context, await order.prepareCheckout(), let current = order.order else { return }
        checkout = CheckoutModel(
            context: context,
            order: current,
            destination: isCounter ? order.destination : .eatIn,
            posContext: app.context
        )
    }
}

struct ServerLineRow: View {
    var line: OrderLine
    var sent: Bool

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Text("\(line.quantity)×")
                .font(.headline)
                .monospacedDigit()
                .frame(width: 36, alignment: .leading)
            VStack(alignment: .leading, spacing: 2) {
                Text(line.productName).font(.body.weight(.medium))
                if !line.modifiersSummary.isEmpty {
                    Text(line.modifiersSummary.joined(separator: " · "))
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
                if line.course != .direct {
                    Text(line.course.label).font(.caption2.weight(.semibold)).foregroundStyle(.orange)
                }
            }
            Spacer()
            VStack(alignment: .trailing, spacing: 2) {
                Text(line.totalPrice.formatted).monospacedDigit()
                Image(systemName: sent ? "checkmark.circle.fill" : "clock")
                    .foregroundStyle(sent ? Color.green : Color.orange)
                    .font(.caption)
            }
        }
        .foregroundStyle(sent ? Color.secondary : Color.primary)
        .padding(.vertical, 4)
        .accessibilityElement(children: .combine)
        .accessibilityIdentifier("cart.line.\(line.productName)")
    }
}

struct PendingLineRow: View {
    @Environment(AppModel.self) private var app
    var line: PendingLine

    var body: some View {
        HStack(spacing: 12) {
            VStack(alignment: .leading, spacing: 2) {
                Text(line.product.name).font(.body.weight(.semibold))
                if !line.modifierLabels.isEmpty {
                    Text(line.modifierLabels.joined(separator: " · "))
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
                if line.course != .direct {
                    Text(line.course.label).font(.caption2.weight(.semibold)).foregroundStyle(.orange)
                }
                Text(line.lineTotal.formatted)
                    .font(.subheadline)
                    .monospacedDigit()
                    .foregroundStyle(.secondary)
            }
            Spacer()
            HStack(spacing: 4) {
                Button {
                    app.order.decrement(line.id)
                } label: {
                    Image(systemName: line.quantity > 1 ? "minus" : "trash")
                        .frame(width: 40, height: 40)
                }
                .accessibilityIdentifier("cart.pending.minus.\(line.product.name)")
                Text("\(line.quantity)")
                    .font(.headline)
                    .monospacedDigit()
                    .frame(minWidth: 26)
                    .accessibilityIdentifier("cart.pending.qty.\(line.product.name)")
                Button {
                    app.order.increment(line.id)
                } label: {
                    Image(systemName: "plus")
                        .frame(width: 40, height: 40)
                }
                .accessibilityIdentifier("cart.pending.plus.\(line.product.name)")
            }
            .buttonStyle(.bordered)
        }
        .padding(.vertical, 4)
    }
}
