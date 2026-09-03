using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;

namespace RestaurantPos.Api.Endpoints;

public static class PrinterEndpoints
{
    public static void MapPrinterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/printers")
                       .WithTags("Printers")
                       .RequireAuthorization();

        group.MapGet("/", async (IPrinterConfigurationService printerService) =>
        {
            var list = await printerService.GetAllPrintersAsync();
            return Results.Ok(list);
        }).AllowAnonymous();

        group.MapPost("/", async (PrinterRegistrationRequest req, IPrinterConfigurationService printerService) =>
        {
            var created = await printerService.RegisterPrinterAsync(req);
            return Results.Ok(created);
        });

        group.MapPut("/{id:guid}", async (Guid id, UpdatePrinterRequest req, IPrinterConfigurationService printerService) =>
        {
            var regReq = new PrinterRegistrationRequest(req.Name, req.IpAddress, req.Port, req.PaperWidthMm, req.HasCashDrawer, req.TargetStations ?? []);
            var updated = await printerService.UpdatePrinterAsync(id, regReq, req.IsActive ?? true);
            return Results.Ok(updated);
        });

        group.MapPost("/{id:guid}/test", async (Guid id, IPrinterConfigurationService printerService) =>
        {
            var result = await printerService.SendTestPrintAsync(id);
            return Results.Ok(result);
        });
    }
}
