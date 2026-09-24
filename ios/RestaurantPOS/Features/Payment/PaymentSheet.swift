import SwiftUI
import PosKit

/// Encaissement : pourboire, partage à parts égales, moyens de paiement, rendu monnaie.
struct PaymentSheet: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    let onComplete: (TicketStore.CheckoutOutcome) -> Void

    @State private var plan = PaymentPlan(due: .zero)
    @State private var method: PaymentMethod = .creditCard
    @State private var tenderedText = ""
    @State private var customTipText = ""
    @State private var buzzer = ""
    @State private var printReceipt = false
    @State private var result: TicketStore.CheckoutOutcome?
    @State private var showsRoomCharge = false
    @State private var isPaying = false

    private var isCounter: Bool { model.ticket.isCounter }

    private var tendered: Money {
        if method == .cash || method == .mealVoucher, let value = Money.parse(tenderedText) { return value }
        return plan.amountToCollect
    }

    var body: some View {
        Group {
            if let result {
                resultView(result)
            } else {
                form
            }
        }
        .padding(28)
        .presentationDetents([.large])
        .interactiveDismissDisabled(isPaying)
        .onAppear { plan = PaymentPlan(due: model.ticket.amountDue) }
        .sheet(isPresented: $showsRoomCharge) {
            RoomChargeSheet(tip: plan.tipAmount) {
                showsRoomCharge = false
                dismiss()
                onComplete(TicketStore.CheckoutOutcome(receiptNumber: "PMS", change: .zero, remaining: .zero))
            }
        }
    }

    // MARK: Formulaire

    private var form: some View {
        HStack(alignment: .top, spacing: 28) {
            VStack(alignment: .leading, spacing: 20) {
                SheetHeader(title: "Encaissement", subtitle: model.ticket.title) { dismiss() }

                VStack(alignment: .leading, spacing: 4) {
                    Text(plan.partLabel ?? "À encaisser").font(.headline).foregroundStyle(.secondary)
                    Text(plan.amountToCollect.formatted)
                        .font(.system(size: 56, weight: .bold, design: .rounded))
                        .monospacedDigit()
                        .contentTransition(.numericText())
                        .accessibilityIdentifier("payment.amount")
                    if plan.tipAmount.cents > 0 {
                        Text("dont pourboire \(plan.tipAmount.formatted)").font(.subheadline).foregroundStyle(.secondary)
                    }
                }

                splitSection
                if !plan.isSplit { tipSection }
                if isCounter { counterOptions }
                Spacer(minLength: 0)
            }
            .frame(maxWidth: .infinity)

            Divider()

            VStack(alignment: .leading, spacing: 16) {
                Text("Moyen de paiement").font(.headline)
                LazyVGrid(columns: [GridItem(.flexible()), GridItem(.flexible())], spacing: 10) {
                    ForEach(availableMethods, id: \.self) { m in
                        Button {
                            method = m
                            tenderedText = ""
                        } label: {
                            Label(m.label, systemImage: Theme.icon(for: m))
                                .font(.headline)
                                .frame(maxWidth: .infinity, minHeight: 60)
                                .background(RoundedRectangle(cornerRadius: Theme.smallRadius).fill(method == m ? Color.accentColor.opacity(0.18) : Color(.tertiarySystemFill)))
                                .overlay(RoundedRectangle(cornerRadius: Theme.smallRadius).strokeBorder(method == m ? Color.accentColor : .clear, lineWidth: 2))
                        }
                        .buttonStyle(.plain)
                        .accessibilityIdentifier("payment.method.\(m)")
                    }
                    if !isCounter && !plan.isSplit {
                        Button { showsRoomCharge = true } label: {
                            Label("Chambre d'hôtel", systemImage: "bed.double")
                                .font(.headline)
                                .frame(maxWidth: .infinity, minHeight: 60)
                                .background(RoundedRectangle(cornerRadius: Theme.smallRadius).fill(Color(.tertiarySystemFill)))
                        }
                        .buttonStyle(.plain)
                        .accessibilityIdentifier("payment.method.room")
                    }
                }

                if method == .cash || method == .mealVoucher {
                    cashSection
                }

                Spacer(minLength: 0)

                ActionButton(title: validateTitle, systemImage: "checkmark.seal.fill", tint: .green, isLoading: isPaying) {
                    Task { await pay() }
                }
                .disabled(plan.amountToCollect.cents <= 0)
                .accessibilityIdentifier("payment.validate")
            }
            .frame(width: 380)
        }
    }

    private var availableMethods: [PaymentMethod] { [.creditCard, .cash, .mealVoucher] }

    private var validateTitle: String {
        if method == .cash, tendered > plan.amountToCollect {
            return "Valider · rendu \(OrderMath.change(tendered: tendered, due: plan.amountToCollect).formatted)"
        }
        return "Valider \(method.label.lowercased())"
    }

    private var splitSection: some View {
        VStack(alignment: .leading, spacing: 10) {
            Picker("Mode", selection: Binding(get: { plan.isSplit }, set: { plan.setGuests($0 ? 2 : nil) })) {
                Text("Addition complète").tag(false)
                Text("Partager à parts égales").tag(true)
            }
            .pickerStyle(.segmented)
            .disabled(plan.paidParts > 0)
            .accessibilityIdentifier("payment.split")
            if let guests = plan.splitGuests {
                HStack(spacing: 12) {
                    Button { plan.setGuests(guests - 1) } label: { Image(systemName: "minus").frame(width: 44, height: 44) }
                        .buttonStyle(.bordered)
                        .disabled(plan.paidParts > 0 || guests <= PaymentPlan.splitRange.lowerBound)
                        .accessibilityIdentifier("payment.guests.minus")
                    Text("\(guests) convives").font(.headline).monospacedDigit().accessibilityIdentifier("payment.guests")
                    Button { plan.setGuests(guests + 1) } label: { Image(systemName: "plus").frame(width: 44, height: 44) }
                        .buttonStyle(.bordered)
                        .disabled(plan.paidParts > 0 || guests >= PaymentPlan.splitRange.upperBound)
                        .accessibilityIdentifier("payment.guests.plus")
                    Spacer()
                }
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: 8) {
                        ForEach(Array(plan.parts.enumerated()), id: \.offset) { index, part in
                            VStack(spacing: 2) {
                                Text("#\(index + 1)").font(.caption.weight(.bold))
                                Text(part.formatted).font(.subheadline.monospacedDigit())
                            }
                            .padding(10)
                            .background(RoundedRectangle(cornerRadius: 10).fill(index < plan.paidParts ? Color.green.opacity(0.2) : index == plan.paidParts ? Color.accentColor.opacity(0.2) : Color(.tertiarySystemFill)))
                            .overlay(alignment: .topTrailing) {
                                if index < plan.paidParts { Image(systemName: "checkmark.circle.fill").foregroundStyle(.green).offset(x: 4, y: -4) }
                            }
                        }
                    }
                }
            }
        }
    }

    private var tipSection: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Pourboire").font(.headline)
            HStack(spacing: 8) {
                ForEach(PaymentPlan.tipPercentages, id: \.self) { percent in
                    ChipButton(title: percent == 0 ? "Aucun" : "\(percent) %", isSelected: isTipSelected(percent)) {
                        plan.tip = percent == 0 ? .none : .percent(percent)
                        customTipText = ""
                    }
                    .accessibilityIdentifier("payment.tip.\(percent)")
                }
                TextField("Autre €", text: $customTipText)
                    .keyboardType(.decimalPad)
                    .textFieldStyle(.roundedBorder)
                    .frame(width: 100)
                    .onChange(of: customTipText) { _, text in
                        if let value = Money.parse(text) { plan.tip = .custom(value) } else if text.isEmpty { plan.tip = .none }
                    }
                    .accessibilityIdentifier("payment.tip.custom")
            }
        }
    }

    private func isTipSelected(_ percent: Int) -> Bool {
        switch plan.tip {
        case .none: percent == 0
        case .percent(let p): p == percent
        case .custom: false
        }
    }

    private var counterOptions: some View {
        VStack(alignment: .leading, spacing: 10) {
            TextField("N° de buzzer / bipeur (facultatif)", text: $buzzer)
                .keyboardType(.numberPad)
                .textFieldStyle(.roundedBorder)
                .accessibilityIdentifier("payment.buzzer")
            Toggle(isOn: $printReceipt) {
                Label("Imprimer le ticket de caisse (loi AGEC : sur demande)", systemImage: "printer")
            }
            .accessibilityIdentifier("payment.printReceipt")
        }
    }

    private var cashSection: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(method == .cash ? "Montant remis" : "Valeur faciale du titre").font(.subheadline.weight(.semibold))
            HStack(spacing: 8) {
                ForEach(OrderMath.suggestedCashAmounts(for: plan.amountToCollect), id: \.self) { amount in
                    Button(amount == plan.amountToCollect ? "Exact" : amount.formatted) { tenderedText = amount.plain }
                        .buttonStyle(.bordered)
                        .accessibilityIdentifier(amount == plan.amountToCollect ? "payment.cash.exact" : "payment.cash.\(amount.cents / 100)")
                }
            }
            HStack {
                Text(tenderedText.isEmpty ? plan.amountToCollect.formatted : (Money.parse(tenderedText)?.formatted ?? tenderedText))
                    .font(.title2.monospacedDigit().weight(.semibold))
                    .accessibilityIdentifier("payment.tendered")
                Spacer()
                if method == .cash {
                    Text("Rendu \(OrderMath.change(tendered: tendered, due: plan.amountToCollect).formatted)")
                        .font(.headline)
                        .foregroundStyle(.green)
                        .accessibilityIdentifier("payment.change")
                }
            }
            NumericKeypad(allowsDecimal: true, keySize: 54, identifierPrefix: "payment.key") { key in
                if key == "," { if !tenderedText.contains(".") { tenderedText += tenderedText.isEmpty ? "0." : "." } } else { tenderedText += key }
            } onDelete: {
                if !tenderedText.isEmpty { tenderedText.removeLast() }
            }
        }
    }

    // MARK: Validation

    private func pay() async {
        isPaying = true
        defer { isPaying = false }
        let amount = plan.amountToCollect
        let outcome: TicketStore.CheckoutOutcome?
        if isCounter {
            outcome = await model.ticket.counterCheckout(method: method, amount: amount, tendered: tendered, tip: plan.tipAmount, buzzer: buzzer, printReceipt: printReceipt, policy: model.settings.mealVoucherPolicy)
        } else {
            outcome = await model.ticket.pay(method: method, amount: amount, tendered: tendered)
        }
        guard let outcome else { Haptics.error(); return }
        Haptics.success()
        tenderedText = ""
        if isCounter && outcome.isComplete {
            dismiss()
            onComplete(outcome)
            return
        }
        if plan.isSplit && !outcome.isComplete {
            plan.markPartPaid()
            if plan.remainingParts == 0 {
                // Écart résiduel (arrondis) : on repasse en paiement du solde.
                plan = PaymentPlan(due: outcome.remaining)
            }
            if outcome.change.cents > 0 { result = outcome }
            return
        }
        if !outcome.isComplete {
            plan = PaymentPlan(due: outcome.remaining)
        }
        result = outcome
    }

    private func resultView(_ outcome: TicketStore.CheckoutOutcome) -> some View {
        VStack(spacing: 20) {
            Spacer()
            Image(systemName: outcome.isComplete ? "checkmark.circle.fill" : "clock.badge.checkmark")
                .font(.system(size: 72))
                .foregroundStyle(.green)
            Text(outcome.isComplete ? "Paiement validé" : "Paiement partiel enregistré").font(.largeTitle.weight(.bold))
            if outcome.change.cents > 0 {
                Text("Rendu monnaie").font(.title3).foregroundStyle(.secondary)
                Text(outcome.change.formatted).font(.system(size: 64, weight: .bold, design: .rounded)).foregroundStyle(.green)
                    .accessibilityIdentifier("result.change")
            }
            if !outcome.isComplete {
                Text("Reste à payer : \(outcome.remaining.formatted)").font(.title2).accessibilityIdentifier("result.remaining")
            }
            if let receipt = outcome.receiptNumber { Text("Reçu \(receipt)").font(.subheadline).foregroundStyle(.secondary) }
            Spacer()
            ActionButton(title: outcome.isComplete ? "Terminer" : "Continuer l'encaissement", systemImage: outcome.isComplete ? "checkmark" : "arrow.right", tint: .green) {
                if outcome.isComplete {
                    dismiss()
                    onComplete(outcome)
                } else {
                    result = nil
                }
            }
            .frame(maxWidth: 360)
            .accessibilityIdentifier("result.done")
        }
        .frame(maxWidth: .infinity)
    }
}

