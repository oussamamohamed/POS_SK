using System;
using System.Linq;
using FluentAssertions;
using RestaurantPos.Infrastructure.Printing;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class EscPosCommandsTests
{
    private static MonoImage Image(int width, int height) => new(width, height, Enumerable.Repeat((byte)0xAA, width / 8 * height).ToArray());

    [Fact]
    public void Build_SplitsIntoBandsOf256_LastBandPartial()
    {
        var bytes = EscPosCommands.Build([Image(576, 600)], openCashDrawer: false);

        bytes.Take(2).Should().Equal((byte)0x1B, (byte)0x40);
        var headers = Enumerable.Range(0, bytes.Length - 3).Where(i => bytes[i] == 0x1D && bytes[i + 1] == 0x76 && bytes[i + 2] == 0x30).ToList();
        headers.Should().HaveCount(3);
        headers.Select(i => bytes[i + 6] | (bytes[i + 7] << 8)).Should().Equal(256, 256, 88);
        headers.Select(i => bytes[i + 4] | (bytes[i + 5] << 8)).Should().AllBeEquivalentTo(72);
        bytes.TakeLast(4).Should().Equal((byte)0x1D, (byte)0x56, (byte)0x42, (byte)0x00);
        bytes.Length.Should().Be(2 + 3 * 8 + 72 * 600 + 4);
    }

    [Fact]
    public void Build_Drawer_KickBeforeFinalCutOnly()
    {
        var bytes = EscPosCommands.Build([Image(384, 10), Image(384, 10)], openCashDrawer: true);
        byte[] kick = [0x1B, 0x70, 0x00, 0x19, 0xFA];
        byte[] cut = [0x1D, 0x56, 0x42, 0x00];
        bytes.TakeLast(9).Should().Equal(kick.Concat(cut));
        CountOf(bytes, kick).Should().Be(1);
        CountOf(bytes, cut).Should().Be(2);
    }

    [Fact]
    public void Build_NoDrawer_NoKick() =>
        CountOf(EscPosCommands.Build([Image(384, 10)], false), [0x1B, 0x70, 0x00, 0x19, 0xFA]).Should().Be(0);

    private static int CountOf(byte[] haystack, byte[] needle) =>
        Enumerable.Range(0, haystack.Length - needle.Length + 1).Count(i => haystack.AsSpan(i, needle.Length).SequenceEqual(needle));
}
