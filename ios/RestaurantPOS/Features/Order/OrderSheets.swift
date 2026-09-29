import SwiftUI
import PosKit

// MARK: - Options d'un article

struct ModifierSheet: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    let product: Product
    @State var course: CourseType
    @State private var selection: ModifierSelection
    @State private var error: String?

    init(product: Product, course: CourseType) {
        self.product = product
        _course = State(initialValue: course)
        _selection = State(initialValue: ModifierSelection(product: product))
    }

    var body: some View {
        VStack(spacing: 0) {
            SheetHeader(title: product.name, subtitle: String(localized: "order.modifier_subtitle \(product.price.formatted) \(product.taxRatePercent.formatted())")) { dismiss() }
                .padding(24)
            ScrollView {
                VStack(alignment: .leading, spacing: 20) {
                    ForEach(product.modifierGroups) { group in
                        VStack(alignment: .leading, spacing: 10) {
                            HStack {
                                Text(group.groupName).font(.headline)
                                Spacer()
                                Badge(text: group.ruleLabel, color: group.isMandatory ? .orange : .secondary)
                            }
                            LazyVGrid(columns: [GridItem(.adaptive(minimum: 150), spacing: 10)], spacing: 10) {
                                ForEach(group.options) { option in
                                    optionButton(option, in: group)
                                }
                            }
                        }
                    }

                    VStack(alignment: .leading, spacing: 8) {
                        Text("order.service_label").font(.headline)
                        Picker("order.service_label", selection: $course) {
                            ForEach([CourseType.direct, .suite, .dessert], id: \.self) { Text($0.label).tag($0) }
                        }
                        .pickerStyle(.segmented)
                        .accessibilityIdentifier("modifiers.course")
                    }

                    VStack(alignment: .leading, spacing: 8) {
                        Text("order.kitchen_comment_label").font(.headline)
                        TextField("order.kitchen_comment_placeholder", text: $selection.kitchenComment, axis: .vertical)
                            .textFieldStyle(.roundedBorder)
                            .lineLimit(1...3)
                            .accessibilityIdentifier("modifiers.comment")
                    }
                }
                .padding(.horizontal, 24)
            }
            Divider()
            HStack(spacing: 16) {
                VStack(alignment: .leading) {
                    Text("order.options_total \(selection.extraTotal.cents > 0 ? "+" : "")\(selection.extraTotal.formatted)").font(.subheadline).foregroundStyle(Theme.inkMuted)
                    Text(selection.effectiveUnitPrice(base: product.price).formatted).font(.title2.weight(.bold)).monospacedDigit()
                        .accessibilityIdentifier("modifiers.price")
                }
                if let error {
                    Text(error).font(.footnote.weight(.medium)).foregroundStyle(Theme.danger).accessibilityIdentifier("modifiers.error")
                }
                Spacer()
                ActionButton(title: String(localized: "order.add_to_ticket_button"), systemImage: "plus.circle.fill", kind: .primary) { confirm() }
                    .frame(maxWidth: 260)
                    .accessibilityIdentifier("modifiers.confirm")
            }
            .padding(24)
        }
        .presentationDetents([.large])
    }

    private func optionButton(_ option: ModifierOption, in group: ModifierGroup) -> some View {
        let selected = selection.isSelected(option)
        return Button {
            do throws(ModifierSelection.ToggleError) {
                try selection.toggle(option, in: group)
                error = nil
            } catch {
                if case let .maximumReached(name, max) = error { self.error = String(localized: "order.max_options_error \(max) \(name)") }
                Haptics.error()
            }
        } label: {
            VStack(alignment: .leading, spacing: 4) {
                Text(option.name).font(.subheadline.weight(.semibold)).multilineTextAlignment(.leading)
                Text(option.extraPrice.cents > 0 ? "order.extra_price_prefix \(option.extraPrice.formatted)" : "order.included_label")
                    .font(.caption.weight(.semibold))
                    .foregroundStyle(option.extraPrice.cents > 0 ? .green : .secondary)
            }
            .frame(maxWidth: .infinity, minHeight: 56, alignment: .leading)
            .padding(.horizontal, 12)
            .background(
                RoundedRectangle(cornerRadius: Theme.smallRadius, style: .continuous)
                    .fill(selected ? Theme.primary.opacity(0.15) : Theme.raised)
            )
            .overlay(
                RoundedRectangle(cornerRadius: Theme.smallRadius, style: .continuous)
                    .strokeBorder(selected ? Theme.primary : .clear, lineWidth: 2)
            )
        }
        .buttonStyle(.plain)
        .accessibilityIdentifier("option.\(option.name)")
        .accessibilityAddTraits(selected ? .isSelected : [])
    }

    private func confirm() {
        do {
            try selection.validate()
            model.ticket.add(product, selection: selection, course: course)
            Haptics.success()
            dismiss()
        } catch {
            self.error = error.errorDescription
            Haptics.error()
        }
    }
}

