import Foundation

extension LocalMigrator {
    /// Mouvements de stock (ajout seulement), ajustements de modificateurs et réglages du stock, mêmes tables et colonnes que
    /// `AppDbContext` / `StockSchema` du .NET (spec 023, lot C).
    static let schemaV15 = """
    CREATE TABLE StockMovements (
        Id TEXT NOT NULL PRIMARY KEY,
        IngredientId TEXT NOT NULL,
        Kind TEXT NOT NULL,
        QuantityMilli INTEGER NOT NULL,
        UnitCostMilliCents INTEGER NULL,
        OrderId TEXT NULL,
        OperatorId TEXT NULL,
        Note TEXT NULL,
        CreatedAtUtc TEXT NOT NULL
    );
    CREATE INDEX IX_StockMovements_IngredientId ON StockMovements (IngredientId);
    CREATE INDEX IX_StockMovements_OrderId ON StockMovements (OrderId);
    CREATE UNIQUE INDEX UX_StockMovements_OrderSale ON StockMovements (OrderId, IngredientId, Kind) WHERE Kind IN ('sale', 'saleReversal');
    CREATE TRIGGER TR_StockMovements_NoUpdate BEFORE UPDATE ON StockMovements BEGIN SELECT RAISE(ABORT, 'stock_movements_append_only'); END;
    CREATE TRIGGER TR_StockMovements_NoDelete BEFORE DELETE ON StockMovements BEGIN SELECT RAISE(ABORT, 'stock_movements_append_only'); END;
    CREATE VIEW IngredientStock AS SELECT IngredientId, SUM(QuantityMilli) AS StockMilli FROM StockMovements GROUP BY IngredientId;
    CREATE TABLE ModifierOptionConsumptions (
        Id TEXT NOT NULL PRIMARY KEY,
        OptionId TEXT NOT NULL,
        IngredientId TEXT NOT NULL,
        DeltaQuantityMilli INTEGER NOT NULL
    );
    CREATE UNIQUE INDEX IX_ModifierOptionConsumptions_OptionId_IngredientId ON ModifierOptionConsumptions (OptionId, IngredientId);
    ALTER TABLE RestaurantSettings ADD COLUMN AutoUnavailableOnStockOut INTEGER NOT NULL DEFAULT 0;
    ALTER TABLE RestaurantSettings ADD COLUMN PrintAllergensOnKitchenTicket INTEGER NOT NULL DEFAULT 0;
    ALTER TABLE RestaurantSettings ADD COLUMN AllergenDisclaimer TEXT NULL;
    """
}
