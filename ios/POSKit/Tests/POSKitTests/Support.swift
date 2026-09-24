import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif
@testable import POSKit

enum Fixture {
    /// Réponses JSON capturées sur la vraie API `RestaurantPos.Api` (voir `Fixtures/README.md`).
    static func data(_ name: String) throws -> Data {
        guard let url = Bundle.module.url(forResource: name, withExtension: "json", subdirectory: "Fixtures") else {
            throw FixtureError.missing(name)
        }
        return try Data(contentsOf: url)
    }

    static func decode<T: Decodable>(_ type: T.Type, _ name: String) throws -> T {
        try POSJSON.decoder().decode(type, from: data(name))
    }

    enum FixtureError: Error { case missing(String) }
}

/// Transport HTTP factice : enregistre les requêtes et renvoie des réponses programmées.
final class StubTransport: HTTPTransport, @unchecked Sendable {
    struct Recorded {
        var method: String
        var path: String
        var query: String?
        var headers: [String: String]
        var body: [String: Any]?
    }

    private let lock = NSLock()
    private var responses: [String: (Int, Data)] = [:]
    private var recorded: [Recorded] = []

    /// Programme une réponse pour `"GET /api/tables"`.
    func on(_ route: String, status: Int = 200, fixture: String) throws {
        let data = try Fixture.data(fixture)
        on(route, status: status, data: data)
    }

    func on(_ route: String, status: Int = 200, json: String) {
        on(route, status: status, data: Data(json.utf8))
    }

    func on(_ route: String, status: Int, data: Data) {
        lock.lock(); defer { lock.unlock() }
        responses[route] = (status, data)
    }

    var requests: [Recorded] {
        lock.lock(); defer { lock.unlock() }
        return recorded
    }

    var last: Recorded? { requests.last }

    private func record(_ entry: Recorded, route: String) -> (Int, Data)? {
        lock.lock(); defer { lock.unlock() }
        recorded.append(entry)
        return responses[route]
    }

    func send(_ request: URLRequest) async throws -> (Data, Int) {
        let method = request.httpMethod ?? "GET"
        let path = request.url?.path ?? ""
        var body: [String: Any]?
        if let data = request.httpBody {
            body = try JSONSerialization.jsonObject(with: data) as? [String: Any]
        }
        let entry = Recorded(
            method: method,
            path: path,
            query: request.url?.query,
            headers: request.allHTTPHeaderFields ?? [:],
            body: body
        )
        let response = record(entry, route: "\(method) \(path)")
        guard let response else {
            return (Data(#"{"message":"route non programmée"}"#.utf8), 404)
        }
        return (response.1, response.0)
    }
}

struct FailingTransport: HTTPTransport {
    func send(_ request: URLRequest) async throws -> (Data, Int) {
        throw URLError(.cannotConnectToHost)
    }
}

extension Money {
    static func euros(_ value: Double) -> Money { Money(euros: value) }
}

@MainActor
func makeApp(backend: InMemoryPOSBackend = InMemoryPOSBackend()) -> AppModel {
    AppModel(
        settingsStore: InMemorySettingsStore(TerminalSettings(terminalId: "IPAD_TEST", demoMode: true)),
        makeAPI: { _ in backend }
    )
}

@MainActor
func loggedInApp(pin: String = "1234", backend: InMemoryPOSBackend = InMemoryPOSBackend()) async -> AppModel {
    let app = makeApp(backend: backend)
    for character in pin {
        await app.pinDigit(Int(String(character))!)
    }
    return app
}
