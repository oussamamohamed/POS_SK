import Foundation
@testable import PosKit

/// API locale en mémoire, déjà déverrouillée avec le PIN donné (1234 = responsable, 2468 = serveur, 9999 = admin).
func makeLocalAPI(pin: String = "1234") async throws -> LocalPosAPI {
    let api = try LocalPosAPI(path: ":memory:")
    _ = try await api.login(pin: pin)
    return api
}

func temporaryDatabasePath() -> String {
    FileManager.default.temporaryDirectory.appendingPathComponent("pos-\(UUID().uuidString).sqlite").path
}
