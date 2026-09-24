using System.IO;
using FluentAssertions;
using Moq;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Client.Maui.Models;
using RestaurantPos.Client.Maui.Persistence;
using RestaurantPos.Client.Maui.Services;
using RestaurantPos.Domain.Entities;
using Xunit;

namespace RestaurantPos.Client.Maui.Tests;

public class LocalSyncWorkerTests
{
    [Fact]
    public async Task ProcessOutboxQueueAsyncReconcilesPendingMessages()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), "SyncTest_" + Guid.NewGuid().ToString("N"));
        var envMock = new Mock<IPlatformEnvironmentService>();
        envMock.Setup(e => e.GetSecureDatabasePath(It.IsAny<string>()))
               .Returns(Path.Combine(tempDir, "test_sync.db"));
        envMock.Setup(e => e.GetCurrentDeviceProfile()).Returns(new DevicePlatformProfile
        {
            DeviceId = "TERM-TEST-02",
            Platform = PlatformType.Windows,
            Idiom = DeviceIdiomType.Desktop,
            ScreenClass = ScreenClassType.StandardTablet,
            ScreenWidthDip = 1024,
            ScreenHeightDip = 768,
            DisplayDensity = 1.0,
            OsVersion = "10.0",
            AppVersion = "1.0.0"
        });

        if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

        using var dbContext = new LocalAppDbContext(envMock.Object);
        await dbContext.Database.EnsureCreatedAsync();

        var journalService = new LocalJournalService(dbContext, envMock.Object);
        var syncWorker = new LocalSyncWorker(dbContext);

        try
        {
            await journalService.RecordTransactionAsync("OrderCreated", "KEY-SYNC-1", new { Item = "Burger" });
            await journalService.RecordTransactionAsync("OrderCreated", "KEY-SYNC-2", new { Item = "Frites" });

            // Act
            int processed = await syncWorker.ProcessOutboxQueueAsync();

            // Assert
            processed.Should().Be(2);
            dbContext.OutboxMessages.All(m => m.Status == SyncStatus.Completed).Should().BeTrue();
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
