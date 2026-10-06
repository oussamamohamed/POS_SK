import Foundation

/// Backend autonome de l'iPad : implémente `PosAPI` sur une base SQLite locale, sans serveur .NET.
/// Les méthodes pas encore portées répondent `501` (voir `LocalPosAPI+Unsupported.swift`).
public actor LocalPosAPI: PosAPI {
    let db: SQLiteDatabase
    private var token: String?
    private var failedLogins: [Date] = []
    private var lockedUntil: Date?

    private static let tokenPrefix = "local-"
    /// Identique à `SessionStore.pinLength` (isolé au MainActor, donc non réutilisable ici).
    static let pinLength = 4
    /// Même règle que `PinRateLimiterService` : 5 échecs en 1 min verrouillent 30 s.
    private static let maxFailedLogins = 5
    private static let failureWindow: TimeInterval = 60
    private static let lockoutDuration: TimeInterval = 30

    /// `path` : fichier SQLite à créer ou rouvrir, ou `":memory:"` (tests).
    public init(path: String) throws {
        let db = try SQLiteDatabase(path: path)
        try LocalMigrator.migrate(db)
        try LocalSeeder.seedIfEmpty(db)
        self.db = db
    }

    var staffRepository: LocalStaffRepository { LocalStaffRepository(db: db) }
    var catalogRepository: LocalCatalogRepository { LocalCatalogRepository(db: db) }
    var floorRepository: LocalFloorRepository { LocalFloorRepository(db: db) }
    var gridRepository: LocalGridRepository { LocalGridRepository(db: db) }

    func unsupported(_ name: String = #function) -> APIError {
        .server(status: 501, message: "Non disponible en mode autonome : \(name)")
    }

    // MARK: Authentification

    public func setToken(_ token: String?) { self.token = token }
    public func setDeviceToken(_ token: String?) {}

    public func login(pin: String) async throws -> LoginResponse {
        let now = Date()
        if let until = lockedUntil, until > now { throw APIError.rateLimited(nil) }
        guard let member = try staffRepository.activeMember(pin: pin) else {
            failedLogins = failedLogins.filter { now.timeIntervalSince($0) < Self.failureWindow } + [now]
            if failedLogins.count >= Self.maxFailedLogins {
                lockedUntil = now.addingTimeInterval(Self.lockoutDuration)
                failedLogins = []
            }
            return LoginResponse(success: false, operatorId: nil, operatorName: nil, role: nil, token: nil, errorMessage: "Code PIN ou identifiants incorrects")
        }
        failedLogins = []
        lockedUntil = nil
        token = Self.tokenPrefix + member.id.uuidString
        return LoginResponse(success: true, operatorId: member.id, operatorName: member.name, role: member.role, token: token)
    }

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
