import Charts
import SwiftUI
import PosKit

/// Encaissements d'une période comparés à la semaine passée ou à la période précédente : tuiles, courbe, détail, tickets, journal du jour. Réservé aux responsables.
struct TakingsScreen: View {
    @Environment(AppModel.self) private var model
    @Environment(Router.self) private var router

    var body: some View {
        let store = model.takings
        ScrollView {
            VStack(alignment: .leading, spacing: Theme.Space.l) {
                controls(store)
                if let report = store.report {
                    tiles(report)
                    extraTiles(report)
                    HStack(alignment: .top, spacing: Theme.Space.l) {
                        series(report, store).frame(maxWidth: .infinity)
                        VStack(spacing: Theme.Space.l) {
                            payments(report)
                            fiscalCard
                        }
                        .frame(width: 320)
                    }
                    HStack(alignment: .top, spacing: Theme.Space.l) {
                        categories(report).frame(maxWidth: .infinity)
                        topProducts(report).frame(maxWidth: .infinity)
                    }
                    HStack(alignment: .top, spacing: Theme.Space.l) {
                        servers(report).frame(maxWidth: .infinity)
                        vat(report).frame(width: 320)
                    }
                    TakingsReceiptsCard(store: store)
                    TakingsJournalCard(entries: store.journal)
                } else if !store.isLoading {
                    EmptyStateView(title: String(localized: "takings.load_error"), systemImage: "chart.line.uptrend.xyaxis")
                }
            }
            .padding(Theme.Space.xl)
        }
        .task { await store.load() }
    }

    // MARK: Contrôles

    private func controls(_ store: TakingsStore) -> some View {
        @Bindable var store = store
        return VStack(alignment: .leading, spacing: Theme.Space.s) {
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
            HStack(spacing: Theme.Space.s) {
                ForEach(TakingsStore.Period.allCases) { p in
                    ChipButton(title: periodTitle(p), isSelected: store.period == p) { Task { await store.select(p) } }
                        .accessibilityIdentifier("takings.period.\(p.rawValue)")
                }
                if store.period == .date {
                    DatePicker("takings.p_date", selection: $store.date, in: ...Date(), displayedComponents: .date)
                        .labelsHidden()
                        .onChange(of: store.date) { Task { await store.load() } }
                        .accessibilityIdentifier("takings.date")
                }
                Spacer()
                Menu {
                    compareItem(store, nil, "takings.cmp_auto")
                    compareItem(store, "lastWeek", "takings.cmp_week")
                    compareItem(store, "previous", "takings.cmp_previous")
                } label: {
                    Label(compareTitle(store.compare), systemImage: "arrow.left.arrow.right").font(.posLabel)
                        .padding(.horizontal, Theme.Space.l).frame(minHeight: Theme.touchMin)
                        .background(Capsule().fill(Theme.raised))
                }
                .accessibilityIdentifier("takings.compare")
            }
        }
    }

    private func compareItem(_ store: TakingsStore, _ value: String?, _ key: LocalizedStringResource) -> some View {
        Button { store.compare = value; Task { await store.load() } } label: {
            if store.compare == value { Label(String(localized: key), systemImage: "checkmark") } else { Text(key) }
        }
    }

    private func compareTitle(_ value: String?) -> String {
        switch value {
        case "lastWeek": String(localized: "takings.cmp_week")
        case "previous": String(localized: "takings.cmp_previous")
        default: String(localized: "takings.cmp_auto")
        }
    }

    private func periodTitle(_ p: TakingsStore.Period) -> String {
        switch p {
        case .today: String(localized: "takings.p_today")
        case .yesterday: String(localized: "takings.p_yesterday")
        case .week: String(localized: "takings.p_7")
        case .month: String(localized: "takings.p_30")
        case .date: String(localized: "takings.p_date")
        }
    }

    // MARK: Tuiles

    private func tiles(_ r: TakingsReport) -> some View {
        let signed = r.deltaPercent.map { "\($0 >= 0 ? "+" : "−")\(abs($0))" }
        let delta = signed.map { r.compare == "previous" ? String(localized: "takings.vs_previous \($0)") : String(localized: "takings.vs_last_week \($0)") } ?? String(localized: "takings.no_comparison")
        return HStack(spacing: Theme.Space.m) {
            KPIView(title: String(localized: "takings.tile_total"), value: r.totalTtc.formatted, subtitle: delta, systemImage: "eurosign.circle", tint: (r.deltaPercent ?? 0) < 0 ? Theme.danger : Theme.inkMuted)
            KPIView(title: String(localized: "takings.tile_covers"), value: "\(r.covers)", systemImage: "person.2")
            KPIView(title: String(localized: "takings.tile_spend"), value: r.spendPerCover.formatted, subtitle: String(localized: "takings.before_tips"), systemImage: "fork.knife")
            KPIView(title: String(localized: "takings.tile_tables"), value: "\(r.tablesTurned)", systemImage: "square.grid.2x2")
        }
        .fixedSize(horizontal: false, vertical: true)
        .accessibilityIdentifier("takings.tiles")
    }

