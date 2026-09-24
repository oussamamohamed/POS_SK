using System.IO;
using FluentAssertions;
using Moq;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Client.Maui.Models;
using RestaurantPos.Client.Maui.Persistence;
using RestaurantPos.Client.Maui.Services;
using Xunit;

namespace RestaurantPos.Client.Maui.Tests;

public class LocalJournalStorageTests
{
    [Fact]
    public async Task RecordTransactionAsyncWritesAppendOnlyEntryWithMonotonicSequence()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), "JournalTest_" + Guid.NewGuid().ToString("N"));
        var envMock = new Mock<IPlatformEnvironmentService>();
        envMock.Setup(e => e.GetSecureDatabasePath(It.IsAny<string>()))
               .Returns(Path.Combine(tempDir, "test_journal.db"));
        envMock.Setup(e => e.GetCurrentDeviceProfile()).Returns(new DevicePlatformProfile
        {
            DeviceId = "TERM-TEST-01",
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

        try
        {
            // Act
            var entry1 = await journalService.RecordTransactionAsync("OrderCreated", "KEY-001", new { OrderId = 101 });
            var entry2 = await journalService.RecordTransactionAsync("PaymentCompleted", "KEY-002", new { OrderId = 101, Total = 2500 });

            // Assert
            entry1.LocalSequence.Should().Be(1);
            entry2.LocalSequence.Should().Be(2);
            entry2.IdempotencyKey.Should().Be("KEY-002");
            entry2.EntryHash.Should().NotBeNullOrEmpty();

            var pending = await journalService.GetPendingJournalEntriesAsync();
            pending.Should().HaveCount(2);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
