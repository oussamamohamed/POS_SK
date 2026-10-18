import SwiftUI
import UniformTypeIdentifiers
import PosKit

// MARK: - Vérification des chaînes

/// Vérification des chaînes fiscales : verdict global, une ligne par chaîne et la rupture détectée.
struct ChainVerificationCard: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        let fiscal = model.fiscal
        Card {
            VStack(alignment: .leading, spacing: 12) {
                Label("fiscal.verification_title", systemImage: "checkmark.shield").font(.headline)
                Text("fiscal.verification_subtitle").font(.subheadline).foregroundStyle(Theme.inkMuted)
                ActionButton(title: String(localized: "fiscal.verify_chains_button"), systemImage: "checkmark.seal", isLoading: fiscal.isWorking, kind: .primary) {
                    Task { await fiscal.verify() }
                }
                .accessibilityIdentifier("fiscal.verifyChains")
                if let result = fiscal.verificationResult {
                    VerificationResultView(result: result)
                }
            }
        }
    }
}

private struct VerificationResultView: View {
    let result: FiscalVerificationResult

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Image(systemName: result.isValid ? "checkmark.circle.fill" : "exclamationmark.triangle.fill")
                Text(result.isValid ? LocalizedStringKey("fiscal.chains_valid") : LocalizedStringKey("fiscal.chains_broken"))
                    .font(.subheadline.bold())
                    .accessibilityIdentifier("fiscal.verificationStatus")
            }
            .foregroundStyle(result.isValid ? Color.green : Color.red)
            ForEach(result.chains, id: \.self) { chain in
                ChainRow(chain: chain)
            }
        }
        .padding(8)
        .background(Color.secondary.opacity(0.1))
        .clipShape(RoundedRectangle(cornerRadius: 8))
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier("fiscal.verificationResult")
    }
}

private struct ChainRow: View {
    let chain: FiscalChainStatus

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack {
                Text(Self.title(chain.chain)).font(.caption.bold())
                if let terminal = chain.terminalId {
                    Text(verbatim: "(\(terminal))").font(.caption2).foregroundStyle(Theme.inkMuted)
                }
                Spacer()
                Text(verbatim: "\(chain.checkedCount)").font(.caption)
                Image(systemName: chain.isValid ? "checkmark.circle" : "xmark.circle")
                    .foregroundStyle(chain.isValid ? Color.green : Color.red)
            }
            if let broken = chain.break {
                HStack(spacing: 4) {
                    Text(verbatim: "\(broken.reference) :")
                    Text(Self.kind(broken.kind))
                }
                .font(.caption2)
                .foregroundStyle(Color.red)
            }
        }
        .accessibilityElement(children: .combine)
        .accessibilityIdentifier("fiscal.chain.\(chain.chain)")
    }

    static func title(_ code: String) -> LocalizedStringKey {
        switch code {
        case "receipts": "fiscal.chain_receipts"
        case "z_closures": "fiscal.chain_z_closures"
        case "period_closures": "fiscal.chain_period_closures"
        case "journal": "fiscal.chain_journal"
        case "archives": "fiscal.chain_archives"
        default: LocalizedStringKey(code)
        }
    }

    static func kind(_ code: String) -> LocalizedStringKey {
        switch code {
        case "sequence_gap": "fiscal.break_sequence_gap"
        case "previous_hash_mismatch": "fiscal.break_previous_hash_mismatch"
        case "signature_mismatch": "fiscal.break_signature_mismatch"
        default: LocalizedStringKey(code)
        }
    }
}

// MARK: - Clôtures de période

/// Clôtures mensuelles et annuelles : sélection du type et de la clé, scellement, historique avec archivage.
struct PeriodClosuresCard: View {
    @Environment(AppModel.self) private var model
    @State private var periodType: FiscalPeriodType = .monthly
    @State private var periodKey = ""

    var body: some View {
        let fiscal = model.fiscal
        Card {
            VStack(alignment: .leading, spacing: 12) {
                Label("fiscal.period_closures_title", systemImage: "calendar.badge.clock").font(.headline)
                Text("fiscal.period_closures_subtitle").font(.subheadline).foregroundStyle(Theme.inkMuted)
                Picker("fiscal.period_type", selection: $periodType) {
                    Text("fiscal.period_monthly").tag(FiscalPeriodType.monthly)
                    Text("fiscal.period_annual").tag(FiscalPeriodType.annual)
                }
                .pickerStyle(.segmented)
                .onChange(of: periodType) { _, newType in periodKey = Self.defaultKey(newType, now: Date()) }
                HStack {
                    Text("fiscal.period_key").font(.caption).foregroundStyle(Theme.inkMuted)
                    Spacer()
                    TextField("YYYY-MM", text: $periodKey)
                        .textFieldStyle(.roundedBorder)
                        .frame(width: 120)
                        .accessibilityIdentifier("fiscal.periodKey")
                }
                ActionButton(title: String(localized: "fiscal.execute_period_closure"), systemImage: "lock.badge.clock", isLoading: fiscal.isWorking, kind: .primary) {
                    Task {
                        if await fiscal.executePeriodClosure(type: periodType, key: periodKey) {
                            periodKey = Self.defaultKey(periodType, now: Date())
                        }
                    }
                }
                .accessibilityIdentifier("fiscal.executePeriodClosure")
                if !fiscal.periodClosures.isEmpty {
                    PeriodClosureList()
                }
            }
        }
        .task {
            if periodKey.isEmpty { periodKey = Self.defaultKey(periodType, now: Date()) }
        }
    }

