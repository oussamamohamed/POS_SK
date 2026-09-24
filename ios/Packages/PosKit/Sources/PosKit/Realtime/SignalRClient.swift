import Foundation

/// Messages du protocole SignalR JSON (séparés par le caractère 0x1E).
public enum SignalRMessage: Equatable, Sendable {
    case invocation(target: String, arguments: [JSONValue])
    case ping
    case close(error: String?)
    case other(type: Int)

    static let recordSeparator: Character = "\u{1e}"

    /// Découpe une trame WebSocket en messages et ignore l'accusé de handshake (`{}`).
    public static func parse(frame: String) -> [SignalRMessage] {
        frame.split(separator: recordSeparator, omittingEmptySubsequences: true).compactMap { chunk in
            guard let data = chunk.data(using: .utf8),
                  let object = try? JSONDecoder().decode([String: JSONValue].self, from: data),
                  case let .number(typeValue)? = object["type"] else {
                return nil
            }
            switch Int(typeValue) {
            case 1:
                guard case let .string(target)? = object["target"] else { return nil }
                if case let .array(arguments)? = object["arguments"] {
                    return .invocation(target: target, arguments: arguments)
                }
                return .invocation(target: target, arguments: [])
            case 6:
                return .ping
            case 7:
                if case let .string(error)? = object["error"] { return .close(error: error) }
                return .close(error: nil)
            default:
                return .other(type: Int(typeValue))
            }
        }
    }

    public static let handshake = "{\"protocol\":\"json\",\"version\":1}\u{1e}"
    public static let pingFrame = "{\"type\":6}\u{1e}"
}

/// Valeur JSON arbitraire (arguments d'invocation SignalR).
public enum JSONValue: Codable, Equatable, Sendable {
    case string(String)
    case number(Double)
    case bool(Bool)
    case object([String: JSONValue])
    case array([JSONValue])
    case null

    public init(from decoder: Decoder) throws {
        let c = try decoder.singleValueContainer()
        if c.decodeNil() { self = .null }
        else if let v = try? c.decode(Bool.self) { self = .bool(v) }
        else if let v = try? c.decode(Double.self) { self = .number(v) }
        else if let v = try? c.decode(String.self) { self = .string(v) }
        else if let v = try? c.decode([JSONValue].self) { self = .array(v) }
        else { self = .object(try c.decode([String: JSONValue].self)) }
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.singleValueContainer()
        switch self {
        case .string(let v): try c.encode(v)
        case .number(let v): try c.encode(v)
        case .bool(let v): try c.encode(v)
        case .object(let v): try c.encode(v)
        case .array(let v): try c.encode(v)
        case .null: try c.encodeNil()
        }
    }

    /// Décode la valeur dans un type `Decodable` (ex. `HappyHourStatus`).
    public func decode<T: Decodable>(_ type: T.Type) -> T? {
        guard let data = try? JSONEncoder().encode(self) else { return nil }
        return try? PosJSON.makeDecoder().decode(T.self, from: data)
    }
}

/// Événements temps réel utiles à l'app.
public enum RealtimeEvent: Equatable, Sendable {
    case kitchenChanged
    case gridLayoutUpdated(TouchGridLayout?)
    case happyHourChanged(HappyHourStatus?)
    case tablesChanged
    case connectionChanged(isConnected: Bool)

    static func from(target: String, arguments: [JSONValue]) -> RealtimeEvent? {
        switch target {
        case "ReceiveKitchenUpdate", "OnNewTicketReceived", "OnTicketStatusChanged", "OnTicketRecalled", "OnItemStatusChanged":
            return .kitchenChanged
        case "OnGridLayoutUpdated":
            return .gridLayoutUpdated(arguments.first?.decode(TouchGridLayout.self))
        case "OnHappyHourStatusChanged":
            return .happyHourChanged(arguments.first?.decode(HappyHourStatus.self))
        case "OnTableStatusChanged":
            return .tablesChanged
        default:
            return nil
        }
    }
}

