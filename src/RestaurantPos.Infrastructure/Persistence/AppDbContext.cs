using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;

namespace RestaurantPos.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<TaxRate> TaxRates => Set<TaxRate>();
    public DbSet<DiningTable> DiningTables => Set<DiningTable>();
    public DbSet<ProductModifierGroup> ModifierGroups => Set<ProductModifierGroup>();
    public DbSet<ProductModifierOption> ModifierOptions => Set<ProductModifierOption>();
    public DbSet<KitchenTicket> KitchenTickets => Set<KitchenTicket>();
    public DbSet<KitchenTicketItem> KitchenTicketItems => Set<KitchenTicketItem>();
    public DbSet<FiscalReceipt> FiscalReceipts => Set<FiscalReceipt>();
    public DbSet<PaymentTender> PaymentTenders => Set<PaymentTender>();
    public DbSet<DailyFiscalClosure> DailyFiscalClosures => Set<DailyFiscalClosure>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<TransactionJournalEntry> JournalEntries => Set<TransactionJournalEntry>();
    public DbSet<OutboxSyncMessage> OutboxMessages => Set<OutboxSyncMessage>();
    public DbSet<PrinterConfiguration> PrinterConfigurations => Set<PrinterConfiguration>();
    public DbSet<TerminalLayoutProfile> TerminalLayoutProfiles => Set<TerminalLayoutProfile>();

    // Hospitality Feature DbSets
    public DbSet<HotelRoomResident> HotelRooms => Set<HotelRoomResident>();
    public DbSet<RoomFolioCharge> RoomFolioCharges => Set<RoomFolioCharge>();
    public DbSet<TableTransferLog> TableTransferLogs => Set<TableTransferLog>();
    public DbSet<OrderDiscountAudit> OrderDiscountAudits => Set<OrderDiscountAudit>();
    public DbSet<GridLayout> GridLayouts => Set<GridLayout>();
    public DbSet<GridSlot> GridSlots => Set<GridSlot>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

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
            entity.Property(s => s.ProductId).HasMaxLength(32);
            entity.Property(s => s.CustomLabel).HasMaxLength(64);
            entity.Property(s => s.CustomColorHex).HasMaxLength(16);
            entity.HasOne(s => s.Product).WithMany().HasForeignKey(s => s.ProductId).OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(s => new { s.GridLayoutId, s.RowIndex, s.ColumnIndex }).IsUnique();
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Name).HasMaxLength(64).IsRequired();
            entity.Property(u => u.PinHash).HasMaxLength(64).IsRequired();
            entity.Property(u => u.PinSalt).HasMaxLength(64).IsRequired();
            entity.HasIndex(u => u.IsActive);
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Name).HasMaxLength(64).IsRequired();
            entity.Property(c => c.ColorHex).HasMaxLength(16);
            entity.HasIndex(c => c.DisplayOrder);
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name).HasMaxLength(100).IsRequired();
            entity.Property(p => p.CategoryId).HasMaxLength(32).IsRequired();
            entity.Property(p => p.ColorHex).HasMaxLength(16);
            entity.Property(p => p.TaxRatePercent).HasPrecision(5, 2);

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
            entity.Property(t => t.Description).HasMaxLength(128).IsRequired();
            entity.Property(t => t.RatePercent).HasPrecision(5, 2);
        });

        modelBuilder.Entity<DiningTable>(entity =>
        {
            entity.HasKey(d => d.TableNumber);
            entity.Property(d => d.TableNumber).HasMaxLength(16).IsRequired();
            entity.Property(d => d.AssignedWaiterName).HasMaxLength(64);
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

        modelBuilder.Entity<KitchenTicket>(entity =>
        {
            entity.HasKey(k => k.Id);
            entity.Property(k => k.TableNumber).HasMaxLength(16).IsRequired();
            entity.Property(k => k.StationId).HasMaxLength(32).IsRequired();
            entity.HasMany(k => k.Items).WithOne().HasForeignKey(i => i.TicketId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<KitchenTicketItem>(entity =>
        {
            entity.HasKey(i => i.Id);
            entity.Property(i => i.ProductName).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<FiscalReceipt>(entity =>
        {
            entity.HasKey(f => f.Id);
            entity.Property(f => f.TerminalId).HasMaxLength(16).IsRequired();
            entity.Property(f => f.ReceiptNumber).HasMaxLength(32).IsRequired();
            entity.Property(f => f.PreviousSignatureHash).HasMaxLength(64).IsRequired();
            entity.Property(f => f.SignatureHash).HasMaxLength(64).IsRequired();
            entity.Property(f => f.TotalTtcAmount)
                  .HasConversion(m => m.AmountInCents, cents => new Money(cents, "EUR"));
            entity.Property(f => f.TotalHtAmount)
                  .HasConversion(m => m.AmountInCents, cents => new Money(cents, "EUR"));
            entity.HasMany(f => f.Tenders).WithOne().HasForeignKey(t => t.FiscalReceiptId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(f => new { f.TerminalId, f.SequenceNumber }).IsUnique();
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
            entity.Property(c => c.PreviousSignatureHash).HasMaxLength(64).IsRequired();
            entity.Property(c => c.SignatureHash).HasMaxLength(64).IsRequired();
            entity.Property(c => c.TotalSalesTtc)
                  .HasConversion(m => m.AmountInCents, cents => new Money(cents, "EUR"));
            entity.Property(c => c.TotalSalesHt)
                  .HasConversion(m => m.AmountInCents, cents => new Money(cents, "EUR"));
            entity.HasIndex(c => new { c.TerminalId, c.ClosureSequence }).IsUnique();
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
                  .HasConversion(
                      m => m.AmountInCents,
                      cents => new Money(cents, "EUR")
                  );

            entity.Property(i => i.ModifiersPriceExtra)
                  .HasConversion(
                      m => m.AmountInCents,
                      cents => new Money(cents, "EUR")
                  );
        });

        modelBuilder.Entity<HotelRoomResident>(entity =>
        {
            entity.HasKey(h => h.Id);
            entity.Property(h => h.RoomNumber).HasMaxLength(16).IsRequired();
            entity.Property(h => h.GuestName).HasMaxLength(100).IsRequired();
            entity.HasIndex(h => h.RoomNumber);
        });

        modelBuilder.Entity<RoomFolioCharge>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.RoomNumber).HasMaxLength(16).IsRequired();
            entity.Property(r => r.GuestName).HasMaxLength(100).IsRequired();
            entity.Property(r => r.Amount)
                  .HasConversion(m => m.AmountInCents, cents => new Money(cents, "EUR"));
            entity.Property(r => r.TipAmount)
                  .HasConversion(m => m.AmountInCents, cents => new Money(cents, "EUR"));
        });

        modelBuilder.Entity<TableTransferLog>(entity =>
        {
            entity.HasKey(l => l.Id);
            entity.Property(l => l.SourceTableNumber).HasMaxLength(16).IsRequired();
            entity.Property(l => l.TargetTableNumber).HasMaxLength(16).IsRequired();
        });

        modelBuilder.Entity<OrderDiscountAudit>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Reason).HasMaxLength(256).IsRequired();
            entity.Property(a => a.AmountSaved)
                  .HasConversion(m => m.AmountInCents, cents => new Money(cents, "EUR"));
        });
    }
}