// MARK: - Remise / gratuité

struct DiscountSheet: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss

    enum Target: Hashable { case global, line(UUID) }
    enum Unit: String, CaseIterable { case percent = "%", euro = "€" }

    static let reasons = ["Geste commercial", "Client fidèle", "Erreur de service", "Attente prolongée", "Repas du personnel", "Autre motif"]

    @State private var target: Target = .global
    @State private var unit: Unit = .percent
    @State private var valueText = "10"
    @State private var reason = DiscountSheet.reasons[0]
    @State private var customReason = ""

    var body: some View {
        let ticket = model.ticket
        VStack(alignment: .leading, spacing: 20) {
            SheetHeader(title: String(localized: "order.discount_sheet_title"), subtitle: ticket.title) { dismiss() }

            Picker("order.discount_target_label", selection: $target) {
                Text("order.discount_global_option").tag(Target.global)
                ForEach(ticket.compEligibleLines) { line in
                    Text("order.discount_comp_option \(line.quantity) \(line.name) \(OrderMath.lineTotal(line).formatted)").tag(Target.line(line.id))
                }
            }
            .pickerStyle(.menu)
            .accessibilityIdentifier("discount.target")

            if target == .global {
                HStack(spacing: 8) {
                    ForEach([("5 %", "5", Unit.percent), ("10 %", "10", .percent), ("20 %", "20", .percent), ("5 €", "5", .euro), ("10 €", "10", .euro)], id: \.0) { preset in
                        ChipButton(title: preset.0, isSelected: valueText == preset.1 && unit == preset.2) {
                            valueText = preset.1
                            unit = preset.2
                        }
                    }
                }
                HStack {
                    TextField("order.discount_value_placeholder", text: $valueText)
                        .keyboardType(.decimalPad)
                        .textFieldStyle(.roundedBorder)
                        .font(.title3)
                        .accessibilityIdentifier("discount.value")
                    Picker("order.discount_unit_label", selection: $unit) {
                        ForEach(Unit.allCases, id: \.self) { Text($0.rawValue) }
                    }
                    .pickerStyle(.segmented)
                    .frame(width: 140)
                }
            }

            Picker("order.discount_reason_label", selection: $reason) {
                ForEach(Self.reasons, id: \.self) { Text($0) }
            }
            .pickerStyle(.menu)
            if reason == "Autre motif" {
                TextField("order.custom_reason_placeholder", text: $customReason).textFieldStyle(.roundedBorder)
            }

            Spacer()
            HStack(spacing: 12) {
                if ticket.discount != nil {
                    ActionButton(title: String(localized: "order.remove_discount_button"), systemImage: "arrow.uturn.backward", kind: .danger) {
                        Task { await ticket.removeDiscount(); dismiss() }
                    }
                    .accessibilityIdentifier("discount.remove")
                }
                ActionButton(title: String(localized: target == .global ? "order.apply_discount_button" : "order.comp_item_button"), systemImage: "checkmark", kind: .primary) {
                    Task { await apply() }
                }
                .accessibilityIdentifier("discount.apply")
            }
        }
        .padding(28)
        .presentationDetents([.medium, .large])
    }

    private func apply() async {
        // Motif envoyé au serveur et stocké dans l'audit : toujours en français, jamais traduit.
        let finalReason = reason == "Autre motif" ? (customReason.isEmpty ? "Remise accordée" : customReason) : reason
        let ok: Bool
        switch target {
        case .global:
            guard let value = Decimal(string: valueText.replacingOccurrences(of: ",", with: "."), locale: Locale(identifier: "en_US_POSIX")) else {
                model.notifier.warning(String(localized: "order.invalid_discount_value"))
                return
            }
            ok = await model.ticket.applyGlobalDiscount(type: unit == .percent ? .percentage : .fixedAmount, value: value, reason: finalReason)
        case .line(let id):
            ok = await model.ticket.comp(lineId: id, reason: finalReason)
        }
        if ok { dismiss() }
    }
}

// MARK: - Transfert / fusion

