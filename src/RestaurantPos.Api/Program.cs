using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using RestaurantPos.Api.Endpoints;
using RestaurantPos.Api.Hubs;
using RestaurantPos.Api.Services;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Localization;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Printing;
using RestaurantPos.Infrastructure.Security;
using RestaurantPos.Infrastructure.Services;

namespace RestaurantPos.Api;

public partial class Program
{
    /// <summary>Applique une instruction de schéma idempotente. « Colonne ou objet déjà présent » est attendu à chaque démarrage ; toute autre erreur (base verrouillée, disque plein…) est journalisée.</summary>
    private static void ApplySchema(AppDbContext db, ILogger log, string sql)
    {
        try { db.Database.ExecuteSqlRaw(sql); }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase)) { }
        catch (Exception ex) when (ex is not OperationCanceledException) { SchemaLogMessages.StatementFailed(log, ex, sql.Length > 80 ? sql[..80] : sql); }
    }

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

        builder.Services.AddSingleton<FiscalImmutabilityInterceptor>();
        builder.Services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.AddInterceptors(sp.GetRequiredService<FiscalImmutabilityInterceptor>());
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

        // Langue des messages : seule la culture d'interface suit Accept-Language.
        // CurrentCulture reste « en » pour que nombres, dates et hash ne changent jamais de format.
        builder.Services.Configure<RequestLocalizationOptions>(options =>
        {
            options.DefaultRequestCulture = new RequestCulture(culture: "en", uiCulture: "en");
            options.SupportedCultures = [CultureInfo.GetCultureInfo("en")];
            options.SupportedUICultures = Texts.SupportedLanguages.Select(CultureInfo.GetCultureInfo).ToList();
            options.RequestCultureProviders = [new AcceptLanguageHeaderRequestCultureProvider()];
        });

        builder.Services.AddScoped<IOperatorAuthenticationService, OperatorAuthenticationService>();
        builder.Services.AddScoped<ITableManagementService, TableManagementService>();
        builder.Services.AddScoped<IModifierValidationService, ModifierValidationService>();
        builder.Services.AddScoped<IKitchenRoutingService, KitchenRoutingService>();
        builder.Services.AddScoped<ICatalogSyncService, CatalogSyncService>();
        builder.Services.AddScoped<IBackOfficeCatalogService, BackOfficeCatalogService>();
        builder.Services.AddScoped<IStaffManagementService, StaffManagementService>();
        builder.Services.AddScoped<IPrinterConfigurationService, PrinterConfigurationService>();
        builder.Services.AddSingleton<IPrinterTransport, EscPosPrinterTransport>();
        builder.Services.AddSingleton<PrintSignal>();
        builder.Services.AddSingleton<PrinterStatusTracker>();
        builder.Services.AddSingleton<IPrinterStatusNotifier, SignalRPrinterStatusNotifier>();
        builder.Services.AddSingleton<PrintQueueProcessor>();
        builder.Services.AddScoped<PrintQueue>();
        builder.Services.AddScoped<ReportPrintDataService>();
        builder.Services.AddScoped<PrintDispatcher>();
        builder.Services.AddScoped<ITerminalLayoutService, TerminalLayoutService>();
        builder.Services.AddScoped<ICheckoutPaymentService, CheckoutPaymentService>();
        builder.Services.AddScoped<INF525FiscalAuditService, NF525FiscalAuditService>();
        builder.Services.AddScoped<IFiscalJournal, FiscalJournalService>();
        builder.Services.AddScoped<IFecExportService, FecExportService>();
        builder.Services.AddScoped<IFinancialDashboardService, FinancialDashboardService>();
        builder.Services.AddScoped<IMenuAnalysisService, MenuAnalysisService>();
        builder.Services.AddScoped<IReservationService, ReservationService>();
        builder.Services.AddScoped<IRecipeService, RecipeService>();
        builder.Services.AddScoped<IStockService, StockService>();
        builder.Services.AddScoped<IAlertService, AlertService>();
        builder.Services.AddScoped<ICashSessionService, CashSessionService>();
        builder.Services.AddScoped<IInsightsService, InsightsService>();
        builder.Services.AddScoped<IOrderDiscountService, OrderDiscountService>();
        builder.Services.AddScoped<IRoomBillingService, RoomBillingService>();
        builder.Services.AddScoped<IGridManagementService, GridManagementService>();
        builder.Services.AddScoped<IHeldOrderStorageService, HeldOrderStorageService>();
        builder.Services.AddSingleton<ITakeawayCounterService, TakeawayCounterService>();
        builder.Services.AddScoped<IMealVoucherPolicyService, MealVoucherPolicyService>();
        builder.Services.AddScoped<IHappyHourPricingService, HappyHourPricingService>();
        builder.Services.AddScoped<IFiscalChainVerificationService, FiscalChainVerificationService>();
        builder.Services.AddScoped<IFiscalArchiveService, FiscalArchiveService>();
        builder.Services.AddScoped<IJwtTokenGeneratorService, JwtTokenGeneratorService>();
        builder.Services.AddSingleton<IPinRateLimiterService, PinRateLimiterService>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<IDeviceService, DeviceService>();
        builder.Services.AddScoped<IRestaurantSettingsService, RestaurantSettingsService>();
        if (!builder.Environment.IsEnvironment("Testing"))
        {
            builder.Services.AddHostedService<BonjourAdvertiserService>();
            builder.Services.AddHostedService<PrintWorker>();
            builder.Services.AddHostedService<ReservationPurgeService>();
        }

        // JWT Authentication Configuration
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                var secret = builder.Configuration["Jwt:Secret"];
                if (string.IsNullOrWhiteSpace(secret) || (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing") && secret.StartsWith("ProdSecretKey", StringComparison.OrdinalIgnoreCase)))
                {
                    if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
                        throw new InvalidOperationException("'Jwt:Secret' must be securely configured via environment variable in non-development environments.");
                    // Pas de clé publique en repli : une clé aléatoire par démarrage ne permet de forger aucun jeton.
                    secret = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
                }
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

                // Allow SignalR WebSockets & HTTP requests to pass JWT via query parameter or cookie
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

            options.AddPolicy("RequireAdmin", policy =>
                policy.RequireRole("Admin"));

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
            var schemaLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RestaurantPos.Schema");
            dbContext.Database.EnsureCreated();
            ApplySchema(dbContext, schemaLog, "ALTER TABLE Products ADD COLUMN IsFoodVoucherEligible INTEGER NOT NULL DEFAULT 1;");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE Products ADD COLUMN TaxRateTakeawayPercent TEXT NULL;");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE Products ADD COLUMN Cost INTEGER NULL;");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE Products ADD COLUMN AllergenMask INTEGER NOT NULL DEFAULT 0;");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE Products ADD COLUMN AllergensDeclared INTEGER NOT NULL DEFAULT 0;");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE Products ADD COLUMN AllergensOverridden INTEGER NOT NULL DEFAULT 0;");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE Products ADD COLUMN AutoUnavailable INTEGER NOT NULL DEFAULT 0;");
            ApplySchema(dbContext, schemaLog, @"CREATE TABLE IF NOT EXISTS Reservations (
                Id TEXT PRIMARY KEY,
                GuestName TEXT NOT NULL,
                Covers INTEGER NOT NULL,
                StartsAtUtc TEXT NOT NULL,
                DurationMinutes INTEGER NOT NULL,
                TableNumber TEXT NULL,
                Note TEXT NULL,
                Phone TEXT NULL,
                Status INTEGER NOT NULL DEFAULT 0,
                IsWaitlist INTEGER NOT NULL DEFAULT 0,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );");
            ApplySchema(dbContext, schemaLog, "CREATE INDEX IF NOT EXISTS IX_Reservations_StartsAtUtc ON Reservations (StartsAtUtc);");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE Reservations ADD COLUMN QuoteMinutes INTEGER NULL;");
            ApplySchema(dbContext, schemaLog, @"CREATE TABLE IF NOT EXISTS Ingredients (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Unit TEXT NOT NULL,
                UnitCostMilliCents INTEGER NOT NULL DEFAULT 0,
                IsPreparation INTEGER NOT NULL DEFAULT 0,
                YieldMilli INTEGER NULL,
                AllergenMask INTEGER NOT NULL DEFAULT 0,
                LowStockMilli INTEGER NULL,
                IsActive INTEGER NOT NULL DEFAULT 1
            );");
            ApplySchema(dbContext, schemaLog, @"CREATE TABLE IF NOT EXISTS RecipeLines (
                Id TEXT PRIMARY KEY,
                OwnerKind TEXT NOT NULL,
                OwnerId TEXT NOT NULL,
                IngredientId TEXT NOT NULL,
                QuantityMilli INTEGER NOT NULL
            );");
            ApplySchema(dbContext, schemaLog, "CREATE UNIQUE INDEX IF NOT EXISTS IX_RecipeLines_OwnerKind_OwnerId_IngredientId ON RecipeLines (OwnerKind, OwnerId, IngredientId);");
            ApplySchema(dbContext, schemaLog, "CREATE INDEX IF NOT EXISTS IX_RecipeLines_IngredientId ON RecipeLines (IngredientId);");
            ApplySchema(dbContext, schemaLog, @"CREATE TABLE IF NOT EXISTS StockMovements (
                Id TEXT PRIMARY KEY,
                IngredientId TEXT NOT NULL,
                Kind TEXT NOT NULL,
                QuantityMilli INTEGER NOT NULL,
                UnitCostMilliCents INTEGER NULL,
                OrderId TEXT NULL,
                OperatorId TEXT NULL,
                Note TEXT NULL,
                CreatedAtUtc TEXT NOT NULL
            );");
            ApplySchema(dbContext, schemaLog, "CREATE INDEX IF NOT EXISTS IX_StockMovements_IngredientId ON StockMovements (IngredientId);");
            ApplySchema(dbContext, schemaLog, "CREATE INDEX IF NOT EXISTS IX_StockMovements_OrderId ON StockMovements (OrderId);");
            // Une vente ne s'écrit qu'une fois par commande et ingrédient ; les mouvements sont en ajout seulement (voir StockSchema).
            foreach (var statement in StockSchema.Statements) { ApplySchema(dbContext, schemaLog, statement); }
            ApplySchema(dbContext, schemaLog, @"CREATE TABLE IF NOT EXISTS ModifierOptionConsumptions (
                Id TEXT PRIMARY KEY,
                OptionId TEXT NOT NULL,
                IngredientId TEXT NOT NULL,
                DeltaQuantityMilli INTEGER NOT NULL
            );");
            ApplySchema(dbContext, schemaLog, "CREATE UNIQUE INDEX IF NOT EXISTS IX_ModifierOptionConsumptions_OptionId_IngredientId ON ModifierOptionConsumptions (OptionId, IngredientId);");
            ApplySchema(dbContext, schemaLog, @"CREATE TABLE IF NOT EXISTS CashSessions (
                Id TEXT PRIMARY KEY,
                TerminalId TEXT NOT NULL,
                OpenedAtUtc TEXT NOT NULL,
                OpenedByOperatorId TEXT NOT NULL,
                OpeningFloatCents INTEGER NOT NULL
            );");
            ApplySchema(dbContext, schemaLog, "CREATE INDEX IF NOT EXISTS IX_CashSessions_TerminalId ON CashSessions (TerminalId);");
            ApplySchema(dbContext, schemaLog, @"CREATE TABLE IF NOT EXISTS CashMovements (
                Id TEXT PRIMARY KEY,
                SessionId TEXT NOT NULL,
                Kind TEXT NOT NULL,
                AmountCents INTEGER NOT NULL,
                Reason TEXT NOT NULL,
                OperatorId TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL
            );");
            ApplySchema(dbContext, schemaLog, "CREATE INDEX IF NOT EXISTS IX_CashMovements_SessionId ON CashMovements (SessionId);");
            ApplySchema(dbContext, schemaLog, @"CREATE TABLE IF NOT EXISTS CashCounts (
                Id TEXT PRIMARY KEY,
                SessionId TEXT NOT NULL,
                ClosedAtUtc TEXT NOT NULL,
                ClosedByOperatorId TEXT NOT NULL,
                CountedCents INTEGER NOT NULL,
                ExpectedCents INTEGER NOT NULL,
                VarianceCents INTEGER NOT NULL,
                DenominationsJson TEXT NULL,
                VarianceNote TEXT NULL
            );");
            ApplySchema(dbContext, schemaLog, "CREATE UNIQUE INDEX IF NOT EXISTS IX_CashCounts_SessionId ON CashCounts (SessionId);");
            ApplySchema(dbContext, schemaLog, @"CREATE TABLE IF NOT EXISTS AlertRules (
                Type TEXT PRIMARY KEY,
                Threshold INTEGER NOT NULL,
                Enabled INTEGER NOT NULL DEFAULT 0,
                UpdatedAtUtc TEXT NOT NULL,
                UpdatedBy TEXT NULL
            );");
            ApplySchema(dbContext, schemaLog, @"CREATE TABLE IF NOT EXISTS Alerts (
                Id TEXT PRIMARY KEY,
                RuleType TEXT NOT NULL,
                OperatorId TEXT NULL,
                TerminalId TEXT NOT NULL,
                PeriodKey TEXT NOT NULL,
                DedupeKey TEXT NOT NULL,
                DetailJson TEXT NOT NULL,
                State TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                ResolvedAtUtc TEXT NULL,
                ResolvedBy TEXT NULL,
                ResolutionNote TEXT NULL
            );");
            ApplySchema(dbContext, schemaLog, "CREATE UNIQUE INDEX IF NOT EXISTS IX_Alerts_DedupeKey ON Alerts (DedupeKey);");
            foreach (var statement in CashSchema.Statements) { ApplySchema(dbContext, schemaLog, statement); }
            ApplySchema(dbContext, schemaLog, "ALTER TABLE OrderItems ADD COLUMN IsFoodVoucherEligible INTEGER NOT NULL DEFAULT 1;");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE OrderItems ADD COLUMN TaxRateTakeawayPercent TEXT NULL;");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE Orders ADD COLUMN Destination INTEGER NOT NULL DEFAULT 0;");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE Orders ADD COLUMN PickupNumber TEXT NULL;");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE Orders ADD COLUMN PickupBuzzer TEXT NULL;");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE Orders ADD COLUMN PickupScheduledAtUtc TEXT NULL;");
            ApplySchema(dbContext, schemaLog, @"CREATE TABLE IF NOT EXISTS HeldOrders (
                Id TEXT PRIMARY KEY,
                TerminalId TEXT NOT NULL,
                OrderId TEXT NOT NULL,
                CustomerLabel TEXT,
                Destination INTEGER NOT NULL DEFAULT 1,
                ItemCount INTEGER NOT NULL DEFAULT 0,
                TotalTtc INTEGER NOT NULL,
                OrderSnapshotJson TEXT NOT NULL,
                HeldAtUtc TEXT NOT NULL,
                HeldByStaffId TEXT NOT NULL,
                IsRecalled INTEGER NOT NULL DEFAULT 0,
                RecalledAtUtc TEXT,
                IsVoided INTEGER NOT NULL DEFAULT 0,
                VoidedAtUtc TEXT,
                VoidReason TEXT,
                VoidedByStaffId TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_HeldOrders_TerminalId_IsRecalled_IsVoided ON HeldOrders(TerminalId, IsRecalled, IsVoided);");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE OrderItems ADD COLUMN IsHappyHourApplied INTEGER NOT NULL DEFAULT 0;");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE OrderItems ADD COLUMN OriginalUnitPrice INTEGER NULL;");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE OrderItems ADD COLUMN AppliedHappyHourScheduleId TEXT NULL;");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE OrderItems ADD COLUMN OrderedAtUtc TEXT NOT NULL DEFAULT '';");
            ApplySchema(dbContext, schemaLog, @"CREATE TABLE IF NOT EXISTS HappyHourSchedules (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                DaysOfWeek TEXT NOT NULL,
                StartTime TEXT NOT NULL,
                EndTime TEXT NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                AppliesToTakeaway INTEGER NOT NULL DEFAULT 0,
                Priority INTEGER NOT NULL DEFAULT 1,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT
            );");
            ApplySchema(dbContext, schemaLog, @"CREATE TABLE IF NOT EXISTS HappyHourPriceRules (
                Id TEXT PRIMARY KEY,
                ScheduleId TEXT NOT NULL,
                TargetType INTEGER NOT NULL,
                TargetId TEXT NOT NULL,
                TargetName TEXT NOT NULL,
                PricingMode INTEGER NOT NULL,
                FixedPrice INTEGER,
                DiscountPercent TEXT,
                CreatedAtUtc TEXT NOT NULL,
                FOREIGN KEY(ScheduleId) REFERENCES HappyHourSchedules(Id) ON DELETE CASCADE
            );");
            ApplySchema(dbContext, schemaLog, @"CREATE TABLE IF NOT EXISTS HappyHourOverrideSessions (
                Id TEXT PRIMARY KEY,
                TerminalId TEXT NOT NULL,
                OperatorId TEXT NOT NULL,
                OperatorName TEXT NOT NULL,
                OverrideType INTEGER NOT NULL,
                StartsAtUtc TEXT NOT NULL,
                ExpiresAtUtc TEXT NOT NULL,
                Reason TEXT NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedAtUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_HappyHourOverrideSessions_TerminalId_IsActive ON HappyHourOverrideSessions(TerminalId, IsActive);");
            ApplySchema(dbContext, schemaLog, @"CREATE TABLE IF NOT EXISTS Devices (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Role INTEGER NOT NULL,
                TerminalId TEXT NOT NULL,
                TokenHash TEXT NOT NULL,
                PairedAtUtc TEXT NOT NULL,
                LastSeenUtc TEXT NULL,
                RevokedAtUtc TEXT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Devices_TerminalId ON Devices(TerminalId);
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Devices_TokenHash ON Devices(TokenHash);");
            ApplySchema(dbContext, schemaLog, @"CREATE TABLE IF NOT EXISTS DevicePairingCodes (
                Id TEXT PRIMARY KEY,
                CodeHash TEXT NOT NULL,
                Name TEXT NOT NULL,
                Role INTEGER NOT NULL,
                ExpiresAtUtc TEXT NOT NULL,
                UsedAtUtc TEXT NULL,
                CreatedByOperatorId TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_DevicePairingCodes_CodeHash ON DevicePairingCodes(CodeHash);");
            ApplySchema(dbContext, schemaLog, @"CREATE TABLE IF NOT EXISTS RestaurantSettings (
                Id INTEGER PRIMARY KEY,
                ReceiptLanguage TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE RestaurantSettings ADD COLUMN KitchenTicketLanguage TEXT NULL;");
            ApplySchema(dbContext, schemaLog, "UPDATE RestaurantSettings SET KitchenTicketLanguage = ReceiptLanguage WHERE KitchenTicketLanguage IS NULL;");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE RestaurantSettings ADD COLUMN CompanyName TEXT NOT NULL DEFAULT 'RESTAURANT L''ANTIGRAVITE';");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE RestaurantSettings ADD COLUMN AddressLines TEXT NOT NULL DEFAULT '12 Rue de la Gastronomie\n75001 Paris';");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE RestaurantSettings ADD COLUMN Siret TEXT NOT NULL DEFAULT '88877766600012';");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE RestaurantSettings ADD COLUMN VatNumber TEXT NOT NULL DEFAULT 'FR12888777666';");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE RestaurantSettings ADD COLUMN CertificateNumber TEXT NULL;");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE RestaurantSettings ADD COLUMN FiscalYearStartMonth INTEGER NOT NULL DEFAULT 1;");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE RestaurantSettings ADD COLUMN FiscalYearStartDay INTEGER NOT NULL DEFAULT 1;");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE RestaurantSettings ADD COLUMN AutoUnavailableOnStockOut INTEGER NOT NULL DEFAULT 0;");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE RestaurantSettings ADD COLUMN PrintAllergensOnKitchenTicket INTEGER NOT NULL DEFAULT 0;");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE RestaurantSettings ADD COLUMN AllergenDisclaimer TEXT NULL;");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE RestaurantSettings ADD COLUMN TrendThresholdPercent INTEGER NOT NULL DEFAULT 15;");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE RestaurantSettings ADD COLUMN CashVarianceNoteRequired INTEGER NOT NULL DEFAULT 1;");
            ApplySchema(dbContext, schemaLog, @"CREATE TABLE IF NOT EXISTS PeriodClosures (
                Id TEXT PRIMARY KEY,
                TerminalId TEXT NOT NULL,
                PeriodType INTEGER NOT NULL,
                PeriodKey TEXT NOT NULL,
                ClosureSequence INTEGER NOT NULL,
                PeriodStartUtc TEXT NOT NULL,
                PeriodEndUtc TEXT NOT NULL,
                TotalTtcAmount INTEGER NOT NULL,
                TotalHtAmount INTEGER NOT NULL,
                TaxesSummaryJson TEXT NOT NULL,
                TenderTotalsJson TEXT NOT NULL,
                PerpetualGrandTotalCents INTEGER NOT NULL,
                DailyClosureCount INTEGER NOT NULL,
                PreviousSignatureHash TEXT NOT NULL,
                SignatureHash TEXT NOT NULL,
                SealedByUserId TEXT NOT NULL,
                SealedByUserName TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL
            );");
            ApplySchema(dbContext, schemaLog, "CREATE UNIQUE INDEX IF NOT EXISTS IX_PeriodClosures_Terminal_Type_Key ON PeriodClosures (TerminalId, PeriodType, PeriodKey);");
            ApplySchema(dbContext, schemaLog, @"CREATE TABLE IF NOT EXISTS FiscalArchives (
                Id TEXT PRIMARY KEY,
                PeriodClosureId TEXT NOT NULL,
                PeriodType INTEGER NOT NULL,
                PeriodKey TEXT NOT NULL,
                FileName TEXT NOT NULL,
                FileSha256 TEXT NOT NULL,
                FileSizeBytes INTEGER NOT NULL,
                ArchiveSequence INTEGER NOT NULL,
                PreviousSignatureHash TEXT NOT NULL,
                SignatureHash TEXT NOT NULL,
                CreatedByUserId TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_FiscalArchives_PeriodClosureId ON FiscalArchives (PeriodClosureId);
            CREATE UNIQUE INDEX IF NOT EXISTS IX_FiscalArchives_ArchiveSequence ON FiscalArchives (ArchiveSequence);");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE Categories ADD COLUMN PreparationStationId TEXT NULL;");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE Devices ADD COLUMN ReceiptPrinterId TEXT NULL;");
            ApplySchema(dbContext, schemaLog, @"CREATE TABLE IF NOT EXISTS PrintJobs (
                Id TEXT PRIMARY KEY,
                PrinterId TEXT NOT NULL,
                Kind INTEGER NOT NULL,
                DocumentJson TEXT NOT NULL,
                OpenCashDrawer INTEGER NOT NULL,
                Status INTEGER NOT NULL,
                Attempts INTEGER NOT NULL,
                NextAttemptAtUtc TEXT NOT NULL,
                DeadlineAtUtc TEXT NOT NULL,
                LastError TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                SentAtUtc TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_PrintJobs_Status_NextAttemptAtUtc ON PrintJobs(Status, NextAttemptAtUtc);");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE PrinterConfigurations ADD COLUMN TextMode INTEGER NOT NULL DEFAULT 0;");
            ApplySchema(dbContext, schemaLog, "CREATE INDEX IF NOT EXISTS IX_FiscalReceipts_VoidedReceiptId ON FiscalReceipts (VoidedReceiptId);");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE JournalEntries ADD COLUMN ChainSequence INTEGER NULL;");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE JournalEntries ADD COLUMN PreviousHash TEXT NULL;");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE JournalEntries ADD COLUMN OperatorId TEXT NULL;");
            ApplySchema(dbContext, schemaLog, "CREATE UNIQUE INDEX IF NOT EXISTS IX_JournalEntries_ChainSequence ON JournalEntries (ChainSequence) WHERE ChainSequence IS NOT NULL;");
            // TerminalId est devenu nullable (événements sans terminal) : SQLite ne sait pas
            // retirer NOT NULL, on reconstruit la table une seule fois.
            try
            {
                var terminalIdNotNull = dbContext.Database
                    .SqlQueryRaw<int>("SELECT \"notnull\" AS Value FROM pragma_table_info('JournalEntries') WHERE name = 'TerminalId'")
                    .AsEnumerable().FirstOrDefault();
                if (terminalIdNotNull == 1)
                {
                    using var tx = dbContext.Database.BeginTransaction();
                    dbContext.Database.ExecuteSqlRaw(@"CREATE TABLE JournalEntries_new (
                        Id TEXT NOT NULL CONSTRAINT PK_JournalEntries PRIMARY KEY,
                        LocalSequence INTEGER NOT NULL,
                        TerminalId TEXT NULL,
                        OccurredAtUtc TEXT NOT NULL,
                        IdempotencyKey TEXT NOT NULL,
                        EventType TEXT NOT NULL,
                        PayloadJson TEXT NOT NULL,
                        EntryHash TEXT NOT NULL,
                        ChainSequence INTEGER NULL,
                        PreviousHash TEXT NULL,
                        OperatorId TEXT NULL);
                    INSERT INTO JournalEntries_new SELECT Id, LocalSequence, TerminalId, OccurredAtUtc, IdempotencyKey, EventType, PayloadJson, EntryHash, ChainSequence, PreviousHash, OperatorId FROM JournalEntries;
                    DROP TABLE JournalEntries;
                    ALTER TABLE JournalEntries_new RENAME TO JournalEntries;
                    CREATE UNIQUE INDEX IX_JournalEntries_ChainSequence ON JournalEntries (ChainSequence) WHERE ChainSequence IS NOT NULL;");
                    tx.Commit();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { SchemaLogMessages.StatementFailed(schemaLog, ex, "JournalEntries rebuild"); }
            ApplySchema(dbContext, schemaLog, "ALTER TABLE PrintJobs ADD COLUMN DuplicateOfDocumentId TEXT NULL;");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE FiscalReceipts ADD COLUMN HtBreakdownJson TEXT NULL;");
            ApplySchema(dbContext, schemaLog, "ALTER TABLE PrintJobs ADD COLUMN DuplicateNumber INTEGER NULL;");
            ApplySchema(dbContext, schemaLog, "CREATE INDEX IF NOT EXISTS IX_PrintJobs_DuplicateOfDocumentId ON PrintJobs (DuplicateOfDocumentId);");

            // Initialise ReceiptLanguage au démarrage plutôt qu'à la première lecture paresseuse :
            // sinon une installation EN/AR fraîche qui prend des commandes avant l'ouverture de
            // l'admin se retrouve avec "fr" persisté (règle : fr si des commandes existent déjà, sinon en).
            schemaScope.ServiceProvider.GetRequiredService<IRestaurantSettingsService>().GetAsync().GetAwaiter().GetResult();
        }

        app.UseCors();
        app.UseRequestLocalization();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();

        // SignalR Hub Endpoints
        app.MapHub<PosHub>("/hubs/pos");
        app.MapHub<RestaurantPos.Api.Hubs.KitchenHub>("/hubs/kitchen");

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
        // En Production / Release : un seul administrateur, sans PIN connu, sans données de test
        using (var scope = app.Services.CreateScope())
        {
            BootstrapAdminAsync(
                scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                scope.ServiceProvider.GetRequiredService<IStaffManagementService>(),
                app.Configuration,
                app.Logger).GetAwaiter().GetResult();
        }
#endif

        if (args.Contains("--seed-prod-data") || args.Contains("--seed-test-data"))
        {
            if (!IsSeedCliAllowed(app.Environment))
            {
                Console.Error.WriteLine("ERREUR : le seed de test est réservé à l'environnement Development.");
                Environment.Exit(1);
                return;
            }

            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var staffService = scope.ServiceProvider.GetRequiredService<IStaffManagementService>();
            var catalogService = scope.ServiceProvider.GetRequiredService<IBackOfficeCatalogService>();
            var printerService = scope.ServiceProvider.GetRequiredService<IPrinterConfigurationService>();
            var layoutService = scope.ServiceProvider.GetRequiredService<ITerminalLayoutService>();

            SeedDatabase(db, staffService, catalogService, printerService, layoutService, force: true).GetAwaiter().GetResult();
            Console.WriteLine("[SEED] Production test data seeded successfully.");
            return;
        }

        // ==================== REST API ENDPOINTS ====================

        // 1. Health
        app.MapGet("/api/health", () => Results.Ok(new { Status = "Healthy", Version = "1.0.0", Timestamp = DateTimeOffset.UtcNow }));

        // Admin Seed Endpoint for Production Verification
        if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
        {
            app.MapPost("/api/admin/seed-test-data", async (
                AppDbContext db,
                IStaffManagementService staffService,
                IBackOfficeCatalogService catalogService,
                IPrinterConfigurationService printerService,
                ITerminalLayoutService layoutService) =>
            {
                await SeedDatabase(db, staffService, catalogService, printerService, layoutService, force: true);
                return Results.Ok(new { Success = true, Message = Texts.T("messages.test_data_seeded") });
            }).RequireAuthorization("RequireAdmin");
        }

        // 2. Auth PIN Login
        app.MapAuthEndpoints();

        // 3. Catalog Management
        app.MapCatalogEndpoints();

        // 4. Staff Management
        app.MapStaffEndpoints();

        // 5. Printers Management
        app.MapPrinterEndpoints();
        app.MapPrintJobEndpoints();
        app.MapSettingsEndpoints();

        // 6. Tables & Floor Plan
        app.MapTableEndpoints();

        // 7. Kitchen Tickets (KDS)
        app.MapKitchenEndpoints();

        // 8. Checkout, Payments & Fiscal Z
        app.MapCheckoutEndpoints();
        app.MapFiscalEndpoints();
        app.MapDashboardEndpoints();
        app.MapMenuAnalysisEndpoints();
        app.MapReservationEndpoints();
        app.MapStockEndpoints();
        app.MapCashEndpoints();
        app.MapAlertEndpoints();
        app.MapCounterSaleEndpoints();
        app.MapHappyHourEndpoints();

        // 9. Hospitality Features (Table Transfer, Merge, Discounts, Course Fire, Hotel PMS)
        app.MapHospitalityEndpoints();

        // 10. Network Discovery & Standalone Sync (Offline-First Edge POS)
        app.MapSyncEndpoints();

        // 11. Touch Grid Management
        app.MapGridEndpoints();
        app.MapDeviceEndpoints();

        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        lifetime.ApplicationStarted.Register(() =>
        {
            try
            {
                using var scope = app.Services.CreateScope();
                var journal = scope.ServiceProvider.GetRequiredService<IFiscalJournal>();
                journal.AppendAsync(
                    JournalEventTypes.ServerStarted,
                    new { ProcessId = Environment.ProcessId, MachineName = Environment.MachineName, StartedAtUtc = DateTimeOffset.UtcNow },
                    cancellationToken: CancellationToken.None
                ).GetAwaiter().GetResult();
            }
            catch
            {
                // Fiscal journal failure during startup should not crash the host
            }
        });

        lifetime.ApplicationStopping.Register(() =>
        {
            try
            {
                using var scope = app.Services.CreateScope();
                var journal = scope.ServiceProvider.GetRequiredService<IFiscalJournal>();
                journal.AppendAsync(
                    JournalEventTypes.ServerStopped,
                    new { ProcessId = Environment.ProcessId, MachineName = Environment.MachineName, StoppedAtUtc = DateTimeOffset.UtcNow },
                    cancellationToken: CancellationToken.None
                ).GetAwaiter().GetResult();
            }
            catch
            {
                // Fiscal journal failure during shutdown should not crash the host
            }
        });

        app.Run();
    }

    internal static bool IsSeedCliAllowed(IHostEnvironment env) => env.IsDevelopment();

    // Base vide : crée l'unique administrateur. PIN de 4 chiffres (celui des claviers web et iPad) : Bootstrap:AdminPin, sinon généré et journalisé une fois.
    internal static async Task BootstrapAdminAsync(AppDbContext db, IStaffManagementService staff, IConfiguration cfg, ILogger log)
    {
        if (await db.Users.AnyAsync().ConfigureAwait(false))
        {
            return;
        }

        var pin = cfg["Bootstrap:AdminPin"];
        if (string.IsNullOrEmpty(pin))
        {
            // 4 chiffres : les claviers web et iPad envoient le PIN au 4e chiffre. Jamais un PIN de démonstration.
            do
            {
                pin = System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 10_000).ToString("D4", CultureInfo.InvariantCulture);
            }
            while (pin is "0000" or "1234" or "2468" or "5678" or "9999");
            BootstrapLogMessages.GeneratedAdminPin(log, pin);
        }
        else if (pin.Length != 4 || !pin.All(char.IsAsciiDigit))
        {
            throw new InvalidOperationException("Bootstrap:AdminPin doit comporter exactement 4 chiffres.");
        }

        await staff.CreateStaffMemberAsync("Admin Système", UserRole.Admin, pin).ConfigureAwait(false);
    }

    public static async Task SeedDatabase(
        AppDbContext db,
        IStaffManagementService staffService,
        IBackOfficeCatalogService catalogService,
        IPrinterConfigurationService printerService,
        ITerminalLayoutService layoutService,
        bool force = false)
    {
        // Seed Staff
        if (!await db.Users.AnyAsync() || force)
        {
            if (!await db.Users.AnyAsync(u => u.Name.Contains("Alexandre Dupont")))
            {
                try { await staffService.CreateStaffMemberAsync("Alexandre Dupont (Manager)", UserRole.FloorManager, "1234"); } catch { }
            }
            if (!await db.Users.AnyAsync(u => u.Name.Contains("Sophie Martin")))
            {
                try { await staffService.CreateStaffMemberAsync("Sophie Martin (Serveuse)", UserRole.Waiter, "2468"); } catch { }
            }
            if (!await db.Users.AnyAsync(u => u.Name.Contains("Thomas Bernard")))
            {
                try { await staffService.CreateStaffMemberAsync("Thomas Bernard (Chef)", UserRole.KitchenStaff, "5678"); } catch { }
            }
            if (!await db.Users.AnyAsync(u => u.Name.Contains("Admin Système")))
            {
                try { await staffService.CreateStaffMemberAsync("Admin Système", UserRole.Admin, "9999"); } catch { }
            }
        }

        // Seed Categories & Products
        Product? prodSalade = null;
        Product? prodBurger = null;
        Product? prodPizza = null;
        Product? prodBeer = null;

        if (!await db.Categories.AnyAsync())
        {
            var catEntrees = await catalogService.CreateCategoryAsync("Entrées Fraîches", "#2ECC71", 1, "salad", preparationStationId: "COLD");
            var catPlats = await catalogService.CreateCategoryAsync("Plats & Grillades", "#E74C3C", 2, "meat");
            var catPizzas = await catalogService.CreateCategoryAsync("Pizzas Artisanales", "#E67E22", 3, "pizza");
            var catDesserts = await catalogService.CreateCategoryAsync("Desserts Maison", "#9B59B6", 4, "cake", preparationStationId: "DESSERT");
            var catBoissons = await catalogService.CreateCategoryAsync("Boissons & Vins", "#3498DB", 5, "glass", preparationStationId: "BAR");

            prodSalade = await catalogService.CreateProductAsync("Salade César Poulet", catEntrees.Id, 9.50m, 10.0m, "Poulet mariné, parmesan, croûtons", "#27AE60", 1, true, "COLD", cost: 2.80m);
            await catalogService.CreateProductAsync("Tartare de Saumon Frais", catEntrees.Id, 12.00m, 10.0m, "Saumon d'Islande, aneth, agrumes", "#2ECC71", 2, false, "COLD", cost: 4.50m);

            prodBurger = await catalogService.CreateProductAsync("Burger Gourmet Rossini", catPlats.Id, 19.50m, 10.0m, "Bœuf charolais, foie gras poêlé", "#C0392B", 1, true, "HOT_KITCHEN", cost: 5.20m);
            await catalogService.CreateProductAsync("Entrecôte Grillée 300g", catPlats.Id, 24.00m, 10.0m, "Frites maison et sauce béarnaise", "#E74C3C", 2, true, "HOT_KITCHEN", cost: 9.00m);

            prodPizza = await catalogService.CreateProductAsync("Pizza Margherita AOP", catPizzas.Id, 12.50m, 10.0m, "Tomates San Marzano, mozzarella fior di latte", "#D35400", 1, true, "HOT_KITCHEN", cost: 2.60m);
            await catalogService.CreateProductAsync("Pizza Reine Royale", catPizzas.Id, 14.50m, 10.0m, "Jambon blanc supérieur, champignons frais", "#E67E22", 2, false, "HOT_KITCHEN", cost: 3.40m);

            await catalogService.CreateProductAsync("Tiramisu Spéculos Maison", catDesserts.Id, 7.50m, 10.0m, "Mascarpone onctueux, café pur arabica", "#8E44AD", 1, true, "DESSERT", cost: 1.80m);
            await catalogService.CreateProductAsync("Fondant Chocolat Valrhona", catDesserts.Id, 8.00m, 10.0m, "Cœur coulant, glace vanille Bourbon", "#9B59B6", 2, false, "DESSERT", cost: 2.20m);

            prodBeer = await catalogService.CreateProductAsync("Bière Artisanale IPA 33cl", catBoissons.Id, 6.00m, 20.0m, "Brasserie locale du terroir", "#2980B9", 1, true, "BAR", cost: 1.50m);
            await catalogService.CreateProductAsync("Verre Bordeaux AOP 12cl", catBoissons.Id, 5.50m, 20.0m, "Grand Cru Saint-Émilion", "#3498DB", 2, false, "BAR", cost: 1.40m);
            await catalogService.CreateProductAsync("Expresso Pur Arabica", catBoissons.Id, 2.50m, 10.0m, "Café torréfaction artisanale", "#1F618D", 3, true, "BAR");
        }

        // Seed Modifier Groups
        if (!await db.ModifierGroups.AnyAsync())
        {
            var burger = await db.Products.FirstOrDefaultAsync(p => p.Name.Contains("Burger"));
            if (burger is not null)
            {
                var groupCuisson = new ProductModifierGroup
                {
                    Id = UuidV7.NewGuid(),
                    ProductId = burger.Id,
                    GroupName = "Cuisson de la Viande",
                    MinSelections = 1,
                    MaxSelections = 1,
                    DisplayOrder = 1,
                    Options =
                    [
                        new ProductModifierOption { GroupId = Guid.Empty, Name = "Bleu", ExtraPrice = Money.Zero(), DisplayOrder = 1 },
                        new ProductModifierOption { GroupId = Guid.Empty, Name = "Saignant", ExtraPrice = Money.Zero(), DisplayOrder = 2 },
                        new ProductModifierOption { GroupId = Guid.Empty, Name = "À Point", ExtraPrice = Money.Zero(), IsDefault = true, DisplayOrder = 3 },
                        new ProductModifierOption { GroupId = Guid.Empty, Name = "Bien Cuit", ExtraPrice = Money.Zero(), DisplayOrder = 4 }
                    ]
                };
                foreach (var opt in groupCuisson.Options) opt.GroupId = groupCuisson.Id;

                var groupSupplements = new ProductModifierGroup
                {
                    Id = UuidV7.NewGuid(),
                    ProductId = burger.Id,
                    GroupName = "Suppléments Gourmands",
                    MinSelections = 0,
                    MaxSelections = 5,
                    DisplayOrder = 2,
                    Options =
                    [
                        new ProductModifierOption { GroupId = Guid.Empty, Name = "Double Cheddar Fondu", ExtraPrice = Money.FromCents(200), DisplayOrder = 1 },
                        new ProductModifierOption { GroupId = Guid.Empty, Name = "Bacon Fumé Croustillant", ExtraPrice = Money.FromCents(150), DisplayOrder = 2 },
                        new ProductModifierOption { GroupId = Guid.Empty, Name = "Foie Gras Poêlé Extra", ExtraPrice = Money.FromCents(450), DisplayOrder = 3 },
                        new ProductModifierOption { GroupId = Guid.Empty, Name = "Sauce Poivre Vert Maison", ExtraPrice = Money.FromCents(100), DisplayOrder = 4 },
                        new ProductModifierOption { GroupId = Guid.Empty, Name = "Frites Rustiques Maison", ExtraPrice = Money.FromCents(250), DisplayOrder = 5 }
                    ]
                };
                foreach (var opt in groupSupplements.Options) opt.GroupId = groupSupplements.Id;

                db.ModifierGroups.AddRange(groupCuisson, groupSupplements);
            }

            var pizza = await db.Products.FirstOrDefaultAsync(p => p.Name.Contains("Pizza Margherita"));
            if (pizza is not null)
            {
                var groupPizza = new ProductModifierGroup
                {
                    Id = UuidV7.NewGuid(),
                    ProductId = pizza.Id,
                    GroupName = "Suppléments Pizza",
                    MinSelections = 0,
                    MaxSelections = 4,
                    DisplayOrder = 1,
                    Options =
                    [
                        new ProductModifierOption { GroupId = Guid.Empty, Name = "Double Mozzarella di Bufala", ExtraPrice = Money.FromCents(250), DisplayOrder = 1 },
                        new ProductModifierOption { GroupId = Guid.Empty, Name = "Jambon de Parme AOP", ExtraPrice = Money.FromCents(300), DisplayOrder = 2 },
                        new ProductModifierOption { GroupId = Guid.Empty, Name = "Roquette & Huile de Truffe", ExtraPrice = Money.FromCents(200), DisplayOrder = 3 },
                        new ProductModifierOption { GroupId = Guid.Empty, Name = "Piment Frais Émincé", ExtraPrice = Money.FromCents(50), DisplayOrder = 4 }
                    ]
                };
                foreach (var opt in groupPizza.Options) opt.GroupId = groupPizza.Id;

                db.ModifierGroups.Add(groupPizza);
            }

            await db.SaveChangesAsync();
        }

        // Seed Tables (all free, no active orders or transactions for clean report control)
        if (!await db.DiningTables.AnyAsync() || force)
        {
            if (force)
            {
                var existingTables = await db.DiningTables.ToListAsync();
                db.DiningTables.RemoveRange(existingTables);
                await db.SaveChangesAsync();
            }

            db.DiningTables.AddRange(
                new DiningTable { TableNumber = "T1", Capacity = 2, Status = TableStatus.Free },
                new DiningTable { TableNumber = "T2", Capacity = 4, Status = TableStatus.Free },
                new DiningTable { TableNumber = "T3", Capacity = 6, Status = TableStatus.Free },
                new DiningTable { TableNumber = "T4", Capacity = 2, Status = TableStatus.Free },
                new DiningTable { TableNumber = "T5", Capacity = 8, Status = TableStatus.Free },
                new DiningTable { TableNumber = "T6", Capacity = 4, Status = TableStatus.Free },
                new DiningTable { TableNumber = "T7", Capacity = 2, Status = TableStatus.Free },
                new DiningTable { TableNumber = "T8", Capacity = 4, Status = TableStatus.Free }
            );
            await db.SaveChangesAsync();
        }
        else
        {
            // Reset any existing tables to Free and clear active orders
            var tables = await db.DiningTables.ToListAsync();
            foreach (var table in tables)
            {
                table.Status = TableStatus.Free;
                table.ActiveOrderId = null;
                table.CoversCount = 0;
                table.AssignedWaiterId = null;
                table.AssignedWaiterName = null;
                table.OpenedAtUtc = null;
            }
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

        // Seed Happy Hour Schedule
        if (!await db.HappyHourSchedules.AnyAsync())
        {
            var beerProd = await db.Products.FirstOrDefaultAsync(p => EF.Functions.Like(p.Name, "%Bière%"));
            var schedule = new HappyHourSchedule
            {
                Name = "Afterwork Standard",
                DaysOfWeek = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
                StartTime = new TimeOnly(17, 0),
                EndTime = new TimeOnly(20, 0),
                IsActive = true,
                AppliesToTakeaway = false,
                Priority = 1
            };

            if (beerProd != null)
            {
                schedule.PriceRules.Add(new HappyHourPriceRule
                {
                    TargetType = HappyHourTargetType.Product,
                    TargetId = beerProd.Id.ToString(),
                    TargetName = beerProd.Name,
                    PricingMode = HappyHourPricingMode.FixedPrice,
                    FixedPrice = Money.FromEuros(5.00m)
                });
            }

            // Also add a 20% discount on "BAR" or "Boissons" category
            var barCat = await db.Categories.FirstOrDefaultAsync(c => EF.Functions.Like(c.Name, "%Boisson%") || EF.Functions.Like(c.Name, "%Bar%"));
            if (barCat != null)
            {
                schedule.PriceRules.Add(new HappyHourPriceRule
                {
                    TargetType = HappyHourTargetType.Category,
                    TargetId = barCat.Id,
                    TargetName = barCat.Name,
                    PricingMode = HappyHourPricingMode.PercentageDiscount,
                    DiscountPercent = 20.0m
                });
            }

            db.HappyHourSchedules.Add(schedule);
            await db.SaveChangesAsync();
        }
    }
}
internal static partial class SchemaLogMessages
{
    [LoggerMessage(EventId = 3001, Level = LogLevel.Error, Message = "Schema statement failed and was skipped: {Statement}")]
    public static partial void StatementFailed(ILogger logger, Exception exception, string statement);
}

