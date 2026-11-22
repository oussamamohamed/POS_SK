// Cartographie de l'API réelle (relevée dans le code, tâche 5) ; c'est elle qui fait foi, pas le plan.
//
// Authentification
// - POST /api/auth/login {pin} -> 200 {success, operatorId, operatorName, role, token}. Comptes seedés (build DEBUG, y compris en Testing) :
//   1234 FloorManager, 2468 Waiter, 5678 KitchenStaff, 9999 Admin. Autres serveurs : POST /api/staff {name, role:"Waiter", pin 4-6 chiffres,
//   unique} avec un jeton responsable (policy RequireManagerOrAdmin) -> 200 {id, name, role, isActive}.
// - TestAuthHandler (Testing) accepte un « Authorization: Bearer <jwt> » ordinaire : il lit les claims du JWT sans le valider. Le jeton est
//   posé par requête (HttpRequestMessage), ce qui permet plusieurs opérateurs sur le même poste.
// Postes appairés
// - DeviceTestHelper.PairAsync pose X-Device-Token sur le HttpClient (un client par poste) ; TerminalId attribué T01, T02… dans l'ordre.
//   /api/checkout/pay, /api/checkout/void/{id} et /api/orders/counter/checkout prennent le terminal du poste, jamais celui du corps.
// Commande à table
// - POST /api/tables/{t}/open OpenTableRequest(waiterName, coversCount, operatorId) -> 200 DiningTableDto {activeOrderId…} ; la table est créée
//   si elle n'existe pas ; destination EatIn (sauf « Comptoir »). Changer : POST /api/orders/{id}/destination {destination} (enum en entier).
// - POST /api/tables/{t}/items AddOrderItemsRequest(items: OrderItemInputDto, prix en euros) -> 200 ActiveTableOrderDto {orderId, lines[{lineId,
//   productId…}]}. Une ligne non envoyée de même produit/prix/suppléments est fusionnée : ProductId = Guid.NewGuid() par ligne l'évite.
// - POST /api/orders/{id}/discount ApplyOrderDiscountRequest(type, value, reason obligatoire, operatorId) -> 200 {success, totalTtc} ;
//   FixedAmount en euros. POST /api/orders/{id}/items/{lineId}/comp CompOrderItemRequest(reason, operatorId) -> 200.
// - POST /api/tables/{t}/dispatch -> 200 {success}.
// - POST /api/checkout/pay PaymentSettlementRequest(orderId, tableNumber, operatorId, tenders[{method, amount, tendered, changeGiven}],
//   terminalId ignoré, requestReceiptPrint, tipAmount) -> 200 {receiptNumber "T01-000001", totalPaid, changeGiven, remainingBalance,
//   fiscalSignature, fiscalTimestampUtc, printQueued}. Pas d'identifiant de reçu : relu dans AppDbContext.FiscalReceipts par son numéro
//   (aucun endpoint ne liste les reçus). Le pourboire n'est accepté que sur le règlement qui solde (400 sinon) ; il s'ajoute à
//   Order.TipAmount et les montants des tenders l'incluent (tender 22 € pour 20 € + 2 € de pourboire).
// - Montant fiscal du reçu (CheckoutPaymentService) : Σ amount des tenders, borné à [0 ; TTC commande] ; si la commande porte un pourboire,
//   borné au TTC restant après les reçus non annulés. Les PaymentTender gardent le montant complet (pourboire compris) ; le rendu
//   (tendered − amount en espèces) n'entre pas dans les totaux.
// - POST /api/checkout/void/{receiptId} VoidReceiptRequest(terminalId, operatorId ≠ Guid.Empty) avec jeton responsable + poste émetteur ->
//   200 {receiptNumber "T01-VOID-000002", refundedAmount…} ; reçu négatif miroir (TTC, HT, TVA, tenders) ; la commande passe Cancelled ;
//   409 {code:"void_after_closure"} après la Z du terminal ; 400 si autre poste ou déjà annulé.
// - POST /api/checkout/receipts/{id|numéro}/reprint (jeton responsable, sans poste) -> 200 {printQueued, duplicateNumber} ; sans effet fiscal.
// Comptoir
// - POST /api/orders/counter/direct DirectCounterOpenRequest(terminalId, destination) -> 200 ActiveTableOrderDto (table « Comptoir »,
//   commande réutilisée si déjà active). Lignes : POST /api/tables/Comptoir/items. Remise/offert : mêmes routes /api/orders/{id}/….
// - POST /api/orders/counter/checkout CounterCheckoutRequest(orderId, terminalId ignoré, destination, buzzer, scheduledAt, tipAmount,
//   requestFiscalReceiptPrint, mealVoucherPolicy, tenders[{method, amount, tendered, facialValue}]) -> 200 CounterCheckoutResponse
//   {orderId, pickupNumber, totalPaid, changeGiven, remainingBalance, receiptNumber…}. Le pourboire écrase Order.TipAmount (pas de
//   contrôle « règlement final ») : le harnais ne l'envoie qu'avec le dernier règlement.
// - POST /api/orders/counter/hold HoldCounterOrderRequest(orderId, terminalId du corps, défaut POS_MAIN_TERM, staffId, customerLabel) ->
//   200 HeldOrderDto {holdId…} ; libère la table Comptoir. POST /api/orders/counter/held/{holdId}/recall -> 200 ActiveTableOrderDto.
//   POST /api/orders/counter/held/{holdId}/void VoidHeldOrderRequest(supervisorPin, voidReason, terminalId) -> 200 ; 403 si PIN non
//   responsable. Une attente non rappelée/non annulée bloque la Z de son terminal (exception, pas de 409).
// Rapports (policy RequireManagerOrAdmin, sans poste)
// - GET /api/fiscal/x-report?terminalId=T01 -> 200 {terminalId, totalSalesTtc, totalSalesHt (euros), receiptCount (annulations comprises),
//   vatBreakdown {"10": euros, "5.5": …} (clé = taux décimal invariant), paymentTotals {"Cash": euros, "CreditCard": …} (nom de l'enum,
//   Σ PaymentTender.Amount, pourboire compris, annulations négatives), perpetualGrandTotal, periodStartUtc, periodEndUtc}.
// - POST /api/fiscal/z-closure ZClosureRequest(terminalId, managerId ≠ Empty, managerName) -> 200 {closureSequence, mêmes champs que le X,
//   signatureHash, closedAtUtc, printQueued} ; 409 {code:"open_orders", openOrders} si une commande est ouverte sur n'importe quel poste.
// - GET /api/fiscal/latest-closure?terminalId=T01 -> 200 mêmes champs (receiptCount recompté sur la période) ; 404 sans Z.
// - POST /api/fiscal/verify -> 200 FiscalVerificationResultDto. GET /api/fiscal/fec?from&to&siren&company -> fichier texte (+ JET
//   FEC_EXPORTED). GET /api/fiscal/journal?from&to&eventType&page&pageSize -> {items[{id, eventType, payloadJson, terminalId, operatorId,
//   chainSequence, previousHash, hashSignature}], totalCount, page, pageSize}.
// Sérialisation : pas de JsonStringEnumConverter côté serveur : les enums des réponses sont des entiers, sauf les clés de paymentTotals.
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Api.Tests.DaySimulation;