    /// Mois ou exercice précédent (calendrier grégorien du fuseau de l'appareil, comme la clôture) : la période qu'on clôture d'habitude.
    static func defaultKey(_ type: FiscalPeriodType, now: Date) -> String {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = .current
        if type == .annual { return String(calendar.component(.year, from: now) - 1) }
        let previous = calendar.date(byAdding: .month, value: -1, to: now) ?? now
        return String(format: "%04d-%02d", calendar.component(.year, from: previous), calendar.component(.month, from: previous))
    }
}

private struct PeriodClosureList: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        let fiscal = model.fiscal
        VStack(alignment: .leading, spacing: 6) {
            Text("fiscal.period_history_title").font(.caption.bold()).foregroundStyle(Theme.inkMuted)
            ForEach(fiscal.periodClosures.prefix(5)) { closure in
                HStack {
                    Text(closure.periodType == .monthly ? LocalizedStringKey("fiscal.period_monthly") : LocalizedStringKey("fiscal.period_annual"))
                        .font(.caption2.bold())
                        .padding(.horizontal, 4)
                        .padding(.vertical, 2)
                        .background(Color.blue.opacity(0.15))
                        .foregroundStyle(Color.blue)
                        .clipShape(RoundedRectangle(cornerRadius: 4))
                    Text(verbatim: closure.periodKey).font(.caption.monospaced())
                    Spacer()
                    Text(verbatim: closure.totalTtc.formatted).font(.caption.bold())
                    if fiscal.archives.contains(where: { $0.periodClosureId == closure.id }) {
                        Label("fiscal.archived", systemImage: "checkmark.circle.fill")
                            .font(.caption2.bold())
                            .foregroundStyle(Color.green)
                            .accessibilityIdentifier("fiscal.archived_\(closure.periodKey)")
                    } else {
                        Button {
                            Task { await fiscal.createArchive(periodClosureId: closure.id) }
                        } label: {
                            Text("fiscal.action_archive")
                                .font(.caption2.bold())
                                .padding(.horizontal, 6)
                                .padding(.vertical, 3)
                                .background(Color.secondary.opacity(0.15))
                                .clipShape(RoundedRectangle(cornerRadius: 4))
                        }
                        .buttonStyle(.plain)
                        .accessibilityIdentifier("fiscal.archivePeriodClosure_\(closure.periodKey)")
                    }
                }
            }
        }
        .padding(8)
        .background(Color.secondary.opacity(0.08))
        .clipShape(RoundedRectangle(cornerRadius: 8))
    }
}

// MARK: - Archives

/// Archives fiscales : liste, partage du ZIP, vérification d'un fichier choisi et son verdict.
struct ArchivesCard: View {
    @Environment(AppModel.self) private var model
    @State private var showsImporter = false

    var body: some View {
        let fiscal = model.fiscal
        Card {
            VStack(alignment: .leading, spacing: 12) {
                HStack {
                    Label("fiscal.archives_title", systemImage: "archivebox").font(.headline)
                    Spacer()
                    Button { showsImporter = true } label: {
                        Label("fiscal.verify_archive_button", systemImage: "checkmark.shield").font(.caption.bold())
                    }
                    .accessibilityIdentifier("fiscal.verifyArchiveButton")
                }
                Text("fiscal.archives_subtitle").font(.subheadline).foregroundStyle(Theme.inkMuted)
                if let result = fiscal.archiveVerification {
                    ArchiveVerdictRow(result: result)
                }
                if fiscal.archives.isEmpty {
                    Text("common.no_data").font(.caption).foregroundStyle(Theme.inkMuted)
                } else {
                    ArchiveList(archives: Array(fiscal.archives.prefix(5)))
                }
                if let file = fiscal.exportedArchiveFile {
                    ShareLink(item: file) { Label("fiscal.share_file \(file.lastPathComponent)", systemImage: "square.and.arrow.up") }
                        .accessibilityIdentifier("fiscal.archiveShare")
                }
            }
        }
        .fileImporter(isPresented: $showsImporter, allowedContentTypes: [.zip]) { result in
            guard case .success(let url) = result else { return }
            Task {
                let scoped = url.startAccessingSecurityScopedResource()
                defer { if scoped { url.stopAccessingSecurityScopedResource() } }
                _ = await fiscal.verifyArchiveFile(at: url)
            }
        }
    }
}