public record CreateCategoryRequest(string Name, string? ColorHex, int DisplayOrder, string? IconName, string? PreparationStationId = null);
public record UpdateCategoryRequest(string Name, string? ColorHex, int DisplayOrder, string? IconName, bool? IsActive, string? PreparationStationId = null);
public record CreateProductRequest(string Name, string CategoryId, decimal Price, decimal TaxRatePercent, string? Description, string? ColorHex, int DisplayOrder, bool IsQuickKey, string? StationId, decimal? Cost = null);
public record UpdateProductRequest(string Name, string CategoryId, decimal Price, decimal TaxRatePercent, string? Description, string? ColorHex, int DisplayOrder, bool? IsAvailable, bool? IsActive, bool IsQuickKey, string? StationId, decimal? Cost = null, bool ClearCost = false);
public record ReservationRequest(string GuestName, int Covers, DateTimeOffset StartsAtUtc, int DurationMinutes = 90, string? TableNumber = null, string? Note = null, string? Phone = null, bool IsWaitlist = false, int? QuoteMinutes = null);
public record SeatReservationRequest(string? TableNumber, Guid? OperatorId, string? WaiterName);
public record ReservationStatusRequest(ReservationStatus Status);
public record CreateStaffRequest(string Name, string Role, string Pin);
public record UpdateStaffRequest(string Name, string Role, string? Pin, bool? IsActive);
public record CreateTableRequest(string TableNumber, int Capacity, double? PositionX, double? PositionY);
public record UpdatePrinterRequest(string Name, string IpAddress, int Port, int PaperWidthMm, bool HasCashDrawer, List<string> TargetStations, bool? IsActive, bool? TextMode = null);
public record OpenTableRequest(string? WaiterName, int CoversCount, Guid? OperatorId);
public record ZClosureRequest(string TerminalId, Guid ManagerId, string ManagerName);
public record AddOrderItemsRequest(List<OrderItemInputDto> Items);
public record SetAvailabilityRequest(bool IsAvailable);
public record SetAllergensRequest(bool Declared, int Mask, bool Overridden);
public record PaymentSettlementRequest(Guid OrderId, string? TableNumber, Guid? OperatorId, List<TenderItemRequest> Tenders, string? TerminalId = null, bool RequestReceiptPrint = false, decimal TipAmount = 0m);
public record TenderItemRequest(PaymentMethod Method, decimal Amount, decimal Tendered, decimal ChangeGiven);
public record TransferTableRequest(string TargetTableNumber);
public record MergeTablesRequest(string TargetTableNumber);
public record ApplyOrderDiscountRequest(DiscountType Type, decimal Value, string Reason, Guid? OperatorId);
public record CompOrderItemRequest(string Reason, Guid? OperatorId);
public record RoomChargeRequest(Guid OrderId, string TableNumber, string RoomNumber, string GuestName, decimal Amount, decimal TipAmount, string? SignatureDataUrl, string? Notes);
public record SyncBatchRequest(List<SyncMessageItemDto> Messages);
public record SyncMessageItemDto(Guid Id, string? TerminalId, string? EventType, string? IdempotencyKey, string PayloadJson, DateTimeOffset CreatedAtUtc);



internal static partial class BootstrapLogMessages
{
    [LoggerMessage(EventId = 1101, Level = LogLevel.Warning, Message = "Aucun Bootstrap:AdminPin défini : PIN de l'administrateur généré {Pin}. Il n'est affiché qu'une fois, changez-le après la première connexion.")]
    public static partial void GeneratedAdminPin(ILogger logger, string pin);
}
