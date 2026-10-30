import Charts
import SwiftUI
import PosKit

/// Encaissements du jour comparés à la semaine passée : tuiles, courbe horaire, moyens de paiement, serveurs. Réservé aux responsables.
struct TakingsScreen: View {
    @Environment(AppModel.self) private var model
    @Environment(Router.self) private var router

    var body: some View {
        let store = model.takings
        ScrollView {
            VStack(alignment: .leading, spacing: Theme.Space.l) {
                HStack {
                    Text("takings.subtitle").font(.posBody).foregroundStyle(Theme.inkMuted)
                    Spacer()
                    Button { Task { await store.load() } } label: {
                        Image(systemName: "arrow.clockwise").font(Font.pos(17, .semibold)).frame(width: Theme.touchMin, height: Theme.touchMin)
                    }
                    .buttonStyle(ActionButtonStyle(kind: .neutral))
                    .accessibilityLabel("common.refresh_label")
                    .accessibilityIdentifier("takings.refresh")
                }
                if let report = store.report {
                    tiles(report)
                    HStack(alignment: .top, spacing: Theme.Space.l) {
                        hourly(report, store).frame(maxWidth: .infinity)
                        VStack(spacing: Theme.Space.l) {
                            payments(report)
                            fiscalCard
                        }
                        .frame(width: 320)
                    }
                    servers(report)
                } else if !store.isLoading {
                    EmptyStateView(title: String(localized: "takings.load_error"), systemImage: "chart.line.uptrend.xyaxis")
                }
            }
            .padding(Theme.Space.xl)
        }
        .task { await store.load() }
    }

    private func tiles(_ r: TakingsReport) -> some View {
        let signed = r.deltaPercent.map { "\($0 >= 0 ? "+" : "−")\(abs($0))" }
        let delta = signed.map { String(localized: "takings.vs_last_week \($0)") } ?? String(localized: "takings.no_comparison")
        return HStack(spacing: Theme.Space.m) {
            KPIView(title: String(localized: "takings.tile_total"), value: r.totalTtc.formatted, subtitle: delta, systemImage: "eurosign.circle", tint: (r.deltaPercent ?? 0) < 0 ? Theme.danger : Theme.inkMuted)
            KPIView(title: String(localized: "takings.tile_covers"), value: "\(r.covers)", systemImage: "person.2")
            KPIView(title: String(localized: "takings.tile_spend"), value: r.spendPerCover.formatted, subtitle: String(localized: "takings.before_tips"), systemImage: "fork.knife")
            KPIView(title: String(localized: "takings.tile_tables"), value: "\(r.tablesTurned)", systemImage: "square.grid.2x2")
        }
        .fixedSize(horizontal: false, vertical: true)
        .accessibilityIdentifier("takings.tiles")
    }

    private func visibleHours(_ r: TakingsReport) -> [HourlyTaking] {
        let used = r.hours.filter { $0.today.cents > 0 || $0.lastWeek.cents > 0 }.map(\.hour)
        let lo = min(11, used.min() ?? 11), hi = max(22, used.max() ?? 22)
        return r.hours.filter { $0.hour >= lo && $0.hour <= hi }
    }

    private func hourly(_ r: TakingsReport, _ store: TakingsStore) -> some View {
        @Bindable var store = store
        let hours = visibleHours(r)
        return Card {
            VStack(alignment: .leading, spacing: Theme.Space.s) {
                HStack {
                    Text("takings.hourly_title").font(.posHeadline).foregroundStyle(Theme.ink)
                    Spacer()
                    Button(store.asTable ? "takings.as_chart" : "takings.as_table") { store.asTable.toggle() }
                        .font(.posLabel).accessibilityIdentifier("takings.toggle")
                }
                if store.asTable {
                    ForEach(hours) { h in
                        HStack {
                            Text(String(format: "%02dh", h.hour)).frame(width: 60, alignment: .leading)
                            Spacer()
                            Text(h.today.formatted).frame(width: 110, alignment: .trailing)
                            Text(h.lastWeek.formatted).frame(width: 110, alignment: .trailing).foregroundStyle(Theme.inkMuted)
                        }
                        .font(Font.posMono(14)).foregroundStyle(Theme.ink).environment(\.layoutDirection, .leftToRight)
                    }
                } else {
                    Chart {
                        ForEach(hours) { h in
                            BarMark(x: .value(String(localized: "takings.col_hour"), h.hour), y: .value(String(localized: "takings.today"), Double(h.today.cents) / 100))
                                .foregroundStyle(Theme.ink)
                            LineMark(x: .value(String(localized: "takings.col_hour"), h.hour), y: .value(String(localized: "takings.last_week"), Double(h.lastWeek.cents) / 100), series: .value("s", "w"))
                                .foregroundStyle(Theme.danger)
                        }
                    }
                    .chartXAxis { AxisMarks(values: hours.map(\.hour)) { AxisValueLabel() } }
                    .frame(height: 280)
                    .accessibilityLabel("takings.chart_label")
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier("takings.hourly")
    }

    private func payments(_ r: TakingsReport) -> some View {
        Card {
            VStack(alignment: .leading, spacing: Theme.Space.s) {
                Text("takings.pay_title").font(.posHeadline).foregroundStyle(Theme.ink)
                if r.paymentMethods.isEmpty { Text("takings.none").font(.posBody).foregroundStyle(Theme.inkSubtle) }
                let maxAmount = Double(max(1, r.paymentMethods.map(\.totalAmount.cents).max() ?? 1))
                ForEach(r.paymentMethods) { p in
                    VStack(alignment: .leading, spacing: 2) {
                        HStack { Text(p.methodName).font(.posBody); Spacer(); Text("\(p.totalAmount.formatted) · \(Int(p.percentageOfTotal)) %").font(Font.posMono(13)) }
                            .foregroundStyle(Theme.ink)
                        ProgressView(value: Double(p.totalAmount.cents) / maxAmount).tint(Theme.ink)
                    }
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
    }

    private var fiscalCard: some View {
        Card {
            VStack(alignment: .leading, spacing: Theme.Space.s) {
                Text("takings.z_title").font(.posHeadline).foregroundStyle(Theme.ink)
                Text("takings.z_hint").font(.posBody).foregroundStyle(Theme.inkMuted)
                ActionButton(title: String(localized: "takings.z_open"), kind: .tonal) { router.section = .fiscal }
                    .accessibilityIdentifier("takings.openFiscal")
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
    }

    private func servers(_ r: TakingsReport) -> some View {
        Card {
            VStack(alignment: .leading, spacing: 0) {
                Text("takings.servers_title").font(.posHeadline).foregroundStyle(Theme.ink).padding(.bottom, Theme.Space.s)
                if r.staffPerformance.isEmpty { Text("takings.none").font(.posBody).foregroundStyle(Theme.inkSubtle) }
                ForEach(r.staffPerformance) { s in
                    HStack {
                        Text(s.serverName).font(.posBody.weight(.semibold)).frame(maxWidth: .infinity, alignment: .leading)
                        Text("\(s.tablesServedCount)").frame(width: 60, alignment: .trailing)
                        Text(s.totalSalesTtc.formatted).frame(width: 120, alignment: .trailing)
                        Text(s.averageTableTtc.formatted).frame(width: 120, alignment: .trailing)
                    }
                    .font(Font.posMono(14)).foregroundStyle(Theme.ink).environment(\.layoutDirection, .leftToRight)
                    .frame(minHeight: Theme.touchTarget)
                    Divider()
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
    }
}
