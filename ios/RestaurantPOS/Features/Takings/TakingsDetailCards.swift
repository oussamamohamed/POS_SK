import SwiftUI
import PosKit

/// Tickets de la période, avec duplicata. Les avoirs n'ont pas de bouton de réimpression.
struct TakingsReceiptsCard: View {
    @Environment(AppModel.self) private var model
    let store: TakingsStore

    var body: some View {
        Card {
            VStack(alignment: .leading, spacing: 0) {
                Text("takings.tickets_title").font(.posHeadline).foregroundStyle(Theme.ink).padding(.bottom, Theme.Space.s)
                if store.receipts.isEmpty { Text("takings.none").font(.posBody).foregroundStyle(Theme.inkSubtle) }
                ForEach(store.receipts) { r in
                    row(r)
                    Divider()
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier("takings.receipts")
    }

    private func row(_ r: TakingsReceipt) -> some View {
        HStack(spacing: Theme.Space.m) {
            Text(r.atUtc.formatted(date: store.period == .today || store.period == .yesterday || store.period == .date ? .omitted : .abbreviated, time: .shortened)).frame(width: 110, alignment: .leading)
            HStack(spacing: Theme.Space.xs) {
                Text(r.receiptNumber)
                if r.isCredit { flag("takings.credit", Theme.danger) } else if r.isCancelled { flag("takings.cancelled", Theme.inkMuted) }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            Text(r.tableNumber ?? "—").frame(width: 70, alignment: .leading)
            Text(r.serverName).frame(width: 150, alignment: .leading).lineLimit(1)
            Text(r.methods.joined(separator: ", ")).frame(width: 150, alignment: .leading).lineLimit(1).foregroundStyle(Theme.inkMuted)
            Text(r.totalTtc.formatted).frame(width: 100, alignment: .trailing)
            if r.isCredit {
                Color.clear.frame(width: 110, height: 1)
            } else {
                Button("takings.reprint") { Task { await reprint(r) } }
                    .font(.posLabel).frame(width: 110)
                    .accessibilityIdentifier("takings.reprint.\(r.receiptNumber)")
            }
        }
        .font(Font.posMono(13)).foregroundStyle(Theme.ink).environment(\.layoutDirection, .leftToRight)
        .frame(minHeight: Theme.touchTarget)
    }

    private func flag(_ key: LocalizedStringKey, _ tint: Color) -> some View {
        Text(key).font(.posLabel).foregroundStyle(tint).padding(.horizontal, 6).background(Capsule().stroke(tint))
    }

    private func reprint(_ r: TakingsReceipt) async {
        guard let result = await store.reprint(r) else { return }
        let message = String(format: String(localized: "takings.reprint_done %lld"), result.duplicateNumber)
        if result.printQueued { model.notifier.success(message) } else { model.notifier.warning(message) }
    }
}

/// Journal technique (JET) du jour en cours seulement.
struct TakingsJournalCard: View {
    let entries: [JournalEntry]

    var body: some View {
        Card {
            VStack(alignment: .leading, spacing: 0) {
                Text("takings.journal_title").font(.posHeadline).foregroundStyle(Theme.ink)
                Text("takings.journal_hint").font(.posLabel).foregroundStyle(Theme.inkMuted).padding(.bottom, Theme.Space.s)
                if entries.isEmpty { Text("takings.none").font(.posBody).foregroundStyle(Theme.inkSubtle) }
                ForEach(entries) { e in
                    HStack(alignment: .top, spacing: Theme.Space.m) {
                        Text(e.atUtc.formatted(date: .omitted, time: .standard)).frame(width: 90, alignment: .leading).font(Font.posMono(13))
                        Text(Self.label(e.eventType)).font(.posBody.weight(.semibold)).frame(width: 240, alignment: .leading)
                        Text(e.summary(translate: Self.translate)).font(.posLabel).foregroundStyle(Theme.inkMuted).frame(maxWidth: .infinity, alignment: .leading)
                    }
                    .foregroundStyle(Theme.ink)
                    .frame(minHeight: Theme.touchMin)
                    Divider()
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier("takings.journal")
    }

    /// Libellé d'une clé du catalogue de l'app, nil si elle n'existe pas.
    static func translate(_ key: String) -> String? {
        let text = Bundle.main.localizedString(forKey: key, value: key, table: nil)
        return text == key ? nil : text
    }

    /// Libellé traduit de l'événement (`journal.<TYPE>`), le code brut à défaut.
    static func label(_ type: String) -> String {
        Bundle.main.localizedString(forKey: "journal.\(type)", value: type, table: nil)
    }
}
