using System.Globalization;

namespace RestaurantPos.Api.Tests.DaySimulation;

/// <summary>
/// Génère une journée déterministe (même graine, même journée) : 3 postes, 4 serveurs, 40 à 60 opérations aléatoires précédées de quatre cas
/// imposés par la spec (total nul, remise fixe supérieure au total, règlement de 1 centime, règlement fractionné en 5 avec reste).
/// Toutes les commandes sont soldées (ou annulées) avant le premier <c>z_closure</c> ; les annulations de reçu des commandes sont toutes jouées
/// avant la Z. La journée se termine par un X puis une Z par poste, un X de la nouvelle période (qui repart de zéro) et une tentative
/// d'annulation tardive d'un reçu scellé (<c>void_receipt</c>, refusée par l'API). Juste avant le X final, onze commandes imposées
/// (<c>v0</c> à <c>v10</c>, sur les trois postes) annulent et corrigent des lignes (<see cref="LineVoidCases"/>).
/// </summary>
public static class DayScenarioGenerator
{
    public const int DefaultSeed = 20261011;

    private const long MealVoucherCapCents = 2500;
    private static readonly string[] TerminalKeys = ["A", "B", "C"];
    private static readonly string[] Waiters = ["waiter1", "waiter2", "waiter3", "waiter4"];
    private static readonly decimal[] Rates = [0m, 2.1m, 5.5m, 10m, 20m];
    private static readonly decimal[] TakeawayRates = [5.5m, 10m];
    private static readonly string[] Methods = ["Cash", "CreditCard", "MealVoucher", "GiftCard"];
    private static readonly decimal[] PercentDiscounts = [10m, 15m, 33.33m];
    private static readonly decimal[] FixedDiscountsEuros = [1m, 2.5m, 5m];

    public static DayScenario Build(int seed)
    {
        var rng = new Random(seed);
        var ops = new List<DayOperation>(MandatoryCases());
        int count = rng.Next(40, 61);
        for (int i = 0; i < count; i++)
        {
            ops.Add(RandomOperation(rng, ops.Count));
            if ((i + 1) % 12 == 0)
            {
                ops.AddRange(TerminalKeys.Select(k => new DayOperation("x_report", k)));
            }
        }

        ops.AddRange(LineVoidCases()); // après les tirages aléatoires : les opérations aléatoires (index, références, valeurs) ne bougent pas
        ops.AddRange(TerminalKeys.Select(k => new DayOperation("x_report", k)));
        ops.AddRange(TerminalKeys.Select(k => new DayOperation("z_closure", k)));
        ops.AddRange(TerminalKeys.Select(k => new DayOperation("x_report", k))); // nouvelle période : repart de zéro
        ops.AddRange(LateVoidAttempts(ops));
        return new DayScenario(seed, TerminalKeys.Select(k => new DayTerminal(k, $"Caisse {k}")).ToList(), ops);
    }

    /// <summary>Le scénario et son grand livre, tels que publiés dans <c>day_scenario.json</c> (identifiants de poste fictifs T01, T02, T03 : l'oracle ne s'en sert que comme clés).</summary>
    public static DayScenarioFile ToFile(DayScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var ids = scenario.Terminals.Select((t, i) => (t.Key, Id: $"T{i + 1:00}")).ToDictionary(x => x.Key, x => x.Id);
        var ledger = DayLedgerOracle.Compute(scenario.Operations, ids);
        var voided = scenario.Operations.Where(o => o.LineVoids is { Count: > 0 }).ToList();
        return new DayScenarioFile(
            scenario.Seed,
            scenario.Terminals,
            scenario.Operations,
            scenario.Terminals.ToDictionary(t => t.Key, t => Expected(ledger.ByTerminal[ids[t.Key]])),
            Expected(ledger.Total),
            DayLedgerOracle.ExpectedLineVoids(scenario.Operations),
            voided.ToDictionary(o => o.Ref!, o => (IReadOnlyList<long?>)DayLedgerOracle.TotalsAfterSteps(o)),
            voided.ToDictionary(o => o.Ref!, o => (IReadOnlyList<DayExpectedTicketLine>)DayLedgerOracle.TicketLines(o)),
            DayLedgerOracle.ExpectedZBlocked(scenario.Operations));
    }