public sealed record DayReceipt(string Number, Guid Id);

/// <summary>Résultat d'un rejeu : réponses brutes des X et Z par poste (clé du scénario), reçus émis par poste.</summary>
public sealed record DayRunResult(
    IReadOnlyDictionary<string, string> TerminalIds,
    IReadOnlyDictionary<string, List<JsonElement>> XReports,
    IReadOnlyDictionary<string, List<JsonElement>> ZClosures,
    IReadOnlyDictionary<string, List<JsonElement>> LatestClosures,
    IReadOnlyDictionary<string, List<DayReceipt>> Receipts,
    IReadOnlyDictionary<string, List<int>> XReportOpIndexes,
    IReadOnlyDictionary<string, List<int>> ZClosureOpIndexes,
    IReadOnlyList<DayVoidAttempt> VoidAttempts,
    IReadOnlyDictionary<string, HttpClient> Clients,
    DayManager Manager,
    IReadOnlyList<DayLineVoidAttempt> LineVoidAttempts,
    IReadOnlyDictionary<string, List<Guid>> LineIds,
    DayManager Admin,
    IReadOnlyList<DayReceiptVoidAttempt> ReceiptVoidAttempts);

/// <summary>Le responsable connecté du rejeu (PIN 1234) : permet aux tests de rappeler X, Z et les rapports après coup.</summary>
public sealed record DayManager(Guid Id, string Name, string Token);

/// <summary>Résultat d'une opération <c>void_receipt</c> : statut HTTP et code d'erreur renvoyés par l'API (null si la réponse n'en a pas).</summary>
public sealed record DayVoidAttempt(int OpIndex, string Key, string? Ref, int Status, string? Code);

/// <summary>Résultat d'une étape <see cref="DayReceiptVoid"/> : statut, code, X avant et après (vérifié inchangé sur un refus).</summary>
public sealed record DayReceiptVoidAttempt(
    int OpIndex, string Key, string Ref, int Step, int Status, string? Code, JsonElement XBefore, JsonElement XAfter);

