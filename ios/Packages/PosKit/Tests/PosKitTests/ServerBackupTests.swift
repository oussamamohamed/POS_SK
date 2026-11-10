import Foundation
import Testing
@testable import PosKit

@Suite("Sauvegarde du serveur")
@MainActor
struct ServerBackupTests {
    /// `backup_status.json` est une vraie réponse de `GET /api/backup/status`.
    @Test func decodesTheServerStatus() throws {
        let url = try #require(Bundle.module.url(forResource: "backup_status", withExtension: "json", subdirectory: "Fixtures"))
        let status = try PosJSON.makeDecoder().decode(ServerBackupStatus.self, from: Data(contentsOf: url))
        #expect(status.configured && !status.overdue && status.copies == 2)
        #expect(status.lastFileName == "pos-backup-20261110T071744Z.posbak" && status.lastSizeBytes == 561_208)
        #expect(status.lastSuccessUtc != nil && status.lastFailureUtc == nil && status.lastError == nil)
    }

    @Test func runNowRefreshesTheStatus() async {
        let (model, _) = await makeModel()
        await model.serverBackup.load()
        #expect(model.serverBackup.status?.overdue == true)

        await model.serverBackup.runNow()

        #expect(model.serverBackup.status?.overdue == false)
        #expect(model.serverBackup.status?.copies == 1)
        #expect(model.serverBackup.isRunning == false)
    }
}
