using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class MealVoucherPolicyTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;
    private readonly MealVoucherPolicyService _policyService = new();

    public MealVoucherPolicyTests(PosApiApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new PinLoginRequest("9999"));
        var result = await response.Content.ReadFromJsonAsync<LoginResultDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result!.Token);
        return client;
    }

    [Fact]
    public void PolicyService_EnforcesLegalCeilingAndItemEligibility()
    {
        var order = new Order { TableNumber = "Comptoir" };

        // 1 Eligible Sandwich (8.00 €) + 1 Ineligible Wine bottle (25.00 €)
        var sandwich = new OrderItem
        {
            ProductName = "Sandwich Jambon",
            Quantity = 1,
            UnitPrice = Money.FromDecimal(8.00m, "EUR"),
            TaxRatePercent = 10m,
            IsFoodVoucherEligible = true
        };
        var wine = new OrderItem
        {
            ProductName = "Bouteille de Vin",
            Quantity = 1,
            UnitPrice = Money.FromDecimal(25.00m, "EUR"),
            TaxRatePercent = 20m,
            IsFoodVoucherEligible = false
        };
        order.Items.AddRange([sandwich, wine]);

        // Total order is 33.00 €, but only 8.00 € is eligible for meal vouchers
        var resultTooHigh = _policyService.ValidateVoucherTender(order, 10.00m, null, MealVoucherOverpaymentPolicy.CapAtBalance);
        resultTooHigh.IsAllowed.Should().BeFalse();
        resultTooHigh.MaxAllowedAmount.Should().Be(8.00m);

        var resultValid = _policyService.ValidateVoucherTender(order, 8.00m, null, MealVoucherOverpaymentPolicy.CapAtBalance);
        resultValid.IsAllowed.Should().BeTrue();

        // Check 25.00 € legal daily cap
        var bigOrder = new Order { TableNumber = "Comptoir" };
        bigOrder.Items.Add(new OrderItem
        {
            ProductName = "Grand Plat Traiteur",
            Quantity = 1,
            UnitPrice = Money.FromDecimal(50.00m, "EUR"),
            TaxRatePercent = 10m,
            IsFoodVoucherEligible = true
        });

        var resultExceedingCap = _policyService.ValidateVoucherTender(bigOrder, 30.00m, null, MealVoucherOverpaymentPolicy.CapAtBalance);
        resultExceedingCap.IsAllowed.Should().BeFalse();
        resultExceedingCap.MaxAllowedAmount.Should().Be(25.00m);
    }

    [Fact]
    public async Task Checkout_PolicyB_RejectsOverpaymentStrictly()
    {
        var client = await CreateAuthenticatedClientAsync();

        var openResponse = await client.PostAsJsonAsync("/api/orders/counter/direct", new DirectCounterOpenRequest("POS_MV_B", OrderDestination.Takeaway));
        var order = await openResponse.Content.ReadFromJsonAsync<ActiveTableOrderDto>();

        await client.PostAsJsonAsync("/api/tables/Comptoir/items", new AddOrderItemsRequest(new List<OrderItemInputDto>
        {
            new(Guid.NewGuid(), "Sandwich Thon", 1, 9.00m, 10.0m, null, null, CourseType.Direct, 0m, 10.0m, true)
        }));

        // Tender a meal voucher with facial value 12.00 € for a 9.00 € note under Policy B
        var checkoutReq = new CounterCheckoutRequest(
            OrderId: order!.OrderId,
            TerminalId: "POS_MV_B",
            Destination: OrderDestination.Takeaway,
            PickupBuzzer: null,
            PickupScheduledAtUtc: null,
            TipAmount: 0m,
            RequestFiscalReceiptPrint: false,
            MealVoucherPolicy: MealVoucherOverpaymentPolicy.StrictRejection,
            Tenders: new List<CounterPaymentTender>
            {
                new(PaymentMethod.MealVoucher, 9.00m, 9.00m, 12.00m)
            }
        );

        var response = await client.PostAsJsonAsync("/api/orders/counter/checkout", checkoutReq);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Surpaiement par Titre-Restaurant refusé");
    }

    [Fact]
    public async Task Checkout_PolicyC_IssuesCustomerCreditVoucher()
    {
        var client = await CreateAuthenticatedClientAsync();

        var openResponse = await client.PostAsJsonAsync("/api/orders/counter/direct", new DirectCounterOpenRequest("POS_MV_C", OrderDestination.Takeaway));
        var order = await openResponse.Content.ReadFromJsonAsync<ActiveTableOrderDto>();

        await client.PostAsJsonAsync("/api/tables/Comptoir/items", new AddOrderItemsRequest(new List<OrderItemInputDto>
        {
            new(Guid.NewGuid(), "Salade Caesar", 1, 10.00m, 10.0m, null, null, CourseType.Direct, 0m, 10.0m, true)
        }));

        // Tender a meal voucher with facial value 14.00 € on a 10.00 € note under Policy C
        var checkoutReq = new CounterCheckoutRequest(
            OrderId: order!.OrderId,
            TerminalId: "POS_MV_C",
            Destination: OrderDestination.Takeaway,
            PickupBuzzer: null,
            PickupScheduledAtUtc: null,
            TipAmount: 0m,
            RequestFiscalReceiptPrint: false,
            MealVoucherPolicy: MealVoucherOverpaymentPolicy.CustomerCreditVoucher,
            Tenders: new List<CounterPaymentTender>
            {
                new(PaymentMethod.MealVoucher, 10.00m, 10.00m, 14.00m)
            }
        );

        var response = await client.PostAsJsonAsync("/api/orders/counter/checkout", checkoutReq);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<CounterCheckoutResponse>();
        result.Should().NotBeNull();
        result!.TotalPaid.Should().Be(10.00m);
        result.ChangeGiven.Should().Be(0.00m);
        result.IssuedCreditVoucher.Should().NotBeNull();
        result.IssuedCreditVoucher!.Amount.Should().Be(4.00m);
        result.IssuedCreditVoucher.VoucherCode.Should().StartWith("CR-");
    }
}
