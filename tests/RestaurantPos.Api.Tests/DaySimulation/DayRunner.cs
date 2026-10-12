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
    DayManager Manager);

/// <summary>Le responsable connecté du rejeu (PIN 1234) : permet aux tests de rappeler X, Z et les rapports après coup.</summary>
public sealed record DayManager(Guid Id, string Name, string Token);

/// <summary>Résultat d'une opération <c>void_receipt</c> : statut HTTP et code d'erreur renvoyés par l'API (null si la réponse n'en a pas).</summary>
public sealed record DayVoidAttempt(int OpIndex, string Key, string? Ref, int Status, string? Code);

/// <summary>Rejoue un <see cref="DayScenario"/> par l'API HTTP réelle (jamais par la base, sauf pour relire l'identifiant d'un reçu).</summary>
public static class DayRunner
{
    public const string ManagerPin = "1234";

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

        run.Manager = await LoginAsync(run.Clients.Values.First(), ManagerPin);
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
            new DayManager(run.Manager.Id, run.Manager.Name, run.Manager.Token));
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
        public Operator Manager { get; set; } = null!;

        public async Task ExecuteAsync(DayOperation op, int index)
        {
            var key = op.Terminal ?? throw new InvalidOperationException($"{op.Type} : poste manquant");
            var client = Clients[key];
            var terminalId = TerminalIds[key];
            switch (op.Type)
            {
                case "table_order":
                    await TableOrderAsync(op, key, client);
                    break;
                case "counter_order":
                    await CounterOrderAsync(op, key, client, terminalId);
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

        private async Task TableOrderAsync(DayOperation op, string key, HttpClient client)
        {
            var waiter = await WaiterAsync(client, op.Waiter!);
            var table = op.Table ?? throw new InvalidOperationException("table_order : table manquante");
            var opened = await SendAsync(client, HttpMethod.Post, $"/api/tables/{table}/open",
                new OpenTableRequest(waiter.Name, op.Covers, waiter.Id), waiter.Token);
            var orderId = opened.GetProperty("activeOrderId").GetGuid();
            if (op.Destination is not null)
            {
                await SendAsync(client, HttpMethod.Post, $"/api/orders/{orderId}/destination",
                    new SwitchDestinationRequest(Enum.Parse<OrderDestination>(op.Destination)), waiter.Token);
            }

            await AddLinesAsync(op, client, table, orderId, waiter);
            await SendAsync(client, HttpMethod.Post, $"/api/tables/{table}/dispatch", null, waiter.Token);

            var payments = op.Payments ?? [];
            var receipts = new List<DayReceipt>();
            for (int p = 0; p < payments.Count; p++)
            {
                bool last = p == payments.Count - 1;
                var paid = await SendAsync(client, HttpMethod.Post, "/api/checkout/pay", new PaymentSettlementRequest(
                    orderId, table, waiter.Id,
                    payments[p].Select(t => new TenderItemRequest(Enum.Parse<PaymentMethod>(t.Method), Euros(t.AmountCents),
                        Euros(t.AmountCents + t.ExtraTenderedCents), Euros(t.ExtraTenderedCents))).ToList(),
                    TipAmount: last ? Euros(op.TipCents) : 0m), waiter.Token);
                receipts.Add(await ReceiptAsync(paid));
            }

            await AfterPaymentsAsync(op, key, client, receipts);
        }

        private async Task CounterOrderAsync(DayOperation op, string key, HttpClient client, string terminalId)
        {
            var waiter = await WaiterAsync(client, op.Waiter!);
            var destination = Enum.Parse<OrderDestination>(op.Destination ?? nameof(OrderDestination.Takeaway));
            var orderId = await OpenCounterAsync(op, client, terminalId, destination, waiter);
            if (op.HoldAndRecall)
            {
                var held = await SendAsync(client, HttpMethod.Post, "/api/orders/counter/hold",
                    new HoldCounterOrderRequest(orderId, terminalId, waiter.Id, "Client"), waiter.Token);
                await SendAsync(client, HttpMethod.Post, $"/api/orders/counter/held/{held.GetProperty("holdId").GetGuid()}/recall", null, waiter.Token);
            }

            var payments = op.Payments ?? [];
            var receipts = new List<DayReceipt>();
            for (int p = 0; p < payments.Count; p++)
            {
                bool last = p == payments.Count - 1;
                var paid = await SendAsync(client, HttpMethod.Post, "/api/orders/counter/checkout", new CounterCheckoutRequest(
                    orderId, terminalId, destination, null, null, last ? Euros(op.TipCents) : 0m, false, null,
                    payments[p].Select(t => new CounterPaymentTender(Enum.Parse<PaymentMethod>(t.Method), Euros(t.AmountCents),
                        Euros(t.AmountCents + t.ExtraTenderedCents))).ToList()), waiter.Token);
                receipts.Add(await ReceiptAsync(paid));
            }

            await AfterPaymentsAsync(op, key, client, receipts);
        }

        private async Task HoldThenVoidAsync(DayOperation op, HttpClient client, string terminalId)
        {
            var waiter = await WaiterAsync(client, op.Waiter!);
            var destination = Enum.Parse<OrderDestination>(op.Destination ?? nameof(OrderDestination.Takeaway));
            var orderId = await OpenCounterAsync(op, client, terminalId, destination, waiter);
            var held = await SendAsync(client, HttpMethod.Post, "/api/orders/counter/hold",
                new HoldCounterOrderRequest(orderId, terminalId, waiter.Id, "Client"), waiter.Token);
            await SendAsync(client, HttpMethod.Post, $"/api/orders/counter/held/{held.GetProperty("holdId").GetGuid()}/void",
                new VoidHeldOrderRequest(ManagerPin, "Client parti", terminalId), waiter.Token);
        }

        private async Task<Guid> OpenCounterAsync(DayOperation op, HttpClient client, string terminalId, OrderDestination destination, Operator waiter)
        {
            var opened = await SendAsync(client, HttpMethod.Post, "/api/orders/counter/direct",
                new DirectCounterOpenRequest(terminalId, destination), waiter.Token);
            var orderId = opened.GetProperty("orderId").GetGuid();
            await AddLinesAsync(op, client, "Comptoir", orderId, waiter);
            return orderId;
        }

        private async Task AddLinesAsync(DayOperation op, HttpClient client, string table, Guid orderId, Operator waiter)
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

            if (lines.Any(x => x.Line.Comp))
            {
                var order = await SendAsync(client, HttpMethod.Get, $"/api/tables/{table}/order", null, waiter.Token);
                var lineIds = order.GetProperty("lines").EnumerateArray()
                    .ToDictionary(l => l.GetProperty("productId").GetGuid(), l => l.GetProperty("lineId").GetGuid());
                foreach (var (_, productId) in lines.Where(x => x.Line.Comp))
                {
                    await SendAsync(client, HttpMethod.Post, $"/api/orders/{orderId}/items/{lineIds[productId]}/comp",
                        new CompOrderItemRequest("Offert", Manager.Id), Manager.Token);
                }
            }
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

        private async Task AfterPaymentsAsync(DayOperation op, string key, HttpClient client, List<DayReceipt> receipts)
        {
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
                Receipts[key].Add(await ReceiptAsync(voided));
            }
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