    private func extraTiles(_ r: TakingsReport) -> some View {
        HStack(spacing: Theme.Space.m) {
            KPIView(title: String(localized: "takings.tile_tips"), value: r.tipsTotalTtc.formatted, systemImage: "heart")
            KPIView(title: String(localized: "takings.tile_discounts"), value: r.discountsTtc.formatted, systemImage: "percent")
            KPIView(title: String(localized: "takings.tile_comps"), value: r.compsTtc.formatted, systemImage: "gift")
            KPIView(title: String(localized: "takings.tile_voids"), value: r.voidsTtc.formatted, subtitle: "\(r.voidsCount)", systemImage: "arrow.uturn.backward.circle", tint: r.voidsCount > 0 ? Theme.danger : Theme.inkMuted)
        }
        .fixedSize(horizontal: false, vertical: true)
        .accessibilityIdentifier("takings.extraTiles")
    }

    // MARK: Courbe

    private struct Plot: Identifiable {
        let id: Int
        let label: String
        let current: Double
        let compare: Double
    }

    /// Heures de 11 h à 22 h (élargies aux heures utilisées) pour un jour, tous les jours sinon ; cumul facultatif.
    private func plots(_ r: TakingsReport, cumulative: Bool) -> [Plot] {
        var pts = Array(r.series.enumerated())
        if r.isHourly {
            let used = pts.filter { $0.element.current.cents > 0 || $0.element.compare.cents > 0 }.map(\.offset)
            let lo = min(11, used.min() ?? 11), hi = max(22, used.max() ?? 22)
            pts = pts.filter { $0.offset >= lo && $0.offset <= hi }
        }
        var cur = 0, cmp = 0
        return pts.map { i, p in
            cur += p.current.cents; cmp += p.compare.cents
            let label = r.isHourly ? String(format: "%02dh", i) : p.label
            return Plot(id: i, label: label, current: Double(cumulative ? cur : p.current.cents) / 100, compare: Double(cumulative ? cmp : p.compare.cents) / 100)
        }
    }

