namespace RestaurantPos.Client.Maui.Models;

public enum PlatformType
{
    iOS = 0,
    Android = 1,
    Windows = 2,
    MacCatalyst = 3
}

public enum DeviceIdiomType
{
    Phone = 0,
    Tablet = 1,
    Desktop = 2
}

public enum ScreenClassType
{
    CompactHandheld = 0, // < 700 dp (Phone, Handheld terminal)
    StandardTablet = 1,  // 700 - 1100 dp (iPad, 10" Android)
    LargeCounterAIO = 2  // > 1100 dp (Windows Touch AIO, large counter displays)
}

public sealed record DevicePlatformProfile
{
    public required string DeviceId { get; init; }
    public required PlatformType Platform { get; init; }
    public required DeviceIdiomType Idiom { get; init; }
    public required ScreenClassType ScreenClass { get; init; }
    public required double ScreenWidthDip { get; init; }
    public required double ScreenHeightDip { get; init; }
    public required double DisplayDensity { get; init; }
    public required string OsVersion { get; init; }
    public required string AppVersion { get; init; }

    public static ScreenClassType DetermineScreenClass(double widthDip, double heightDip)
    {
        double effectiveDimension = Math.Max(widthDip, heightDip);
        return effectiveDimension switch
        {
            < 700 => ScreenClassType.CompactHandheld,
            <= 1100 => ScreenClassType.StandardTablet,
            _ => ScreenClassType.LargeCounterAIO
        };
    }
}
