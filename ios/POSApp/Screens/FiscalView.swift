import POSKit
import SwiftUI

/// Rapport X (ventes en cours) et clôture journalière Z scellée (NF525).
struct FiscalView: View {
    @Environment(AppModel.self) private var app
    @State private var confirmClosure = false

    var body: some View {
        if !app.fiscal.isAllowed {
            EmptyStateView(
                title: "Accès réservé",
                message: "Les rapports fiscaux et la clôture Z sont réservés aux responsables. Verrouillez la caisse et identifiez-vous avec un code responsable.",
                systemImage: "lock.shield"
            ) {
                EmptyView()
            }
            .accessibilityIdentifier("fiscal.locked")
        } else {
            ScrollView {
                VStack(alignment: .leading, spacing: 20) {
                    HStack {
                        Text("Rapports fiscaux").font(.largeTitle.weight(.bold))
                        Spacer()
                        Button {
                            Task { await app.fiscal.load() }
                        } label: {
                            Label("Actualiser", systemImage: "arrow.clockwise")
                        }
                        .buttonStyle(.bordered)
                        .accessibilityIdentifier("fiscal.refresh")
                        Button(role: .destructive) {
                            confirmClosure = true
                        } label: {
                            Label("Clôture Z", systemImage: "lock.doc.fill")
                        }
                        .buttonStyle(.borderedProminent)
                        .tint(.red)
                        .disabled(app.fiscal.isClosing)
                        .accessibilityIdentifier("fiscal.closeDay")
                    }

                    HStack(alignment: .top, spacing: 20) {
                        FiscalSlip(
                            title: "Rapport X — service en cours",
                            subtitle: "Données en direct, non scellées",
                            report: app.fiscal.xReport,
                            emptyText: "Aucune vente depuis la dernière clôture.",
                            identifier: "fiscal.x"
                        )
                        FiscalSlip(
                            title: app.fiscal.lastClosure.map { "Clôture Z n°\($0.closureSequence ?? 0)" } ?? "Dernière clôture Z",
                            subtitle: "Chaîne d'audit scellée (SHA-256)",
                            report: app.fiscal.lastClosure,
                            emptyText: "Aucune clôture Z pour ce terminal.",
                            identifier: "fiscal.z"
                        )
                    }
                }
                .padding(24)
            }
            .task { await app.fiscal.load() }
            .confirmationDialog("Clôturer la journée ?", isPresented: $confirmClosure, titleVisibility: .visible) {
                Button("Sceller la clôture Z", role: .destructive) {
                    Task { _ = await app.fiscal.closeDay() }
                }
                .accessibilityIdentifier("fiscal.confirmClose")
                Button("Annuler", role: .cancel) {}
            } message: {
                Text("La clôture Z est définitive : les ventes de la période sont scellées et ne peuvent plus être modifiées.")
            }
        }
    }
}

/// Rapport présenté comme un ticket de caisse.
struct FiscalSlip: View {
    var title: String
    var subtitle: String
    var report: FiscalReport?
    var emptyText: String
    var identifier: String

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(title).font(.title3.weight(.bold))
            Text(subtitle).font(.footnote).foregroundStyle(.secondary)
            Divider()
            if let report {
                content(report)
            } else {
                Text(emptyText)
                    .foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity, minHeight: 120)
            }
        }
        .padding(20)
        .frame(maxWidth: .infinity, alignment: .topLeading)
        .background(RoundedRectangle(cornerRadius: 18, style: .continuous).fill(Color.posSurface))
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier(identifier)
    }

    @ViewBuilder
    private func content(_ report: FiscalReport) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            if let closed = report.closedAtUtc {
                line("Clôturé le", closed.formatted(date: .abbreviated, time: .shortened))
            } else if let end = report.periodEndUtc {
                line("Arrêté à", end.formatted(date: .abbreviated, time: .shortened))
            }
            line("Tickets", "\(report.receiptCount)")
            line("Total HT", report.totalSalesHt.formatted)
            line("TVA", report.totalVat.formatted)
            HStack {
                Text("Total TTC").font(.headline)
                Spacer()
                Text(report.totalSalesTtc.formatted)
                    .font(.title2.weight(.bold))
                    .monospacedDigit()
                    .accessibilityIdentifier("\(identifier).total")
            }
            if !report.vatBreakdown.isEmpty {
                Divider()
                Text("Ventilation TVA").font(.subheadline.weight(.semibold))
                ForEach(report.sortedVat, id: \.rate) { entry in
                    line("TVA \(TaxCalculator.rateLabel(entry.rate))", entry.amount.formatted)
                }
            }
            if !report.paymentTotals.isEmpty {
                Divider()
                Text("Règlements").font(.subheadline.weight(.semibold))
                ForEach(report.sortedPayments, id: \.label) { entry in
                    line(entry.label, entry.amount.formatted)
                }
            }
            Divider()
            line("Grand total perpétuel", report.perpetualGrandTotal.formatted)
            if let hash = report.signatureHash {
                Label("Scellé : \(hash.prefix(24))…", systemImage: "checkmark.seal.fill")
                    .font(.caption.monospaced())
                    .foregroundStyle(.green)
            }
        }
    }

    private func line(_ title: String, _ value: String) -> some View {
        HStack {
            Text(title).foregroundStyle(.secondary)
            Spacer()
            Text(value).monospacedDigit()
        }
        .font(.subheadline)
    }
}
