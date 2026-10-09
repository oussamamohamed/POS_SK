import Foundation

/// Backend autonome de l'iPad : implémente `PosAPI` sur une base SQLite locale, sans serveur .NET.
/// Les méthodes pas encore portées répondent `501` (voir `LocalPosAPI+Unsupported.swift`).
public actor LocalPosAPI: PosAPI {
    let db: SQLiteDatabase
    private var token: String?
    /// Horloge et calendrier injectables : verrouillage des PIN, Happy Hour (heure locale) et tests sans attente réelle.
    let clock: @Sendable () -> Date
    let calendar: Calendar

    private static let tokenPrefix = "local-"
    /// Identique à `SessionStore.pinLength` (isolé au MainActor, donc non réutilisable ici).
    static let pinLength = 4
    /// Le poste autonome est son propre terminal : les numéros de reçu (`NF-T01-…`) et de retrait (`#A-…`) en dépendent.
    public static let standaloneTerminalId = "T01"

    /// `path` : fichier SQLite à créer ou rouvrir, ou `":memory:"` (tests).
    public init(path: String, seed: LocalSeedMode = .demo, clock: @escaping @Sendable () -> Date = { Date() }, calendar: Calendar = .autoupdatingCurrent) throws {
        let db = try SQLiteDatabase(path: path)
        try LocalMigrator.migrate(db)
        try LocalSeeder.seedIfEmpty(db, mode: seed)
        self.db = db
        self.clock = clock
        self.calendar = calendar
    }

    var staffRepository: LocalStaffRepository { LocalStaffRepository(db: db) }
    var catalogRepository: LocalCatalogRepository { LocalCatalogRepository(db: db) }
    var floorRepository: LocalFloorRepository { LocalFloorRepository(db: db) }
    var gridRepository: LocalGridRepository { LocalGridRepository(db: db) }
    var orderRepository: LocalOrderRepository { LocalOrderRepository(db: db) }
    var kitchenRepository: LocalKitchenRepository { LocalKitchenRepository(db: db) }
    var paymentRepository: LocalPaymentRepository { LocalPaymentRepository(db: db) }
    var holdRepository: LocalHoldRepository { LocalHoldRepository(db: db) }
    var hotelRepository: LocalHotelRepository { LocalHotelRepository(db: db) }
    var printerRepository: LocalPrinterRepository { LocalPrinterRepository(db: db) }
    var happyHourRepository: LocalHappyHourRepository { LocalHappyHourRepository(db: db) }

    func unsupported(_ name: String = #function) -> APIError {
        .server(status: 501, message: "Non disponible en mode autonome : \(name)")
    }

    // MARK: Authentification

    public func setToken(_ token: String?) { self.token = token }
    public func setDeviceToken(_ token: String?) {}

    public func login(pin: String) async throws -> LoginResponse {
        try ensurePinAttemptsAllowed()
        guard let member = try staffRepository.activeMember(pin: pin) else {
            try recordFailedPin()
            return LoginResponse(success: false, operatorId: nil, operatorName: nil, role: nil, token: nil, errorMessage: "Code PIN ou identifiants incorrects")
        }
        token = Self.tokenPrefix + member.id.uuidString
        return LoginResponse(success: true, operatorId: member.id, operatorName: member.name, role: member.role, token: token)
    }

    /// La connexion et le PIN superviseur partagent le même compteur d'échecs : un PIN deviné par l'une ou l'autre voie reste soumis au verrouillage.
    var pinGuard: LocalPinGuard { LocalPinGuard(db: db) }

    func ensurePinAttemptsAllowed() throws { try pinGuard.ensureAllowed(now: clock()) }

    func recordFailedPin() throws { try pinGuard.recordFailure(now: clock()) }

    @discardableResult
    func requireAuth() throws -> StaffMember {
        guard let token, token.hasPrefix(Self.tokenPrefix),
              let id = UUID(uuidString: String(token.dropFirst(Self.tokenPrefix.count))),
              let member = try staffRepository.member(id: id), member.isActive
        else { throw APIError.unauthorized }
        return member
    }

    @discardableResult
    func requireManager() throws -> StaffMember {
        let member = try requireAuth()
        guard member.role.isManager else { throw APIError.forbidden("Action réservée à un responsable.") }
        return member
    }

    public func health() async throws -> TimeInterval { 0 }
}
