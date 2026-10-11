import Foundation

extension LocalMigrator {
    /// `FiscalReceipts.HtBreakdownJson` : HT par taux en centimes (`{"10":909,"5.5":948}`), même colonne que `AppDbContext`.
    /// Colonne NON signée (hors hash, hors JSON signé), posée à l'INSERT uniquement ; les triggers d'immuabilité de la v5 la couvrent
    /// (ils portent sur toute la ligne). Reçus antérieurs : NULL.
    static let schemaV6 = """
    ALTER TABLE FiscalReceipts ADD COLUMN HtBreakdownJson TEXT;
    """
}
