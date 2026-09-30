using System;
using System.Linq;
using System.Text;
using FluentAssertions;
using RestaurantPos.Infrastructure.Printing;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class EscPosTextRendererTests
{
    private static readonly byte[] Header = [0x1B, 0x40, 0x1B, 0x74, 19];
    private static readonly byte[] Cut = [0x1D, 0x56, 0x42, 0x00];
    private static readonly byte[] Kick = [0x1B, 0x70, 0x00, 0x19, 0xFA];

    private static TicketDocument Doc(params TicketLine[] lines) => new("fr", false, lines);

    private static byte[] Body(byte[] bytes) => bytes[Header.Length..^Cut.Length];

    private static bool Contains(byte[] haystack, byte[] needle) =>
        Enumerable.Range(0, haystack.Length - needle.Length + 1).Any(i => haystack.AsSpan(i, needle.Length).SequenceEqual(needle));

    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    [Theory]
    [InlineData(80, 48)]
    [InlineData(58, 32)]
    [InlineData(0, 48)]
    public void ColumnsFor_MapsPaperWidth(int mm, int cols) => EscPosTextRenderer.ColumnsFor(mm).Should().Be(cols);

    [Fact]
    public void Render_StartsWithInitAndCodePage_EndsWithCut()
    {
        var bytes = EscPosTextRenderer.Render(Doc(new TicketText("A")), 80, openCashDrawer: false);
        bytes.Take(5).Should().Equal(Header);
        bytes.TakeLast(4).Should().Equal(Cut);
        Contains(bytes, Kick).Should().BeFalse();
    }

    [Fact]
    public void Render_Drawer_KickBeforeFinalCut()
    {
        var bytes = EscPosTextRenderer.Render(Doc(new TicketText("A")), 80, openCashDrawer: true);
        bytes.TakeLast(9).Should().Equal(Kick.Concat(Cut));
    }

    [Fact]
    public void Text_CenteredBold_UsesAlignAndEmphasis()
    {
        var body = Body(EscPosTextRenderer.Render(Doc(new TicketText("TOTAL", TicketAlign.Center, Bold: true)), 80, false));
        body.Should().Equal([0x1B, 0x61, 1, 0x1B, 0x45, 1, .. Ascii("TOTAL"), 0x0A, 0x1B, 0x45, 0]);
    }

    [Fact]
    public void Text_Large_DoubleSize_AndHalfWidthWrap()
    {
        var body = Body(EscPosTextRenderer.Render(Doc(new TicketText(new string('A', 20) + " " + new string('B', 5), Large: true)), 58, false));
        body.Should().Equal([0x1B, 0x61, 0, 0x1D, 0x21, 0x11, .. Ascii(new string('A', 16)), 0x0A, .. Ascii("AAAA BBBBB"), 0x0A, 0x1D, 0x21, 0x00]);
    }

    [Theory]
    [InlineData(80)]
    [InlineData(58)]
    public void Columns_PaddedToFullWidth(int mm)
    {
        var cols = EscPosTextRenderer.ColumnsFor(mm);
        var body = Body(EscPosTextRenderer.Render(Doc(new TicketColumns("Total", "12.50")), mm, false));
        body.Should().Equal([0x1B, 0x61, 0, .. Ascii("Total" + new string(' ', cols - 10) + "12.50"), 0x0A]);
    }

    [Fact]
    public void Columns_TooLong_WrapsLabel_ValueOnNextLine()
    {
        var label = "3x Entrecote grillee sauce bearnaise maison";   // 43 caractères ASCII
        var body = Body(EscPosTextRenderer.Render(Doc(new TicketColumns(label, "72.00")), 58, false));
        var lines = Encoding.ASCII.GetString(body[3..]).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().Equal("3x Entrecote grillee sauce", "bearnaise maison", new string(' ', 27) + "72.00");
    }

    [Fact]
    public void Text_WordLongerThanLine_IsHardSplit() =>
        EscPosTextRenderer.Wrap(new string('F', 64), 32).Should().Equal(new string('F', 32), new string('F', 32));

    [Fact]
    public void Wrap_EmptyOrSpaces_GivesOneEmptyLine() =>
        EscPosTextRenderer.Wrap("   ", 32).Should().Equal("");

    [Fact]
    public void Encoding_Pc858_AccentsEuroAndUnknown()
    {
        var body = Body(EscPosTextRenderer.Render(Doc(new TicketText("é è à ç € سفري")), 80, false));
        var textBytes = body[3..^1];
        textBytes.Take(10).Should().Equal(new byte[] { 0x82, 0x20, 0x8A, 0x20, 0x85, 0x20, 0x87, 0x20, 0xD5, 0x20 });
        textBytes.Skip(10).Should().OnlyContain(b => b == (byte)'?');
    }

    [Fact]
    public void Separator_DashesAcrossWidth_CutSeparatorCuts()
    {
        var body = Body(EscPosTextRenderer.Render(Doc(new TicketSeparator(), new TicketText("B"), new TicketSeparator(Cut: true), new TicketText("C")), 58, false));
        Contains(body, [0x1B, 0x61, 0, .. Ascii(new string('-', 32)), 0x0A]).Should().BeTrue();
        Contains(body, Cut).Should().BeTrue();
    }

    [Fact]
    public void TrailingCutSeparator_DoesNotDoubleCut()
    {
        var bytes = EscPosTextRenderer.Render(Doc(new TicketText("A"), new TicketSeparator(Cut: true)), 80, false);
        Enumerable.Range(0, bytes.Length - 3).Count(i => bytes.AsSpan(i, 4).SequenceEqual(Cut)).Should().Be(1);
    }
}
