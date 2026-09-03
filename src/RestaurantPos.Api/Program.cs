using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using RestaurantPos.Api.Endpoints;
using RestaurantPos.Api.Hubs;
using RestaurantPos.Api.Services;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Security;
using RestaurantPos.Infrastructure.Services;

namespace RestaurantPos.Api;

public partial class Program
{
    public static void Main(string[] args)
    {
        var projectDir = Path.Combine(Directory.GetCurrentDirectory(), "src", "RestaurantPos.Api");
        var contentRoot = Directory.Exists(projectDir) ? projectDir : Directory.GetCurrentDirectory();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = contentRoot,
            WebRootPath = "wwwroot"
        });

        // Core & Persistence Services
        // B4 FIX: Use SQLite as the default persistent store.
        // Fall back to InMemory only in testing / when no connection string is configured.
        var dbConnection = builder.Configuration.GetConnectionString("DefaultConnection")
            ?? "Data Source=restaurantpos.db";

        builder.Services.AddDbContext<AppDbContext>(options =>
        {
            if (builder.Environment.IsEnvironment("Testing"))
            {
                options.UseInMemoryDatabase("RestaurantPosTestDb");
            }
            else
            {
                options.UseSqlite(dbConnection);
            }
        });

        builder.Services.AddMemoryCache();

        builder.Services.AddScoped<IOperatorAuthenticationService, OperatorAuthenticationService>();
        builder.Services.AddScoped<ITableManagementService, TableManagementService>();
        builder.Services.AddScoped<IModifierValidationService, ModifierValidationService>();
        builder.Services.AddScoped<IKitchenRoutingService, KitchenRoutingService>();
        builder.Services.AddScoped<ICatalogSyncService, CatalogSyncService>();
        builder.Services.AddScoped<IBackOfficeCatalogService, BackOfficeCatalogService>();
        builder.Services.AddScoped<IStaffManagementService, StaffManagementService>();
        builder.Services.AddScoped<IPrinterConfigurationService, PrinterConfigurationService>();
        builder.Services.AddScoped<ITerminalLayoutService, TerminalLayoutService>();
        builder.Services.AddScoped<ICheckoutPaymentService, CheckoutPaymentService>();
        builder.Services.AddScoped<INF525FiscalAuditService, NF525FiscalAuditService>();
        builder.Services.AddScoped<IOrderDiscountService, OrderDiscountService>();
        builder.Services.AddScoped<IRoomBillingService, RoomBillingService>();
        builder.Services.AddScoped<IGridManagementService, GridManagementService>();
        builder.Services.AddScoped<IJwtTokenGeneratorService, JwtTokenGeneratorService>();
        builder.Services.AddSingleton<IPinRateLimiterService, PinRateLimiterService>();
        builder.Services.AddHostedService<NetworkDiscoveryBeaconService>();

        // JWT Authentication Configuration
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                var secret = builder.Configuration["Jwt:Secret"] ?? "SuperSecretKeyForRestaurantPosSystemThatIsAtLeast32BytesLong!";
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "RestaurantPos.Api",
                    ValidAudience = builder.Configuration["Jwt:Audience"] ?? "RestaurantPos.Client",
                    IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(secret))
                };

                // Allow SignalR WebSockets & HTTP requests to pass JWT via query parameter, cookie, or dev fallback
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        // 1. Check Query parameter ('access_token' or 'token')
                        var accessToken = context.Request.Query["access_token"].ToString();
                        if (string.IsNullOrEmpty(accessToken))
                        {
                            accessToken = context.Request.Query["token"].ToString();
                        }

                        // 2. Check Cookie ('pos_jwt_token')
                        if (string.IsNullOrEmpty(accessToken))
                        {
                            accessToken = context.Request.Cookies["pos_jwt_token"];
                        }

                        // 3. In Development / Localhost environment: if no auth header/cookie/query is provided,
                        // automatically generate a developer operator session so direct browser testing is never blocked
                        if (string.IsNullOrEmpty(accessToken) &&
                            string.IsNullOrEmpty(context.Request.Headers.Authorization.ToString()) &&
                            (builder.Environment.IsDevelopment() || context.HttpContext.Request.Host.Host is "localhost" or "127.0.0.1" or "::1"))
                        {
                            var tokenGen = context.HttpContext.RequestServices.GetService<IJwtTokenGeneratorService>();
                            if (tokenGen != null)
                            {
                                accessToken = tokenGen.GenerateToken(
                                    Guid.Parse("01a067d9-b8b9-7b6a-8b3c-d272e6128c35"),
                                    "Alexandre Dupont (Manager)",
                                    UserRole.FloorManager);

                                context.HttpContext.Response.Cookies.Append("pos_jwt_token", accessToken, new CookieOptions
                                {
                                    HttpOnly = true,
                                    SameSite = SameSiteMode.Lax,
                                    Secure = false,
                                    Expires = DateTimeOffset.UtcNow.AddHours(12)
                                });
                            }
                        }

                        if (!string.IsNullOrEmpty(accessToken))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("RequireAuthenticatedOperator", policy =>
                policy.RequireAuthenticatedUser());

            options.AddPolicy("RequireManagerOrAdmin", policy =>
                policy.RequireRole("FloorManager", "Admin"));

            options.AddPolicy("RequireKitchenOrAdmin", policy =>
                policy.RequireRole("KitchenStaff", "FloorManager", "Admin"));
        });

        // Real-Time SignalR
        builder.Services.AddSignalR();
        builder.Services.AddEndpointsApiExplorer();

        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
            });
        });

        var app = builder.Build();

        // B4 FIX: Ensure the SQLite database schema is created on first launch.
        // EnsureCreated() is idempotent — safe to call on every startup.
        if (!builder.Environment.IsEnvironment("Testing"))
        {
            using var schemaScope = app.Services.CreateScope();
            var dbContext = schemaScope.ServiceProvider.GetRequiredService<AppDbContext>();
            dbContext.Database.EnsureCreated();
        }

        app.UseCors();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseRouting();
        
        app.UseAuthentication();
        app.UseAuthorization();

        // SignalR Hub Endpoint
        app.MapHub<PosHub>("/hubs/pos");

