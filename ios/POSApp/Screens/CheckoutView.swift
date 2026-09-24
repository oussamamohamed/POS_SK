import POSKit
import SwiftUI

/// Encaissement plein écran : récapitulatif et partage à gauche, règlement à droite.
struct CheckoutView: View {
    @Environment(AppModel.self) private var app
    @Environment(\.dismiss) private var dismiss
    @Bindable var model: CheckoutModel

    var body: some View {
        NavigationStack {
            Group {
                if case .completed(let receipt) = model.phase {
                    CheckoutSuccessView(receipt: receipt, context: model.context) {
                        Task { await finish() }
                    }
                } else {
                    HStack(alignment: .top, spacing: 24) {
                        summaryColumn
                            .frame(maxWidth: 420)
                        paymentColumn
                    }
                    .padding(24)
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(Color.posBackground)
            .navigationTitle("Encaissement — \(model.context.title)")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                if !model.isCompleted {
                    ToolbarItem(placement: .cancellationAction) {
                        Button("Fermer") { closeWithoutCompleting() }
                            .disabled(model.phase == .processing)
                            .accessibilityIdentifier("checkout.close")
                    }
                }
            }
        }
        .noticeOverlay()
        .sensoryFeedback(.success, trigger: model.isCompleted)
        .interactiveDismissDisabled()
    }

    // MARK: Récapitulatif

    private var summaryColumn: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                card {
                    row("Total de la note", model.orderTotal)
                    if model.remaining != model.orderTotal {
                        row("Déjà réglé", model.orderTotal - model.remaining)
                    }
                    Divider()
                    HStack(alignment: .firstTextBaseline) {
                        Text("Reste à payer").font(.title3.weight(.semibold))
                        Spacer()
                        Text(model.remaining.formatted)
                            .font(.system(size: 34, weight: .bold, design: .rounded))
                            .monospacedDigit()
                            .accessibilityIdentifier("checkout.remaining")
                    }
                }

                card {
                    HStack {
                        Label("Partager l'addition", systemImage: "person.2.fill").font(.headline)
                        Spacer()
                        Button {
                            model.split(into: model.splitCount - 1)
                        } label: {
                            Image(systemName: "minus").frame(width: 36, height: 36)
                        }
                        .buttonStyle(.bordered)
                        .disabled(model.splitCount <= 1 || model.paidParts > 0)
                        .accessibilityIdentifier("split.minus")
                        Text("\(model.splitCount)")
                            .font(.title3.weight(.bold))
                            .monospacedDigit()
                            .frame(minWidth: 32)
                            .accessibilityIdentifier("split.count")
                        Button {
                            model.split(into: model.splitCount + 1)
                        } label: {
                            Image(systemName: "plus").frame(width: 36, height: 36)
                        }
                        .buttonStyle(.bordered)
                        .disabled(model.paidParts > 0)
                        .accessibilityIdentifier("split.plus")
                    }
                    if model.isSplit {
                        ForEach(Array(model.parts.enumerated()), id: \.offset) { index, part in
                            HStack {
                                Image(systemName: index < model.paidParts ? "checkmark.circle.fill" : (index == model.paidParts ? "arrow.right.circle.fill" : "circle"))
                                    .foregroundStyle(index < model.paidParts ? Color.green : (index == model.paidParts ? Color.accentColor : Color.secondary))
                                Text("Convive \(index + 1)")
                                Spacer()
                                Text(part.formatted).monospacedDigit()
                            }
                            .font(.subheadline)
                            .accessibilityElement(children: .combine)
                            .accessibilityIdentifier("split.part.\(index + 1)")
                        }
                    } else {
                        Text("Parts égales calculées au centime près.")
                            .font(.footnote)
                            .foregroundStyle(.secondary)
                    }
                }

                if model.context == .counter {
                    counterOptions
                }
            }
        }
    }

    private var counterOptions: some View {
        card {
            Text("Vente comptoir — \(model.destination.label)").font(.headline)
            TextField("N° de bipeur (facultatif)", text: $model.pickupBuzzer)
                .keyboardType(.numberPad)
                .textFieldStyle(.roundedBorder)
                .accessibilityIdentifier("checkout.buzzer")
            Picker("Titre-restaurant supérieur au dû", selection: $model.mealVoucherPolicy) {
                ForEach(MealVoucherPolicy.allCases) { policy in
                    Text(policy.label).tag(policy)
                }
            }
            if !model.counterTenders.isEmpty {
                Divider()
                ForEach(Array(model.counterTenders.enumerated()), id: \.offset) { _, tender in
                    HStack {
                        Label(tender.method.label, systemImage: tender.method.systemImage)
                        Spacer()
                        Text(tender.amount.formatted).monospacedDigit()
                    }
                    .font(.subheadline)
                }
                Button("Annuler les règlements saisis", role: .destructive) {
                    model.resetCounterTenders()
                }
                .accessibilityIdentifier("checkout.resetTenders")
                if !model.remaining.isPositive {
                    Button("Réessayer l'envoi") {
                        Task { await model.submitCounter() }
                    }
                    .accessibilityIdentifier("checkout.retry")
                }
            }
        }
    }

    // MARK: Règlement

    private var paymentColumn: some View {
        VStack(spacing: 16) {
            HStack(spacing: 10) {
                ForEach(PaymentMethod.checkoutMethods) { method in
                    Button {
                        model.method = method
                    } label: {
                        VStack(spacing: 6) {
                            Image(systemName: method.systemImage).font(.title2)
                            Text(method.label).font(.caption.weight(.semibold)).lineLimit(1).minimumScaleFactor(0.7)
                        }
                        .frame(maxWidth: .infinity, minHeight: 68)
                        .foregroundStyle(model.method == method ? Color.white : Color.primary)
                        .background(
                            RoundedRectangle(cornerRadius: 14, style: .continuous)
                                .fill(model.method == method ? Color.accentColor : Color.posSurface)
                        )
                    }
                    .buttonStyle(.plain)
                    .accessibilityIdentifier("method.\(method.apiName)")
                    .accessibilityAddTraits(model.method == method ? .isSelected : [])
                }
            }

            HStack(spacing: 16) {
                amountBox(title: model.isSplit ? "Part du convive \(model.paidParts + 1)" : "À régler", amount: model.amountDue, identifier: "checkout.due")
                amountBox(title: "Montant remis", amount: model.tendered, identifier: "checkout.tendered", highlight: !model.entry.isEmpty)
                amountBox(title: "Rendu monnaie", amount: model.change, identifier: "checkout.change", tint: model.change.isPositive ? .green : .secondary)
            }

            if model.overpaymentNotAllowed {
                Label("Le montant dépasse le reste dû pour ce moyen de paiement.", systemImage: "exclamationmark.triangle.fill")
                    .foregroundStyle(.orange)
                    .font(.callout)
            }

            if model.method == .cash {
                HStack(spacing: 10) {
                    ForEach(Array(model.cashSuggestions.enumerated()), id: \.offset) { index, amount in
                        Button(amount.formatted) {
                            model.entry.set(amount)
                        }
                        .buttonStyle(ActionButtonStyle(tint: .green, prominent: false))
                        .accessibilityIdentifier("quick.\(index)")
                    }
                }
            }

            NumericKeypad(identifierPrefix: "amount", bottomLeft: .doubleZero, keyHeight: 58) { key in
                switch key {
                case .digit(let digit): model.entry.append(digit)
                case .doubleZero: model.entry.appendDoubleZero()
                case .delete: model.entry.backspace()
                case .clear: model.entry.clear()
                }
            }

            Button {
                Task { await model.submit() }
            } label: {
                if model.phase == .processing {
                    ProgressView().tint(.white)
                } else {
                    Label("Valider \(model.applied.formatted) — \(model.method.label)", systemImage: "checkmark.seal.fill")
                }
            }
            .buttonStyle(ActionButtonStyle(tint: .green))
            .disabled(!model.canSubmit)
            .accessibilityIdentifier("checkout.submit")
        }
    }

    private func amountBox(title: String, amount: Money, identifier: String, highlight: Bool = false, tint: Color = .primary) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(title).font(.caption).foregroundStyle(.secondary).lineLimit(1)
            Text(amount.formatted)
                .font(.system(size: 26, weight: .bold, design: .rounded))
                .monospacedDigit()
                .foregroundStyle(tint)
                .lineLimit(1)
                .minimumScaleFactor(0.6)
                .accessibilityIdentifier(identifier)
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(
            RoundedRectangle(cornerRadius: 14, style: .continuous)
                .fill(Color.posSurface)
        )
        .overlay(
            RoundedRectangle(cornerRadius: 14, style: .continuous)
                .stroke(highlight ? Color.accentColor : Color.clear, lineWidth: 2)
        )
    }

    private func card<Content: View>(@ViewBuilder _ content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            content()
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 16, style: .continuous).fill(Color.posSurface))
    }

    private func row(_ title: String, _ amount: Money) -> some View {
        HStack {
            Text(title).foregroundStyle(.secondary)
            Spacer()
            Text(amount.formatted).monospacedDigit()
        }
    }

    // MARK: Fin

    private func closeWithoutCompleting() {
        if model.context == .counter, !model.counterTenders.isEmpty {
            model.resetCounterTenders()
        }
        Task {
            // Des règlements partiels ont pu être enregistrés en salle : on recharge le reste dû.
            await app.order.reload()
        }
        dismiss()
    }

    private func finish() async {
        let wasTable = model.context != .counter
        await app.order.paymentCompleted()
        dismiss()
        if wasTable {
            app.section = .floor
            await app.floor.load()
        }
    }
}

