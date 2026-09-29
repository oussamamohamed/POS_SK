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
                        Label("fiscal.reports_title", systemImage: "doc.text").font(.headline)
                        Text("fiscal.x_z_explanation")
                            .font(.subheadline).foregroundStyle(Theme.inkMuted)
                        ActionButton(title: String(localized: "fiscal.preview_x_button"), systemImage: "eye", kind: .tonal) {
                            Task { await fiscal.previewX() }
                        }
                        .accessibilityIdentifier("fiscal.previewX")
                        ActionButton(title: String(localized: "fiscal.execute_z_button"), systemImage: "lock.doc", isLoading: fiscal.isWorking, kind: .danger) {
                            confirmsZ = true
                        }
                        .accessibilityIdentifier("fiscal.executeZ")
                    }
                }
                Card {
                    VStack(alignment: .leading, spacing: 12) {
                        Label("fiscal.fec_export_title", systemImage: "square.and.arrow.up").font(.headline)
                        DatePicker("fiscal.fec_from", selection: $fecFrom, displayedComponents: .date)
                        DatePicker("fiscal.fec_to", selection: $fecTo, in: fecFrom..., displayedComponents: .date)
                        HStack {
                            Text("fiscal.siren_label")
                            TextField("123456789", text: Binding(get: { model.settings.siren }, set: { model.settings.siren = $0 }))
                                .keyboardType(.numberPad)
                                .textFieldStyle(.roundedBorder)
                                .accessibilityIdentifier("fec.siren")
                        }
                        ActionButton(title: String(localized: "fiscal.generate_fec_button"), systemImage: "doc.badge.gearshape", kind: .tonal) {
                            let end = Calendar.current.date(bySettingHour: 23, minute: 59, second: 59, of: fecTo) ?? fecTo
                            Task { await fiscal.exportFec(from: Calendar.current.startOfDay(for: fecFrom), to: end) }
                        }
                        .accessibilityIdentifier("fec.generate")
                        if let status = fiscal.fecStatus {
                            Text(status).font(.footnote).foregroundStyle(Theme.inkMuted).accessibilityIdentifier("fec.status")
                        }
                        if let file = fiscal.exportedFile {
                            ShareLink(item: file) { Label("fiscal.share_file \(file.lastPathComponent)", systemImage: "square.and.arrow.up") }
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
        .confirmationDialog("fiscal.confirm_z_dialog_title", isPresented: $confirmsZ, titleVisibility: .visible) {
            Button("fiscal.confirm_z_button", role: .destructive) { Task { await fiscal.executeZ() } }
                .accessibilityIdentifier("fiscal.confirmZ")
        } message: {
            Text("fiscal.confirm_z_message")
        }
    }
}

/// Ticket de rapport au format « imprimante thermique ».
struct FiscalSlip: View {
    let report: FiscalReport
    let isSealed: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(isSealed ? "fiscal.slip_title_z \(report.closureSequence ?? 1)" : "fiscal.slip_title_x")
                .font(.headline.monospaced())
                .multilineTextAlignment(.center)
                .frame(maxWidth: .infinity)
                .accessibilityIdentifier("slip.title")
            dashed
            line(String(localized: "common.field_terminal"), report.terminalId ?? "—")
            line(String(localized: "fiscal.slip_date_label"), (report.closedAtUtc ?? report.periodEndUtc ?? Date()).formatted(date: .abbreviated, time: .standard))
            line(String(localized: "fiscal.slip_tickets_label"), "\(report.receiptCount)")
            dashed
            line(String(localized: "fiscal.slip_total_ttc_label"), report.totalSalesTtc.formatted, bold: true)
                .accessibilityIdentifier("slip.totalTtc")
            line(String(localized: "fiscal.slip_total_ht_label"), report.totalSalesHt.formatted)
            ForEach(report.sortedVat, id: \.rate) { vat in
                line(String(localized: "fiscal.slip_vat_row \(Double(vat.rate)?.formatted() ?? vat.rate)"), vat.amount.formatted)
            }
            dashed
            ForEach(report.sortedPayments, id: \.method) { payment in
                line(payment.method, payment.amount.formatted)
            }
            dashed
            line(String(localized: "fiscal.slip_perpetual_label"), report.perpetualGrandTotal.formatted)
            VStack(alignment: .leading, spacing: 4) {
                Text("fiscal.slip_signature_label").font(.caption.monospaced())
                Text(report.signatureHash ?? String(localized: "fiscal.slip_hash_pending")).font(.caption2.monospaced()).textSelection(.enabled)
                    .accessibilityIdentifier("slip.hash")
            }
            Label(isSealed ? "fiscal.slip_sealed_status" : "fiscal.slip_unsealed_status", systemImage: isSealed ? "checkmark.seal.fill" : "info.circle")
                .font(.footnote.weight(.semibold))
                .foregroundStyle(isSealed ? .green : .blue)
                .padding(.top, 6)
                .accessibilityIdentifier("slip.status")
        }
        .padding(24)
        .frame(maxWidth: 440)
        .background(Theme.surface)
        .clipShape(RoundedRectangle(cornerRadius: 6))
        .shadow(color: .black.opacity(0.15), radius: 12, y: 6)
    }

    private var dashed: some View {
        Line().stroke(style: StrokeStyle(lineWidth: 1, dash: [4, 3])).foregroundStyle(Theme.inkMuted).frame(height: 1)
    }

    private func line(_ label: String, _ value: String, bold: Bool = false) -> some View {
        HStack {
            Text(label)
            Spacer()
            Text(value).monospacedDigit().environment(\.layoutDirection, .leftToRight)
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
