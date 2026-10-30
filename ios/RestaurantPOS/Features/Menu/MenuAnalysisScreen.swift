import Charts
import SwiftUI
import PosKit

extension MenuClass {
    var label: String {
        switch self {
        case .star: String(localized: "menu.class_star")
        case .plowhorse: String(localized: "menu.class_plowhorse")
        case .puzzle: String(localized: "menu.class_puzzle")
        case .dog: String(localized: "menu.class_dog")
        }
    }

    var hint: String {
        switch self {
        case .star: String(localized: "menu.hint_star")
        case .plowhorse: String(localized: "menu.hint_plowhorse")
        case .puzzle: String(localized: "menu.hint_puzzle")
        case .dog: String(localized: "menu.hint_dog")
        }
    }

    var color: Color {
        switch self {
        case .star: Theme.success
        case .plowhorse: Theme.warning
        case .puzzle: Theme.brandCyan
        case .dog: Theme.danger
        }
    }
}

/// Analyse du menu : popularité (part des ventes) face à la marge unitaire HT. Réservée aux responsables.
struct MenuAnalysisScreen: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        @Bindable var store = model.menuAnalysis
        ScrollView {
            VStack(alignment: .leading, spacing: Theme.Space.l) {
                HStack {
                    Text("menu.subtitle").font(.posBody).foregroundStyle(Theme.inkMuted)
                    Spacer()
                    Picker("menu.range_label", selection: $store.range) {
                        Text("menu.range_today").tag(MenuRange.today)
                        Text("menu.range_7").tag(MenuRange.week)
                        Text("menu.range_30").tag(MenuRange.month)
                    }
                    .pickerStyle(.segmented)
                    .frame(width: 360)
                    .accessibilityIdentifier("menu.range")
                }
                if store.isForbidden {
                    EmptyStateView(title: String(localized: "menu.forbidden"), systemImage: "lock")
                } else if let report = store.report, !report.items.isEmpty {
                    if report.missingCostCount > 0 {
                        Label(String(localized: "menu.missing_cost_banner \(report.missingCostCount)"), systemImage: "exclamationmark.triangle")
                            .font(.posBody).foregroundStyle(Theme.ink)
                            .padding(Theme.Space.m)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .background(RoundedRectangle(cornerRadius: Theme.Radius.sm, style: .continuous).fill(Theme.warning.opacity(0.14)))
                            .accessibilityIdentifier("menu.missingCost")
                    }
                    cards(store)
                    MenuScatter(report: report)
                    list(store)
                } else if !store.isLoading {
                    EmptyStateView(title: String(localized: "menu.no_sales"), systemImage: "chart.bar")
                }
            }
            .padding(Theme.Space.xl)
        }
        .task { await store.load() }
        .onChange(of: store.range) { Task { await store.load() } }
    }

    private func cards(_ store: MenuAnalysisStore) -> some View {
        HStack(spacing: Theme.Space.m) {
            ForEach(MenuClass.allCases, id: \.self) { menuClass in
                let selected = store.filter == menuClass
                Button { store.toggleFilter(menuClass) } label: {
                    VStack(alignment: .leading, spacing: 2) {
                        Text("\(store.count(menuClass))").font(Font.posMono(30, .semibold)).foregroundStyle(Theme.ink)
                        Text(menuClass.label).font(.posHeadline).foregroundStyle(Theme.ink)
                        Text(menuClass.hint).font(.posCaption).foregroundStyle(Theme.inkMuted).multilineTextAlignment(.leading)
                    }
                    .padding(Theme.Space.m)
                    .frame(maxWidth: .infinity, minHeight: Theme.touchLarge, alignment: .leading)
                    .background(RoundedRectangle(cornerRadius: Theme.Radius.md, style: .continuous).fill(selected ? menuClass.color.opacity(0.14) : Theme.surface))
                    .overlay(alignment: .top) { Rectangle().fill(menuClass.color).frame(height: 4).clipShape(RoundedRectangle(cornerRadius: 2)) }
                    .overlay(RoundedRectangle(cornerRadius: Theme.Radius.md, style: .continuous).strokeBorder(selected ? menuClass.color : Theme.line, lineWidth: selected ? 2 : 1))
                }
                .buttonStyle(.plain)
                .accessibilityAddTraits(selected ? .isSelected : [])
                .accessibilityIdentifier("menu.class.\(menuClass.rawValue)")
            }
        }
    }

    private func list(_ store: MenuAnalysisStore) -> some View {
        Card {
            VStack(alignment: .leading, spacing: 0) {
                HStack {
                    Text("menu.col_dish").frame(maxWidth: .infinity, alignment: .leading)
                    Text("menu.col_qty").frame(width: 60, alignment: .trailing)
                    Text("menu.col_share").frame(width: 80, alignment: .trailing)
                    Text("menu.col_revenue").frame(width: 110, alignment: .trailing)
                    Text("menu.col_cost").frame(width: 90, alignment: .trailing)
                    Text("menu.col_margin").frame(width: 110, alignment: .trailing)
                    Text("menu.col_class").frame(width: 140, alignment: .leading).padding(.leading, Theme.Space.m)
                }
                .font(.posCaption).foregroundStyle(Theme.inkMuted)
                .padding(.vertical, Theme.Space.s)
                Divider()
                ForEach(store.items) { item in
                    HStack {
                        VStack(alignment: .leading, spacing: 0) {
                            Text(item.name).font(.posBody.weight(.semibold)).foregroundStyle(Theme.ink)
                            if let category = item.categoryName { Text(category).font(.posCaption).foregroundStyle(Theme.inkMuted) }
                        }
                        .frame(maxWidth: .infinity, alignment: .leading)
                        Text("\(item.quantity)").frame(width: 60, alignment: .trailing)
                        Text((item.share as NSDecimalNumber).doubleValue.formatted(.percent.precision(.fractionLength(1)))).frame(width: 80, alignment: .trailing)
                        Text(item.revenueHt.formatted).frame(width: 110, alignment: .trailing)
                        Text(item.unitCost?.formatted ?? "—").frame(width: 90, alignment: .trailing)
                        Text(item.unitMarginHt?.formatted ?? "—").frame(width: 110, alignment: .trailing)
                        Group {
                            if let menuClass = item.class {
                                Badge(text: menuClass.label, color: menuClass.color)
                            } else {
                                Badge(text: String(localized: "menu.missing_cost"), color: Theme.inkSubtle)
                            }
                        }
                        .frame(width: 140, alignment: .leading).padding(.leading, Theme.Space.m)
                    }
                    .font(Font.posMono(14)).foregroundStyle(Theme.ink)
                    .environment(\.layoutDirection, .leftToRight)
                    .frame(minHeight: Theme.touchTarget)
                    .accessibilityElement(children: .combine)
                    .accessibilityIdentifier("menu.row.\(item.productId.uuidString.prefix(8))")
                    Divider()
                }
            }
        }
    }
}