#if DEBUG
        // Seed Initial Master Data for Local Development & Testing only in Debug mode
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var staffService = scope.ServiceProvider.GetRequiredService<IStaffManagementService>();
            var catalogService = scope.ServiceProvider.GetRequiredService<IBackOfficeCatalogService>();
            var printerService = scope.ServiceProvider.GetRequiredService<IPrinterConfigurationService>();
            var layoutService = scope.ServiceProvider.GetRequiredService<ITerminalLayoutService>();

            SeedDatabase(db, staffService, catalogService, printerService, layoutService).GetAwaiter().GetResult();
        }
#else
        // In Production / Release mode, ensure minimal default administrator without mock test data
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var staffService = scope.ServiceProvider.GetRequiredService<IStaffManagementService>();
            if (!db.Users.Any())
            {
                staffService.CreateStaffMemberAsync("Administrateur", UserRole.Admin, "9999").GetAwaiter().GetResult();
            }
        }
#endif

        // ==================== REST API ENDPOINTS ====================

        // 1. Health
        app.MapGet("/api/health", () => Results.Ok(new { Status = "Healthy", Version = "1.0.0", Timestamp = DateTimeOffset.UtcNow }));

        // 2. Auth PIN Login
        app.MapAuthEndpoints();

        // 3. Catalog Management
        app.MapCatalogEndpoints();

        // 4. Staff Management
        app.MapStaffEndpoints();

        // 5. Printers Management
        app.MapPrinterEndpoints();

        // 6. Tables & Floor Plan
        app.MapTableEndpoints();

        // 7. Kitchen Tickets (KDS)
        app.MapKitchenEndpoints();

        // 8. Checkout, Payments & Fiscal Z
        app.MapCheckoutEndpoints();
        app.MapFiscalEndpoints();

        // 9. Hospitality Features (Table Transfer, Merge, Discounts, Course Fire, Hotel PMS)
        app.MapHospitalityEndpoints();

        // 10. Network Discovery & Standalone Sync (Offline-First Edge POS)
        app.MapSyncEndpoints();

        // 11. Touch Grid Management
        app.MapGridEndpoints();

        app.Run();
    }

