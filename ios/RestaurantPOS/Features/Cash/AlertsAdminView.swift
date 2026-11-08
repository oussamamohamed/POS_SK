import SwiftUI
import PosKit

/// Gestion → Alertes : seuils, réglages, liste à traiter ou traitée, clôture avec note.
struct AlertsAdminView: View {
    @Environment(AppModel.self) private var model
    @State private var resolving: PilotAlert?

    var body: some View {
        let store = model.pilotAlerts
        List {
            Section("alerts.rules_title") {
                Text("alerts.rules_hint").font(.posCaption).foregroundStyle(Theme.inkMuted)
                ForEach(store.rules) { rule in
                    RuleRow(rule: rule) { updated in Task { await store.setRule(updated) } }
                }
                Toggle("alerts.note_required_label", isOn: Binding(get: { store.settings.cashVarianceNoteRequired }, set: { on in Task { await store.updateSettings { $0.cashVarianceNoteRequired = on } } }))
                    .accessibilityIdentifier("alerts.noteRequired")
                Stepper(value: Binding(get: { store.settings.trendThresholdPercent }, set: { v in Task { await store.updateSettings { $0.trendThresholdPercent = v } } }), in: 1...100, step: 5) {
                    Text("\(String(localized: "insights.threshold")) : \(store.settings.trendThresholdPercent)")
                }
                .accessibilityIdentifier("alerts.trendThreshold")
            }
            Section("alerts.list_title") {
                Picker("alerts.list_title", selection: Binding(get: { store.state }, set: { s in Task { await store.select(s) } })) {
                    Text("alerts.open").tag("open")
                    Text("alerts.resolved").tag("resolved")
                }
                .pickerStyle(.segmented)
                .accessibilityIdentifier("alerts.state")
                if store.alerts.isEmpty { Text("alerts.none").foregroundStyle(Theme.inkSubtle) }
                ForEach(store.alerts) { alert in
                    VStack(alignment: .leading, spacing: 4) {
                        Text(LocalizedStringKey("alerts.rule_\(alert.ruleType)")).font(.headline)
                        Text(AlertStore.summary(of: alert)).font(.subheadline)
                        Text("\(alert.createdAtUtc.formatted(date: .abbreviated, time: .shortened)) · \(alert.terminalId)").font(.caption).foregroundStyle(Theme.inkMuted)
                        if alert.isOpen {
                            Button("alerts.resolve") { resolving = alert }.buttonStyle(.bordered).accessibilityIdentifier("alerts.resolve.\(alert.ruleType)")
                        } else if let note = alert.resolutionNote {
                            Text(note).font(.caption).foregroundStyle(Theme.inkMuted)
                        }
                    }
                    .accessibilityElement(children: .contain)
                    .accessibilityIdentifier("alerts.row.\(alert.ruleType)")
                }
            }
        }
        .listStyle(.insetGrouped)
        .task { await store.load() }
        .sheet(item: $resolving) { ResolveAlertSheet(alert: $0) }
    }
}

private struct RuleRow: View {
    let rule: AlertRule
    let onChange: (AlertRule) -> Void
    @State private var thresholdText = ""

    private var isCents: Bool { rule.type == AlertRuleType.cashVariance || rule.type == AlertRuleType.compsTotal }

    var body: some View {
        HStack {
            Toggle(LocalizedStringKey("alerts.rule_\(rule.type)"), isOn: Binding(get: { rule.enabled }, set: { var r = rule; r.enabled = $0; onChange(r) }))
                .accessibilityIdentifier("alerts.toggle.\(rule.type)")
            TextField("alerts.threshold", text: $thresholdText)
                .keyboardType(.decimalPad).multilineTextAlignment(.trailing).frame(width: 90)
                .accessibilityIdentifier("alerts.threshold.\(rule.type)")
                .onSubmit(commit)
            Text(LocalizedStringKey(isCents ? "alerts.unit_cents" : rule.type == AlertRuleType.voidsCount ? "alerts.unit_count" : "alerts.unit_percent"))
        }
        .onAppear { thresholdText = isCents ? "\(Decimal(rule.threshold) / 100)" : "\(rule.threshold)" }
        .onDisappear(perform: commit)
    }

    private func commit() {
        guard let value = Decimal(string: thresholdText.replacingOccurrences(of: ",", with: "."), locale: nil) else { return }
        let threshold = Int(truncating: NSDecimalNumber(decimal: isCents ? value * 100 : value))
        guard threshold != rule.threshold else { return }
        var r = rule
        r.threshold = threshold
        onChange(r)
    }
}

struct ResolveAlertSheet: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    let alert: PilotAlert
    @State private var note = ""

    var body: some View {
        NavigationStack {
            Form {
                Section { Text(AlertStore.summary(of: alert)) }
                Section { TextField("alerts.note", text: $note).accessibilityIdentifier("alerts.note") }
            }
            .navigationTitle("alerts.resolve")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("common.cancel") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("alerts.resolve") { Task { if await model.pilotAlerts.resolve(alert, note: note) { dismiss() } } }.accessibilityIdentifier("alerts.resolveConfirm")
                }
            }
        }
        .presentationDetents([.medium])
    }
}