/// Nuage popularité (x) × marge unitaire HT (y) ; les plats sans coût n'y figurent pas (pas d'ordonnée).
private struct MenuScatter: View {
    let report: MenuAnalysisReport

    private struct Point: Identifiable {
        let id: UUID
        let name: String
        let share: Double
        let margin: Double
        let menuClass: MenuClass
    }

    private var points: [Point] {
        report.items.compactMap { item in
            guard let margin = item.unitMarginHt, let menuClass = item.class else { return nil }
            return Point(id: item.productId, name: item.name, share: (item.share as NSDecimalNumber).doubleValue, margin: Double(margin.cents) / 100, menuClass: menuClass)
        }
    }

    var body: some View {
        if let average = report.averageUnitMarginHt, !points.isEmpty {
            Card {
                Chart {
                    ForEach(points) { p in
                        PointMark(x: .value(String(localized: "menu.axis_popularity"), p.share), y: .value(String(localized: "menu.axis_margin"), p.margin))
                            .foregroundStyle(p.menuClass.color)
                            .symbolSize(140)
                            .accessibilityLabel(p.name)
                    }
                    RuleMark(x: .value("threshold", (report.popularityThreshold as NSDecimalNumber).doubleValue))
                        .foregroundStyle(Theme.lineStrong).lineStyle(StrokeStyle(lineWidth: 1, dash: [4, 4]))
                    RuleMark(y: .value("average", Double(average.cents) / 100))
                        .foregroundStyle(Theme.lineStrong).lineStyle(StrokeStyle(lineWidth: 1, dash: [4, 4]))
                }
                .chartXAxisLabel(String(localized: "menu.axis_popularity"))
                .chartYAxisLabel(String(localized: "menu.axis_margin"))
                .chartXAxis { AxisMarks(format: Decimal.FormatStyle.Percent.percent.precision(.fractionLength(0))) }
                .frame(height: 280)
                .accessibilityLabel("menu.chart_label")
            }
            .accessibilityIdentifier("menu.chart")
        }
    }
}