#if DEBUG
    private static async Task SeedDatabase(
        AppDbContext db,
        IStaffManagementService staffService,
        IBackOfficeCatalogService catalogService,
        IPrinterConfigurationService printerService,
        ITerminalLayoutService layoutService)
    {
        // Seed Staff
        if (!await db.Users.AnyAsync())
        {
            await staffService.CreateStaffMemberAsync("Alexandre Dupont (Manager)", UserRole.FloorManager, "1234");
            await staffService.CreateStaffMemberAsync("Sophie Martin (Serveuse)", UserRole.Waiter, "2468");
            await staffService.CreateStaffMemberAsync("Thomas Bernard (Chef)", UserRole.KitchenStaff, "5678");
            await staffService.CreateStaffMemberAsync("Admin Système", UserRole.Admin, "9999");
        }

        // Seed Categories & Products
        Product? prodSalade = null;
        Product? prodBurger = null;
        Product? prodPizza = null;
        Product? prodBeer = null;

        if (!await db.Categories.AnyAsync())
        {
            var catEntrees = await catalogService.CreateCategoryAsync("Entrées Fraîches", "#2ECC71", 1, "salad");
            var catPlats = await catalogService.CreateCategoryAsync("Plats & Grillades", "#E74C3C", 2, "meat");
            var catPizzas = await catalogService.CreateCategoryAsync("Pizzas Artisanales", "#E67E22", 3, "pizza");
            var catDesserts = await catalogService.CreateCategoryAsync("Desserts Maison", "#9B59B6", 4, "cake");
            var catBoissons = await catalogService.CreateCategoryAsync("Boissons & Vins", "#3498DB", 5, "glass");

            prodSalade = await catalogService.CreateProductAsync("Salade César Poulet", catEntrees.Id, 9.50m, 10.0m, "Poulet mariné, parmesan, croûtons", "#27AE60", 1, true, "COLD");
            await catalogService.CreateProductAsync("Tartare de Saumon Frais", catEntrees.Id, 12.00m, 10.0m, "Saumon d'Islande, aneth, agrumes", "#2ECC71", 2, false, "COLD");

            prodBurger = await catalogService.CreateProductAsync("Burger Gourmet Rossini", catPlats.Id, 19.50m, 10.0m, "Bœuf charolais, foie gras poêlé", "#C0392B", 1, true, "HOT_KITCHEN");
            await catalogService.CreateProductAsync("Entrecôte Grillée 300g", catPlats.Id, 24.00m, 10.0m, "Frites maison et sauce béarnaise", "#E74C3C", 2, true, "HOT_KITCHEN");

            prodPizza = await catalogService.CreateProductAsync("Pizza Margherita AOP", catPizzas.Id, 12.50m, 10.0m, "Tomates San Marzano, mozzarella fior di latte", "#D35400", 1, true, "HOT_KITCHEN");
            await catalogService.CreateProductAsync("Pizza Reine Royale", catPizzas.Id, 14.50m, 10.0m, "Jambon blanc supérieur, champignons frais", "#E67E22", 2, false, "HOT_KITCHEN");

            await catalogService.CreateProductAsync("Tiramisu Spéculos Maison", catDesserts.Id, 7.50m, 10.0m, "Mascarpone onctueux, café pur arabica", "#8E44AD", 1, true, "DESSERT");
            await catalogService.CreateProductAsync("Fondant Chocolat Valrhona", catDesserts.Id, 8.00m, 10.0m, "Cœur coulant, glace vanille Bourbon", "#9B59B6", 2, false, "DESSERT");

            prodBeer = await catalogService.CreateProductAsync("Bière Artisanale IPA 33cl", catBoissons.Id, 6.00m, 20.0m, "Brasserie locale du terroir", "#2980B9", 1, true, "BAR");
            await catalogService.CreateProductAsync("Verre Bordeaux AOP 12cl", catBoissons.Id, 5.50m, 20.0m, "Grand Cru Saint-Émilion", "#3498DB", 2, false, "BAR");
            await catalogService.CreateProductAsync("Expresso Pur Arabica", catBoissons.Id, 2.50m, 10.0m, "Café torréfaction artisanale", "#1F618D", 3, true, "BAR");
        }

        // Seed Tables with Active Orders for Instant Recall Demo
        if (!await db.DiningTables.AnyAsync())
        {
            var orderT2 = new Order
            {
                Id = UuidV7.NewGuid(),
                TableNumber = "T2",
                Status = OrderStatus.SentToKitchen,
                CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-20)
            };

            if (prodSalade is not null)
            {
                orderT2.Items.Add(new OrderItem
                {
                    Id = UuidV7.NewGuid(),
                    ProductId = prodSalade.Id,
                    ProductName = prodSalade.Name,
                    Quantity = 2,
                    UnitPrice = prodSalade.Price,
                    TaxRatePercent = 10.0m,
                    PreparationStationId = "COLD",
                    IsDispatched = true
                });
            }

            if (prodBurger is not null)
            {
                orderT2.Items.Add(new OrderItem
                {
                    Id = UuidV7.NewGuid(),
                    ProductId = prodBurger.Id,
                    ProductName = prodBurger.Name,
                    Quantity = 1,
                    UnitPrice = prodBurger.Price,
                    TaxRatePercent = 10.0m,
                    PreparationStationId = "HOT_KITCHEN",
                    IsDispatched = true,
                    SelectedModifiers = ["Cuisson : À Point", "Sauce Poivre Vert"]
                });
            }

            var orderT6 = new Order
            {
                Id = UuidV7.NewGuid(),
                TableNumber = "T6",
                Status = OrderStatus.SentToKitchen,
                CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
            };

            if (prodPizza is not null)
            {
                orderT6.Items.Add(new OrderItem
                {
                    Id = UuidV7.NewGuid(),
                    ProductId = prodPizza.Id,
                    ProductName = prodPizza.Name,
                    Quantity = 1,
                    UnitPrice = prodPizza.Price,
                    TaxRatePercent = 10.0m,
                    PreparationStationId = "HOT_KITCHEN",
                    IsDispatched = true
                });
            }

            if (prodBeer is not null)
            {
                orderT6.Items.Add(new OrderItem
                {
                    Id = UuidV7.NewGuid(),
                    ProductId = prodBeer.Id,
                    ProductName = prodBeer.Name,
                    Quantity = 2,
                    UnitPrice = prodBeer.Price,
                    TaxRatePercent = 20.0m,
                    PreparationStationId = "BAR",
                    IsDispatched = true
                });
            }

            db.Orders.AddRange(orderT2, orderT6);

            db.DiningTables.AddRange(
                new DiningTable { TableNumber = "T1", Capacity = 2, Status = TableStatus.Free },
                new DiningTable { TableNumber = "T2", Capacity = 4, Status = TableStatus.Occupied, AssignedWaiterName = "Sophie Martin", CoversCount = 3, ActiveOrderId = orderT2.Id, OpenedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-20) },
                new DiningTable { TableNumber = "T3", Capacity = 6, Status = TableStatus.BillRequested, AssignedWaiterName = "Alexandre Dupont", CoversCount = 4 },
                new DiningTable { TableNumber = "T4", Capacity = 2, Status = TableStatus.Free },
                new DiningTable { TableNumber = "T5", Capacity = 8, Status = TableStatus.Free },
                new DiningTable { TableNumber = "T6", Capacity = 4, Status = TableStatus.Occupied, AssignedWaiterName = "Sophie Martin", CoversCount = 2, ActiveOrderId = orderT6.Id, OpenedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10) },
                new DiningTable { TableNumber = "T7", Capacity = 2, Status = TableStatus.Free },
                new DiningTable { TableNumber = "T8", Capacity = 4, Status = TableStatus.Paid, AssignedWaiterName = "Alexandre Dupont", CoversCount = 2 }
            );
            await db.SaveChangesAsync();
        }

        // Seed Printers
        if (!await db.PrinterConfigurations.AnyAsync())
        {
            await printerService.RegisterPrinterAsync(new PrinterRegistrationRequest(
                "Imprimante Caisse Comptoir", "192.168.1.200", 9100, 80, true, ["RECEIPT"]));
            await printerService.RegisterPrinterAsync(new PrinterRegistrationRequest(
                "Imprimante Cuisine Chaude", "192.168.1.201", 9100, 80, false, ["HOT_KITCHEN", "GRILL"]));
            await printerService.RegisterPrinterAsync(new PrinterRegistrationRequest(
                "Imprimante Bar & Boissons", "192.168.1.202", 9100, 80, false, ["BAR"]));
        }

        // Seed Layout Profile
        await layoutService.GetActiveProfileAsync("MAIN_TERMINAL");

        // Seed Hotel Rooms (PMS)
        if (!await db.HotelRooms.AnyAsync())
        {
            db.HotelRooms.AddRange(
                new HotelRoomResident { RoomNumber = "101", GuestName = "Jean Dujardin", CheckInDateUtc = DateTimeOffset.UtcNow.AddDays(-1), CheckOutDateUtc = DateTimeOffset.UtcNow.AddDays(2), IsOccupied = true, MaxCreditLimit = 300.0m },
                new HotelRoomResident { RoomNumber = "204", GuestName = "Alexandre Dupont", CheckInDateUtc = DateTimeOffset.UtcNow.AddDays(-2), CheckOutDateUtc = DateTimeOffset.UtcNow.AddDays(3), IsOccupied = true, MaxCreditLimit = 600.0m },
                new HotelRoomResident { RoomNumber = "305", GuestName = "Sophie Marceau", CheckInDateUtc = DateTimeOffset.UtcNow.AddDays(-1), CheckOutDateUtc = DateTimeOffset.UtcNow.AddDays(1), IsOccupied = true, MaxCreditLimit = 450.0m }
            );
            await db.SaveChangesAsync();
        }
    }
