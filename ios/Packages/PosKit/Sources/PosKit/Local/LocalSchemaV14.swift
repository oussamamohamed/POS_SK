import Foundation

extension LocalMigrator {
    /// Ingrédients et fiches techniques, mêmes tables et colonnes que `AppDbContext` (spec 023, lot B).
    static let schemaV14 = """
    CREATE TABLE Ingredients (
        Id TEXT NOT NULL PRIMARY KEY,
        Name TEXT NOT NULL,
        Unit TEXT NOT NULL,
        UnitCostMilliCents INTEGER NOT NULL DEFAULT 0,
        IsPreparation INTEGER NOT NULL DEFAULT 0,
        YieldMilli INTEGER NULL,
        AllergenMask INTEGER NOT NULL DEFAULT 0,
        LowStockMilli INTEGER NULL,
        IsActive INTEGER NOT NULL DEFAULT 1
    );
    CREATE TABLE RecipeLines (
        Id TEXT NOT NULL PRIMARY KEY,
        OwnerKind TEXT NOT NULL,
        OwnerId TEXT NOT NULL,
        IngredientId TEXT NOT NULL,
        QuantityMilli INTEGER NOT NULL
    );
    CREATE UNIQUE INDEX IX_RecipeLines_OwnerKind_OwnerId_IngredientId ON RecipeLines (OwnerKind, OwnerId, IngredientId);
    CREATE INDEX IX_RecipeLines_IngredientId ON RecipeLines (IngredientId);
    """
}