    private static DayExpected Expected(TerminalLedger t) => new(
        t.TtcCents,
        t.HtCents,
        t.ReceiptCount,
        t.VatByRate.ToDictionary(kv => kv.Key.ToString(CultureInfo.InvariantCulture), kv => kv.Value),
        t.TenderByMethod.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value));

    private static List<DayOperation> MandatoryCases()
    {
        // destination = null : le client n'en choisit pas, la table reste sur place (valeur par défaut du serveur pour une table de salle).
        static DayOperation Order(string reference, string key, string waiter, string table, int covers, DayLine[] lines, DayOrderDiscount? discount, DayTender[][] payments, string? destination = "EatIn") =>
            new("table_order", key, waiter, table, covers, destination, lines, discount, 0,
                payments.Select(p => (IReadOnlyList<DayTender>)p.ToList()).ToList(), Ref: reference);

        return
        [
            // total nul : tout est offert
            Order("m0", "A", "waiter1", "1", 2, [new("Offert", 1, 1200, 10m, Comp: true)], null, [[]]), // aucun règlement : l'API refuse un règlement à 0
            // remise fixe de 10 € sur 5,00 € : total borné à zéro
            Order("m1", "B", "waiter2", "2", 1, [new("Café", 2, 250, 20m)], new("FixedAmount", 10m), [[]]),
            // règlement de 1 centime, puis le reste
            Order("m2", "C", "waiter3", "3", 2, [new("Pain", 1, 1000, 5.5m)], null, [[new("Cash", 1)], [new("CreditCard", 999)]]),
            // 10,03 € en cinq règlements dont un reste de division
            Order("m3", "A", "waiter4", "4", 4, [new("Plat", 1, 1003, 10m)], null,
                [[new("Cash", 200, 300)], [new("Cash", 200)], [new("CreditCard", 200)], [new("MealVoucher", 200)], [new("GiftCard", 203)]]),
            // table ouverte sans choisir de mode : sur place par défaut, donc TVA 10 % et non le taux à emporter (5,5 %)
            Order("m4", "B", "waiter1", "5", 2, [new("Menu midi", 1, 1500, 10m, 5.5m)], null, [[new("CreditCard", 1500)]], destination: null),
            // la même table passée explicitement à emporter : taux à emporter
            Order("m5", "C", "waiter2", "6", 2, [new("Menu midi", 1, 1500, 10m, 5.5m)], null, [[new("CreditCard", 1500)]], destination: "Takeaway"),
        ];
    }

    /// <summary>
    /// Annulations (lignes envoyées) et corrections (lignes non envoyées) : une commande par cas, tables 21 à 30 (jamais servies par les
    /// tirages), sur les trois postes. Les règlements soldent le total après les étapes acceptées ; une étape refusée ne change rien.
    /// </summary>
    private static List<DayOperation> LineVoidCases()
    {
        static DayLineVoid Sent(int line, int quantity, string? reason = "Erreur de saisie", string by = "manager", int expect = 200, string? code = null, int reAdd = 0, bool legacy = false) =>
            new("sent", line, quantity, reason, by, expect, code, reAdd, legacy);

        static DayOperation Table(string reference, string key, string waiter, string table, DayLine[] lines, DayLineVoid[] steps, DayOrderDiscount? discount = null) =>
            new("table_order", key, waiter, table, 2, "EatIn", lines, discount, Ref: reference, LineVoids: steps);

        static IReadOnlyList<IReadOnlyList<DayTender>> Pay(string method, long cents, long extra = 0) => [[new DayTender(method, cents, extra)]];

        var cases = new List<DayOperation>
        {
            // annulation complète d'une ligne envoyée
            Table("v0", "A", "waiter1", "21", [new("Entrecôte", 1, 2400, 10m), new("Vin rouge", 2, 650, 20m)], [Sent(0, 1)]),
            // deux annulations partielles d'une ligne à suppléments (R9 : prix + suppléments), la deuxième au-delà du reste (400), puis une troisième
            Table("v1", "B", "waiter2", "22", [new("Pizza", 3, 1200, 10m, ModifiersExtraCents: 150), new("Eau", 1, 350, 10m)],
                [Sent(0, 1, "Trop commandé"), Sent(0, 3, "Trop commandé", expect: 400), Sent(0, 1, "Client parti")]),
            // annulation partielle puis même produit rajouté (second bon), annulation de la ligne rajoutée : le produit est annulé sur deux bons
            Table("v2", "C", "waiter3", "23", [new("Burger", 2, 1500, 10m), new("Frites", 1, 450, 10m)],
                [Sent(0, 1, reAdd: 1), Sent(2, 1, "Plus faim")]),
            // corrections par le serveur de lignes enregistrées non envoyées (sans motif, sans trace), puis envoi
            Table("v3", "A", "waiter4", "24", [new("Salade", 2, 900, 10m), new("Soupe", 1, 700, 10m), new("Thé", 1, 300, 10m)],
                [new("draft", 0, 1, null, "waiter"), new("draft", 1, 1, null, "waiter")]),
            // remise de commande de 10 % et ligne offerte : annuler l'offert ne change pas le total, annuler une ligne payante le recalcule
            Table("v4", "B", "waiter1", "25", [new("Plat du jour", 2, 1890, 10m), new("Dessert", 1, 700, 5.5m, Comp: true), new("Café", 2, 250, 20m)],
                [Sent(1, 1, "Offert retiré"), Sent(2, 1)], new DayOrderDiscount("Percentage", 10m)),
            // vente comptoir à emporter envoyée en cuisine
            new("counter_order", "C", "waiter2", null, 0, "Takeaway", [new("Sandwich", 2, 850, 10m, 5.5m), new("Boisson", 1, 300, 5.5m)], Ref: "v5",
                LineVoids: [Sent(0, 1)]),
            // refus sans effet : serveur (403), motif absent ou blanc (400) ; puis ligne ancienne (article de bon sans OrderItemId)
            Table("v6", "A", "waiter3", "26", [new("Côte de bœuf", 1, 2800, 10m), new("Verre de vin", 2, 600, 20m)],
                [Sent(0, 1, by: "waiter", expect: 403), Sent(0, 1, null, expect: 400), Sent(0, 1, "   ", expect: 400), Sent(1, 1, "Ancienne ligne", legacy: true)]),
            // note partagée : après le premier règlement 409 order_has_receipts (numéros listés), après le dernier 409 order_not_modifiable
            Table("v7", "B", "waiter4", "27", [new("Menu", 2, 1600, 10m)],
                [new("part_paid", 0, 1, Expect: 409, Code: "order_has_receipts"), new("paid", 0, 1, Expect: 409, Code: "order_not_modifiable")])
                with { Payments = [[new DayTender("Cash", 1000)], [new DayTender("CreditCard", 2200)]] },
            // premier règlement puis annulation de son reçu (paire reçu + avoir exclue) : l'annulation de ligne passe, le reste est réglé
            Table("v8", "C", "waiter1", "28", [new("Poisson", 1, 2200, 10m), new("Vin blanc", 1, 700, 20m)],
                [new("reopened", 1, 1, "Bouteille bouchonnée")])
                with { Payments = [[new DayTender("Cash", 1000)], [new DayTender("CreditCard", 2200)]] },
            // toutes les lignes annulées : commande vide à 0 (remise fixe bornée), ni réglable (aucune ligne) ni bloquante pour la Z
            Table("v9", "A", "waiter2", "29", [new("Apéritif", 2, 450, 10m), new("Planche", 1, 1200, 10m)],
                [Sent(0, 2, "Client parti"), Sent(1, 1, "Client parti")], new DayOrderDiscount("FixedAmount", 5m)) with { Payments = [] },
            // commande table avec annulation avant les cas combinés étendus
            Table("v10", "B", "waiter3", "30", [new("Tarte", 2, 650, 5.5m), new("Café", 1, 250, 10m)], [Sent(0, 1)]),

            // --- Combinaisons étendues et cas limites (v11 à v25) ---

            // v11 : rôles (Admin 200, Kitchen 200 brouillon / 403 envoyé), quantités limites (0, -1, > reste -> 400),
            // ligne inconnue / commande inconnue / ligne déjà retirée (404), motif avec espaces, accents et guillemets
            Table("v11", "A", "waiter1", "31", [new("Entrecôte", 2, 2400, 10m), new("Soupe", 2, 700, 10m), new("Café", 1, 250, 20m)],
            [
                new("draft", 1, 1, null, "kitchen"),
                Sent(0, 1, "Test", by: "kitchen", expect: 403),
                Sent(0, 0, "Quantité nulle", expect: 400),
                Sent(0, -1, "Quantité négative", expect: 400),
                Sent(0, 5, "Au-delà", expect: 400),
                new("sent", 0, 1, "Inconnue", Expect: 404, UnknownLine: true),
                new("sent", 0, 1, "Commande inconnue", Expect: 404, UnknownOrder: true),
                Sent(0, 2, "  Plat froid — retour \"client\"  ", by: "admin"),
                Sent(0, 1, "Déjà retirée", expect: 404),
            ]),

            // v12 : commande mixte (lignes envoyées + ligne brouillon ajoutée après envoi) : serveur corrige brouillon (200, 0 JET), refusé sur envoyée (403)
            Table("v12", "B", "waiter2", "32", [new("Steak", 2, 1900, 10m), new("Frites", 1, 500, 10m)],
            [
                new("sent", 0, 1, "Changement d'avis", AddDraft: new DayLine("Dessert du jour", 3, 650, 5.5m)),
                new("sent", 2, 1, null, "waiter"),
                Sent(0, 1, "Non", by: "waiter", expect: 403),
            ]),

            // v13 : multi-bons entrelacés sur le même produit (qty 3 -> -1 -> reAdd 2 -> -1 #1 -> -1 #2 -> -1 #1 -> -1 #2 : deux bons Cancelled)
            Table("v13", "C", "waiter3", "33", [new("Brochette", 3, 1400, 10m), new("Salade verte", 1, 500, 10m)],
            [
                Sent(0, 1, "Annul 1", reAdd: 2),
                Sent(0, 1, "Annul 2"),
                Sent(2, 1, "Annul 3"),
                Sent(0, 1, "Annul 4"),
                Sent(2, 1, "Annul 5"),
            ]),

            // v14 : remise % 33.33 % (arrondi délicat) + 4 taux de TVA (0 %, 5.5 %, 10 %, 20 %) + suppléments + annulations partielle et totale
            Table("v14", "A", "waiter4", "34",
                [new("Presse", 1, 300, 0m), new("Eau minérale", 2, 450, 5.5m), new("Plat chef", 2, 1850, 10m, ModifiersExtraCents: 150), new("Champagne", 2, 1200, 20m)],
                [Sent(1, 2, "Annulé avant service"), Sent(3, 1, "Un verre en trop")],
                new DayOrderDiscount("Percentage", 33.33m)),

            // v15 : remise fixe > sous-total APRÈS annulation d'une ligne -> total borné à 0 €, soldé sans règlement ([])
            Table("v15", "B", "waiter1", "35", [new("Plat du jour", 1, 1000, 10m), new("Salade", 1, 800, 10m)],
                [Sent(0, 1, "Trop d'attente")], new DayOrderDiscount("FixedAmount", 10m)) with { Payments = [[]] },

            // v16 : annulation de la seule ligne payante laissant uniquement une ligne offerte -> total 0 €, soldé sans règlement ([])
            Table("v16", "C", "waiter2", "36", [new("Assiette", 1, 1200, 10m), new("Café gourmand", 1, 700, 5.5m, Comp: true)],
                [Sent(0, 1, "Erreur table")]) with { Payments = [[]] },

            // v17 : comptoir à emporter — correction brouillon + envoi + annulation ligne + HoldAndRecall + paiement + annulation reçu + voidLine sur Cancelled (409)
            new("counter_order", "A", "waiter3", null, 0, "Takeaway",
                [new("Wrap", 2, 850, 10m, 5.5m), new("Jus", 2, 400, 5.5m)],
                HoldAndRecall: true, Ref: "v17",
                LineVoids: [new("draft", 1, 1, null, "waiter"), Sent(0, 1, "Client change"), new("cancelled", 0, 1, "Après annulation", Expect: 409, Code: "order_not_modifiable")],
                Payments: [[new DayTender("CreditCard", 1250)]],
                ReceiptVoids: [new DayReceiptVoid(null, 0)]),

            // v18 : refus d'annulation de reçu — serveur (403), autre poste (400 .NET), annulation valide (200), double annulation (400), annulation du -VOID- (400)
            new("counter_order", "C", "waiter4", null, 0, "Takeaway",
                [new("Panini", 1, 950, 10m, 5.5m), new("Eau", 1, 250, 5.5m)],
                Ref: "v18",
                Payments: [[new DayTender("Cash", 1200, 800)]],
                ReceiptVoids:
                [
                    new DayReceiptVoid(null, 0, By: "waiter", Expect: 403),
                    new DayReceiptVoid(null, 0, FromTerminal: "B", Expect: 400),
                    new DayReceiptVoid(null, 0, By: "manager", Expect: 200),
                    new DayReceiptVoid(null, 0, By: "manager", Expect: 400),
                    new DayReceiptVoid(null, 0, By: "manager", Expect: 400, VoidSelf: true),
                ]),

            // v20 : note partagée + cycle complet du verrou order_has_receipts (1 reçu -> 2 reçus -> 1 reçu annulé/1 actif -> 2 reçus annulés -> voidLine 200 -> re-solde en 2 parts)
            Table("v20", "A", "waiter1", "37", [new("Menu dégustation", 2, 2000, 10m), new("Bouteille vin", 1, 1000, 20m)],
            [
                new("part_paid", 1, 1, Expect: 409, Code: "order_has_receipts"),
                new("part_paid_two", 1, 1, Expect: 409, Code: "order_has_receipts"),
                new("part_voided_still_has_receipt", 1, 1, Expect: 409, Code: "order_has_receipts"),
                new("all_voided_reopened", 1, 1, "Bouteille retirée"),
                new("paid", 0, 1, Expect: 409, Code: "order_not_modifiable"),
            ]) with
            {
                Payments = [[new DayTender("Cash", 1000)], [new DayTender("CreditCard", 1000)], [new DayTender("Cash", 1500, 500)], [new DayTender("CreditCard", 2500)]],
                ReceiptVoids = [new DayReceiptVoid(1, 0, Stage: 0), new DayReceiptVoid(1, 1, Stage: 1)],
            },

            // v21 : note partagée soldée (2 parts) -> annulation 1re part (Cash avec rendu) -> table reliée -> re-règlement 1re part en Carte (M2, E14, R8)
            new("table_order", "B", "waiter2", "38", 2, "EatIn", [new("Côtelette", 2, 1500, 10m)], null, 0,
                [[new DayTender("Cash", 1200, 300)], [new DayTender("CreditCard", 1800)], [new DayTender("CreditCard", 1200)]],
                Ref: "v21", ReceiptVoids: [new DayReceiptVoid(1, 0)]),

            // v22 : note partagée soldée AVEC POURBOIRE sur la 2e part -> annulation 1re part (hors pourboire) -> re-règlement 1re part (pourboire conservé)
            new("table_order", "C", "waiter3", "39", 2, "EatIn", [new("Magret", 1, 2000, 10m)], null, 300,
                [[new DayTender("Cash", 1000)], [new DayTender("CreditCard", 1300)], [new DayTender("CreditCard", 1000)]],
                Ref: "v22", TipPaymentIndex: 1, ReceiptVoids: [new DayReceiptVoid(1, 0)]),

            // v23 : note partagée soldée AVEC POURBOIRE sur la 2e part -> annulation 2e part (celle du pourboire, qui remet TipAmount à 0) -> re-règlement du solde 10 € sans pourboire
            new("table_order", "A", "waiter4", "40", 2, "EatIn", [new("Risotto", 1, 2000, 10m)], null, 250,
                [[new DayTender("Cash", 1000)], [new DayTender("CreditCard", 1250)], [new DayTender("Cash", 1000, 500)]],
                Ref: "v23", TipPaymentIndex: 1, ReceiptVoids: [new DayReceiptVoid(1, 1)]),

            // v24 : 4 moyens (Cash+rendu, Card, MealVoucher, GiftCard) + 5 taux TVA (0 %, 2.1 %, 5.5 %, 10 %, 20 %) + remise 10 % + pourboire -> annulation totale + voidLine 409
            Table("v24", "B", "waiter1", "41",
                [new("Article 0", 1, 500, 0m), new("Presse", 1, 500, 2.1m), new("Jus", 1, 500, 5.5m), new("Plat", 1, 1500, 10m), new("Vin", 1, 1000, 20m)],
                [new("cancelled", 0, 1, "Après annulation totale", Expect: 409, Code: "order_not_modifiable")],
                new DayOrderDiscount("Percentage", 10m)) with
            {
                TipCents = 400,
                Payments = [[new DayTender("Cash", 500, 500), new DayTender("CreditCard", 1500), new DayTender("MealVoucher", 500), new DayTender("GiftCard", 1500)]],
                ReceiptVoids = [new DayReceiptVoid(null, 0)],
            },

            // v25 : reçu à 0 € (100 % offert) annulé + tentative de double annulation (400)
            new("table_order", "C", "waiter2", "42", 1, "EatIn", [new("Dégustation offerte", 1, 1500, 10m, Comp: true)], null, 0,
                [[]], Ref: "v25", ReceiptVoids: [new DayReceiptVoid(null, 0), new DayReceiptVoid(null, 0, Expect: 400)]),

            // --- Lecteurs après annulation d'une part (audit des lecteurs, v26 à v28) ---

            // v26 : pourboire de la 2e part (12 €) plus grand que la 1re part annulée (10 €) -> Z refusée (reste 10 € = 30 + 12 − 32),
            // puis re-règlement mixte titre-restaurant + espèces avec rendu ; la Z de fin de journée passe
            new("table_order", "A", "waiter1", "43", 2, "EatIn", [new("Plat signature", 1, 3000, 10m)], null, 1200,
                [[new DayTender("Cash", 1000)], [new DayTender("CreditCard", 3200)], [new DayTender("MealVoucher", 600), new DayTender("Cash", 400, 600)]],
                Ref: "v26", TipPaymentIndex: 1, ReceiptVoids: [new DayReceiptVoid(1, 0, ZBlocked: true)]),

            // v27 : un autre client s'installe sur la table libérée, puis la 2e part est annulée : la commande rouverte reste sans table, bloque la Z
            // (reste 14 €) et son règlement par identifiant laisse la table à l'autre client
            new("table_order", "B", "waiter2", "44", 2, "EatIn", [new("Menu enfant", 2, 1400, 10m)], null, 0,
                [[new DayTender("CreditCard", 1400)], [new DayTender("Cash", 1400, 100)], [new DayTender("GiftCard", 1400)]],
                Ref: "v27", ReceiptVoids: [new DayReceiptVoid(1, 1, OccupyTable: true, ZBlocked: true)]),

            // v28 : deux taux, pourboire sur la 2e part, les deux parts annulées (pourboire retiré, commande toujours due) -> Z refusée (reste 25 €),
            // puis re-règlement en une fois carte + espèces sans pourboire
            new("table_order", "C", "waiter3", "45", 2, "EatIn", [new("Assiette", 1, 1800, 10m), new("Verre", 1, 700, 20m)], null, 300,
                [[new DayTender("Cash", 1000)], [new DayTender("CreditCard", 1800)], [new DayTender("CreditCard", 1500), new DayTender("Cash", 1000)]],
                Ref: "v28", TipPaymentIndex: 1, ReceiptVoids: [new DayReceiptVoid(1, 0, Stage: 0), new DayReceiptVoid(1, 1, Stage: 1, ZBlocked: true)]),

            // v19 : dernière commande comptoir (EatIn) vidée entièrement par annulations de ligne -> reste à 0 € sans reçu et ne bloque pas la Z
            new("counter_order", "B", "waiter3", null, 0, "EatIn", [new("Croque", 1, 750, 10m), new("Café", 1, 250, 20m)],
                Ref: "v19", LineVoids: [Sent(0, 1, "Abandon comptoir"), Sent(1, 1, "Abandon comptoir")], Payments: []),
        };

        // Règlement unique du total final (v4 avec un pourboire), sauf les cas qui portent déjà leurs règlements.
        return cases.Select(op => op.Payments is not null ? op : op.Ref switch
        {
            "v1" => op with { Payments = Pay("Cash", DayLedgerOracle.OrderTotalCents(op), 300) },
            "v3" => op with { Payments = Pay("MealVoucher", DayLedgerOracle.OrderTotalCents(op)) },
            "v4" => op with { TipCents = 200, Payments = Pay("CreditCard", DayLedgerOracle.OrderTotalCents(op) + 200) },
            "v5" or "v10" or "v13" => op with { Payments = Pay("Cash", DayLedgerOracle.OrderTotalCents(op)) },
            _ => op with { Payments = Pay("CreditCard", DayLedgerOracle.OrderTotalCents(op)) },
        }).ToList();
    }

    private static DayOperation RandomOperation(Random rng, int index)
    {
        string key = Pick(rng, TerminalKeys);
        string waiter = Pick(rng, Waiters);
        int kind = rng.Next(100);
        if (kind < 5)
        {
            return new DayOperation("hold_then_void", key, waiter, null, 0, "Takeaway", [new DayLine("Café", 1, 200, 10m, 5.5m)]);
        }

        bool table = kind < 55;
        var lines = MakeLines(rng);
        DayOrderDiscount? discount = rng.Next(100) switch
        {
            < 50 => null,
            < 75 => new DayOrderDiscount("Percentage", Pick(rng, PercentDiscounts)),
            _ => new DayOrderDiscount("FixedAmount", Pick(rng, FixedDiscountsEuros)),
        };
        string? destination = table ? (rng.Next(100) < 10 ? "Takeaway" : "EatIn") : (rng.Next(2) == 0 ? "Takeaway" : "EatIn");
        if (table && destination == "EatIn" && index % 2 == 0)
        {
            destination = null; // une table sur deux garde le mode par défaut du serveur (sans consommer le générateur : les autres tirages ne bougent pas)
        }

        var draft = new DayOperation(table ? "table_order" : "counter_order", key, waiter, table ? (1 + rng.Next(12)).ToString(CultureInfo.InvariantCulture) : null,
            table ? 1 + rng.Next(6) : 0, destination, lines, discount, Ref: $"o{index}");
        if (DayLedgerOracle.OrderTotalCents(draft) <= 0)
        {
            draft = draft with { Discount = null }; // une remise qui annule tout est un cas imposé, pas un cas aléatoire
        }

        long total = DayLedgerOracle.OrderTotalCents(draft);
        long tip = rng.Next(100) < 25 ? rng.Next(50, 501) : 0;
        var payments = MakePayments(rng, total, tip);
        return draft with
        {
            TipCents = tip,
            Payments = payments,
            // Annuler la part d'une note réglée en plusieurs fois rouvre la commande (elle doit encore le solde) : le scénario n'annule que les règlements uniques.
            VoidPaymentIndex = VoidablePaymentIndex(rng, payments.Count),
            Reprint = rng.Next(100) < 10,
            HoldAndRecall = !table && rng.Next(100) < 10,
        };
    }

    /// <summary>Tire toujours les mêmes valeurs du générateur ; l'annulation n'est retenue que pour un règlement unique.</summary>
    private static int? VoidablePaymentIndex(Random rng, int paymentCount)
    {
        int? index = rng.Next(100) < 15 ? rng.Next(paymentCount) : null;
        return paymentCount == 1 ? index : null;
    }

    private static List<DayLine> MakeLines(Random rng)
    {
        int count = rng.Next(1, 7);
        var lines = new List<DayLine>();
        for (int i = 0; i < count; i++)
        {
            decimal rate = Pick(rng, Rates);
            decimal? takeaway = rng.Next(100) < 30 ? Pick(rng, TakeawayRates) : null;
            long extra = rng.Next(100) < 20 ? rng.Next(50, 201) : 0;
            bool comp = i > 0 && rng.Next(100) < 8; // la première ligne est payante : le total n'est jamais nul par hasard
            lines.Add(new DayLine($"Article {i + 1}", rng.Next(1, 5), rng.Next(50, 2501), rate, takeaway, extra, comp));
        }

        return lines;
    }

    /// <summary>Découpe le total en 1 à 5 règlements d'au moins 1 centime ; le pourboire s'ajoute au dernier (seul règlement qui solde).</summary>
    private static List<IReadOnlyList<DayTender>> MakePayments(Random rng, long total, long tip)
    {
        int n = rng.Next(100) switch { < 50 => 1, < 75 => 2, < 87 => 3, < 94 => 4, _ => 5 };
        n = (int)Math.Min(n, total);
        var payments = new List<IReadOnlyList<DayTender>>();
        long remaining = total;
        for (int i = 0; i < n; i++)
        {
            long part = i == n - 1 ? remaining : rng.NextInt64(1, remaining - (n - 1 - i) + 1);
            remaining -= part;
            payments.Add(MakeTenders(rng, i == n - 1 ? part + tip : part));
        }

        return payments;
    }

    private static List<DayTender> MakeTenders(Random rng, long amount)
    {
        long first = amount >= 2 && rng.Next(100) < 25 ? rng.NextInt64(1, amount) : amount;
        var tenders = new List<DayTender> { Tender(rng, first) };
        if (first < amount)
        {
            tenders.Add(Tender(rng, amount - first));
        }

        return tenders;
    }

    private static DayTender Tender(Random rng, long amount)
    {
        string method = Pick(rng, Methods);
        if (method == "MealVoucher" && amount > MealVoucherCapCents)
        {
            method = "CreditCard"; // le comptoir refuse un titre-restaurant au-delà du plafond légal de 25 € (400 « exceeds the legal eligible cap »)
        }

        long extra = method == "Cash" && rng.Next(100) < 40 ? rng.Next(1, 2001) : 0; // rendu monnaie : espèces remises en plus
        return new DayTender(method, amount, extra);
    }

    /// <summary>Une annulation tardive par poste ayant un reçu non annulé : après la Z, l'API la refuse.</summary>
    private static IEnumerable<DayOperation> LateVoidAttempts(List<DayOperation> ops)
    {
        foreach (string key in TerminalKeys)
        {
            var target = ops.FirstOrDefault(o => o.Terminal == key && o.Ref is not null && o.VoidPaymentIndex is null && o.Payments is { Count: > 0 }
                && DayLedgerOracle.OrderTotalCents(o) > 0 && !DayLedgerOracle.IsReopened(o));
            if (target is not null)
            {
                yield return new DayOperation("void_receipt", key, VoidPaymentIndex: 0, Ref: target.Ref);
            }
        }
    }

    private static T Pick<T>(Random rng, T[] values) => values[rng.Next(values.Length)];
}
