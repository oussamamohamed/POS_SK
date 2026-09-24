import Foundation

/// Contrat entre l'app iPad et le serveur de caisse.
///
/// Deux implémentations : `POSAPIClient` (HTTP vers l'API ASP.NET Core) et
/// `InMemoryPOSBackend` (données de démonstration, utilisé par les tests UI et le mode démo).
public protocol POSAPI: Sendable {
    func health() async throws -> Bool

    // Authentification
    func login(pin: String) async throws -> OperatorSession
    func logout() async

    // Carte
    func categories() async throws -> [Category]
    func products() async throws -> [Product]

    // Salle
    func tables() async throws -> [DiningTable]
    func openTable(_ tableNumber: String, covers: Int, operator: Operator) async throws -> DiningTable
    /// `nil` si la table n'a pas de commande active (HTTP 404).
    func activeOrder(tableNumber: String) async throws -> ActiveOrder?
    func addItems(tableNumber: String, items: [OrderItemInput]) async throws -> ActiveOrder
    func dispatch(tableNumber: String) async throws

    // Encaissement salle
    func pay(_ request: PaymentSettlementRequest) async throws -> PaymentResult

    // Comptoir / vente à emporter
    func openCounterOrder(terminalId: String, destination: OrderDestination) async throws -> ActiveOrder
    func switchDestination(orderId: UUID, to destination: OrderDestination) async throws -> ActiveOrder
    func holdCounterOrder(orderId: UUID, terminalId: String, label: String?) async throws -> HeldOrder
    func heldOrders(terminalId: String) async throws -> [HeldOrder]
    func recallHeldOrder(holdId: UUID) async throws -> ActiveOrder
    func voidHeldOrder(holdId: UUID, supervisorPin: String, reason: String, terminalId: String) async throws
    func counterCheckout(_ request: CounterCheckoutRequest) async throws -> CounterCheckoutResult

    // Cuisine
    func kitchenTickets() async throws -> [KitchenTicket]
    func bumpTicket(id: UUID) async throws -> KitchenTicket

    // Fiscal NF525
    func xReport(terminalId: String) async throws -> FiscalReport
    /// `nil` si aucune clôture Z n'existe encore.
    func latestClosure(terminalId: String) async throws -> FiscalReport?
    func zClosure(terminalId: String, manager: Operator) async throws -> FiscalReport
}

/// Erreurs remontées à l'interface, avec message prêt à afficher.
public enum APIError: Error, Equatable, Sendable, LocalizedError {
    case unauthorized
    case forbidden(String?)
    case notFound(String?)
    case rateLimited(String?)
    case server(status: Int, message: String?)
    case network(String)
    case decoding(String)
    case invalidConfiguration(String)

    public var errorDescription: String? {
        switch self {
        case .unauthorized:
            "Session expirée. Veuillez saisir à nouveau votre code PIN."
        case .forbidden(let message):
            message ?? "Action non autorisée pour votre rôle."
        case .notFound(let message):
            message ?? "Élément introuvable."
        case .rateLimited(let message):
            message ?? "Trop de tentatives. Patientez avant de réessayer."
        case .server(let status, let message):
            message ?? "Erreur serveur (\(status))."
        case .network(let detail):
            "Serveur de caisse injoignable. Vérifiez le Wi-Fi et l'adresse du serveur. (\(detail))"
        case .decoding(let detail):
            "Réponse du serveur illisible : \(detail)"
        case .invalidConfiguration(let detail):
            detail
        }
    }

    /// Message d'erreur contenu dans le corps JSON (`message` ou `errorMessage`).
    static func message(from data: Data) -> String? {
        struct Body: Decodable {
            var message: String?
            var errorMessage: String?
            var title: String?
        }
        guard let body = try? JSONDecoder().decode(Body.self, from: data) else { return nil }
        return body.message ?? body.errorMessage ?? body.title
    }
}