/// Client SignalR minimal (transport WebSocket, protocole JSON) avec reconnexion automatique.
/// Suffisant pour recevoir les diffusions serveur ; l'app n'invoque aucune méthode de hub.
public final class SignalRConnection: @unchecked Sendable {
    private let hubURL: URL
    private let tokenProvider: @Sendable () async -> String?
    private let session: URLSession
    private let lock = NSLock()
    private var task: URLSessionWebSocketTask?
    private var loop: Task<Void, Never>?
    private let continuation: AsyncStream<RealtimeEvent>.Continuation
    public let events: AsyncStream<RealtimeEvent>

    public init(hubURL: URL, session: URLSession = .shared, tokenProvider: @escaping @Sendable () async -> String?) {
        self.hubURL = hubURL
        self.session = session
        self.tokenProvider = tokenProvider
        (events, continuation) = AsyncStream.makeStream(of: RealtimeEvent.self, bufferingPolicy: .bufferingNewest(64))
    }

    deinit { stop() }

    public func start() {
        lock.lock(); defer { lock.unlock() }
        guard loop == nil else { return }
        loop = Task { [weak self] in await self?.runLoop() }
    }

    public func stop() {
        lock.lock()
        loop?.cancel(); loop = nil
        task?.cancel(with: .goingAway, reason: nil); task = nil
        lock.unlock()
    }

    private func setTask(_ socket: URLSessionWebSocketTask) {
        lock.withLock { task = socket }
    }

    private func runLoop() async {
        var attempt = 0
        while !Task.isCancelled {
            do {
                try await connectOnce()
                attempt = 0
            } catch {
                continuation.yield(.connectionChanged(isConnected: false))
            }
            attempt += 1
            let delay = min(30.0, pow(2.0, Double(min(attempt, 5))))
            try? await Task.sleep(for: .seconds(delay))
        }
    }

    private func connectOnce() async throws {
        let token = await tokenProvider()
        let connectionToken = try await negotiate(token: token)

        var components = URLComponents(url: hubURL, resolvingAgainstBaseURL: false)!
        components.scheme = hubURL.scheme == "https" ? "wss" : "ws"
        var items = [URLQueryItem(name: "id", value: connectionToken)]
        if let token { items.append(URLQueryItem(name: "access_token", value: token)) }
        components.queryItems = items

        let socket = session.webSocketTask(with: components.url!)
        setTask(socket)
        socket.resume()
        try await socket.send(.string(SignalRMessage.handshake))
        continuation.yield(.connectionChanged(isConnected: true))

        let pinger = Task {
            while !Task.isCancelled {
                try? await Task.sleep(for: .seconds(15))
                try? await socket.send(.string(SignalRMessage.pingFrame))
            }
        }
        defer { pinger.cancel() }

        while !Task.isCancelled {
            let message = try await socket.receive()
            let text: String
            switch message {
            case .string(let s): text = s
            case .data(let d): text = String(decoding: d, as: UTF8.self)
            @unknown default: continue
            }
            for parsed in SignalRMessage.parse(frame: text) {
                switch parsed {
                case let .invocation(target, arguments):
                    if let event = RealtimeEvent.from(target: target, arguments: arguments) {
                        continuation.yield(event)
                    }
                case .close:
                    throw APIError.transport("Hub fermé")
                case .ping, .other:
                    break
                }
            }
        }
    }

    private struct NegotiateResponse: Decodable { let connectionToken: String?; let connectionId: String? }

    private func negotiate(token: String?) async throws -> String {
        var components = URLComponents(url: hubURL.appendingPathComponent("negotiate"), resolvingAgainstBaseURL: false)!
        components.queryItems = [URLQueryItem(name: "negotiateVersion", value: "1")]
        var request = URLRequest(url: components.url!)
        request.httpMethod = "POST"
        if let token { request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization") }
        let (data, response) = try await session.data(for: request)
        guard (response as? HTTPURLResponse)?.statusCode == 200 else { throw APIError.transport("Négociation SignalR refusée") }
        let negotiated = try JSONDecoder().decode(NegotiateResponse.self, from: data)
        guard let id = negotiated.connectionToken ?? negotiated.connectionId else { throw APIError.transport("Jeton SignalR absent") }
        return id
    }
}
