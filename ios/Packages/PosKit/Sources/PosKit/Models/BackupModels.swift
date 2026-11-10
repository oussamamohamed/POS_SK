import Foundation

/// Sauvegarde de la base du serveur .NET (`GET /api/backup/status`). Le mode autonome a son propre `BackupStatus`.
public struct ServerBackupStatus: Codable, Hashable, Sendable {
    public var configured: Bool
    public var folder: String
    public var lastSuccessUtc: Date?
    public var lastFileName: String?
    public var lastSizeBytes: Int64?
    public var lastFailureUtc: Date?
    public var lastError: String?
    public var copies: Int
    public var overdue: Bool

    public init(configured: Bool, folder: String, lastSuccessUtc: Date? = nil, lastFileName: String? = nil, lastSizeBytes: Int64? = nil,
                lastFailureUtc: Date? = nil, lastError: String? = nil, copies: Int = 0, overdue: Bool = false) {
        self.configured = configured
        self.folder = folder
        self.lastSuccessUtc = lastSuccessUtc
        self.lastFileName = lastFileName
        self.lastSizeBytes = lastSizeBytes
        self.lastFailureUtc = lastFailureUtc
        self.lastError = lastError
        self.copies = copies
        self.overdue = overdue
    }
}

/// Réponse de `POST /api/backup/run`.
public struct ServerBackupResult: Codable, Hashable, Sendable {
    public var fileName: String
    public var sizeBytes: Int64

    public init(fileName: String, sizeBytes: Int64) {
        self.fileName = fileName
        self.sizeBytes = sizeBytes
    }
}
