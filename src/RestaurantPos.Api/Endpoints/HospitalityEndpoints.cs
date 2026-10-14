using System;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Api.Hubs;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Domain.Common;
using RestaurantPos.Infrastructure.Localization;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Printing;
using RestaurantPos.Infrastructure.Services;

namespace RestaurantPos.Api.Endpoints;

public static class HospitalityEndpoints
{
    public static void MapHospitalityEndpoints(this IEndpointRouteBuilder app)
    {
        // 9. Hospitality Features
        var tableGroup = app.MapGroup("/api/tables").WithTags("Hospitality & Tables").RequireAuthorization();
        var orderGroup = app.MapGroup("/api/orders").WithTags("Orders & Discounts").RequireAuthorization();
        var hotelGroup = app.MapGroup("/api/hotel").WithTags("Hotel PMS").RequireAuthorization();

        tableGroup.MapPost("/{tableNumber}/transfer", async (string tableNumber, TransferTableRequest req, ITableManagementService tableService) =>
        {
            var ok = await tableService.TransferTableAsync(tableNumber, req.TargetTableNumber);
            return ok
                ? Results.Ok(new { Success = true, Message = Texts.T("messages.order_transferred", ("from", tableNumber), ("to", req.TargetTableNumber)) })
                : Results.BadRequest(new { Success = false, Message = Texts.T("errors.table_transfer_failed") });
        });

        tableGroup.MapPost("/{tableNumber}/merge", async (string tableNumber, MergeTablesRequest req, ITableManagementService tableService) =>
        {
            var ok = await tableService.MergeTablesAsync(tableNumber, req.TargetTableNumber);
            return ok
                ? Results.Ok(new { Success = true, Message = Texts.T("messages.tables_merged", ("from", tableNumber), ("to", req.TargetTableNumber)) })
                : Results.BadRequest(new { Success = false, Message = Texts.T("errors.table_merge_failed") });
        });

        orderGroup.MapPost("/{orderId:guid}/discount", async (
            Guid orderId,
            ApplyOrderDiscountRequest req,
            IOrderDiscountService discountService,
            System.Security.Claims.ClaimsPrincipal user) =>
        {
            // Plus de 2 décimales : serveur (AwayFromZero) et clients (Math.round) divergeraient d'un centime.
            if (req.Value != Math.Round(req.Value, 2))
            {
                return Results.BadRequest(new { Success = false, Message = Texts.T("errors.discount_value_precision") });
            }

            var opId = Guid.Empty;
            var subClaim = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? user.FindFirst("sub")?.Value;
            if (!Guid.TryParse(subClaim, out opId) && req.OperatorId.HasValue)
            {
                opId = req.OperatorId.Value;
            }

            try
            {
                var updated = await discountService.ApplyGlobalDiscountAsync(orderId, req.Type, req.Value, req.Reason, opId);
                return Results.Ok(new { Success = true, TotalTtc = updated.TotalTtc.ToDecimal() });
            }
            catch (Exception)
            {
                return Results.BadRequest(new { Success = false, Message = Texts.T("errors.discount_failed") });
            }
        }).RequireAuthorization("RequireManagerOrAdmin");

        orderGroup.MapPost("/{orderId:guid}/items/{itemId:guid}/comp", async (
            Guid orderId,
            Guid itemId,
            CompOrderItemRequest req,
            IOrderDiscountService discountService,
            System.Security.Claims.ClaimsPrincipal user) =>
        {
            var opId = Guid.Empty;
            var subClaim = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? user.FindFirst("sub")?.Value;
            if (!Guid.TryParse(subClaim, out opId) && req.OperatorId.HasValue)
            {
                opId = req.OperatorId.Value;
            }

            try
            {
                var updated = await discountService.CompOrderItemAsync(orderId, itemId, req.Reason, opId);
                return Results.Ok(new { Success = true, TotalTtc = updated.TotalTtc.ToDecimal() });
            }
            catch (Exception)
            {
                return Results.BadRequest(new { Success = false, Message = Texts.T("errors.comp_failed") });
            }
        }).RequireAuthorization("RequireManagerOrAdmin");

        orderGroup.MapDelete("/{orderId:guid}/discount", async (Guid orderId, IOrderDiscountService discountService) =>
        {
            var updated = await discountService.RemoveDiscountAsync(orderId);
            return Results.Ok(new { Success = true, TotalTtc = updated.TotalTtc.ToDecimal() });
        }).RequireAuthorization("RequireManagerOrAdmin");

        tableGroup.MapPost("/{tableNumber}/fire-suite", async (string tableNumber, AppDbContext db, IHubContext<KitchenHub> hubContext, PrintDispatcher printing) =>
        {
            var table = await db.DiningTables.FirstOrDefaultAsync(t => t.TableNumber == tableNumber);
            if (table?.ActiveOrderId is not null)
            {
                var order = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == table.ActiveOrderId.Value);
                if (order is not null)
                {
                    var suiteItems = order.Items.Where(i => i.Course == CourseType.Suite).ToList();
                    if (suiteItems.Count > 0)
                    {
                        var ticket = new KitchenTicket
                        {
                            Id = UuidV7.NewGuid(),
                            OrderId = order.Id,
                            TableNumber = tableNumber,
                            StationId = "HOT_KITCHEN",
                            Status = TicketStatus.Pending,
                            DispatchedAtUtc = DateTimeOffset.UtcNow,
                            Items = suiteItems.Select(i => new KitchenTicketItem
                            {
                                Id = UuidV7.NewGuid(),
                                ProductId = i.ProductId,
                                ProductName = $"[RÉCLAME SUITE] {i.ProductName}",
                                Quantity = i.Quantity,
                                ModifiersSummary = i.SelectedModifiers.Count > 0 ? string.Join(", ", i.SelectedModifiers) : null
                            }).ToList()
                        };
                        db.KitchenTickets.Add(ticket);
                        await db.SaveChangesAsync();
                        await printing.QueueKitchenTicketsAsync([ticket.Id]);
                    }
                }
            }
            await hubContext.Clients.All.SendAsync("ReceiveKitchenUpdate", "SUITE_CLAIMED", tableNumber);
            return Results.Ok(new { Success = true, Message = Texts.T("messages.next_course_fired", ("table", tableNumber)) });
        });

