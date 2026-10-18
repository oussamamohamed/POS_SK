import Foundation
import Testing
@testable import PosKit

/// `FiscalStore` branché sur la base locale (horloge de test) : ce que voit l'écran Fiscal en mode autonome.
@MainActor
@Suite("FiscalStore sur la base locale")
struct LocalFiscalStoreTests {
    private func makeModel(clock: LocalTestClock) async throws -> (AppModel, URL) {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("archives-\(UUID().uuidString)", isDirectory: true)
        let api = try LocalPosAPI(path: ":memory:", clock: { clock.now }, calendar: localTestCalendar, archivesDirectory: folder)
        let defaults = UserDefaults(suiteName: "PosKitTests-\(UUID().uuidString)")!
        let model = AppModel(api: api, settings: TerminalSettings(defaults: defaults, credentialStore: InMemoryCredentialStore(.standalone)))
        model.notifier.autoDismissAfter = nil
        for digit in "1234" { await model.session.appendDigit(String(digit)) }
        return (model, folder)
    }

    @Test func aRunningMonthShowsTheLocalizedReason() async throws {
        let (model, _) = try await makeModel(clock: LocalTestClock("2026-09-20T09:00:00Z"))
        #expect(await model.fiscal.executePeriodClosure(type: .monthly, key: "2026-09") == false)
        #expect(model.notifier.lastMessage == L10n.string("fiscal.period_error_not_ended"))
    }

    @Test func closuresAreListedNewestFirstAndKeysAreTrimmed() async throws {
        let clock = LocalTestClock("2026-11-02T09:00:00Z")
        let (model, _) = try await makeModel(clock: clock)
        #expect(await model.fiscal.executePeriodClosure(type: .monthly, key: " 2026-09 "))
        clock.advance(60)
        #expect(await model.fiscal.executePeriodClosure(type: .monthly, key: "2026-10"))
        await model.fiscal.loadPeriodClosures()
        #expect(model.fiscal.periodClosures.map(\.periodKey) == ["2026-10", "2026-09"])
    }

    @Test func closeArchiveShareExportAndVerifyGreen() async throws {
        let (model, folder) = try await makeModel(clock: LocalTestClock("2026-10-02T09:00:00Z"))
        #expect(await model.fiscal.executePeriodClosure(type: .monthly, key: "2026-09"))
        #expect(await model.fiscal.createArchive(periodClosureId: model.fiscal.periodClosures[0].id))
        let archive = try #require(model.fiscal.archives.first)
        #expect(await model.fiscal.shareArchive(id: archive.id))
        let shared = try #require(model.fiscal.exportedArchiveFile)
        #expect(shared.standardizedFileURL == folder.appendingPathComponent(archive.fileName).standardizedFileURL)
        #expect(await model.fiscal.verifyArchiveFile(at: shared)?.isValid == true)
        #expect(model.fiscal.archiveVerification?.isValid == true)

        await model.fiscal.exportFec(from: Date(timeIntervalSince1970: 0), to: Date(timeIntervalSince1970: 1_791_000_000), company: "Chez Marie")
        #expect(model.fiscal.exportedFile?.lastPathComponent.hasPrefix("123456789FEC") == true)
        #expect(await model.fiscal.verify())
        #expect(model.fiscal.verificationResult?.chains.map(\.chain) == ["receipts", "z_closures", "period_closures", "journal", "archives"])
    }

    @Test func anInvalidSirenIsExplainedAndNoFileIsKept() async throws {
        let (model, _) = try await makeModel(clock: LocalTestClock("2026-10-02T09:00:00Z"))
        model.settings.siren = "12345"
        await model.fiscal.exportFec(from: Date(timeIntervalSince1970: 0), to: Date(timeIntervalSince1970: 1_791_000_000))
        #expect(model.fiscal.exportedFile == nil)
        #expect(model.notifier.lastMessage == L10n.string("fiscal.fec_error_invalid_siren"))
    }
}
