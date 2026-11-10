using System.Net;
using System.Text;
using FluentAssertions;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Infrastructure.Payments;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class StripeCardTerminalTests
{
    private sealed class FakeStripe(params string[] readerStates) : HttpMessageHandler
    {
        private int _poll;
        public List<string> Calls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Calls.Add($"{request.Method} {path}");
            string json = path.EndsWith("/payment_intents", StringComparison.Ordinal) ? "{\"id\":\"pi_1\"}"
                : request.Method == HttpMethod.Get ? readerStates[Math.Min(_poll++, readerStates.Length - 1)]
                : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    private static StripeCardTerminal Terminal(FakeStripe fake) =>
        new(new HttpClient(fake) { BaseAddress = new Uri("https://stripe.test/v1/") }, "tmr_1");

    [Fact]
    public async Task Succeeded_IsApproved_AfterWaiting()
    {
        var fake = new FakeStripe("{\"action\":{\"status\":\"in_progress\"}}", "{\"action\":{\"status\":\"succeeded\"}}");

        var result = await Terminal(fake).ChargeAsync(1250, "ref", CancellationToken.None);

        result.Status.Should().Be(CardChargeStatus.Approved);
        result.Reference.Should().Be("pi_1");
        fake.Calls.Should().StartWith(["POST /v1/payment_intents", "POST /v1/terminal/readers/tmr_1/process_payment_intent"]);
    }

    [Fact]
    public async Task Failed_IsDeclined_WithStripeReason()
    {
        var fake = new FakeStripe("{\"action\":{\"status\":\"failed\",\"failure_code\":\"card_declined\"}}");

        var result = await Terminal(fake).ChargeAsync(1250, "ref", CancellationToken.None);

        result.Status.Should().Be(CardChargeStatus.Declined);
        result.Reason.Should().Be("card_declined");
    }

    [Fact]
    public async Task Cancelled_AsksTheReaderToCancel()
    {
        var fake = new FakeStripe("{\"action\":{\"status\":\"in_progress\"}}");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var act = () => Terminal(fake).ChargeAsync(1250, "ref", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        fake.Calls.Should().Contain("POST /v1/terminal/readers/tmr_1/cancel_action");
    }
}
