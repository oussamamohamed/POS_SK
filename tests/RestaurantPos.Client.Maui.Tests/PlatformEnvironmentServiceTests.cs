using System.IO;
using FluentAssertions;
using RestaurantPos.Client.Maui.Models;
using RestaurantPos.Client.Maui.Services;
using Xunit;

namespace RestaurantPos.Client.Maui.Tests;

public class PlatformEnvironmentServiceTests
{
    [Fact]
    public void GetSecureDatabasePathWithCustomDirectoryReturnsCorrectPath()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), "PosTestStorage_" + Guid.NewGuid().ToString("N"));
        var service = new PlatformEnvironmentService(tempDir);

        try
        {
            // Act
            string dbPath = service.GetSecureDatabasePath("pos_sandbox");

            // Assert
            dbPath.Should().Be(Path.Combine(tempDir, "pos_sandbox.db"));
            Directory.Exists(tempDir).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Theory]
    [InlineData(600, 800, ScreenClassType.StandardTablet)]
    [InlineData(400, 650, ScreenClassType.CompactHandheld)]
    [InlineData(1920, 1080, ScreenClassType.LargeCounterAIO)]
    [InlineData(1024, 768, ScreenClassType.StandardTablet)]
    public void DetermineScreenClassCalculatesAppropriateBreakpoint(double width, double height, ScreenClassType expectedClass)
    {
        // Act
        ScreenClassType actualClass = DevicePlatformProfile.DetermineScreenClass(width, height);

        // Assert
        actualClass.Should().Be(expectedClass);
    }
}
