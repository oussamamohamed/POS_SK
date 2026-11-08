namespace RestaurantPos.Infrastructure.Persistence;

/// <summary>Contraintes SQL du stock que le modèle EF ne sait pas exprimer (idempotence de la vente, mouvements en ajout seulement, vue de stock). Rejouées par <c>Program.cs</c> et par les tests.</summary>
public static class StockSchema
{
    public static readonly string[] Statements =
    [
        "CREATE UNIQUE INDEX IF NOT EXISTS UX_StockMovements_OrderSale ON StockMovements (OrderId, IngredientId, Kind) WHERE Kind IN ('sale', 'saleReversal');",
        "CREATE TRIGGER IF NOT EXISTS TR_StockMovements_NoUpdate BEFORE UPDATE ON StockMovements BEGIN SELECT RAISE(ABORT, 'stock_movements_append_only'); END;",
        "CREATE TRIGGER IF NOT EXISTS TR_StockMovements_NoDelete BEFORE DELETE ON StockMovements BEGIN SELECT RAISE(ABORT, 'stock_movements_append_only'); END;",
        "CREATE VIEW IF NOT EXISTS IngredientStock AS SELECT IngredientId, SUM(QuantityMilli) AS StockMilli FROM StockMovements GROUP BY IngredientId;",
    ];
}
