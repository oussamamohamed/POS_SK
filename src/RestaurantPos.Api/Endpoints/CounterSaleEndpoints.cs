using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Api.Endpoints;

public static class CounterSaleEndpoints
{
    public static void MapCounterSaleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orders/counter")
                       .WithTags("Counter & Takeaway Sales")
                       .RequireAuthorization();

        // 1. Get or Open Direct Counter Order
        group.MapPost("/direct", async (DirectCounterOpenRequest req, AppDbContext db, ITableManagementService tableService) =>
        {
            const string counterTableNumber = "Comptoir";
            var table = await db.DiningTables.FirstOrDefaultAsync(t => t.TableNumber == counterTableNumber);
            if (table == null)
            {
                table = new DiningTable
                {
                    TableNumber = counterTableNumber,
                    Capacity = 1,
                    Status = TableStatus.Free,
                    PositionX = 0,
                    PositionY = 0
                };
                db.DiningTables.Add(table);
                await db.SaveChangesAsync();
            }

            Order order;
            if (table.ActiveOrderId.HasValue)
            {
                order = await db.Orders
                    .Include(o => o.Items)
                    .FirstOrDefaultAsync(o => o.Id == table.ActiveOrderId.Value) ?? new Order();
            }
            else
            {
                order = new Order
                {
                    TableNumber = counterTableNumber,
                    Status = OrderStatus.Open,
                    Destination = req.Destination ?? OrderDestination.Takeaway
                };
                db.Orders.Add(order);
                table.ActiveOrderId = order.Id;
                table.Status = TableStatus.Occupied;
                table.OpenedAtUtc = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }

            var activeOrderDto = await tableService.GetActiveOrderForTableAsync(counterTableNumber);
            return Results.Ok(activeOrderDto);
        });

