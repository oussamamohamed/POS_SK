import POSKit
import SwiftUI

/// Commandes comptoir mises en attente : reprise ou annulation (PIN responsable).
struct HeldOrdersSheet: View {
    @Environment(AppModel.self) private var app
    @Environment(\.dismiss) private var dismiss
    @State private var orderToVoid: HeldOrder?

    var body: some View {
        NavigationStack {
            Group {
                if app.held.orders.isEmpty {
                    ContentUnavailableView(
                        "Aucune commande en attente",
                        systemImage: "tray",
                        description: Text("Utilisez « Attente » pour garder une vente de côté.")
                    )
                } else {
                    List(app.held.orders) { held in
                        HStack(spacing: 16) {
                            VStack(alignment: .leading, spacing: 4) {
                                Text(held.customerLabel ?? "Client").font(.headline)
                                Text("\(held.itemCount) article\(held.itemCount > 1 ? "s" : "") · \(held.destination.label)\(held.heldAtUtc.map { " · \($0.shortTime)" } ?? "")")
                                    .font(.subheadline)
                                    .foregroundStyle(.secondary)
                            }
                            Spacer()
                            Text(held.totalTtc.formatted).font(.headline).monospacedDigit()
                            Button("Reprendre") {
                                Task {
                                    if await app.order.recall(held) {
                                        app.held.remove(held.holdId)
                                        dismiss()
                                    }
                                }
                            }
                            .buttonStyle(.borderedProminent)
                            .accessibilityIdentifier("held.recall.\(held.customerLabel ?? "Client")")
                            Button(role: .destructive) {
                                orderToVoid = held
                            } label: {
                                Image(systemName: "trash")
                            }
                            .buttonStyle(.bordered)
                            .accessibilityIdentifier("held.void.\(held.customerLabel ?? "Client")")
                        }
                        .padding(.vertical, 6)
                    }
                }
            }
            .navigationTitle("Commandes en attente")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button("Fermer") { dismiss() }
                        .accessibilityIdentifier("held.close")
                }
            }
            .task { await app.held.load() }
            .noticeOverlay()
            .sheet(item: $orderToVoid) { held in
                SupervisorPinSheet(title: "Annuler « \(held.customerLabel ?? "Client") »") { pin in
                    await app.held.void(held, supervisorPin: pin)
                }
            }
        }
    }
}

/// Saisie du PIN d'un responsable pour autoriser une action sensible.
struct SupervisorPinSheet: View {
    var title: String
    var onSubmit: (String) async -> Bool
    @State private var pin = PinEntry()
    @State private var isWorking = false
    @State private var failures = 0
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(spacing: 24) {
            Text(title).font(.title2.weight(.bold))
            Text("Code PIN responsable requis").foregroundStyle(.secondary)
            PinDots(count: pin.count, length: PinEntry.minLength)
                .modifier(ShakeEffect(animatableData: CGFloat(failures)))
                .animation(.linear(duration: 0.4), value: failures)
            NumericKeypad(identifierPrefix: "supervisor", keyHeight: 64) { key in
                switch key {
                case .digit(let digit):
                    if pin.append(digit) {
                        submit()
                    }
                case .delete: pin.backspace()
                case .clear: pin.clear()
                case .doubleZero: break
                }
            }
            .frame(maxWidth: 340)
            .disabled(isWorking)
            Button("Annuler", role: .cancel) { dismiss() }
                .accessibilityIdentifier("supervisor.cancel")
        }
        .padding(32)
        .noticeOverlay()
        .sensoryFeedback(.error, trigger: failures)
    }

    private func submit() {
        let code = pin.code
        isWorking = true
        Task {
            let accepted = await onSubmit(code)
            isWorking = false
            if accepted {
                dismiss()
            } else {
                pin.clear()
                failures += 1
            }
        }
    }
}
