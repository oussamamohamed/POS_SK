namespace RestaurantPos.Infrastructure.Persistence;

/// <summary>Contraintes SQL de la caisse que le modèle EF n'exprime pas : sessions, mouvements et comptages en ajout seulement, une seule session sans comptage par poste. Rejouées par <c>Program.cs</c> et par les tests.</summary>
public static class CashSchema
{
    public static readonly string[] Statements =
    [
        "CREATE TRIGGER IF NOT EXISTS TR_CashSessions_NoUpdate BEFORE UPDATE ON CashSessions BEGIN SELECT RAISE(ABORT, 'cash_append_only'); END;",
        "CREATE TRIGGER IF NOT EXISTS TR_CashSessions_NoDelete BEFORE DELETE ON CashSessions BEGIN SELECT RAISE(ABORT, 'cash_append_only'); END;",
        "CREATE TRIGGER IF NOT EXISTS TR_CashMovements_NoUpdate BEFORE UPDATE ON CashMovements BEGIN SELECT RAISE(ABORT, 'cash_append_only'); END;",
        "CREATE TRIGGER IF NOT EXISTS TR_CashMovements_NoDelete BEFORE DELETE ON CashMovements BEGIN SELECT RAISE(ABORT, 'cash_append_only'); END;",
        "CREATE TRIGGER IF NOT EXISTS TR_CashCounts_NoUpdate BEFORE UPDATE ON CashCounts BEGIN SELECT RAISE(ABORT, 'cash_append_only'); END;",
        "CREATE TRIGGER IF NOT EXISTS TR_CashCounts_NoDelete BEFORE DELETE ON CashCounts BEGIN SELECT RAISE(ABORT, 'cash_append_only'); END;",
        // Index partiel impossible entre deux tables : le déclencheur refuse une 2e session tant que la précédente du poste n'est pas comptée.
        "CREATE TRIGGER IF NOT EXISTS TR_CashSessions_OneOpen BEFORE INSERT ON CashSessions WHEN EXISTS (SELECT 1 FROM CashSessions s WHERE s.TerminalId = NEW.TerminalId AND NOT EXISTS (SELECT 1 FROM CashCounts c WHERE c.SessionId = s.Id)) BEGIN SELECT RAISE(ABORT, 'cash_session_already_open'); END;",
        "INSERT OR IGNORE INTO AlertRules (Type, Threshold, Enabled, UpdatedAtUtc) VALUES ('cash_variance', 1000, 0, '2026-01-01T00:00:00.000Z'), ('voids_count', 3, 0, '2026-01-01T00:00:00.000Z'), ('discount_percent', 30, 0, '2026-01-01T00:00:00.000Z'), ('comps_total', 5000, 0, '2026-01-01T00:00:00.000Z');",
    ];
}