        hotelGroup.MapGet("/rooms", async (IRoomBillingService roomService) =>
        {
            var rooms = await roomService.GetAllOccupiedRoomsAsync();
            return Results.Ok(rooms);
        });

        hotelGroup.MapGet("/rooms/{roomNumber}", async (string roomNumber, IRoomBillingService roomService) =>
        {
            var room = await roomService.GetRoomOccupantAsync(roomNumber);
            return room is not null ? Results.Ok(room) : Results.NotFound(new { Message = Texts.T("errors.room_not_found", ("room", roomNumber)) });
        });

        hotelGroup.MapPost("/room-charge", async (RoomChargeRequest req, IRoomBillingService roomService, ITableManagementService tableService, ICheckoutPaymentService checkoutService, IDeviceService deviceService, AppDbContext db, HttpContext http) =>
        {
            try
            {
                var charge = await roomService.PostRoomChargeAsync(
                    req.OrderId,
                    req.TableNumber,
                    req.RoomNumber,
                    req.GuestName,
                    Money.FromDecimal(req.Amount, "EUR"),
                    Money.FromDecimal(req.TipAmount, "EUR"),
                    req.SignatureDataUrl,
                    req.Notes
                );

                string terminalId = "POS_MAIN_TERM";
                if (http.Request.Headers.TryGetValue("X-Device-Token", out var token) && !string.IsNullOrWhiteSpace(token))
                {
                    var dev = await deviceService.AuthenticateAsync(token.ToString(), http.RequestAborted);
                    if (dev != null && !string.IsNullOrWhiteSpace(dev.TerminalId)) terminalId = dev.TerminalId;
                }

                var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == req.OrderId);
                if (order != null && PaymentSettlementRules.IsSettleable(order.Status))
                {
                    if (req.TipAmount > 0)
                    {
                        order.TipAmount = Money.FromDecimal(req.TipAmount, "EUR");
                        await db.SaveChangesAsync();
                    }
                    long totalCents = Money.FromDecimal(req.Amount + req.TipAmount, "EUR").AmountInCents;
                    var tenders = new List<PaymentTenderRequest>
                    {
                        new(PaymentMethod.RoomCharge, totalCents, totalCents)
                    };
                    await checkoutService.ProcessPaymentTendersAsync(req.OrderId, terminalId, tenders);
                }

                await tableService.UpdateTableStatusAsync(req.TableNumber, TableStatus.Free);

                return Results.Ok(new
                {
                    Success = true,
                    ChargeId = charge.Id,
                    RoomNumber = charge.RoomNumber,
                    GuestName = charge.GuestName,
                    TotalCharged = (charge.Amount + charge.TipAmount).ToDecimal(),
                    Message = Texts.T("messages.room_charge_recorded",
                        ("amount", (charge.Amount + charge.TipAmount).ToDecimal().ToString("0.00", CultureInfo.InvariantCulture)),
                        ("room", charge.RoomNumber), ("guest", charge.GuestName))
                });
            }
            catch (Exception)
            {
                return Results.BadRequest(new { Success = false, Message = Texts.T("errors.room_charge_failed") });
            }
        });
    }
}
