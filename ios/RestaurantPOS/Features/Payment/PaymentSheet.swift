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
                SheetHeader(title: String(localized: "payment.sheet_title"), subtitle: model.ticket.title) { dismiss() }

                VStack(alignment: .leading, spacing: 4) {
                    Text(plan.partLabel ?? String(localized: "payment.to_collect_label")).font(.headline).foregroundStyle(Theme.inkMuted)
                    Text(plan.amountToCollect.formatted)
                        .font(.system(size: 56, weight: .bold, design: .rounded))
                        .monospacedDigit()
                        .contentTransition(.numericText())
                        .environment(\.layoutDirection, .leftToRight)
                        .accessibilityIdentifier("payment.amount")
                    if plan.tipAmount.cents > 0 {
                        Text("payment.tip_included \(plan.tipAmount.formatted)").font(.subheadline).foregroundStyle(Theme.inkMuted)
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
                Text("payment.method_label").font(.headline)
                LazyVGrid(columns: [GridItem(.flexible()), GridItem(.flexible())], spacing: 10) {
                    ForEach(availableMethods, id: \.self) { m in
                        Button {
                            method = m
                            tenderedText = ""
                        } label: {
                            Label(m.label, systemImage: Theme.icon(for: m))
                                .font(.headline)
                                .frame(maxWidth: .infinity, minHeight: 60)
                                .background(RoundedRectangle(cornerRadius: Theme.smallRadius).fill(method == m ? Theme.primary.opacity(0.18) : Theme.raised))
                                .overlay(RoundedRectangle(cornerRadius: Theme.smallRadius).strokeBorder(method == m ? Theme.primary : .clear, lineWidth: 2))
                        }
                        .buttonStyle(.plain)
                        .accessibilityIdentifier("payment.method.\(m)")
                    }
                    if !isCounter && !plan.isSplit {
                        Button { showsRoomCharge = true } label: {
                            Label("payment.room_charge_label", systemImage: "bed.double")
                                .font(.headline)
                                .frame(maxWidth: .infinity, minHeight: 60)
                                .background(RoundedRectangle(cornerRadius: Theme.smallRadius).fill(Theme.raised))
                        }
                        .buttonStyle(.plain)
                        .accessibilityIdentifier("payment.method.room")
                    }
                }

                if method == .cash || method == .mealVoucher {
                    cashSection
                }

                Spacer(minLength: 0)

                ActionButton(title: validateTitle, systemImage: "checkmark.seal.fill", isLoading: isPaying, kind: .primary) {
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
            return String(localized: "payment.validate_with_change \(OrderMath.change(tendered: tendered, due: plan.amountToCollect).formatted)")
        }
        return String(localized: "payment.validate_method \(method.label.lowercased())")
    }

    private var splitSection: some View {
        VStack(alignment: .leading, spacing: 10) {
            Picker("payment.split_mode_label", selection: Binding(get: { plan.isSplit }, set: { plan.setGuests($0 ? 2 : nil) })) {
                Text("payment.split_full_bill").tag(false)
                Text("payment.split_equal_parts").tag(true)
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
                    Text("payment.guests_count \(guests)").font(.headline).monospacedDigit().accessibilityIdentifier("payment.guests")
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
                            .background(RoundedRectangle(cornerRadius: 10).fill(index < plan.paidParts ? Theme.success.opacity(0.2) : index == plan.paidParts ? Theme.primary.opacity(0.2) : Theme.raised))
                            .overlay(alignment: .topTrailing) {
                                if index < plan.paidParts { Image(systemName: "checkmark.circle.fill").foregroundStyle(Theme.success).offset(x: 4, y: -4) }
                            }
                        }
                    }
                }
            }
        }
    }

    private var tipSection: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("payment.tip_label").font(.headline)
            HStack(spacing: 8) {
                ForEach(PaymentPlan.tipPercentages, id: \.self) { percent in
                    ChipButton(title: String(localized: percent == 0 ? "payment.tip_none" : "payment.tip_percent \(percent)"), isSelected: isTipSelected(percent)) {
                        plan.tip = percent == 0 ? .none : .percent(percent)
                        customTipText = ""
                    }
                    .accessibilityIdentifier("payment.tip.\(percent)")
                }
                TextField("payment.tip_custom_placeholder", text: $customTipText)
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
            TextField("payment.buzzer_placeholder", text: $buzzer)
                .keyboardType(.numberPad)
                .textFieldStyle(.roundedBorder)
                .accessibilityIdentifier("payment.buzzer")
            Toggle(isOn: $printReceipt) {
                Label("payment.print_receipt_label", systemImage: "printer")
            }
            .accessibilityIdentifier("payment.printReceipt")
        }
    }

    private var cashSection: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(method == .cash ? "payment.cash_tendered_label" : "payment.voucher_face_value_label").font(.subheadline.weight(.semibold))
            HStack(spacing: 8) {
                ForEach(OrderMath.suggestedCashAmounts(for: plan.amountToCollect), id: \.self) { amount in
                    Button(amount == plan.amountToCollect ? String(localized: "order.exact_amount") : amount.formatted) { tenderedText = amount.plain }
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
                    Text("payment.change_due \(OrderMath.change(tendered: tendered, due: plan.amountToCollect).formatted)")
                        .font(.headline)
                        .foregroundStyle(Theme.success)
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
                .foregroundStyle(Theme.success)
            Text(outcome.isComplete ? "payment.result_paid" : "payment.result_partial").font(.largeTitle.weight(.bold))
            if outcome.change.cents > 0 {
                Text("payment.change_due_title").font(.title3).foregroundStyle(Theme.inkMuted)
                Text(outcome.change.formatted).font(.system(size: 64, weight: .bold, design: .rounded)).foregroundStyle(Theme.success)
                    .accessibilityIdentifier("result.change")
            }
            if !outcome.isComplete {
                Text("payment.remaining_due \(outcome.remaining.formatted)").font(.title2).accessibilityIdentifier("result.remaining")
            }
            if let receipt = outcome.receiptNumber { Text("payment.receipt_number \(receipt)").font(.subheadline).foregroundStyle(Theme.inkMuted) }
            Spacer()
            ActionButton(title: String(localized: outcome.isComplete ? "payment.finish_button" : "payment.continue_button"), systemImage: outcome.isComplete ? "checkmark" : "arrow.right", kind: .primary) {
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
            Text("payment.change_to_give").font(.title2).foregroundStyle(Theme.inkMuted)
            Text(outcome.change.formatted)
                .font(.system(size: 96, weight: .heavy, design: .rounded))
                .foregroundStyle(Theme.success)
                .accessibilityIdentifier("change.amount")
            HStack(spacing: 40) {
                VStack {
                    Text("payment.pickup_number_label").font(.headline).foregroundStyle(Theme.inkMuted)
                    Text(outcome.pickupNumber ?? "—").font(.system(size: 48, weight: .bold, design: .rounded))
                        .accessibilityIdentifier("change.pickup")
                }
                if let buzzer = outcome.buzzer {
                    VStack {
                        Text("payment.buzzer_label").font(.headline).foregroundStyle(Theme.inkMuted)
                        Text(buzzer).font(.system(size: 48, weight: .bold, design: .rounded))
                    }
                }
            }
            if let voucher = outcome.creditVoucher {
                Card {
                    VStack(alignment: .leading, spacing: 4) {
                        Label("payment.voucher_issued_label", systemImage: "ticket").font(.headline)
                        Text(voucher.voucherCode).font(.title2.monospaced().weight(.bold)).accessibilityIdentifier("change.voucher")
                        Text("payment.voucher_amount_validity \(voucher.amount.formatted)").font(.subheadline).foregroundStyle(Theme.inkMuted)
                    }
                }
                .frame(maxWidth: 420)
            }
            if let receipt = outcome.receiptNumber { Text("payment.receipt_number \(receipt)").foregroundStyle(Theme.inkMuted) }
            Spacer()
            ActionButton(title: String(localized: "payment.next_customer_button"), systemImage: "arrow.right.circle.fill", kind: .primary) { onDone() }
                .frame(maxWidth: 360)
                .accessibilityIdentifier("change.done")
                .padding(.bottom, 40)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Theme.surface)
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
            SheetHeader(title: String(localized: "payment.room_charge_title"), subtitle: String(localized: "payment.room_charge_total \((model.ticket.amountDue + tip).formatted)")) { dismiss() }
            if rooms.isEmpty {
                ProgressView().frame(maxWidth: .infinity)
            } else {
                Picker("payment.room_picker_label", selection: $selected) {
                    ForEach(rooms) { room in
                        Text("payment.room_option \(room.roomNumber) \(room.guestName)").tag(Optional(room))
                    }
                }
                .pickerStyle(.menu)
                .accessibilityIdentifier("room.picker")
                if let selected {
                    HStack {
                        Label(selected.guestName, systemImage: "person")
                        Spacer()
                        Text("payment.available_credit \(selected.availableCredit.formatted)").foregroundStyle(selected.availableCredit >= model.ticket.amountDue + tip ? Theme.success : Theme.danger)
                    }
                    .font(.subheadline)
                }
            }
            HStack {
                Text("payment.customer_signature_label").font(.headline)
                Spacer()
                Button("common.clear") { strokes = [] }.accessibilityIdentifier("room.clearSignature")
            }
            SignaturePad(strokes: $strokes)
                .frame(height: 180)
                .accessibilityIdentifier("room.signature")
            ActionButton(title: String(localized: "payment.confirm_room_charge_button"), systemImage: "bed.double.fill", kind: .primary) {
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
            .overlay(RoundedRectangle(cornerRadius: Theme.smallRadius).strokeBorder(Theme.lineStrong))
            .overlay { if strokes.isEmpty { Text("payment.sign_here_label").foregroundStyle(.gray) } }
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
