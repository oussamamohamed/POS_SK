using System;
using System.Collections.Generic;

namespace RestaurantPos.Infrastructure.Printing;

public static class EscPosCommands
{
    public const int BandHeight = 256;
    private static readonly byte[] Init = [0x1B, 0x40];
    private static readonly byte[] DrawerKick = [0x1B, 0x70, 0x00, 0x19, 0xFA];
    private static readonly byte[] FeedAndCut = [0x1D, 0x56, 0x42, 0x00];

    /// <summary>ESC @, puis pour chaque segment : bandes GS v 0 de 256 lignes et coupe ; impulsion tiroir avant la dernière coupe.</summary>
    public static byte[] Build(IReadOnlyList<MonoImage> segments, bool openCashDrawer)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var bytes = new List<byte>(Init);
        for (var i = 0; i < segments.Count; i++)
        {
            var img = segments[i];
            for (var top = 0; top < img.Height; top += BandHeight)
            {
                var rows = Math.Min(BandHeight, img.Height - top);
                bytes.AddRange([0x1D, 0x76, 0x30, 0x00, (byte)(img.BytesPerRow & 0xFF), (byte)(img.BytesPerRow >> 8), (byte)(rows & 0xFF), (byte)(rows >> 8)]);
                bytes.AddRange(img.Bits.AsSpan(top * img.BytesPerRow, rows * img.BytesPerRow).ToArray());
            }
            if (openCashDrawer && i == segments.Count - 1) bytes.AddRange(DrawerKick);
            bytes.AddRange(FeedAndCut);
        }
        return [.. bytes];
    }
}
