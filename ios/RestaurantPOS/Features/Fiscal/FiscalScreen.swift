import SwiftUI
import PosKit

/// Rapports NF525 : aperçu X, clôture Z scellée, export FEC.
struct FiscalScreen: View {
    @Environment(AppModel.self) private var model
    @State private var confirmsZ = false
    @State private var fecFrom = Calendar.current.date(from: Calendar.current.dateComponents([.year, .month], from: Date())) ?? Date()
    @State private var fecTo = Date()

    var body: some View {
        let fiscal = model.fiscal
        HStack(alignment: .top, spacing: 24) {
            ScrollView {
                if let report = fiscal.report {
                    FiscalSlip(report: report, isSealed: fiscal.isSealed)
                        .padding(.vertical, 24)
                } else {
                    ProgressView().padding(60)
                }
            }
            .frame(maxWidth: .infinity)

            VStack(alignment: .leading, spacing: 16) {
                Card {
                    VStack(alignment: .leading, spacing: 12) {
                        Label("Rapports de caisse", systemImage: "doc.text").font(.headline)
                        Text("Le rapport X est un aperçu en direct, non scellé. La clôture Z scelle la journée par chaînage SHA-256 et remet les compteurs à zéro.")
                            .font(.subheadline).foregroundStyle(.secondary)
                        ActionButton(title: "Aperçu rapport X", systemImage: "eye", tint: .blue, prominent: false) {
                            Task { await fiscal.previewX() }
                        }
                        .accessibilityIdentifier("fiscal.previewX")
                        ActionButton(title: "Clôture Z du jour", systemImage: "lock.doc", tint: .red, isLoading: fiscal.isWorking) {
                            confirmsZ = true
                        }
                        .accessibilityIdentifier("fiscal.executeZ")
                    }
                }
                Card {
                    VStack(alignment: .leading, spacing: 12) {
                        Label("Export comptable FEC", systemImage: "square.and.arrow.up").font(.headline)
                        DatePicker("Du", selection: $fecFrom, displayedComponents: .date)
                        DatePicker("Au", selection: $fecTo, in: fecFrom..., displayedComponents: .date)
                        HStack {
                            Text("SIREN")
                            TextField("123456789", text: Binding(get: { model.settings.siren }, set: { model.settings.siren = $0 }))
                                .keyboardType(.numberPad)
                                .textFieldStyle(.roundedBorder)
                                .accessibilityIdentifier("fec.siren")
                        }
                        ActionButton(title: "Générer le FEC", systemImage: "doc.badge.gearshape", tint: .indigo, prominent: false) {
                            let end = Calendar.current.date(bySettingHour: 23, minute: 59, second: 59, of: fecTo) ?? fecTo
                            Task { await fiscal.exportFec(from: Calendar.current.startOfDay(for: fecFrom), to: end) }
                        }
                        .accessibilityIdentifier("fec.generate")
                        if let status = fiscal.fecStatus {
                            Text(status).font(.footnote).foregroundStyle(.secondary).accessibilityIdentifier("fec.status")
                        }
                        if let file = fiscal.exportedFile {
                            ShareLink(item: file) { Label("Partager \(file.lastPathComponent)", systemImage: "square.and.arrow.up") }
                                .accessibilityIdentifier("fec.share")
                        }
                    }
                }
                Spacer()
            }
            .frame(width: 360)
        }
        .padding(24)
        .task { await fiscal.loadLatest() }
        .confirmationDialog("Exécuter la clôture Z ?", isPresented: $confirmsZ, titleVisibility: .visible) {
            Button("Clôturer et sceller la journée", role: .destructive) { Task { await fiscal.executeZ() } }
                .accessibilityIdentifier("fiscal.confirmZ")
        } message: {
            Text("Opération irréversible : les ventes de la période sont scellées (NF525).")
        }
    }
}

/// Ticket de rapport au format « imprimante thermique ».
struct FiscalSlip: View {
    let report: FiscalReport
    let isSealed: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(isSealed ? "*** CLÔTURE JOURNALIÈRE — RAPPORT Z n°\(report.closureSequence ?? 1) ***" : "*** RAPPORT FINANCIER EN COURS — RAPPORT X ***")
                .font(.headline.monospaced())
                .multilineTextAlignment(.center)
                .frame(maxWidth: .infinity)
                .accessibilityIdentifier("slip.title")
            dashed
            line("Terminal", report.terminalId ?? "—")
            line("Date", (report.closedAtUtc ?? report.periodEndUtc ?? Date()).formatted(date: .abbreviated, time: .standard))
            line("Tickets", "\(report.receiptCount)")
            dashed
            line("TOTAL TTC", report.totalSalesTtc.formatted, bold: true)
                .accessibilityIdentifier("slip.totalTtc")
            line("Total HT", report.totalSalesHt.formatted)
            ForEach(report.sortedVat, id: \.rate) { vat in
                line("TVA \(Double(vat.rate)?.formatted() ?? vat.rate) %", vat.amount.formatted)
            }
            dashed
            ForEach(report.sortedPayments, id: \.method) { payment in
                line(payment.method, payment.amount.formatted)
            }
            dashed
            line("Grand total perpétuel", report.perpetualGrandTotal.formatted)
            VStack(alignment: .leading, spacing: 4) {
                Text("Signature").font(.caption.monospaced())
                Text(report.signatureHash ?? "GÉNÉRÉE À LA CLÔTURE Z").font(.caption2.monospaced()).textSelection(.enabled)
                    .accessibilityIdentifier("slip.hash")
            }
            Label(isSealed ? "Chaîne d'audit fiscale scellée et valide (NF525)" : "Données en direct du service (non scellées)", systemImage: isSealed ? "checkmark.seal.fill" : "info.circle")
                .font(.footnote.weight(.semibold))
                .foregroundStyle(isSealed ? .green : .blue)
                .padding(.top, 6)
                .accessibilityIdentifier("slip.status")
        }
        .padding(24)
        .frame(maxWidth: 440)
        .background(Color(.systemBackground))
        .clipShape(RoundedRectangle(cornerRadius: 6))
        .shadow(color: .black.opacity(0.15), radius: 12, y: 6)
    }

    private var dashed: some View {
        Line().stroke(style: StrokeStyle(lineWidth: 1, dash: [4, 3])).foregroundStyle(.secondary).frame(height: 1)
    }

    private func line(_ label: String, _ value: String, bold: Bool = false) -> some View {
        HStack {
            Text(label)
            Spacer()
            Text(value).monospacedDigit()
        }
        .font(bold ? .headline.monospaced() : .subheadline.monospaced())
        .accessibilityElement(children: .combine)
    }

    private struct Line: Shape {
        func path(in rect: CGRect) -> Path {
            var p = Path()
            p.move(to: CGPoint(x: 0, y: rect.midY))
            p.addLine(to: CGPoint(x: rect.maxX, y: rect.midY))
            return p
        }
    }
}
