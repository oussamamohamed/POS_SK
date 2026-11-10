using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Application.Common.Interfaces;

namespace RestaurantPos.Infrastructure.Payments;

/// <summary>
/// Stripe Terminal, intégration pilotée par le serveur (lecteurs BBPOS WisePOS E, Stripe S700…) : un PaymentIntent est créé,
/// envoyé au lecteur, puis l'action du lecteur est interrogée jusqu'à la décision. Aucun SDK : API REST seule.
/// </summary>
public sealed class StripeCardTerminal : ICardTerminal
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1500);
    private readonly HttpClient _http;
    private readonly string _readerId;

    public StripeCardTerminal(HttpClient http, string readerId)
    {
        _http = http;
        _readerId = Uri.EscapeDataString(readerId);
    }

    public async Task<CardChargeResult> ChargeAsync(long amountCents, string reference, CancellationToken cancellationToken)
    {
        try
        {
            using var intent = await PostAsync("payment_intents", new()
            {
                ["amount"] = amountCents.ToString(CultureInfo.InvariantCulture),
                ["currency"] = "eur",
                ["payment_method_types[]"] = "card_present",
                ["capture_method"] = "automatic",
                ["metadata[reference]"] = reference
            }, cancellationToken).ConfigureAwait(false);
            if (intent is null) return Unavailable(reference, "intent_not_created");
            var intentId = intent.RootElement.GetProperty("id").GetString()!;

            using var sent = await PostAsync($"terminal/readers/{_readerId}/process_payment_intent", new() { ["payment_intent"] = intentId }, cancellationToken).ConfigureAwait(false);
            if (sent is null) return Unavailable(reference, "reader_unavailable");

            try
            {
                while (true)
                {
                    await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
                    using var reader = await GetAsync($"terminal/readers/{_readerId}", cancellationToken).ConfigureAwait(false);
                    if (reader is null) return Unavailable(reference, "reader_unreachable");
                    if (!reader.RootElement.TryGetProperty("action", out var action) || action.ValueKind != JsonValueKind.Object) continue;
                    switch (action.GetProperty("status").GetString())
                    {
                        case "succeeded":
                            return new CardChargeResult(CardChargeStatus.Approved, intentId);
                        case "failed":
                            var reason = action.TryGetProperty("failure_code", out var code) ? code.GetString() : null;
                            return new CardChargeResult(CardChargeStatus.Declined, intentId, reason ?? "card_declined");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Délai dépassé ou requête abandonnée : le client ne doit plus voir la saisie du code sur le terminal.
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var _ = await PostAsync($"terminal/readers/{_readerId}/cancel_action", [], cleanup.Token).ConfigureAwait(false);
                throw;
            }
        }
        catch (HttpRequestException)
        {
            return Unavailable(reference, "network");
        }
    }

    private static CardChargeResult Unavailable(string reference, string reason) => new(CardChargeStatus.Unavailable, reference, reason);

    private async Task<JsonDocument?> PostAsync(string path, Dictionary<string, string> form, CancellationToken ct)
    {
        using var response = await _http.PostAsync(new Uri(path, UriKind.Relative), new FormUrlEncodedContent(form), ct).ConfigureAwait(false);
        return await ReadAsync(response, ct).ConfigureAwait(false);
    }

    private async Task<JsonDocument?> GetAsync(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(new Uri(path, UriKind.Relative), ct).ConfigureAwait(false);
        return await ReadAsync(response, ct).ConfigureAwait(false);
    }

    private static async Task<JsonDocument?> ReadAsync(HttpResponseMessage response, CancellationToken ct) =>
        response.IsSuccessStatusCode
            ? await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false)
            : null;
}