        // 2. Switch Destination & Recalculate Taxes
        app.MapPost("/api/orders/{orderId:guid}/destination", async (Guid orderId, SwitchDestinationRequest req, AppDbContext db, ITableManagementService tableService) =>
        {
            var order = await db.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null)
            {
                return Results.NotFound(new { Message = "Commande introuvable." });
            }

            order.Destination = req.Destination;
            await db.SaveChangesAsync();

            var activeOrderDto = await tableService.GetActiveOrderForTableAsync(order.TableNumber);
            return Results.Ok(activeOrderDto);
        }).RequireAuthorization();

        // 3. Park Cart on Hold
        group.MapPost("/hold", async (HoldCounterOrderRequest req, AppDbContext db, IHeldOrderStorageService heldStorage, ClaimsPrincipal user) =>
        {
            var order = await db.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == req.OrderId);

            if (order == null || order.Items.Count == 0)
            {
                return Results.BadRequest(new { Message = "Impossible de mettre en attente un panier vide." });
            }

            string terminalId = !string.IsNullOrWhiteSpace(req.TerminalId) ? req.TerminalId : "POS_MAIN_TERM";
            Guid staffId = req.StaffId ?? Guid.Empty;
            if (staffId == Guid.Empty && Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedUserId))
            {
                staffId = parsedUserId;
            }

            var heldOrder = await heldStorage.HoldOrderAsync(order, terminalId, staffId, req.CustomerLabel);

            // Detach from active table so register is free for next sale
            var table = await db.DiningTables.FirstOrDefaultAsync(t => t.TableNumber == order.TableNumber);
            if (table != null)
            {
                table.ActiveOrderId = null;
                table.Status = TableStatus.Free;
                table.OpenedAtUtc = null;
                await db.SaveChangesAsync();
            }

            return Results.Ok(new HeldOrderDto(
                heldOrder.Id,
                heldOrder.TerminalId,
                heldOrder.OrderId,
                heldOrder.CustomerLabel,
                heldOrder.Destination,
                heldOrder.ItemCount,
                heldOrder.TotalTtc,
                heldOrder.HeldAtUtc,
                heldOrder.HeldByStaffId
            ));
        });

        // 4. List Held Orders
        group.MapGet("/held", async (string? terminalId, IHeldOrderStorageService heldStorage) =>
        {
            string term = terminalId ?? "POS_MAIN_TERM";
            var list = await heldStorage.GetActiveHeldOrdersAsync(term);
            return Results.Ok(list);
        });

        // 5. Recall Held Order
        group.MapPost("/held/{holdId:guid}/recall", async (Guid holdId, AppDbContext db, IHeldOrderStorageService heldStorage, ITableManagementService tableService) =>
        {
            var order = await heldStorage.RecallOrderAsync(holdId);
            if (order == null)
            {
                return Results.NotFound(new { Message = "Commande en attente introuvable ou déjà rappelée." });
            }

            const string counterTableNumber = "Comptoir";
            var table = await db.DiningTables.FirstOrDefaultAsync(t => t.TableNumber == counterTableNumber);
            if (table == null)
            {
                table = new DiningTable { TableNumber = counterTableNumber, Capacity = 1, Status = TableStatus.Occupied, PositionX = 0, PositionY = 0 };
                db.DiningTables.Add(table);
            }

            table.ActiveOrderId = order.Id;
            table.Status = TableStatus.Occupied;
            table.OpenedAtUtc ??= DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            var activeOrderDto = await tableService.GetActiveOrderForTableAsync(counterTableNumber);
            return Results.Ok(activeOrderDto);
        });

        // 6. Void Held Order (Supervisor PIN Protected)
        group.MapPost("/held/{holdId:guid}/void", async (Guid holdId, VoidHeldOrderRequest req, AppDbContext db, IHeldOrderStorageService heldStorage, IOperatorAuthenticationService authService) =>
        {
            if (string.IsNullOrWhiteSpace(req.SupervisorPin))
            {
                return Results.BadRequest(new { Message = "Code PIN superviseur requis." });
            }

            var auth = await authService.AuthenticatePinAsync(req.SupervisorPin);
            if (!auth.IsSuccess || (auth.Role != UserRole.FloorManager && auth.Role != UserRole.Admin))
            {
                return Results.Json(new { Message = "Autorisation insuffisante : code PIN superviseur ou gérant requis." }, statusCode: StatusCodes.Status403Forbidden);
            }

            var success = await heldStorage.VoidHeldOrderAsync(holdId, auth.OperatorId!.Value, req.VoidReason);
            if (!success)
            {
                return Results.NotFound(new { Message = "Commande en attente introuvable." });
            }

            // Log JET audit event
            var journalEntry = new TransactionJournalEntry
            {
                TerminalId = !string.IsNullOrWhiteSpace(req.TerminalId) ? req.TerminalId : "POS_MAIN_TERM",
                IdempotencyKey = Guid.NewGuid().ToString("N"),
                EventType = "EVENT_HELD_ORDER_VOIDED",
                PayloadJson = JsonSerializer.Serialize(new { HoldId = holdId, VoidedBy = auth.OperatorName, Reason = req.VoidReason }),
                EntryHash = "JET_VOID_" + Guid.NewGuid().ToString("N")[..16]
            };
            db.JournalEntries.Add(journalEntry);
            await db.SaveChangesAsync();

            return Results.Ok(new { Message = "Commande en attente annulée avec succès." });
        });

        // 7. Counter & Takeaway Multi-Tender Checkout
        group.MapPost("/checkout", async (CounterCheckoutRequest req, AppDbContext db, ICheckoutPaymentService checkout, ITakeawayCounterService counterService, IMealVoucherPolicyService mealVoucherPolicyService) =>
        {
            var order = await db.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == req.OrderId);

            if (order == null || order.Items.Count == 0)
            {
                return Results.BadRequest(new { Message = "Commande introuvable ou panier vide." });
            }

            string terminalId = !string.IsNullOrWhiteSpace(req.TerminalId) ? req.TerminalId : "POS_MAIN_TERM";

            // 1. Assign sequential daily pickup number (#A-01 .. #A-99)
            string pickupNumber = await counterService.GetNextPickupNumberAsync(terminalId);
            order.PickupNumber = pickupNumber;
            order.Destination = req.Destination;
            order.PickupBuzzer = req.PickupBuzzer;
            order.PickupScheduledAtUtc = req.PickupScheduledAtUtc;
            if (req.TipAmount > 0)
            {
                order.TipAmount = Money.FromDecimal(req.TipAmount, "EUR");
            }
            await db.SaveChangesAsync();

            // 2. Meal voucher overpayment and eligibility policy enforcement
            CustomerCreditVoucherDto? issuedCreditVoucher = null;
            var policy = req.MealVoucherPolicy ?? MealVoucherOverpaymentPolicy.CapAtBalance;

            var voucherTender = req.Tenders.FirstOrDefault(t => t.Method == PaymentMethod.MealVoucher);
            if (voucherTender != null)
            {
                var validation = mealVoucherPolicyService.ValidateVoucherTender(order, voucherTender.Amount, voucherTender.FacialValue, policy);
                if (!validation.IsAllowed)
                {
                    return Results.BadRequest(new { Message = validation.ErrorMessage });
                }

                if (policy == MealVoucherOverpaymentPolicy.CustomerCreditVoucher && validation.SurplusAmount > 0)
                {
                    string voucherCode = "CR-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
                    var creditVoucher = new CustomerCreditVoucher
                    {
                        VoucherCode = voucherCode,
                        OriginalOrderId = order.Id,
                        TerminalId = terminalId,
                        Amount = Money.FromDecimal(validation.SurplusAmount, "EUR"),
                        ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(90)
                    };
                    db.CustomerCreditVouchers.Add(creditVoucher);
                    await db.SaveChangesAsync();

                    issuedCreditVoucher = new CustomerCreditVoucherDto(voucherCode, validation.SurplusAmount, creditVoucher.ExpiresAtUtc);
                }
            }

            // 3. Process payment tenders through NF525 checkout service
            var paymentTenderRequests = req.Tenders.Select(t => new PaymentTenderRequest(
                t.Method,
                (long)Math.Round(t.Amount * 100),
                (long)Math.Round(t.Tendered * 100)
            )).ToList();

            var checkoutResult = await checkout.ProcessPaymentTendersAsync(order.Id, terminalId, paymentTenderRequests);

            bool hasCash = req.Tenders.Any(t => t.Method == PaymentMethod.Cash);
            bool printPickupVoucher = true; // Systematic takeaway pickup coupon
            bool printFiscalReceipt = req.RequestFiscalReceiptPrint; // Anti-waste AGEC: only if requested

            return Results.Ok(new CounterCheckoutResponse(
                order.Id,
                pickupNumber,
                checkoutResult.TotalPaidCents / 100.0m,
                checkoutResult.ChangeGivenCents / 100.0m,
                checkoutResult.RemainingBalanceCents / 100.0m,
                checkoutResult.ReceiptNumber,
                checkoutResult.FiscalSignature,
                issuedCreditVoucher,
                printPickupVoucher,
                printFiscalReceipt,
                hasCash
            ));
        });
    }
}
