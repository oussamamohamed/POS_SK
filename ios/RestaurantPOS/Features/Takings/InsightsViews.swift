import SwiftUI
import PosKit

/// Carte « À surveiller » : tendances du jour vs même jour de la semaine passée (au plus 4 lignes).
struct InsightsCard: View {
    let store: InsightsStore

    var body: some View {
        Card {
            VStack(alignment: .leading, spacing: Theme.Space.s) {
                Text("insights.title").font(.posHeadline).foregroundStyle(Theme.ink).accessibilityIdentifier("insights.card")
                if let insights = store.insights {
                    if insights.insufficientData {
                        Text("insights.insufficient").font(.posBody).foregroundStyle(Theme.inkSubtle).accessibilityIdentifier("insights.insufficient")
                    } else if insights.cards.isEmpty {
                        Text("insights.none").font(.posBody).foregroundStyle(Theme.inkSubtle).accessibilityIdentifier("insights.none")
                    } else {
                        ForEach(insights.cards) { card in
                            HStack {
                                Text(LocalizedStringKey(card.titleKey)).font(.posBody)
                                Spacer()
                                Text("\(card.formatted(card.currentCents)) (\(card.formatted(card.referenceCents)))").font(Font.posMono(13)).foregroundStyle(Theme.inkMuted)
                                Text(delta(card)).font(Font.posMono(13).bold()).foregroundStyle(card.deltaPercent < 0 ? Theme.danger : Theme.success)
                            }
                            .foregroundStyle(Theme.ink)
                            .accessibilityIdentifier("insights.card.\(card.metric)")
                        }
                        Text("insights.vs_last_week").font(.posCaption).foregroundStyle(Theme.inkSubtle)
                    }
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
    }

    private func delta(_ card: InsightCard) -> String {
        let value = NSDecimalNumber(decimal: card.deltaPercent).doubleValue
        return String(format: "%@%.1f %%", value > 0 ? "+" : "", value)
    }
}

/// Résumé de fin de journée : mêmes totaux que le dernier Z, partageable en texte.
struct DailySummarySheet: View {
    let store: InsightsStore
    let terminalId: String
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: Theme.Space.m) {
                    if let s = store.summary {
                        row("summary.sales", Money(cents: s.totalSalesTtcCents).formatted, id: "sales")
                        row("summary.receipts", "\(s.receiptCount)", id: "receipts")
                        row("summary.avg_ticket", Money(cents: s.averageTicketCents).formatted, id: "avg")
                        row("summary.covers", "\(s.covers)", id: "covers")
                        ForEach(s.paymentTotalsCents.sorted { $0.key < $1.key }, id: \.key) { method, cents in
                            HStack { Text(method).font(.posBody); Spacer(); Text(Money(cents: cents).formatted).font(Font.posMono(15)) }.foregroundStyle(Theme.ink)
                        }
                        ForEach(s.tipsByServer, id: \.serverName) { tip in
                            HStack { Text("\(String(localized: "summary.tips")) · \(tip.serverName)").font(.posBody); Spacer(); Text(Money(cents: tip.tipsCents).formatted).font(Font.posMono(15)) }.foregroundStyle(Theme.ink)
                        }
                        if let cash = s.cash {
                            row("summary.cash", "\(Money(cents: cash.floatCents).formatted) → \(Money(cents: cash.countedCents).formatted) (\(Money(cents: cash.varianceCents).formatted))", id: "cash")
                        }
                        row("summary.open_alerts", "\(s.openAlerts.count)", id: "alerts")
                    } else if store.summaryLoaded {
                        Text("summary.none").font(.posBody).foregroundStyle(Theme.inkSubtle).accessibilityIdentifier("summary.none")
                    }
                }
                .padding(Theme.Space.xl)
            }
            .navigationTitle(Text("summary.title"))
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("common.close_label") { dismiss() }.accessibilityIdentifier("summary.close") }
                if let s = store.summary { ToolbarItem(placement: .primaryAction) { ShareLink(item: s.shareText) { Image(systemName: "square.and.arrow.up") }.accessibilityIdentifier("summary.share") } }
            }
        }
        .task { await store.loadSummary(terminalId: terminalId) }
        .accessibilityIdentifier("summary.sheet")
    }

    private func row(_ key: LocalizedStringKey, _ value: String, id: String) -> some View {
        HStack { Text(key).font(.posBody); Spacer(); Text(value).font(Font.posMono(15)) }
            .foregroundStyle(Theme.ink)
            .accessibilityElement(children: .combine)
            .accessibilityIdentifier("summary.\(id)")
    }
}
