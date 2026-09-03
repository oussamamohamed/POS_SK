using System;
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
using RestaurantPos.Infrastructure.Persistence;

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
            return ok ? Results.Ok(new { Success = true, Message = $"Commande transférée de {tableNumber} vers {req.TargetTableNumber}" }) : Results.BadRequest(new { Success = false, Message = "Échec du transfert de table." });
        });

        tableGroup.MapPost("/{tableNumber}/merge", async (string tableNumber, MergeTablesRequest req, ITableManagementService tableService) =>
        {
            var ok = await tableService.MergeTablesAsync(tableNumber, req.TargetTableNumber);
            return ok ? Results.Ok(new { Success = true, Message = $"Tables {tableNumber} et {req.TargetTableNumber} fusionnées" }) : Results.BadRequest(new { Success = false, Message = "Échec de la fusion de tables." });
        });

        orderGroup.MapPost("/{orderId:guid}/discount", async (Guid orderId, ApplyOrderDiscountRequest req, IOrderDiscountService discountService) =>
        {
            try
            {
                var updated = await discountService.ApplyGlobalDiscountAsync(orderId, req.Type, req.Value, req.Reason, req.OperatorId ?? Guid.Empty);
                return Results.Ok(new { Success = true, TotalTtc = updated.TotalTtc.ToDecimal() });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Success = false, Message = ex.Message });
            }
        });

        orderGroup.MapPost("/{orderId:guid}/items/{itemId:guid}/comp", async (Guid orderId, Guid itemId, CompOrderItemRequest req, IOrderDiscountService discountService) =>
        {
            try
            {
                var updated = await discountService.CompOrderItemAsync(orderId, itemId, req.Reason, req.OperatorId ?? Guid.Empty);
                return Results.Ok(new { Success = true, TotalTtc = updated.TotalTtc.ToDecimal() });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Success = false, Message = ex.Message });
            }
        });

        orderGroup.MapDelete("/{orderId:guid}/discount", async (Guid orderId, IOrderDiscountService discountService) =>
        {
            var updated = await discountService.RemoveDiscountAsync(orderId);
            return Results.Ok(new { Success = true, TotalTtc = updated.TotalTtc.ToDecimal() });
        });

        tableGroup.MapPost("/{tableNumber}/fire-suite", async (string tableNumber, AppDbContext db, IHubContext<KitchenHub> hubContext) =>
        {
            var table = await db.DiningTables.FirstOrDefaultAsync(t => t.TableNumber == tableNumber);
            if (table?.ActiveOrderId is not null)
            {
                var order = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == table.ActiveOrderId.Value);
                if (order is not null)
                {
                    var suiteItems = order.Items.Where(i => i.Course == CourseType.Suite).ToList();
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
                }
            }
            await hubContext.Clients.All.SendAsync("ReceiveKitchenUpdate", "SUITE_CLAIMED", tableNumber);
            return Results.Ok(new { Success = true, Message = $"Réclame suite transmise en cuisine pour la table {tableNumber}" });
        });

        hotelGroup.MapGet("/rooms", async (IRoomBillingService roomService) =>
        {
            var rooms = await roomService.GetAllOccupiedRoomsAsync();
            return Results.Ok(rooms);
        });

        hotelGroup.MapGet("/rooms/{roomNumber}", async (string roomNumber, IRoomBillingService roomService) =>
        {
            var room = await roomService.GetRoomOccupantAsync(roomNumber);
            return room is not null ? Results.Ok(room) : Results.NotFound(new { Message = $"Chambre {roomNumber} introuvable ou non occupée." });
        });

        hotelGroup.MapPost("/room-charge", async (RoomChargeRequest req, IRoomBillingService roomService, ITableManagementService tableService) =>
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

                await tableService.UpdateTableStatusAsync(req.TableNumber, TableStatus.Free);

                return Results.Ok(new
                {
                    Success = true,
                    ChargeId = charge.Id,
                    RoomNumber = charge.RoomNumber,
                    GuestName = charge.GuestName,
                    TotalCharged = (charge.Amount + charge.TipAmount).ToDecimal(),
                    Message = $"Facturation de {(charge.Amount + charge.TipAmount).ToDecimal():F2} € enregistrée sur la chambre {charge.RoomNumber} ({charge.GuestName})"
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Success = false, Message = ex.Message });
            }
        });
    }
}
