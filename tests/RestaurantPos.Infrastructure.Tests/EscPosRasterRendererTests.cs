using System.Linq;
using System.Numerics;
using FluentAssertions;
using RestaurantPos.Infrastructure.Printing;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class EscPosRasterRendererTests
{
    private static int Ink(MonoImage img) => img.Bits.Sum(b => BitOperations.PopCount(b));

    private static int InkInColumns(MonoImage img, int fromX, int toX)
    {
        var count = 0;
        for (var y = 0; y < img.Height; y++)
            for (var x = fromX; x < toX; x++)
                if ((img.Bits[y * img.BytesPerRow + x / 8] & (0x80 >> (x % 8))) != 0) count++;
        return count;
    }

    private static TicketDocument Doc(bool rtl, params TicketLine[] lines) => new(rtl ? "ar" : "fr", rtl, lines);

    [Theory]
    [InlineData(80, 576)]
    [InlineData(58, 384)]
    [InlineData(0, 576)]
    public void DotsFor_MapsPaperWidth(int mm, int dots) => EscPosRasterRenderer.DotsFor(mm).Should().Be(dots);

    [Theory]
    [InlineData(576)]
    [InlineData(384)]
    public void Render_UsesRequestedWidth_AndPositiveHeight(int width)
    {
        var img = EscPosRasterRenderer.Render(Doc(false, new TicketText("Crème brûlée"), new TicketColumns("Total", "12.50")), width).Single();
        img.Width.Should().Be(width);
        img.Height.Should().BeGreaterThan(0);
        img.Bits.Length.Should().Be(img.BytesPerRow * img.Height);
        Ink(img).Should().BeGreaterThan(0);
    }

    [Fact]
    public void Render_ArabicText_ProducesInk()
    {
        var img = EscPosRasterRenderer.Render(Doc(true, new TicketText("استلام", TicketAlign.Center, Large: true)), 576).Single();
        Ink(img).Should().BeGreaterThan(200);
    }

    [Fact]
    public void Render_MixedArabicLatin_DrawsInkOnRightHalfForRtlStart()
    {
        var img = EscPosRasterRenderer.Render(Doc(true, new TicketText("طاولة T05")), 576).Single();
        InkInColumns(img, 288, 576).Should().BeGreaterThan(0);
        InkInColumns(img, 0, 200).Should().Be(0);
    }

    [Fact]
    public void Render_LtrStart_DrawsInkOnLeftOnly()
    {
        var img = EscPosRasterRenderer.Render(Doc(false, new TicketText("Table 5")), 576).Single();
        InkInColumns(img, 0, 200).Should().BeGreaterThan(0);
        InkInColumns(img, 376, 576).Should().Be(0);
    }

    [Fact]
    public void Render_CutSeparator_SplitsSegments_IgnoringEmpty()
    {
        var images = EscPosRasterRenderer.Render(Doc(false, new TicketText("A"), new TicketSeparator(Cut: true), new TicketText("B"), new TicketSeparator(Cut: true)), 576);
        images.Should().HaveCount(2);
    }

    [Fact]
    public void Render_EmptyText_DoesNotThrow()
    {
        var act = () => EscPosRasterRenderer.Render(Doc(true, new TicketText(""), new TicketColumns("", ""), new TicketSeparator()), 384);
        act.Should().NotThrow();
    }
}
