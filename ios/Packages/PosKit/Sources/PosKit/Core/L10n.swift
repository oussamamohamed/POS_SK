import Foundation

/// Textes affichés par PosKit (notifications, erreurs), par clé sémantique ; repli anglais géré par le bundle.
enum L10n {
    static func string(_ key: String) -> String {
        String(localized: String.LocalizationValue(key), bundle: .module)
    }
}