struct CheckoutSuccessView: View {
    var receipt: CheckoutReceipt
    var context: OrderContext
    var onDone: () -> Void

    var body: some View {
        VStack(spacing: 24) {
            Image(systemName: "checkmark.circle.fill")
                .font(.system(size: 96))
                .foregroundStyle(.green)
            Text("Paiement accepté")
                .font(.largeTitle.weight(.bold))
                .accessibilityIdentifier("checkout.success")

            if receipt.changeGiven.isPositive {
                VStack(spacing: 4) {
                    Text("Monnaie à rendre").font(.title3).foregroundStyle(.secondary)
                    Text(receipt.changeGiven.formatted)
                        .font(.system(size: 64, weight: .heavy, design: .rounded))
                        .monospacedDigit()
                        .accessibilityIdentifier("checkout.success.change")
                }
            }

            if let pickup = receipt.pickupNumber {
                VStack(spacing: 4) {
                    Text("Numéro de retrait").font(.title3).foregroundStyle(.secondary)
                    Text(pickup)
                        .font(.system(size: 56, weight: .bold, design: .rounded))
                        .accessibilityIdentifier("checkout.success.pickup")
                }
            }

            if let voucher = receipt.creditVoucher {
                Label("Avoir \(voucher.voucherCode) : \(voucher.amount.formatted)", systemImage: "ticket")
                    .font(.headline)
            }

            VStack(spacing: 4) {
                Text("Encaissé : \(receipt.totalPaid.formatted)").font(.headline)
                if !receipt.receiptNumbers.isEmpty {
                    Text("Ticket\(receipt.receiptNumbers.count > 1 ? "s" : "") \(receipt.receiptNumbers.joined(separator: ", "))")
                        .foregroundStyle(.secondary)
                }
                if let signature = receipt.fiscalSignature {
                    Text("Signature NF525 \(signature.prefix(16))…")
                        .font(.caption.monospaced())
                        .foregroundStyle(.tertiary)
                }
            }

            Button(action: onDone) {
                Text(context == .counter ? "Client suivant" : "Retour à la salle")
            }
            .buttonStyle(ActionButtonStyle())
            .frame(maxWidth: 360)
            .accessibilityIdentifier("checkout.done")
        }
        .padding(40)
    }
}
