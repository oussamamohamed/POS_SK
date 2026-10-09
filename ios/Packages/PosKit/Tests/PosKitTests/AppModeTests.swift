import Foundation
import Testing
@testable import PosKit

@Suite("AppMode : choix du mode de fonctionnement")
struct AppModeTests {
    @Test func aStoredChoiceAlwaysWins() {
        #expect(AppMode.resolve(stored: .standalone, hasPairedCredentials: true) == .standalone)
        #expect(AppMode.resolve(stored: .server, hasPairedCredentials: false) == .server)
    }

    @Test func pairedLegacyInstallStaysInServerMode() {
        // Un iPad appairé avant l'existence du mode autonome n'a aucun choix mémorisé : il reste en mode serveur, sans question.
        #expect(AppMode.resolve(stored: nil, hasPairedCredentials: true) == .server)
    }

    @Test func freshInstallAsksForAChoice() {
        #expect(AppMode.resolve(stored: nil, hasPairedCredentials: false) == nil)
        #expect(AppMode.server.rawValue == "server" && AppMode.standalone.rawValue == "standalone")
        #expect(AppMode.storageKey == "pos.appMode")
    }
}
