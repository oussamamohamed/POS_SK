import Foundation
import Network
import Observation

public struct DiscoveredServer: Identifiable, Hashable, Sendable {
    public var name: String
    public var id: String { name }
}

/// Recherche Bonjour des serveurs de caisse (`_restaurantpos._tcp`) du réseau local.
@MainActor @Observable
public final class ServerBrowser {
    public static let serviceType = "_restaurantpos._tcp"

    public private(set) var servers: [DiscoveredServer] = []
    @ObservationIgnored private var browser: NWBrowser?

    public init() {}

    public func start() {
        guard browser == nil else { return }
        let browser = NWBrowser(for: .bonjour(type: Self.serviceType, domain: nil), using: .tcp)
        browser.browseResultsChangedHandler = { [weak self] results, _ in
            let names = results.compactMap { result -> String? in
                if case let .service(name, _, _, _) = result.endpoint { return name }
                return nil
            }.sorted()
            Task { @MainActor in self?.servers = names.map(DiscoveredServer.init(name:)) }
        }
        browser.start(queue: .main)
        self.browser = browser
    }

    public func stop() {
        browser?.cancel()
        browser = nil
        servers = []
    }

    /// Résout un serveur annoncé en URL HTTP IPv4 en ouvrant une connexion TCP vers le service.
    public static func resolve(name: String, timeoutSeconds: Int = 5) async -> URL? {
        let parameters = NWParameters.tcp
        if let ip = parameters.defaultProtocolStack.internetProtocol as? NWProtocolIP.Options { ip.version = .v4 }
        let connection = NWConnection(to: .service(name: name, type: serviceType, domain: "local.", interface: nil), using: parameters)
        return await withCheckedContinuation { continuation in
            let once = ResumeOnce(continuation)
            connection.stateUpdateHandler = { state in
                switch state {
                case .ready:
                    var url: URL?
                    if case let .hostPort(host, port) = connection.currentPath?.remoteEndpoint, case let .ipv4(address) = host {
                        url = URL(string: "http://\(address):\(port.rawValue)")
                    }
                    connection.cancel()
                    once.resume(url)
                case .failed, .cancelled:
                    once.resume(nil)
                default:
                    break
                }
            }
            connection.start(queue: .global())
            DispatchQueue.global().asyncAfter(deadline: .now() + .seconds(timeoutSeconds)) {
                connection.cancel()
                once.resume(nil)
            }
        }
    }
}

/// Garantit une seule reprise de la continuation (connexion prête, échec ou délai dépassé).
private final class ResumeOnce: @unchecked Sendable {
    private let lock = NSLock()
    private var continuation: CheckedContinuation<URL?, Never>?

    init(_ continuation: CheckedContinuation<URL?, Never>) { self.continuation = continuation }

    func resume(_ value: URL?) {
        lock.lock()
        let pending = continuation
        continuation = nil
        lock.unlock()
        pending?.resume(returning: value)
    }
}
