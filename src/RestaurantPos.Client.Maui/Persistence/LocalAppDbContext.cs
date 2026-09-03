using Microsoft.EntityFrameworkCore;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;

namespace RestaurantPos.Client.Maui.Persistence;

public class LocalAppDbContext : DbContext
{
    private readonly IPlatformEnvironmentService _environmentService;

    public DbSet<User> Users => Set<User>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<TaxRate> TaxRates => Set<TaxRate>();
    public DbSet<DiningTable> DiningTables => Set<DiningTable>();
    public DbSet<ProductModifierGroup> ModifierGroups => Set<ProductModifierGroup>();
    public DbSet<ProductModifierOption> ModifierOptions => Set<ProductModifierOption>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<FiscalReceipt> FiscalReceipts => Set<FiscalReceipt>();
    public DbSet<PaymentTender> PaymentTenders => Set<PaymentTender>();
    public DbSet<DailyFiscalClosure> DailyFiscalClosures => Set<DailyFiscalClosure>();
    public DbSet<TransactionJournalEntry> JournalEntries => Set<TransactionJournalEntry>();
    public DbSet<OutboxSyncMessage> OutboxMessages => Set<OutboxSyncMessage>();
    public DbSet<PrinterConfiguration> PrinterConfigurations => Set<PrinterConfiguration>();
    public DbSet<TerminalLayoutProfile> TerminalLayoutProfiles => Set<TerminalLayoutProfile>();
    public DbSet<GridLayout> GridLayouts => Set<GridLayout>();
    public DbSet<GridSlot> GridSlots => Set<GridSlot>();