// MARK: - Rendu monnaie comptoir

struct ChangeOverlay: View {
    let outcome: TicketStore.CheckoutOutcome
    let onDone: () -> Void

    var body: some View {
        VStack(spacing: 24) {
            Spacer()
            Text("Monnaie à rendre").font(.title2).foregroundStyle(.secondary)
            Text(outcome.change.formatted)
                .font(.system(size: 96, weight: .heavy, design: .rounded))
                .foregroundStyle(.green)
                .accessibilityIdentifier("change.amount")
            HStack(spacing: 40) {
                VStack {
                    Text("N° de retrait").font(.headline).foregroundStyle(.secondary)
                    Text(outcome.pickupNumber ?? "—").font(.system(size: 48, weight: .bold, design: .rounded))
                        .accessibilityIdentifier("change.pickup")
                }
                if let buzzer = outcome.buzzer {
                    VStack {
                        Text("Buzzer").font(.headline).foregroundStyle(.secondary)
                        Text(buzzer).font(.system(size: 48, weight: .bold, design: .rounded))
                    }
                }
            }
            if let voucher = outcome.creditVoucher {
                Card {
                    VStack(alignment: .leading, spacing: 4) {
                        Label("Avoir client émis", systemImage: "ticket").font(.headline)
                        Text(voucher.voucherCode).font(.title2.monospaced().weight(.bold)).accessibilityIdentifier("change.voucher")
                        Text("Montant \(voucher.amount.formatted) · valable 90 jours").font(.subheadline).foregroundStyle(.secondary)
                    }
                }
                .frame(maxWidth: 420)
            }
            if let receipt = outcome.receiptNumber { Text("Reçu \(receipt)").foregroundStyle(.secondary) }
            Spacer()
            ActionButton(title: "Client suivant", systemImage: "arrow.right.circle.fill", tint: .green) { onDone() }
                .frame(maxWidth: 360)
                .accessibilityIdentifier("change.done")
                .padding(.bottom, 40)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Color(.systemBackground))
    }
}