#endif
}
public record CreateCategoryRequest(string Name, string? ColorHex, int DisplayOrder, string? IconName);
public record UpdateCategoryRequest(string Name, string? ColorHex, int DisplayOrder, string? IconName, bool? IsActive);
public record CreateProductRequest(string Name, string CategoryId, decimal Price, decimal TaxRatePercent, string? Description, string? ColorHex, int DisplayOrder, bool IsQuickKey, string? StationId);
public record UpdateProductRequest(string Name, string CategoryId, decimal Price, decimal TaxRatePercent, string? Description, string? ColorHex, int DisplayOrder, bool? IsAvailable, bool? IsActive, bool IsQuickKey, string? StationId);
public record CreateStaffRequest(string Name, string Role, string Pin);
public record UpdateStaffRequest(string Name, string Role, string? Pin, bool? IsActive);
public record CreateTableRequest(string TableNumber, int Capacity, double? PositionX, double? PositionY);
public record UpdatePrinterRequest(string Name, string IpAddress, int Port, int PaperWidthMm, bool HasCashDrawer, List<string> TargetStations, bool? IsActive);
public record OpenTableRequest(string? WaiterName, int CoversCount, Guid? OperatorId);
public record ZClosureRequest(string TerminalId, Guid ManagerId, string ManagerName);
public record AddOrderItemsRequest(List<OrderItemInputDto> Items);
public record PaymentSettlementRequest(Guid OrderId, string TableNumber, Guid OperatorId, List<TenderItemRequest> Tenders);
public record TenderItemRequest(PaymentMethod Method, decimal Amount, decimal Tendered, decimal ChangeGiven);
public record TransferTableRequest(string TargetTableNumber);
public record MergeTablesRequest(string TargetTableNumber);
public record ApplyOrderDiscountRequest(DiscountType Type, decimal Value, string Reason, Guid? OperatorId);
public record CompOrderItemRequest(string Reason, Guid? OperatorId);
public record RoomChargeRequest(Guid OrderId, string TableNumber, string RoomNumber, string GuestName, decimal Amount, decimal TipAmount, string? SignatureDataUrl, string? Notes);
public record SyncBatchRequest(List<SyncMessageItemDto> Messages);
public record SyncMessageItemDto(Guid Id, string? TerminalId, string? EventType, string? IdempotencyKey, string PayloadJson, DateTimeOffset CreatedAtUtc);