private struct ArchiveList: View {
    @Environment(AppModel.self) private var model
    let archives: [FiscalArchive]

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            ForEach(archives) { archive in
                HStack {
                    VStack(alignment: .leading, spacing: 2) {
                        Text(verbatim: archive.fileName).font(.caption.monospaced().bold())
                        Text(verbatim: "\(archive.periodKey) • \(ByteCountFormatter.string(fromByteCount: Int64(archive.fileSizeBytes), countStyle: .file))")
                            .font(.caption2)
                            .foregroundStyle(Theme.inkMuted)
                    }
                    Spacer()
                    Text(verbatim: "#\(archive.archiveSequence)").font(.caption2.monospaced()).foregroundStyle(Theme.inkMuted)
                    Button {
                        Task { await model.fiscal.shareArchive(id: archive.id) }
                    } label: {
                        Label("fiscal.archive_share", systemImage: "square.and.arrow.up").labelStyle(.iconOnly)
                    }
                    .accessibilityIdentifier("fiscal.shareArchive_\(archive.periodKey)")
                }
                .padding(.vertical, 2)
            }
        }
        .padding(8)
        .background(Color.secondary.opacity(0.08))
        .clipShape(RoundedRectangle(cornerRadius: 8))
    }
}

private struct ArchiveVerdictRow: View {
    let result: ArchiveVerificationResult

    var body: some View {
        Label(Self.text(result), systemImage: result.isValid ? "checkmark.seal.fill" : "exclamationmark.triangle.fill")
            .font(.caption.bold())
            .foregroundStyle(result.isValid ? Color.green : Color.red)
            .accessibilityIdentifier("fiscal.archiveVerification")
    }

    static func text(_ result: ArchiveVerificationResult) -> LocalizedStringKey {
        if result.isValid { return "fiscal.archive_result_valid" }
        switch result.reason {
        case "hash_mismatch": return "fiscal.archive_result_hash_mismatch"
        case "chain_break": return "fiscal.archive_result_chain_break"
        default: return "fiscal.archive_result_unknown"
        }
    }
}

// MARK: - FEC

/// Export FEC : dates, SIREN (9 chiffres ou vide), société (journalisée), partage du fichier.
struct FecExportCard: View {
    @Environment(AppModel.self) private var model
    @State private var from = Calendar.current.date(from: Calendar.current.dateComponents([.year, .month], from: Date())) ?? Date()
    @State private var to = Date()
    @State private var company = ""

    var body: some View {
        let fiscal = model.fiscal
        let sirenIsValid = Siren.isValid(Siren.normalized(model.settings.siren))
        Card {
            VStack(alignment: .leading, spacing: 12) {
                Label("fiscal.fec_export_title", systemImage: "square.and.arrow.up").font(.headline)
                DatePicker("fiscal.fec_from", selection: $from, displayedComponents: .date)
                DatePicker("fiscal.fec_to", selection: $to, in: from..., displayedComponents: .date)
                HStack {
                    Text("fiscal.siren_label")
                    TextField("123456789", text: Binding(get: { model.settings.siren }, set: { model.settings.siren = $0 }))
                        .keyboardType(.numberPad)
                        .textFieldStyle(.roundedBorder)
                        .accessibilityIdentifier("fec.siren")
                }
                if !sirenIsValid {
                    Text("fiscal.siren_invalid_hint").font(.caption).foregroundStyle(Color.red).accessibilityIdentifier("fec.sirenHint")
                }
                HStack {
                    Text("fiscal.fec_company")
                    TextField("", text: $company).textFieldStyle(.roundedBorder).accessibilityIdentifier("fec.company")
                }
                ActionButton(title: String(localized: "fiscal.generate_fec_button"), systemImage: "doc.badge.gearshape", isLoading: fiscal.isWorking, kind: .tonal) {
                    let end = Calendar.current.date(bySettingHour: 23, minute: 59, second: 59, of: to) ?? to
                    Task { await fiscal.exportFec(from: Calendar.current.startOfDay(for: from), to: end, company: company) }
                }
                .disabled(!sirenIsValid)
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
        .task(id: model.settingsStore.settings?.companyName) {
            if company.isEmpty { company = model.settingsStore.settings?.companyName ?? "" }
        }
    }
}