// MARK: - Facturation chambre (PMS)

struct RoomChargeSheet: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    let tip: Money
    let onCharged: () -> Void

    @State private var rooms: [HotelRoom] = []
    @State private var selected: HotelRoom?
    @State private var strokes: [[CGPoint]] = []

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            SheetHeader(title: "Facturer sur une chambre", subtitle: "Total \((model.ticket.amountDue + tip).formatted)") { dismiss() }
            if rooms.isEmpty {
                ProgressView().frame(maxWidth: .infinity)
            } else {
                Picker("Chambre", selection: $selected) {
                    ForEach(rooms) { room in
                        Text("Chambre \(room.roomNumber) — \(room.guestName)").tag(Optional(room))
                    }
                }
                .pickerStyle(.menu)
                .accessibilityIdentifier("room.picker")
                if let selected {
                    HStack {
                        Label(selected.guestName, systemImage: "person")
                        Spacer()
                        Text("Crédit disponible \(selected.availableCredit.formatted)").foregroundStyle(selected.availableCredit >= model.ticket.amountDue + tip ? Color.green : Color.red)
                    }
                    .font(.subheadline)
                }
            }
            HStack {
                Text("Signature du client").font(.headline)
                Spacer()
                Button("Effacer") { strokes = [] }.accessibilityIdentifier("room.clearSignature")
            }
            SignaturePad(strokes: $strokes)
                .frame(height: 180)
                .accessibilityIdentifier("room.signature")
            ActionButton(title: "Valider la facturation", systemImage: "bed.double.fill", tint: .indigo) {
                Task { await charge() }
            }
            .disabled(selected == nil || strokes.isEmpty)
            .accessibilityIdentifier("room.confirm")
        }
        .padding(28)
        .task {
            rooms = (try? await model.api.hotelRooms()) ?? []
            selected = rooms.first
        }
    }

    @MainActor
    private func charge() async {
        guard let selected else { return }
        let renderer = ImageRenderer(content: SignaturePad.Drawing(strokes: strokes).frame(width: 480, height: 180).background(Color.white))
        let png = renderer.uiImage?.pngData()
        if await model.ticket.chargeRoom(selected, tip: tip, signaturePNG: png) { onCharged() }
    }
}

struct SignaturePad: View {
    @Binding var strokes: [[CGPoint]]

    struct Drawing: View {
        let strokes: [[CGPoint]]
        var body: some View {
            Canvas { context, _ in
                for stroke in strokes where stroke.count > 1 {
                    var path = Path()
                    path.addLines(stroke)
                    context.stroke(path, with: .color(.black), style: StrokeStyle(lineWidth: 2.5, lineCap: .round, lineJoin: .round))
                }
            }
        }
    }

    var body: some View {
        Drawing(strokes: strokes)
            .background(RoundedRectangle(cornerRadius: Theme.smallRadius).fill(Color.white))
            .overlay(RoundedRectangle(cornerRadius: Theme.smallRadius).strokeBorder(Color(.separator)))
            .overlay { if strokes.isEmpty { Text("Signez ici").foregroundStyle(.gray) } }
            .gesture(
                DragGesture(minimumDistance: 0)
                    .onChanged { value in
                        if strokes.isEmpty || value.translation == .zero {
                            strokes.append([value.location])
                        } else {
                            strokes[strokes.count - 1].append(value.location)
                        }
                    }
            )
    }
}