struct TransferSheet: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var merge = false
    @State private var target: String?

    var body: some View {
        let ticket = model.ticket
        let candidates = model.floor.diningTables.filter { $0.tableNumber != ticket.tableNumber }
        VStack(alignment: .leading, spacing: 20) {
            SheetHeader(title: String(localized: "order.transfer_sheet_title \(ticket.title)"), subtitle: String(localized: "order.transfer_sheet_subtitle")) { dismiss() }
            Picker("order.transfer_mode_label", selection: $merge) {
                Text("order.transfer_option_move").tag(false)
                Text("order.transfer_option_merge").tag(true)
            }
            .pickerStyle(.segmented)
            .accessibilityIdentifier("transfer.mode")
            ScrollView {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 120), spacing: 12)], spacing: 12) {
                    ForEach(candidates.filter { merge ? $0.status != .free : $0.status == .free }) { table in
                        Button {
                            target = table.tableNumber
                        } label: {
                            VStack(spacing: 4) {
                                Text(table.tableNumber).font(.title3.weight(.bold))
                                Text(table.status.label).font(.caption).foregroundStyle(Theme.color(for: table.status))
                            }
                            .frame(maxWidth: .infinity, minHeight: 72)
                            .background(RoundedRectangle(cornerRadius: Theme.smallRadius).fill(target == table.tableNumber ? Theme.primary.opacity(0.2) : Theme.raised))
                            .overlay(RoundedRectangle(cornerRadius: Theme.smallRadius).strokeBorder(target == table.tableNumber ? Theme.primary : .clear, lineWidth: 2))
                        }
                        .buttonStyle(.plain)
                        .accessibilityIdentifier("transfer.table.\(table.tableNumber)")
                    }
                }
            }
            ActionButton(title: String(localized: merge ? "order.transfer_merge_button" : "order.transfer_move_button"), systemImage: "arrow.left.arrow.right", kind: .primary) {
                guard let target else { return }
                Task { if await ticket.transfer(to: target, merge: merge) { dismiss() } }
            }
            .disabled(target == nil)
            .accessibilityIdentifier("transfer.confirm")
        }
        .padding(28)
        .onChange(of: merge) { target = nil }
    }
}

// MARK: - Commandes en attente (comptoir)

struct HoldSheet: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var label = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 20) {
            SheetHeader(title: String(localized: "order.hold_sheet_title"), subtitle: String(localized: "order.hold_sheet_subtitle")) { dismiss() }
            TextField("order.hold_label_placeholder", text: $label)
                .textFieldStyle(.roundedBorder)
                .font(.title3)
                .accessibilityIdentifier("hold.label")
            ActionButton(title: String(localized: "order.hold_sheet_title"), systemImage: "pause.circle.fill", kind: .primary) {
                Task { if await model.ticket.hold(label: label.isEmpty ? model.ticket.suggestedHoldLabel : label) { dismiss() } }
            }
            .accessibilityIdentifier("hold.confirm")
            Spacer()
        }
        .padding(28)
        .onAppear { if label.isEmpty { label = model.ticket.suggestedHoldLabel } }
        .presentationDetents([.height(260)])
    }
}

struct HeldOrdersSheet: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var voiding: HeldOrder?
    @State private var supervisorPin = ""

    var body: some View {
        let ticket = model.ticket
        VStack(alignment: .leading, spacing: 16) {
            SheetHeader(title: String(localized: "order.held_orders_title"), subtitle: String(localized: "order.held_orders_count \(ticket.heldOrders.count)")) { dismiss() }
            if ticket.heldOrders.isEmpty {
                EmptyStateView(title: String(localized: "order.no_held_orders_title"), systemImage: "pause.circle", message: String(localized: "order.no_held_orders_message"))
            } else {
                List(ticket.heldOrders) { held in
                    HStack {
                        VStack(alignment: .leading, spacing: 4) {
                            Text(held.customerLabel ?? String(localized: "order.default_customer_label")).font(.headline)
                            Text("order.held_order_summary \(held.itemCount) \(held.totalTtc.money.formatted) \(held.heldAtUtc.formatted(date: .omitted, time: .shortened))")
                                .font(.subheadline).foregroundStyle(Theme.inkMuted)
                        }
                        Spacer()
                        Button("order.recall_button") {
                            Task { if await ticket.recall(held) { dismiss() } }
                        }
                        .buttonStyle(.borderedProminent)
                        .accessibilityIdentifier("held.recall.\(held.customerLabel ?? "")")
                        Button("common.cancel", role: .destructive) {
                            supervisorPin = ""
                            voiding = held
                        }
                        .buttonStyle(.bordered)
                        .accessibilityIdentifier("held.void.\(held.customerLabel ?? "")")
                    }
                    .padding(.vertical, 6)
                }
                .listStyle(.plain)
            }
        }
        .padding(28)
        .sheet(item: $voiding) { held in
            VStack(spacing: 20) {
                SheetHeader(title: String(localized: "order.supervisor_auth_title"), subtitle: String(localized: "order.void_confirmation_subtitle \(held.customerLabel ?? String(localized: "order.default_customer_label"))")) { voiding = nil }
                SupervisorPinPad(pin: $supervisorPin, identifierPrefix: "void.pin")
                ActionButton(title: String(localized: "order.confirm_void_button"), systemImage: "trash", kind: .danger) {
                    Task { if await ticket.voidHeld(held, supervisorPin: supervisorPin) { voiding = nil } else { supervisorPin = "" } }
                }
                .accessibilityIdentifier("void.confirm")
            }
            .padding(28)
        }
    }
}