/// <summary>
/// Résultat d'une étape <see cref="DayLineVoid"/> : statut, code et message de l'API, total TTC renvoyé (centimes, nul sur un refus),
/// numéros des reçus actifs de la commande à cet instant, et le X du poste juste avant et juste après l'étape.
/// </summary>
public sealed record DayLineVoidAttempt(
    int OpIndex, string Key, string Ref, int Step, int Status, string? Code, string? Message, long? TotalCents,
    IReadOnlyList<string> ActiveReceipts, JsonElement XBefore, JsonElement XAfter);

/// <summary>Rejoue un <see cref="DayScenario"/> par l'API HTTP réelle (jamais par la base, sauf pour relire l'identifiant d'un reçu).</summary>
public static class DayRunner
{
    public const string ManagerPin = "1234";
    public const string AdminPin = "9999";
    public const string KitchenPin = "5678";

    public static async Task<DayRunResult> RunAsync(PosApiApplicationFactory factory, DayScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(scenario);
        var run = new Run(factory);
        foreach (var terminal in scenario.Terminals)
        {
            var client = factory.CreateClient();
            var paired = await DeviceTestHelper.PairAsync(factory, client, terminal.Name);
            run.Clients[terminal.Key] = client;
            run.TerminalIds[terminal.Key] = paired.TerminalId;
            run.XReports[terminal.Key] = [];
            run.XReportOpIndexes[terminal.Key] = [];
            run.ZClosureOpIndexes[terminal.Key] = [];
            run.ZClosures[terminal.Key] = [];
            run.LatestClosures[terminal.Key] = [];
            run.Receipts[terminal.Key] = [];
        }

        var firstClient = run.Clients.Values.First();
        run.Manager = await LoginAsync(firstClient, ManagerPin);
        run.Admin = await LoginAsync(firstClient, AdminPin);
        run.Kitchen = await LoginAsync(firstClient, KitchenPin);
        if (scenario.Operations.Any(o => o.LineVoids is { Count: > 0 }))
        {
            // Une imprimante du poste chaud (station par défaut des lignes) : chaque annulation doit y poser son bon d'annulation.
            await SendAsync(firstClient, HttpMethod.Post, "/api/printers",
                new PrinterRegistrationRequest("Cuisine chaude", "10.0.0.9", 9100, 80, false, ["HOT_KITCHEN"], true), run.Manager.Token);
        }

        for (int i = 0; i < scenario.Operations.Count; i++)
        {
            var op = scenario.Operations[i];
            try
            {
                await run.ExecuteAsync(op, i);
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or KeyNotFoundException)
            {
                throw new InvalidOperationException($"[{Context(scenario.Seed, i, op.Terminal)}] {op.Type} : {ex.Message}", ex);
            }
        }

        return new DayRunResult(run.TerminalIds, run.XReports, run.ZClosures, run.LatestClosures, run.Receipts,
            run.XReportOpIndexes, run.ZClosureOpIndexes, run.VoidAttempts, run.Clients,
            new DayManager(run.Manager.Id, run.Manager.Name, run.Manager.Token), run.LineVoidAttempts, run.LineIds,
            new DayManager(run.Admin.Id, run.Admin.Name, run.Admin.Token), run.ReceiptVoidAttempts);
    }

    /// <summary>Contexte d'échec commun à tous les messages : graine, index d'opération et terminal.</summary>
    public static string Context(int seed, int opIndex, string? terminal) => $"graine {seed}, opération #{opIndex}, terminal {terminal ?? "-"}";

    private static async Task<Operator> LoginAsync(HttpClient client, string pin)
    {
        var body = await SendAsync(client, HttpMethod.Post, "/api/auth/login", new PinLoginRequest(pin), token: null);
        return new Operator(body.GetProperty("operatorId").GetGuid(), body.GetProperty("operatorName").GetString()!, body.GetProperty("token").GetString()!);
    }

    public static async Task<JsonElement> SendAsync(HttpClient client, HttpMethod method, string url, object? body, string? token)
    {
        var (status, response) = await TrySendAsync(client, method, url, body, token);
        if (status is < 200 or > 299)
        {
            throw new InvalidOperationException($"{method} {url} -> {status} {response}");
        }

        return response;
    }

    /// <summary>Envoie la requête et rend statut + corps JSON (un corps vide ou non JSON devient <c>null</c>), sans lever d'exception sur un refus.</summary>
    public static async Task<(int Status, JsonElement Body)> TrySendAsync(HttpClient client, HttpMethod method, string url, object? body, string? token)
    {
        ArgumentNullException.ThrowIfNull(client);
        using var request = new HttpRequestMessage(method, url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType());
        }

