import Foundation

/// Réponse de `POST /api/devices/pair`.
public struct PairResponse: Codable, Hashable, Sendable {
    public var deviceId: UUID
    public var token: String
    public var terminalId: String
    public var name: String
    public var role: String
    public var serverName: String

    public init(deviceId: UUID, token: String, terminalId: String, name: String, role: String, serverName: String) {
        self.deviceId = deviceId
        self.token = token
        self.terminalId = terminalId
        self.name = name
        self.role = role
        self.serverName = serverName
    }
}

/// Identité de l'iPad après appairage, conservée dans le trousseau.
/// `serverName` sert à retrouver le serveur en Bonjour si son adresse IP change.
public struct DeviceCredentials: Codable, Hashable, Sendable {
    public var deviceId: UUID
    public var token: String
    public var terminalId: String
    public var name: String
    public var role: String
    public var serverName: String

    public init(_ response: PairResponse) {
        deviceId = response.deviceId
        token = response.token
        terminalId = response.terminalId
        name = response.name
        role = response.role
        serverName = response.serverName
    }

    /// Poste fictif des tests et du mode `-UITestMode -UITestPaired`.
    public static let demo = DeviceCredentials(PairResponse(
        deviceId: UUID(uuidString: "00000000-0000-0000-0000-0000000000D1")!,
        token: "device-token-demo", terminalId: "T01", name: "iPad démo", role: "Caisse", serverName: "Serveur démo"
    ))
}

/// Contenu du QR affiché par le back-office : `posdevice://pair?url=<serveur>&code=<code>`.
public struct PairingLink: Hashable, Sendable {
    public var serverURL: URL
    public var code: String

    public init?(string: String) {
        guard let components = URLComponents(string: string),
              components.scheme == "posdevice", components.host == "pair",
              let items = components.queryItems,
              let rawURL = items.first(where: { $0.name == "url" })?.value,
              let url = URL(string: rawURL), url.scheme == "http" || url.scheme == "https", url.host != nil,
              let code = items.first(where: { $0.name == "code" })?.value, !code.isEmpty
        else { return nil }
        serverURL = url
        self.code = code
    }
}
