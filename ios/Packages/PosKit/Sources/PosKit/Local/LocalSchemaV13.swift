import Foundation

extension LocalMigrator {
    /// Allergènes et rupture automatique des produits, miroir des colonnes de `Products` du .NET (spec 023).
    /// Les colonnes de `RestaurantSettings` (ticket de cuisine) viendront avec leur pendant .NET (T013/T025).
    static let schemaV13 = """
    ALTER TABLE Products ADD COLUMN AllergenMask INTEGER NOT NULL DEFAULT 0;
    ALTER TABLE Products ADD COLUMN AllergensDeclared INTEGER NOT NULL DEFAULT 0;
    ALTER TABLE Products ADD COLUMN AllergensOverridden INTEGER NOT NULL DEFAULT 0;
    ALTER TABLE Products ADD COLUMN AutoUnavailable INTEGER NOT NULL DEFAULT 0;
    """
}