    private func series(_ r: TakingsReport, _ store: TakingsStore) -> some View {
        let rows = plots(r, cumulative: store.cumulative)
        return Card {
            VStack(alignment: .leading, spacing: Theme.Space.s) {
                HStack {
                    Text(r.isHourly ? "takings.hourly_title" : "takings.daily_title").font(.posHeadline).foregroundStyle(Theme.ink)
                    Spacer()
                    ChipButton(title: String(localized: "takings.cumulative"), isSelected: store.cumulative) { store.cumulative.toggle() }
                        .accessibilityIdentifier("takings.cumulative")
                    Button(store.asTable ? "takings.as_chart" : "takings.as_table") { store.asTable.toggle() }
                        .font(.posLabel).accessibilityIdentifier("takings.toggle")
                }
                if store.asTable { seriesTable(rows) } else { seriesChart(rows, cumulative: store.cumulative) }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier("takings.hourly")
    }

    private func seriesTable(_ rows: [Plot]) -> some View {
        ForEach(rows) { h in
            HStack {
                Text(h.label).frame(width: 70, alignment: .leading)
                Spacer()
                Text(Money(cents: Int((h.current * 100).rounded())).formatted).frame(width: 110, alignment: .trailing)
                Text(Money(cents: Int((h.compare * 100).rounded())).formatted).frame(width: 110, alignment: .trailing).foregroundStyle(Theme.inkMuted)
            }
            .font(Font.posMono(14)).foregroundStyle(Theme.ink).environment(\.layoutDirection, .leftToRight)
        }
    }

    private func seriesChart(_ rows: [Plot], cumulative: Bool) -> some View {
        Chart {
            ForEach(rows) { h in
                if !cumulative {
                    BarMark(x: .value(String(localized: "takings.col_hour"), h.label), y: .value(String(localized: "takings.current"), h.current)).foregroundStyle(Theme.ink)
                } else {
                    LineMark(x: .value(String(localized: "takings.col_hour"), h.label), y: .value(String(localized: "takings.current"), h.current), series: .value("s", "c")).foregroundStyle(Theme.ink)
                }
                LineMark(x: .value(String(localized: "takings.col_hour"), h.label), y: .value(String(localized: "takings.compare"), h.compare), series: .value("s", "w")).foregroundStyle(Theme.danger)
            }
        }
        .frame(height: 280)
        .accessibilityLabel("takings.chart_label")
    }

    // MARK: Cartes

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

    private func categories(_ r: TakingsReport) -> some View {
        Card {
            VStack(alignment: .leading, spacing: Theme.Space.s) {
                Text("takings.categories_title").font(.posHeadline).foregroundStyle(Theme.ink)
                if r.categories.isEmpty { Text("takings.none").font(.posBody).foregroundStyle(Theme.inkSubtle) }
                ForEach(r.categories, id: \.name) { c in
                    VStack(alignment: .leading, spacing: 2) {
                        HStack { Text(c.name).font(.posBody); Spacer(); Text("\(c.salesTtc.formatted) · \(Int((c.share * 100).rounded())) %").font(Font.posMono(13)) }
                            .foregroundStyle(Theme.ink)
                        ProgressView(value: c.share).tint(Theme.ink)
                    }
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier("takings.categories")
    }

    private func topProducts(_ r: TakingsReport) -> some View {
        Card {
            VStack(alignment: .leading, spacing: Theme.Space.s) {
                Text("takings.top_title").font(.posHeadline).foregroundStyle(Theme.ink)
                if r.topProducts.isEmpty { Text("takings.none").font(.posBody).foregroundStyle(Theme.inkSubtle) }
                ForEach(r.topProducts) { p in
                    HStack {
                        Text(p.productName).font(.posBody).frame(maxWidth: .infinity, alignment: .leading)
                        Text("× \(p.quantitySold)").frame(width: 70, alignment: .trailing)
                        Text(p.totalSalesTtc.formatted).frame(width: 110, alignment: .trailing)
                    }
                    .font(Font.posMono(14)).foregroundStyle(Theme.ink).environment(\.layoutDirection, .leftToRight)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier("takings.top")
    }

    private func servers(_ r: TakingsReport) -> some View {
        let tips = Dictionary(r.tipsByServer.map { ($0.serverName, $0.tipsTtc) }, uniquingKeysWith: { a, _ in a })
        return Card {
            VStack(alignment: .leading, spacing: 0) {
                Text("takings.servers_title").font(.posHeadline).foregroundStyle(Theme.ink).padding(.bottom, Theme.Space.s)
                if r.staffPerformance.isEmpty { Text("takings.none").font(.posBody).foregroundStyle(Theme.inkSubtle) }
                ForEach(r.staffPerformance) { s in
                    HStack {
                        Text(s.serverName).font(.posBody.weight(.semibold)).frame(maxWidth: .infinity, alignment: .leading)
                        Text("\(s.tablesServedCount)").frame(width: 50, alignment: .trailing)
                        Text(s.totalSalesTtc.formatted).frame(width: 110, alignment: .trailing)
                        Text(s.averageTableTtc.formatted).frame(width: 110, alignment: .trailing)
                        Text((tips[s.serverName] ?? .zero).formatted).frame(width: 100, alignment: .trailing).foregroundStyle(Theme.inkMuted)
                    }
                    .font(Font.posMono(14)).foregroundStyle(Theme.ink).environment(\.layoutDirection, .leftToRight)
                    .frame(minHeight: Theme.touchTarget)
                    Divider()
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
    }

    private func vat(_ r: TakingsReport) -> some View {
        Card {
            VStack(alignment: .leading, spacing: Theme.Space.s) {
                Text("takings.vat_title").font(.posHeadline).foregroundStyle(Theme.ink)
                if r.vat.isEmpty { Text("takings.none").font(.posBody).foregroundStyle(Theme.inkSubtle) }
                ForEach(r.vat) { v in
                    HStack {
                        Text("\(NSDecimalNumber(decimal: v.ratePercent).stringValue) %").frame(width: 60, alignment: .leading)
                        Text(v.baseHt.formatted).frame(maxWidth: .infinity, alignment: .trailing)
                        Text(v.vat.formatted).frame(width: 90, alignment: .trailing)
                    }
                    .font(Font.posMono(13)).foregroundStyle(Theme.ink).environment(\.layoutDirection, .leftToRight)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier("takings.vat")
    }
}
