import Foundation

/// Emplacement du fichier de la base locale.
public enum LocalDatabaseLocation {
    /// `…/Application Support/RestaurantPOS/pos-local.sqlite`. Sur iOS, le dossier est créé avec la protection
    /// `completeUntilFirstUserAuthentication` : lisible après le premier déverrouillage de l'iPad, y compris écran verrouillé
    /// (l'app doit pouvoir encaisser en arrière-plan). `base` remplace `Application Support` (tests).
    public static func defaultURL(base: URL? = nil) throws -> URL {
        let root = try base ?? FileManager.default.url(for: .applicationSupportDirectory, in: .userDomainMask, appropriateFor: nil, create: true)
        let directory = root.appendingPathComponent("RestaurantPOS", isDirectory: true)
        #if os(iOS)
        let attributes: [FileAttributeKey: Any]? = [.protectionKey: FileProtectionType.completeUntilFirstUserAuthentication]
        #else
        let attributes: [FileAttributeKey: Any]? = nil
        #endif
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true, attributes: attributes)
        return directory.appendingPathComponent("pos-local.sqlite")
    }
}
