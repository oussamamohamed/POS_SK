import Foundation

/// Mode de fonctionnement de l'iPad, choisi une fois au premier lancement.
public enum AppMode: String, Sendable {
    /// Relié à un serveur de caisse (appairage, temps réel).
    case server
    /// Tout vit sur l'iPad (`LocalPosAPI`), sans serveur.
    case standalone

    public static let storageKey = "pos.appMode"

    /// Mode à utiliser au lancement. Le choix mémorisé l'emporte ; sans choix, un iPad déjà appairé (installation antérieure au mode
    /// autonome) reste en mode serveur sans rien demander ; un iPad neuf renvoie `nil` : l'application propose le choix.
    public static func resolve(stored: AppMode?, hasPairedCredentials: Bool) -> AppMode? {
        stored ?? (hasPairedCredentials ? .server : nil)
    }
}