        using var response = await client.SendAsync(request);
        string text = await response.Content.ReadAsStringAsync();
        JsonElement parsed;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "null" : text);
            parsed = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            using var doc = JsonDocument.Parse("null");
            parsed = doc.RootElement.Clone();
        }

        return ((int)response.StatusCode, parsed);
    }

    private static decimal Euros(long cents) => cents / 100m;

    private sealed record Operator(Guid Id, string Name, string Token);

    /// <summary>Une ligne du scénario et sa ligne côté serveur.</summary>
    private sealed record DayServerLine(DayLine Line, Guid ProductId, Guid LineId);

    /// <summary>État d'une commande dont le scénario annule ou corrige des lignes : lignes (rajouts compris), reçus émis et reçus annulés.</summary>
    private sealed record LineVoidContext(DayOperation Op, int OpIndex, string Key, HttpClient Client, string Table, Guid OrderId, Operator Waiter, List<DayServerLine> Lines)
    {
        public List<DayReceipt> Receipts { get; } = [];
        public List<string> VoidedReceipts { get; } = [];
        public Dictionary<int, DayReceipt> VoidReceiptsByPayment { get; } = [];
    }

    private sealed class Run(PosApiApplicationFactory factory)
    {
        private readonly Dictionary<string, Operator> _waiters = [];
        private readonly Dictionary<string, List<DayReceipt>> _receiptsByRef = [];

        public Dictionary<string, HttpClient> Clients { get; } = [];
        public Dictionary<string, string> TerminalIds { get; } = [];
        public Dictionary<string, List<JsonElement>> XReports { get; } = [];
        public Dictionary<string, List<JsonElement>> ZClosures { get; } = [];
        public Dictionary<string, List<JsonElement>> LatestClosures { get; } = [];
        public Dictionary<string, List<DayReceipt>> Receipts { get; } = [];
        public Dictionary<string, List<int>> XReportOpIndexes { get; } = [];
        public Dictionary<string, List<int>> ZClosureOpIndexes { get; } = [];
        public List<DayVoidAttempt> VoidAttempts { get; } = [];
        public List<DayLineVoidAttempt> LineVoidAttempts { get; } = [];
        public List<DayReceiptVoidAttempt> ReceiptVoidAttempts { get; } = [];
        public Dictionary<string, List<Guid>> LineIds { get; } = [];
        public Operator Manager { get; set; } = null!;
        public Operator Admin { get; set; } = null!;
        public Operator Kitchen { get; set; } = null!;

        public async Task ExecuteAsync(DayOperation op, int index)
        {
            var key = op.Terminal ?? throw new InvalidOperationException($"{op.Type} : poste manquant");
            var client = Clients[key];
            var terminalId = TerminalIds[key];
            switch (op.Type)
            {
                case "table_order":
                    await TableOrderAsync(op, key, client, index);
                    break;
                case "counter_order":
                    await CounterOrderAsync(op, key, client, terminalId, index);
                    break;
                case "hold_then_void":
                    await HoldThenVoidAsync(op, client, terminalId);
                    break;
                case "void_receipt":
                    await VoidReceiptAsync(op, key, client, terminalId, index);
                    break;
                case "x_report":
                    XReportOpIndexes[key].Add(index);
                    XReports[key].Add(await SendAsync(client, HttpMethod.Get, $"/api/fiscal/x-report?terminalId={terminalId}", null, Manager.Token));
                    break;
                case "z_closure":
                    ZClosureOpIndexes[key].Add(index);
                    ZClosures[key].Add(await SendAsync(client, HttpMethod.Post, "/api/fiscal/z-closure",
                        new ZClosureRequest(terminalId, Manager.Id, Manager.Name), Manager.Token));
                    LatestClosures[key].Add(await SendAsync(client, HttpMethod.Get, $"/api/fiscal/latest-closure?terminalId={terminalId}", null, Manager.Token));
                    break;
                default:
                    throw new NotSupportedException($"Opération inconnue : {op.Type}");
            }
        }

        private async Task TableOrderAsync(DayOperation op, string key, HttpClient client, int index)
        {
            var waiter = await WaiterAsync(client, op.Waiter!);
            var table = op.Table ?? throw new InvalidOperationException("table_order : table manquante");
            var opened = await SendAsync(client, HttpMethod.Post, $"/api/tables/{table}/open",
                new OpenTableRequest(waiter.Name, op.Covers, waiter.Id), waiter.Token);
            var orderId = opened.GetProperty("activeOrderId").GetGuid();
            if (op.Destination is null)
            {
                // Sans choix du client, une table de salle est servie sur place (c'est le comptoir seul qui est à emporter par défaut).
                var fresh = await SendAsync(client, HttpMethod.Get, $"/api/tables/{table}/order", null, waiter.Token);
                if (fresh.GetProperty("destination").GetInt32() != (int)OrderDestination.EatIn)
                {
                    throw new InvalidOperationException($"table {table} ouverte sans mode : la commande devrait être sur place");
                }
            }
            else
            {
                await SendAsync(client, HttpMethod.Post, $"/api/orders/{orderId}/destination",
                    new SwitchDestinationRequest(Enum.Parse<OrderDestination>(op.Destination)), waiter.Token);
            }

            var lines = await AddLinesAsync(op, client, table, orderId, waiter);
            var voids = new LineVoidContext(op, index, key, client, table, orderId, waiter, lines);
            await StepsAsync(voids, "draft");
            await SendAsync(client, HttpMethod.Post, $"/api/tables/{table}/dispatch", null, waiter.Token);
            await StepsAsync(voids, "sent");

            var payments = op.Payments ?? [];
            int tipIndex = op.TipPaymentIndex ?? (payments.Count - 1);
            for (int p = 0; p < payments.Count; p++)
            {
                var paid = await SendAsync(client, HttpMethod.Post, "/api/checkout/pay", new PaymentSettlementRequest(
                    orderId, table, waiter.Id,
                    payments[p].Select(t => new TenderItemRequest(Enum.Parse<PaymentMethod>(t.Method), Euros(t.AmountCents),
                        Euros(t.AmountCents + t.ExtraTenderedCents), Euros(t.ExtraTenderedCents))).ToList(),
                    TipAmount: p == tipIndex ? Euros(op.TipCents) : 0m), waiter.Token);
                voids.Receipts.Add(await ReceiptAsync(paid));
                await AfterPaymentAsync(voids, p);
            }

            await StepsAsync(voids, "paid");
            await AfterPaymentsAsync(voids);
            await StepsAsync(voids, "cancelled");
        }

        private async Task CounterOrderAsync(DayOperation op, string key, HttpClient client, string terminalId, int index)
        {
            var waiter = await WaiterAsync(client, op.Waiter!);
            var destination = Enum.Parse<OrderDestination>(op.Destination ?? nameof(OrderDestination.Takeaway));
            var (orderId, lines) = await OpenCounterAsync(op, client, terminalId, destination, waiter);
            var voids = new LineVoidContext(op, index, key, client, "Comptoir", orderId, waiter, lines);
            await StepsAsync(voids, "draft");
            if (op.LineVoids?.Any(v => v.Phase != "draft") == true)
            {
                // Une vente comptoir n'est envoyée en cuisine que si le scénario y annule une ligne envoyée.
                await SendAsync(client, HttpMethod.Post, "/api/tables/Comptoir/dispatch", null, waiter.Token);
                await StepsAsync(voids, "sent");
            }

            if (op.HoldAndRecall)
            {
                var held = await SendAsync(client, HttpMethod.Post, "/api/orders/counter/hold",
                    new HoldCounterOrderRequest(orderId, terminalId, waiter.Id, "Client"), waiter.Token);
                await SendAsync(client, HttpMethod.Post, $"/api/orders/counter/held/{held.GetProperty("holdId").GetGuid()}/recall", null, waiter.Token);
            }

            var payments = op.Payments ?? [];
            int tipIndex = op.TipPaymentIndex ?? (payments.Count - 1);
            for (int p = 0; p < payments.Count; p++)
            {
                var paid = await SendAsync(client, HttpMethod.Post, "/api/orders/counter/checkout", new CounterCheckoutRequest(
                    orderId, terminalId, destination, null, null, p == tipIndex ? Euros(op.TipCents) : 0m, false, null,
                    payments[p].Select(t => new CounterPaymentTender(Enum.Parse<PaymentMethod>(t.Method), Euros(t.AmountCents),
                        Euros(t.AmountCents + t.ExtraTenderedCents))).ToList()), waiter.Token);
                voids.Receipts.Add(await ReceiptAsync(paid));
                await AfterPaymentAsync(voids, p);
            }

            await StepsAsync(voids, "paid");
            await AfterPaymentsAsync(voids);
            await StepsAsync(voids, "cancelled");
        }

        private async Task HoldThenVoidAsync(DayOperation op, HttpClient client, string terminalId)
        {
            var waiter = await WaiterAsync(client, op.Waiter!);
            var destination = Enum.Parse<OrderDestination>(op.Destination ?? nameof(OrderDestination.Takeaway));
            var (orderId, _) = await OpenCounterAsync(op, client, terminalId, destination, waiter);
            var held = await SendAsync(client, HttpMethod.Post, "/api/orders/counter/hold",
                new HoldCounterOrderRequest(orderId, terminalId, waiter.Id, "Client"), waiter.Token);
            await SendAsync(client, HttpMethod.Post, $"/api/orders/counter/held/{held.GetProperty("holdId").GetGuid()}/void",
                new VoidHeldOrderRequest(ManagerPin, "Client parti", terminalId), waiter.Token);
        }

        private async Task<(Guid OrderId, List<DayServerLine> Lines)> OpenCounterAsync(DayOperation op, HttpClient client, string terminalId, OrderDestination destination, Operator waiter)
        {
            var opened = await SendAsync(client, HttpMethod.Post, "/api/orders/counter/direct",
                new DirectCounterOpenRequest(terminalId, destination), waiter.Token);
            var orderId = opened.GetProperty("orderId").GetGuid();
            return (orderId, await AddLinesAsync(op, client, "Comptoir", orderId, waiter));
        }

        /// <summary>Ajoute les lignes (remise et offerts compris) ; rend, pour une commande qui annule ou corrige des lignes, la ligne serveur de chacune.</summary>
        private async Task<List<DayServerLine>> AddLinesAsync(DayOperation op, HttpClient client, string table, Guid orderId, Operator waiter)
        {
            var lines = (op.Lines ?? []).Select(l => (Line: l, ProductId: Guid.NewGuid())).ToList();
            await SendAsync(client, HttpMethod.Post, $"/api/tables/{table}/items", new AddOrderItemsRequest(lines.Select(x =>
                new OrderItemInputDto(x.ProductId, x.Line.Name, x.Line.Quantity, Euros(x.Line.UnitPriceCents), x.Line.TaxRatePercent, null, null,
                    ModifiersPriceExtra: Euros(x.Line.ModifiersExtraCents), TaxRateTakeawayPercent: x.Line.TaxRateTakeawayPercent)).ToList()),
                waiter.Token);

            if (op.Discount is { } discount)
            {
                await SendAsync(client, HttpMethod.Post, $"/api/orders/{orderId}/discount",
                    new ApplyOrderDiscountRequest(Enum.Parse<DiscountType>(discount.Type), discount.Value, "Geste commercial", Manager.Id), Manager.Token);
            }

            if (!lines.Any(x => x.Line.Comp) && op.LineVoids is not { Count: > 0 })
            {
                return [];
            }

            var order = await SendAsync(client, HttpMethod.Get, $"/api/tables/{table}/order", null, waiter.Token);
            var lineIds = order.GetProperty("lines").EnumerateArray()
                .ToDictionary(l => l.GetProperty("productId").GetGuid(), l => l.GetProperty("lineId").GetGuid());
            foreach (var (_, productId) in lines.Where(x => x.Line.Comp))
            {
                await SendAsync(client, HttpMethod.Post, $"/api/orders/{orderId}/items/{lineIds[productId]}/comp",
                    new CompOrderItemRequest("Offert", Manager.Id), Manager.Token);
            }

            return lines.Select(x => new DayServerLine(x.Line, x.ProductId, lineIds[x.ProductId])).ToList();
        }

        /// <summary>Après un règlement : réouverture historique (`v8`), étapes de phase et annulations de reçu (<c>ReceiptVoids</c>) à ce rang.</summary>
        private async Task AfterPaymentAsync(LineVoidContext ctx, int payment)
        {
            if (payment == 0 && DayLedgerOracle.IsReopened(ctx.Op))
            {
                var voided = await SendAsync(ctx.Client, HttpMethod.Post, $"/api/checkout/void/{ctx.Receipts[0].Id}",
                    new VoidReceiptRequest(TerminalIds[ctx.Key], Manager.Id), Manager.Token);
                var voidReceipt = await ReceiptAsync(voided);
                Receipts[ctx.Key].Add(voidReceipt);
                ctx.VoidedReceipts.Add(ctx.Receipts[0].Number);
                ctx.VoidReceiptsByPayment[0] = voidReceipt;
                await StepsAsync(ctx, "reopened");
            }

            if (payment == 0)
            {
                await StepsAsync(ctx, "part_paid");
            }
            else if (payment == 1)
            {
                await StepsAsync(ctx, "part_paid_two");
            }

            await ReceiptVoidStepsAsync(ctx, afterPayment: payment, stage: 0);
            if (payment == 1)
            {
                await StepsAsync(ctx, "part_voided_still_has_receipt");
            }

            await ReceiptVoidStepsAsync(ctx, afterPayment: payment, stage: 1);
            if (payment == 1)
            {
                await StepsAsync(ctx, "all_voided_reopened");
            }
        }

        private async Task ReceiptVoidStepsAsync(LineVoidContext ctx, int? afterPayment, int? stage)
        {
            if (ctx.Op.ReceiptVoids is not { Count: > 0 } steps)
            {
                return;
            }

            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                if (step.AfterPayment != afterPayment || (stage is not null && step.Stage != stage))
                {
                    continue;
                }

                string callerKey = step.FromTerminal ?? ctx.Key;
                var callerClient = Clients[callerKey];
                var callerTerminalId = TerminalIds[callerKey];
                var opActor = ResolveActor(step.By, ctx.Waiter);
                var target = step.VoidSelf ? ctx.VoidReceiptsByPayment[step.PaymentIndex] : ctx.Receipts[step.PaymentIndex];
                string ownTerminalId = TerminalIds[ctx.Key];
                var xBefore = await SendAsync(ctx.Client, HttpMethod.Get, $"/api/fiscal/x-report?terminalId={ownTerminalId}", null, Manager.Token);
                var (status, body) = await TrySendAsync(callerClient, HttpMethod.Post, $"/api/checkout/void/{target.Id}",
                    new VoidReceiptRequest(callerTerminalId, opActor.Id), opActor.Token);
                var xAfter = await SendAsync(ctx.Client, HttpMethod.Get, $"/api/fiscal/x-report?terminalId={ownTerminalId}", null, Manager.Token);
                string? code = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                ReceiptVoidAttempts.Add(new DayReceiptVoidAttempt(ctx.OpIndex, ctx.Key, ctx.Op.Ref!, i, status, code, xBefore, xAfter));
                if (status == 200)
                {
                    var voidReceipt = await ReceiptAsync(body);
                    Receipts[ctx.Key].Add(voidReceipt);
                    ctx.VoidedReceipts.Add(target.Number);
                    ctx.VoidReceiptsByPayment[step.PaymentIndex] = voidReceipt;
                }
            }
        }

        private Operator ResolveActor(string by, Operator waiter) => by switch
        {
            "waiter" => waiter,
            "admin" => Admin,
            "kitchen" => Kitchen,
            _ => Manager,
        };

        /// <summary>Joue les étapes d'une phase : refus compris, la réponse est enregistrée (statut, code, total) avec le X du poste avant et après.</summary>
        private async Task StepsAsync(LineVoidContext ctx, string phase)
        {
            if (ctx.Op.LineVoids is not { Count: > 0 } steps)
            {
                return;
            }

            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                if (step.Phase != phase)
                {
                    continue;
                }

                var line = ctx.Lines[step.Line];
                if (step.Legacy)
                {
                    // Ligne envoyée avant OrderItemId : la seule écriture directe en base du rejeu (aucune route ne peut produire ce cas).
                    using var scope = factory.Services.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    foreach (var item in await db.KitchenTicketItems.Where(t => t.OrderItemId == line.LineId).ToListAsync())
                    {
                        item.OrderItemId = null;
                    }

                    await db.SaveChangesAsync();
                }

                Guid targetOrderId = step.UnknownOrder ? Guid.NewGuid() : ctx.OrderId;
                Guid targetLineId = step.UnknownLine ? Guid.NewGuid() : line.LineId;
                var actor = ResolveActor(step.By, ctx.Waiter);
                string terminalId = TerminalIds[ctx.Key];
                var xBefore = await SendAsync(ctx.Client, HttpMethod.Get, $"/api/fiscal/x-report?terminalId={terminalId}", null, Manager.Token);
                var (status, body) = await TrySendAsync(ctx.Client, HttpMethod.Post, $"/api/orders/{targetOrderId}/items/{targetLineId}/void",
                    new VoidOrderItemRequest(step.Quantity, step.Reason), actor.Token);
                var xAfter = await SendAsync(ctx.Client, HttpMethod.Get, $"/api/fiscal/x-report?terminalId={terminalId}", null, Manager.Token);
                bool hasBody = body.ValueKind == JsonValueKind.Object;
                string? Text(string name) => hasBody && body.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                long? total = status == 200 ? DayAssert.Cents(body.GetProperty("totalTtc")) : null;
                LineVoidAttempts.Add(new DayLineVoidAttempt(ctx.OpIndex, ctx.Key, ctx.Op.Ref!, i, status, Text("code"), Text("message"), total,
                    ctx.Receipts.Select(r => r.Number).Except(ctx.VoidedReceipts).ToList(), xBefore, xAfter));

                if (step.ReAdd > 0)
                {
                    await ReAddAsync(ctx, line, step.ReAdd);
                }

                if (step.AddDraft is { } draft)
                {
                    await AddDraftLineAsync(ctx, draft);
                }
            }

            LineIds[ctx.Op.Ref!] = ctx.Lines.Select(l => l.LineId).ToList();
        }

        /// <summary>Ajoute une ligne brouillon (non envoyée) en cours de commande, sans dispatch.</summary>
        private static async Task AddDraftLineAsync(LineVoidContext ctx, DayLine draft)
        {
            var productId = Guid.NewGuid();
            var known = ctx.Lines.Select(x => x.LineId).ToHashSet();
            var order = await SendAsync(ctx.Client, HttpMethod.Post, $"/api/tables/{ctx.Table}/items", new AddOrderItemsRequest(
                [new OrderItemInputDto(productId, draft.Name, draft.Quantity, Euros(draft.UnitPriceCents), draft.TaxRatePercent, null, null,
                    ModifiersPriceExtra: Euros(draft.ModifiersExtraCents), TaxRateTakeawayPercent: draft.TaxRateTakeawayPercent)]), ctx.Waiter.Token);
            var added = order.GetProperty("lines").EnumerateArray().Select(x => x.GetProperty("lineId").GetGuid()).Single(id => !known.Contains(id));
            ctx.Lines.Add(new DayServerLine(draft, productId, added));
        }

        /// <summary>Rajoute le même produit (même identifiant : la ligne envoyée n'est pas fusionnée) et l'envoie en cuisine : second bon.</summary>
        private static async Task ReAddAsync(LineVoidContext ctx, DayServerLine line, int quantity)
        {
            var l = line.Line;
            var known = ctx.Lines.Select(x => x.LineId).ToHashSet();
            var order = await SendAsync(ctx.Client, HttpMethod.Post, $"/api/tables/{ctx.Table}/items", new AddOrderItemsRequest(
                [new OrderItemInputDto(line.ProductId, l.Name, quantity, Euros(l.UnitPriceCents), l.TaxRatePercent, null, null,
                    ModifiersPriceExtra: Euros(l.ModifiersExtraCents), TaxRateTakeawayPercent: l.TaxRateTakeawayPercent)]), ctx.Waiter.Token);
            var added = order.GetProperty("lines").EnumerateArray().Select(x => x.GetProperty("lineId").GetGuid()).Single(id => !known.Contains(id));
            ctx.Lines.Add(new DayServerLine(l with { Quantity = quantity, Comp = false }, line.ProductId, added));
            await SendAsync(ctx.Client, HttpMethod.Post, $"/api/tables/{ctx.Table}/dispatch", null, ctx.Waiter.Token);
        }

        /// <summary>Annulation tardive du reçu d'une commande déjà réglée : on enregistre la réponse de l'API, refus compris.</summary>
        private async Task VoidReceiptAsync(DayOperation op, string key, HttpClient client, string terminalId, int index)
        {
            string reference = op.Ref ?? throw new InvalidOperationException("void_receipt : Ref manquante");
            var receipts = _receiptsByRef.GetValueOrDefault(reference) ?? throw new InvalidOperationException($"void_receipt : commande {reference} inconnue ou sans reçu");
            int paymentIndex = op.VoidPaymentIndex ?? throw new InvalidOperationException("void_receipt : VoidPaymentIndex manquant");
            var (status, body) = await TrySendAsync(client, HttpMethod.Post, $"/api/checkout/void/{receipts[paymentIndex].Id}",
                new VoidReceiptRequest(terminalId, Manager.Id), Manager.Token);
            string? code = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("code", out var c) ? c.GetString() : null;
            VoidAttempts.Add(new DayVoidAttempt(index, key, reference, status, code));
        }

        private async Task AfterPaymentsAsync(LineVoidContext ctx)
        {
            var op = ctx.Op;
            var key = ctx.Key;
            var client = ctx.Client;
            var receipts = ctx.Receipts;
            Receipts[key].AddRange(receipts);
            if (op.Ref is not null)
            {
                _receiptsByRef[op.Ref] = receipts;
            }

            if (op.Reprint)
            {
                foreach (var receipt in receipts)
                {
                    await SendAsync(client, HttpMethod.Post, $"/api/checkout/receipts/{receipt.Id}/reprint", null, Manager.Token);
                }
            }

            if (op.VoidPaymentIndex is { } index)
            {
                var voided = await SendAsync(client, HttpMethod.Post, $"/api/checkout/void/{receipts[index].Id}",
                    new VoidReceiptRequest(TerminalIds[key], Manager.Id), Manager.Token);
                var voidReceipt = await ReceiptAsync(voided);
                Receipts[key].Add(voidReceipt);
                ctx.VoidedReceipts.Add(receipts[index].Number);
                ctx.VoidReceiptsByPayment[index] = voidReceipt;
            }

            await ReceiptVoidStepsAsync(ctx, afterPayment: null, stage: null);
        }

        private async Task<DayReceipt> ReceiptAsync(JsonElement response)
        {
            var number = response.GetProperty("receiptNumber").GetString()!;
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var id = await db.FiscalReceipts.AsNoTracking().Where(r => r.ReceiptNumber == number).Select(r => r.Id).SingleAsync();
            return new DayReceipt(number, id);
        }

        /// <summary>Un compte serveur par clé du scénario, créé par l'API du personnel (PIN 7001, 7002…), connecté par son PIN.</summary>
        private async Task<Operator> WaiterAsync(HttpClient client, string key)
        {
            if (_waiters.TryGetValue(key, out var known))
            {
                return known;
            }

            string pin = (7001 + _waiters.Count).ToString(System.Globalization.CultureInfo.InvariantCulture);
            await SendAsync(client, HttpMethod.Post, "/api/staff", new CreateStaffRequest(key, "Waiter", pin), Manager.Token);
            var waiter = await LoginAsync(client, pin);
            _waiters[key] = waiter;
            return waiter;
        }
    }
}