    public LocalAppDbContext(IPlatformEnvironmentService environmentService)
    {
        _environmentService = environmentService;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            string dbPath = _environmentService.GetSecureDatabasePath("pos_offline_cache.db");
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Name).HasMaxLength(64).IsRequired();
            entity.Property(u => u.PinHash).HasMaxLength(64).IsRequired();
            entity.Property(u => u.PinSalt).HasMaxLength(64).IsRequired();
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Name).HasMaxLength(64).IsRequired();
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name).HasMaxLength(100).IsRequired();
            entity.Property(p => p.Price)
                  .HasConversion(
                      m => m.AmountInCents,
                      cents => new Money(cents, "EUR")
                  );
        });

        modelBuilder.Entity<TaxRate>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Code).HasMaxLength(16).IsRequired();
        });

        modelBuilder.Entity<DiningTable>(entity =>
        {
            entity.HasKey(d => d.TableNumber);
            entity.Property(d => d.TableNumber).HasMaxLength(16).IsRequired();
        });

        modelBuilder.Entity<ProductModifierGroup>(entity =>
        {
            entity.HasKey(g => g.Id);
            entity.Property(g => g.GroupName).HasMaxLength(64).IsRequired();
            entity.HasMany(g => g.Options).WithOne().HasForeignKey(o => o.GroupId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProductModifierOption>(entity =>
        {
            entity.HasKey(o => o.Id);
            entity.Property(o => o.Name).HasMaxLength(64).IsRequired();
            entity.Property(o => o.ExtraPrice)
                  .HasConversion(
                      m => m.AmountInCents,
                      cents => new Money(cents, "EUR")
                  );
        });

        modelBuilder.Entity<FiscalReceipt>(entity =>
        {
            entity.HasKey(f => f.Id);
            entity.Property(f => f.TerminalId).HasMaxLength(16).IsRequired();
            entity.Property(f => f.ReceiptNumber).HasMaxLength(32).IsRequired();
            entity.Property(f => f.TotalTtcAmount)
                  .HasConversion(m => m.AmountInCents, cents => new Money(cents, "EUR"));
            entity.Property(f => f.TotalHtAmount)
                  .HasConversion(m => m.AmountInCents, cents => new Money(cents, "EUR"));
            entity.HasMany(f => f.Tenders).WithOne().HasForeignKey(t => t.FiscalReceiptId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PaymentTender>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Amount)
                  .HasConversion(m => m.AmountInCents, cents => new Money(cents, "EUR"));
            entity.Property(t => t.Tendered)
                  .HasConversion(m => m.AmountInCents, cents => new Money(cents, "EUR"));
            entity.Property(t => t.ChangeGiven)
                  .HasConversion(m => m.AmountInCents, cents => new Money(cents, "EUR"));
        });

        modelBuilder.Entity<DailyFiscalClosure>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.TerminalId).HasMaxLength(16).IsRequired();
            entity.Property(c => c.TotalSalesTtc)
                  .HasConversion(m => m.AmountInCents, cents => new Money(cents, "EUR"));
            entity.Property(c => c.TotalSalesHt)
                  .HasConversion(m => m.AmountInCents, cents => new Money(cents, "EUR"));
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(o => o.Id);
            entity.Property(o => o.TableNumber).HasMaxLength(16).IsRequired();
            entity.Property(o => o.TipAmount)
                  .HasConversion(m => m.AmountInCents, cents => new Money(cents, "EUR"));
            entity.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.HasKey(i => i.Id);
            entity.Property(i => i.ProductName).HasMaxLength(100).IsRequired();
            entity.Property(i => i.TaxRatePercent).HasPrecision(5, 2);
            entity.Property(i => i.UnitPrice)
                  .HasConversion(m => m.AmountInCents, cents => new Money(cents, "EUR"));
            entity.Property(i => i.ModifiersPriceExtra)
                  .HasConversion(m => m.AmountInCents, cents => new Money(cents, "EUR"));
        });

        modelBuilder.Entity<TransactionJournalEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.LocalSequence);
            entity.HasIndex(e => e.IdempotencyKey).IsUnique();
        });

        modelBuilder.Entity<OutboxSyncMessage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAtUtc);
        });

        modelBuilder.Entity<GridLayout>(entity =>
        {
            entity.HasKey(g => g.Id);
            entity.Property(g => g.CategoryId).HasMaxLength(32).IsRequired();
            entity.Property(g => g.Name).HasMaxLength(64).IsRequired();
            entity.HasMany(g => g.Slots).WithOne(s => s.GridLayout).HasForeignKey(s => s.GridLayoutId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(g => new { g.CategoryId, g.PageIndex }).IsUnique();
        });

        modelBuilder.Entity<GridSlot>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.CustomLabel).HasMaxLength(64);
            entity.Property(s => s.CustomColorHex).HasMaxLength(16);
            entity.HasOne(s => s.Product).WithMany().HasForeignKey(s => s.ProductId).OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(s => new { s.GridLayoutId, s.RowIndex, s.ColumnIndex }).IsUnique();
        });
    }

    public async Task ApplyCatalogSyncPayloadAsync(
        IReadOnlyList<Category> categories,
        IReadOnlyList<Product> products,
        IReadOnlyList<User> operators,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var cat in categories)
        {
            var existing = await Categories.FindAsync([cat.Id], cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                existing.Name = cat.Name;
                existing.ColorHex = cat.ColorHex;
                existing.DisplayOrder = cat.DisplayOrder;
                existing.UpdatedAtUtc = cat.UpdatedAtUtc;
            }
            else
            {
                Categories.Add(cat);
            }
        }

        foreach (var prod in products)
        {
            var existing = await Products.FindAsync([prod.Id], cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                existing.Name = prod.Name;
                existing.CategoryId = prod.CategoryId;
                existing.Price = prod.Price;
                existing.TaxRatePercent = prod.TaxRatePercent;
                existing.ColorHex = prod.ColorHex;
                existing.IsAvailable = prod.IsAvailable;
                existing.UpdatedAtUtc = prod.UpdatedAtUtc;
            }
            else
            {
                Products.Add(prod);
            }
        }

        foreach (var user in operators)
        {
            var existing = await Users.FindAsync([user.Id], cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                existing.Name = user.Name;
                existing.Role = user.Role;
                existing.PinHash = user.PinHash;
                existing.PinSalt = user.PinSalt;
                existing.IsActive = user.IsActive;
                existing.UpdatedAtUtc = user.UpdatedAtUtc;
            }
            else
            {
                Users.Add(user);
            }
        }

        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
