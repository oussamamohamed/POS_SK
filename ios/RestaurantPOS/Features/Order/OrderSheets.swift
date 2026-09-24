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
            SheetHeader(title: product.name, subtitle: "Prix de base \(product.price.formatted) · TVA \(product.taxRatePercent.formatted()) %") { dismiss() }
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
                        Text("Service").font(.headline)
                        Picker("Service", selection: $course) {
                            ForEach([CourseType.direct, .suite, .dessert], id: \.self) { Text($0.label).tag($0) }
                        }
                        .pickerStyle(.segmented)
                        .accessibilityIdentifier("modifiers.course")
                    }

                    VStack(alignment: .leading, spacing: 8) {
                        Text("Commentaire cuisine").font(.headline)
                        TextField("Ex. sans oignons, allergie…", text: $selection.kitchenComment, axis: .vertical)
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
                    Text("Options \(selection.extraTotal.cents > 0 ? "+" : "")\(selection.extraTotal.formatted)").font(.subheadline).foregroundStyle(.secondary)
                    Text(selection.effectiveUnitPrice(base: product.price).formatted).font(.title2.weight(.bold)).monospacedDigit()
                        .accessibilityIdentifier("modifiers.price")
                }
                if let error {
                    Text(error).font(.footnote.weight(.medium)).foregroundStyle(.red).accessibilityIdentifier("modifiers.error")
                }
                Spacer()
                ActionButton(title: "Ajouter au ticket", systemImage: "plus.circle.fill") { confirm() }
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
                if case let .maximumReached(name, max) = error { self.error = "Maximum \(max) option(s) pour « \(name) »" }
                Haptics.error()
            }
        } label: {
            VStack(alignment: .leading, spacing: 4) {
                Text(option.name).font(.subheadline.weight(.semibold)).multilineTextAlignment(.leading)
                Text(option.extraPrice.cents > 0 ? "+\(option.extraPrice.formatted)" : "Inclus")
                    .font(.caption.weight(.semibold))
                    .foregroundStyle(option.extraPrice.cents > 0 ? .green : .secondary)
            }
            .frame(maxWidth: .infinity, minHeight: 56, alignment: .leading)
            .padding(.horizontal, 12)
            .background(
                RoundedRectangle(cornerRadius: Theme.smallRadius, style: .continuous)
                    .fill(selected ? Color.accentColor.opacity(0.15) : Color(.tertiarySystemFill))
            )
            .overlay(
                RoundedRectangle(cornerRadius: Theme.smallRadius, style: .continuous)
                    .strokeBorder(selected ? Color.accentColor : .clear, lineWidth: 2)
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
            SheetHeader(title: "Remise & gratuité", subtitle: ticket.title) { dismiss() }

            Picker("Cible", selection: $target) {
                Text("Remise globale sur la note").tag(Target.global)
                ForEach(ticket.compEligibleLines) { line in
                    Text("Offrir : \(line.quantity)× \(line.name) (\(OrderMath.lineTotal(line).formatted))").tag(Target.line(line.id))
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
                    TextField("Valeur", text: $valueText)
                        .keyboardType(.decimalPad)
                        .textFieldStyle(.roundedBorder)
                        .font(.title3)
                        .accessibilityIdentifier("discount.value")
                    Picker("Unité", selection: $unit) {
                        ForEach(Unit.allCases, id: \.self) { Text($0.rawValue) }
                    }
                    .pickerStyle(.segmented)
                    .frame(width: 140)
                }
            }

            Picker("Motif", selection: $reason) {
                ForEach(Self.reasons, id: \.self) { Text($0) }
            }
            .pickerStyle(.menu)
            if reason == "Autre motif" {
                TextField("Précisez le motif", text: $customReason).textFieldStyle(.roundedBorder)
            }

            Spacer()
            HStack(spacing: 12) {
                if ticket.discount != nil {
                    ActionButton(title: "Supprimer la remise", systemImage: "arrow.uturn.backward", tint: .red, prominent: false) {
                        Task { await ticket.removeDiscount(); dismiss() }
                    }
                    .accessibilityIdentifier("discount.remove")
                }
                ActionButton(title: target == .global ? "Appliquer la remise" : "Offrir l'article", systemImage: "checkmark", tint: .green) {
                    Task { await apply() }
                }
                .accessibilityIdentifier("discount.apply")
            }
        }
        .padding(28)
        .presentationDetents([.medium, .large])
    }

    private func apply() async {
        let finalReason = reason == "Autre motif" ? (customReason.isEmpty ? "Remise accordée" : customReason) : reason
        let ok: Bool
        switch target {
        case .global:
            guard let value = Decimal(string: valueText.replacingOccurrences(of: ",", with: "."), locale: Locale(identifier: "en_US_POSIX")) else {
                model.notifier.warning("Valeur de remise invalide")
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
            SheetHeader(title: "Transférer \(ticket.title)", subtitle: "Choisissez la table de destination") { dismiss() }
            Picker("Mode", selection: $merge) {
                Text("Transférer (table libre)").tag(false)
                Text("Fusionner (table occupée)").tag(true)
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
                            .background(RoundedRectangle(cornerRadius: Theme.smallRadius).fill(target == table.tableNumber ? Color.accentColor.opacity(0.2) : Color(.tertiarySystemFill)))
                            .overlay(RoundedRectangle(cornerRadius: Theme.smallRadius).strokeBorder(target == table.tableNumber ? Color.accentColor : .clear, lineWidth: 2))
                        }
                        .buttonStyle(.plain)
                        .accessibilityIdentifier("transfer.table.\(table.tableNumber)")
                    }
                }
            }
            ActionButton(title: merge ? "Fusionner" : "Transférer", systemImage: "arrow.left.arrow.right", tint: .indigo) {
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
            SheetHeader(title: "Mettre en attente", subtitle: "Libère la caisse pour le client suivant") { dismiss() }
            TextField("Nom ou repère du client", text: $label)
                .textFieldStyle(.roundedBorder)
                .font(.title3)
                .accessibilityIdentifier("hold.label")
            ActionButton(title: "Mettre en attente", systemImage: "pause.circle.fill", tint: .orange) {
                Task { if await model.ticket.hold(label: label.isEmpty ? "Client #\(model.ticket.heldOrders.count + 1)" : label) { dismiss() } }
            }
            .accessibilityIdentifier("hold.confirm")
            Spacer()
        }
        .padding(28)
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
            SheetHeader(title: "Commandes en attente", subtitle: "\(ticket.heldOrders.count) commande(s)") { dismiss() }
            if ticket.heldOrders.isEmpty {
                EmptyStateView(title: "Aucune commande en attente", systemImage: "pause.circle", message: "Utilisez « Attente » pour parquer un ticket.")
            } else {
                List(ticket.heldOrders) { held in
                    HStack {
                        VStack(alignment: .leading, spacing: 4) {
                            Text(held.customerLabel ?? "Client").font(.headline)
                            Text("\(held.itemCount) article(s) · \(held.totalTtc.money.formatted) · \(held.heldAtUtc.formatted(date: .omitted, time: .shortened))")
                                .font(.subheadline).foregroundStyle(.secondary)
                        }
                        Spacer()
                        Button("Rappeler") {
                            Task { if await ticket.recall(held) { dismiss() } }
                        }
                        .buttonStyle(.borderedProminent)
                        .accessibilityIdentifier("held.recall.\(held.customerLabel ?? "")")
                        Button("Annuler", role: .destructive) {
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
                SheetHeader(title: "Autorisation superviseur", subtitle: "Annulation de « \(held.customerLabel ?? "Client") »") { voiding = nil }
                SupervisorPinPad(pin: $supervisorPin, identifierPrefix: "void.pin")
                ActionButton(title: "Confirmer l'annulation", systemImage: "trash", tint: .red) {
                    Task { if await ticket.voidHeld(held, supervisorPin: supervisorPin) { voiding = nil } else { supervisorPin = "" } }
                }
                .accessibilityIdentifier("void.confirm")
            }
            .padding(28)
        }
    }
}
