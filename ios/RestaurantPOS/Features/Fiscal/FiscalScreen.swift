import SwiftUI
import UniformTypeIdentifiers
import PosKit

/// Rapports NF525 : aperçu X, clôture Z scellée, export FEC.
struct FiscalScreen: View {
    @Environment(AppModel.self) private var model
    @State private var confirmsZ = false
    @State private var showArchiveImporter = false
    @State private var fecFrom = Calendar.current.date(from: Calendar.current.dateComponents([.year, .month], from: Date())) ?? Date()
    @State private var fecTo = Date()
    @State private var periodType: FiscalPeriodType = .monthly
    @State private var periodKey = ""
    @State private var receiptIdentifier = ""

    var body: some View {
        let fiscal = model.fiscal
        HStack(alignment: .top, spacing: 24) {
            ScrollView {
                if let report = fiscal.report {
                    FiscalSlip(report: report, isSealed: fiscal.isSealed, duplicateNumber: fiscal.lastDuplicateNumber)
                        .padding(.vertical, 24)
                } else {
                    ProgressView().padding(60)
                }
            }
            .frame(maxWidth: .infinity)

            ScrollView(showsIndicators: false) {
                VStack(alignment: .leading, spacing: 16) {
                    Card {
                        VStack(alignment: .leading, spacing: 12) {
                            HStack {
                                Label("fiscal.reports_title", systemImage: "doc.text").font(.headline)
                                Spacer()
                                if let cert = model.settingsStore.settings?.certificateNumber, !cert.isEmpty {
                                    Label(String(localized: "fiscal.cert_badge \(cert)"), systemImage: "checkmark.seal.fill")
                                        .font(.caption.bold())
                                        .foregroundStyle(Color.green)
                                        .accessibilityIdentifier("fiscal.certificateBadge")
                                } else {
                                    Label("fiscal.cert_pending", systemImage: "hourglass")
                                        .font(.caption.bold())
                                        .foregroundStyle(Color.orange)
                                        .accessibilityIdentifier("fiscal.certificateBadge")
                                }
                            }
                            Text("fiscal.x_z_explanation")
                                .font(.subheadline).foregroundStyle(Theme.inkMuted)
                            ActionButton(title: String(localized: "fiscal.preview_x_button"), systemImage: "eye", kind: .tonal) {
                                Task { await fiscal.previewX() }
                            }
                            .accessibilityIdentifier("fiscal.previewX")
                            if fiscal.report != nil {
                                if fiscal.isSealed {
                                    ActionButton(title: String(localized: "fiscal.reprint_z"), systemImage: "printer", kind: .tonal) {
                                        Task { await fiscal.reprintZ() }
                                    }
                                    .accessibilityIdentifier("fiscal.reprintZ")
                                } else {
                                    ActionButton(title: String(localized: "fiscal.print_x_report"), systemImage: "printer", kind: .tonal) {
                                        Task { await fiscal.printX() }
                                    }
                                    .accessibilityIdentifier("fiscal.printX")
                                }
                            }
                            ActionButton(title: String(localized: "fiscal.execute_z_button"), systemImage: "lock.doc", isLoading: fiscal.isWorking, kind: .danger) {
                                confirmsZ = true
                            }
                            .accessibilityIdentifier("fiscal.executeZ")
                        }
                    }
                    Card {
                        VStack(alignment: .leading, spacing: 12) {
                            Label("fiscal.verification_title", systemImage: "checkmark.shield").font(.headline)
                            Text("fiscal.verification_subtitle")
                                .font(.subheadline).foregroundStyle(Theme.inkMuted)
                            ActionButton(title: String(localized: "fiscal.verify_chains_button"), systemImage: "checkmark.seal", isLoading: fiscal.isWorking, kind: .primary) {
                                Task { await fiscal.verify() }
                            }
                            .accessibilityIdentifier("fiscal.verifyChains")

                            if let res = fiscal.verificationResult {
                                VStack(alignment: .leading, spacing: 8) {
                                    HStack {
                                        Image(systemName: res.isValid ? "checkmark.circle.fill" : "exclamationmark.triangle.fill")
                                            .foregroundStyle(res.isValid ? Color.green : Color.red)
                                        Text(res.isValid ? "fiscal.verification_success" : "fiscal.verification_failed")
                                            .font(.subheadline.bold())
                                            .foregroundStyle(res.isValid ? Color.green : Color.red)
                                    }
                                    ForEach(res.chains, id: \.chain) { chain in
                                        HStack {
                                            Text(chain.chain)
                                                .font(.caption.bold())
                                            if let tid = chain.terminalId {
                                                Text("(\(tid))")
                                                    .font(.caption2)
                                                    .foregroundStyle(Theme.inkMuted)
                                            }
                                            Spacer()
                                            Text("\(chain.checkedCount)")
                                                .font(.caption)
                                            Image(systemName: chain.isValid ? "checkmark.circle" : "xmark.circle")
                                                .foregroundStyle(chain.isValid ? Color.green : Color.red)
                                        }
                                    }
                                }
                                .padding(8)
                                .background(Color.secondary.opacity(0.1))
                                .clipShape(RoundedRectangle(cornerRadius: 8))
                                .accessibilityIdentifier("fiscal.verificationResult")
                            }
                        }
                    }
                    Card {
                        VStack(alignment: .leading, spacing: 12) {
                            Label("fiscal.period_closures_title", systemImage: "calendar.badge.clock").font(.headline)
                            Text("fiscal.period_closures_subtitle")
                                .font(.subheadline).foregroundStyle(Theme.inkMuted)

                            Picker("fiscal.period_type", selection: $periodType) {
                                Text("fiscal.period_monthly").tag(FiscalPeriodType.monthly)
                                Text("fiscal.period_annual").tag(FiscalPeriodType.annual)
                            }
                            .pickerStyle(.segmented)
                            .onChange(of: periodType) { _, newType in
                                updateDefaultPeriodKey(newType)
                            }

                            HStack {
                                Text("fiscal.period_key")
                                    .font(.caption)
                                    .foregroundStyle(Theme.inkMuted)
                                Spacer()
                                TextField("YYYY-MM", text: $periodKey)
                                    .textFieldStyle(.roundedBorder)
                                    .frame(width: 120)
                                    .accessibilityIdentifier("fiscal.periodKey")
                            }

                            ActionButton(title: String(localized: "fiscal.execute_period_closure"), systemImage: "lock.badge.clock", isLoading: fiscal.isWorking, kind: .primary) {
                                Task {
                                    let success = await fiscal.executePeriodClosure(type: periodType, key: periodKey)
                                    if success {
                                        updateDefaultPeriodKey(periodType)
                                    }
                                }
                            }
                            .accessibilityIdentifier("fiscal.executePeriodClosure")

                            if !fiscal.periodClosures.isEmpty {
                                VStack(alignment: .leading, spacing: 6) {
                                    Text("fiscal.period_history_title")
                                        .font(.caption.bold())
                                        .foregroundStyle(Theme.inkMuted)
                                    ForEach(fiscal.periodClosures.prefix(5)) { c in
                                        HStack {
                                            Text(c.periodType == .monthly ? "fiscal.period_monthly" : "fiscal.period_annual")
                                                .font(.caption2.bold())
                                                .padding(.horizontal, 4)
                                                .padding(.vertical, 2)
                                                .background(Color.blue.opacity(0.15))
                                                .foregroundStyle(Color.blue)
                                                .clipShape(RoundedRectangle(cornerRadius: 4))
                                            Text(c.periodKey)
                                                .font(.caption.monospaced())
                                            Spacer()
                                            Text(c.totalTtc.formatted)
                                                .font(.caption.bold())

                                            if fiscal.archives.contains(where: { $0.periodClosureId == c.id }) {
                                                Label("fiscal.archived", systemImage: "checkmark.circle.fill")
                                                    .font(.caption2.bold())
                                                    .foregroundStyle(Color.green)
                                            } else {
                                                Button {
                                                    Task { await fiscal.createArchive(periodClosureId: c.id) }
                                                } label: {
                                                    Text("fiscal.action_archive")
                                                        .font(.caption2.bold())
                                                        .padding(.horizontal, 6)
                                                        .padding(.vertical, 3)
                                                        .background(Color.secondary.opacity(0.15))
                                                        .clipShape(RoundedRectangle(cornerRadius: 4))
                                                }
                                                .buttonStyle(.plain)
                                                .accessibilityIdentifier("fiscal.archivePeriodClosure_\(c.periodKey)")
                                            }
                                        }
                                    }
                                }
                                .padding(8)
                                .background(Color.secondary.opacity(0.08))
                                .clipShape(RoundedRectangle(cornerRadius: 8))
                            }
                        }
                    }
                    Card {
                        VStack(alignment: .leading, spacing: 12) {
                            HStack {
                                Label("fiscal.archives_title", systemImage: "archivebox").font(.headline)
                                Spacer()
                                Button {
                                    showArchiveImporter = true
                                } label: {
                                    Label("fiscal.verify_chains_button", systemImage: "checkmark.shield")
                                        .font(.caption.bold())
                                }
                                .accessibilityIdentifier("fiscal.verifyArchiveButton")
                            }
                            Text("fiscal.archives_subtitle")
                                .font(.subheadline).foregroundStyle(Theme.inkMuted)

                            if fiscal.archives.isEmpty {
                                Text("common.no_data")
                                    .font(.caption)
                                    .foregroundStyle(Theme.inkMuted)
                            } else {
                                VStack(alignment: .leading, spacing: 6) {
                                    ForEach(fiscal.archives.prefix(5)) { a in
                                        HStack {
                                            VStack(alignment: .leading, spacing: 2) {
                                                Text(a.fileName)
                                                    .font(.caption.monospaced().bold())
                                                Text("\(a.periodKey) • \(ByteCountFormatter.string(fromByteCount: Int64(a.fileSizeBytes), countStyle: .file))")
                                                    .font(.caption2)
                                                    .foregroundStyle(Theme.inkMuted)
                                            }
                                            Spacer()
                                            Text("#\(a.archiveSequence)")
                                                .font(.caption2.monospaced())
                                                .foregroundStyle(Theme.inkMuted)
                                        }
                                        .padding(.vertical, 2)
                                    }
                                }
                                .padding(8)
                                .background(Color.secondary.opacity(0.08))
                                .clipShape(RoundedRectangle(cornerRadius: 8))
                            }
                        }
                    }
                    Card {
                        VStack(alignment: .leading, spacing: 12) {
                            Label("fiscal.reprint_receipt_title", systemImage: "doc.text.magnifyingglass").font(.headline)
                            Text("fiscal.reprint_receipt_subtitle")
                                .font(.subheadline).foregroundStyle(Theme.inkMuted)

                            HStack {
                                TextField("fiscal.reprint_receipt_placeholder", text: $receiptIdentifier)
                                    .textFieldStyle(.roundedBorder)
                                    .accessibilityIdentifier("fiscal.reprintReceiptInput")
                                ActionButton(title: String(localized: "fiscal.reprint_receipt_btn"), systemImage: "printer", isLoading: fiscal.isWorking, kind: .primary) {
                                    Task {
                                        let trimmed = receiptIdentifier.trimmingCharacters(in: .whitespacesAndNewlines)
                                        if !trimmed.isEmpty {
                                            await fiscal.reprintReceipt(receiptIdentifier: trimmed)
                                        }
                                    }
                                }
                                .accessibilityIdentifier("fiscal.reprintReceiptBtn")
                            }
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
            }
            .frame(width: 360)
        }
        .padding(24)
        .task {
            await fiscal.loadLatest()
            await fiscal.loadPeriodClosures()
            await fiscal.loadArchives()
            await model.settingsStore.load()
            if periodKey.isEmpty {
                updateDefaultPeriodKey(periodType)
            }
        }
        .fileImporter(isPresented: $showArchiveImporter, allowedContentTypes: [.zip]) { result in
            switch result {
            case .success(let url):
                guard url.startAccessingSecurityScopedResource() else { return }
                defer { url.stopAccessingSecurityScopedResource() }
                if let data = try? Data(contentsOf: url) {
                    Task {
                        _ = await fiscal.verifyArchive(data: data, fileName: url.lastPathComponent)
                    }
                }
            case .failure:
                break
            }
        }
        .confirmationDialog("fiscal.confirm_z_dialog_title", isPresented: $confirmsZ, titleVisibility: .visible) {
            Button("fiscal.confirm_z_button", role: .destructive) { Task { await fiscal.executeZ() } }
                .accessibilityIdentifier("fiscal.confirmZ")
        } message: {
            Text("fiscal.confirm_z_message")
        }
    }

    private func updateDefaultPeriodKey(_ type: FiscalPeriodType) {
        let calendar = Calendar.current
        let now = Date()
        if type == .annual {
            let year = calendar.component(.year, from: now) - 1
            periodKey = String(year)
        } else {
            if let prevMonth = calendar.date(byAdding: .month, value: -1, to: now) {
                let y = calendar.component(.year, from: prevMonth)
                let m = calendar.component(.month, from: prevMonth)
                periodKey = String(format: "%04d-%02d", y, m)
            }
        }
    }
}

/// Ticket de rapport au format « imprimante thermique ».
struct FiscalSlip: View {
    let report: FiscalReport
    let isSealed: Bool
    var duplicateNumber: Int? = nil

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            if let dup = duplicateNumber {
                Text(String(localized: "fiscal.duplicate_banner \(dup)"))
                    .font(.caption.bold())
                    .padding(6)
                    .frame(maxWidth: .infinity)
                    .background(Color.orange.opacity(0.2))
                    .foregroundStyle(Color.orange)
                    .clipShape(RoundedRectangle(cornerRadius: 6))
                    .accessibilityIdentifier("slip.duplicateBanner")
            }
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
