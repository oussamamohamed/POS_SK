import Foundation

public enum CardChargeStatus: Sendable, Equatable {
    case approved, declined
    /// Terminal injoignable, occupé ou mal configuré : rien n'a été débité.
    case unavailable
}

public struct CardChargeResult: Sendable, Equatable {
    public var status: CardChargeStatus
    public var reference: String?
    public var reason: String?

    public init(_ status: CardChargeStatus, reference: String? = nil, reason: String? = nil) {
        self.status = status
        self.reference = reference
        self.reason = reason
    }
}

/// Terminal de paiement (TPE) piloté par l'iPad en mode autonome : même rôle que `ICardTerminal` côté serveur .NET.
/// L'annulation de la tâche (délai dépassé) doit annuler la transaction sur le terminal.
public protocol CardTerminal: Sendable {
    func charge(amount: Money, reference: String) async throws -> CardChargeResult
}

/// Stripe Terminal, intégration pilotée par l'application (lecteurs BBPOS WisePOS E, Stripe S700…) : un PaymentIntent est créé, envoyé au
/// lecteur, puis l'action du lecteur est interrogée jusqu'à la décision. Aucun SDK : API REST seule, comme `StripeCardTerminal.cs`.
/// La clé doit être une clé **restreinte** (`rk_…`, droits PaymentIntents et Terminal seulement) : elle est stockée sur l'iPad.
public struct StripeCardTerminal: CardTerminal {
    public typealias Send = @Sendable (URLRequest) async throws -> (Data, HTTPURLResponse)

    private let secretKey: String
    private let readerId: String
    private let send: Send
    private let pollInterval: Duration
    private static let base = "https://api.stripe.com/v1/"

    public init(secretKey: String, readerId: String, pollInterval: Duration = .milliseconds(1500), send: Send? = nil) {
        self.secretKey = secretKey
        self.readerId = readerId
        self.pollInterval = pollInterval
        self.send = send ?? { request in
            let (data, response) = try await URLSession.shared.data(for: request)
            guard let http = response as? HTTPURLResponse else { throw URLError(.badServerResponse) }
            return (data, http)
        }
    }

    public func charge(amount: Money, reference: String) async throws -> CardChargeResult {
        do {
            guard let intent = try await post("payment_intents", [
                "amount": String(amount.cents), "currency": "eur", "payment_method_types[]": "card_present",
                "capture_method": "automatic", "metadata[reference]": reference
            ]), let intentId = intent["id"] as? String
            else { return CardChargeResult(.unavailable, reference: reference, reason: "intent_not_created") }

            guard try await post("terminal/readers/\(readerId)/process_payment_intent", ["payment_intent": intentId]) != nil
            else { return CardChargeResult(.unavailable, reference: reference, reason: "reader_unavailable") }

            do {
                while true {
                    try await Task.sleep(for: pollInterval)
                    guard let reader = try await get("terminal/readers/\(readerId)")
                    else { return CardChargeResult(.unavailable, reference: reference, reason: "reader_unreachable") }
                    guard let action = reader["action"] as? [String: Any] else { continue }
                    switch action["status"] as? String {
                    case "succeeded": return CardChargeResult(.approved, reference: intentId)
                    case "failed": return CardChargeResult(.declined, reference: intentId, reason: action["failure_code"] as? String ?? "card_declined")
                    default: continue
                    }
                }
            } catch is CancellationError {
                // Délai dépassé ou requête abandonnée : le client ne doit plus voir la saisie du code sur le terminal.
                _ = try? await post("terminal/readers/\(readerId)/cancel_action", [:])
                throw CancellationError()
            }
        } catch is URLError {
            return CardChargeResult(.unavailable, reference: reference, reason: "network")
        }
    }

    private func request(_ method: String, _ path: String, form: [String: String] = [:]) -> URLRequest {
        var request = URLRequest(url: URL(string: Self.base + path)!)
        request.httpMethod = method
        request.setValue("Bearer \(secretKey)", forHTTPHeaderField: "Authorization")
        if method == "POST" {
            let unreserved = CharacterSet(charactersIn: "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~")
            let encode = { (text: String) in text.addingPercentEncoding(withAllowedCharacters: unreserved) ?? "" }
            request.httpBody = Data(form.sorted { $0.key < $1.key }.map { "\(encode($0.key))=\(encode($0.value))" }.joined(separator: "&").utf8)
            request.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
        }
        return request
    }

    private func post(_ path: String, _ form: [String: String]) async throws -> [String: Any]? { try await run(request("POST", path, form: form)) }
    private func get(_ path: String) async throws -> [String: Any]? { try await run(request("GET", path)) }

    private func run(_ request: URLRequest) async throws -> [String: Any]? {
        let (data, response) = try await send(request)
        guard (200..<300).contains(response.statusCode) else { return nil }
        return (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] ?? [:]
    }
}
