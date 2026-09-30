using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RestaurantPos.Infrastructure.Printing;

/// <summary>Rendu texte ESC/POS (page de code PC858) d'un TicketDocument LTR. Plus rapide que l'image sur les imprimantes lentes.</summary>
public static class EscPosTextRenderer
{
    private static readonly Encoding Pc858 = CreateEncoding();
    private static readonly byte[] Init = [0x1B, 0x40, 0x1B, 0x74, 19];
    private static readonly byte[] DrawerKick = [0x1B, 0x70, 0x00, 0x19, 0xFA];
    private static readonly byte[] FeedAndCut = [0x1D, 0x56, 0x42, 0x00];

    public static int ColumnsFor(int paperWidthMm) => paperWidthMm == 58 ? 32 : 48;

    public static byte[] Render(TicketDocument doc, int paperWidthMm, bool openCashDrawer)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var cols = ColumnsFor(paperWidthMm);
        var bytes = new List<byte>(Init);
        var lines = doc.Lines.ToList();
        // La coupe finale est toujours ajoutée : une coupe en fin de document ferait une coupe à vide.
        while (lines.Count > 0 && lines[^1] is TicketSeparator { Cut: true }) lines.RemoveAt(lines.Count - 1);
        foreach (var line in lines)
        {
            switch (line)
            {
                case TicketText t:
                    AppendText(bytes, t, cols);
                    break;
                case TicketColumns c:
                    AppendColumns(bytes, c, cols);
                    break;
                case TicketSeparator { Cut: true }:
                    bytes.AddRange(FeedAndCut);
                    break;
                default:
                    bytes.AddRange([0x1B, 0x61, 0]);
                    AppendLine(bytes, new string('-', cols));
                    break;
            }
        }
        if (openCashDrawer) bytes.AddRange(DrawerKick);
        bytes.AddRange(FeedAndCut);
        return [.. bytes];
    }

    /// <summary>Coupe aux espaces ; un mot plus long que la ligne est coupé net. Toujours au moins une ligne.</summary>
    public static List<string> Wrap(string text, int width)
    {
        var result = new List<string>();
        var line = new StringBuilder();
        foreach (var word in (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var rest = word;
            while (rest.Length > width)
            {
                if (line.Length > 0) { result.Add(line.ToString()); line.Clear(); }
                result.Add(rest[..width]);
                rest = rest[width..];
            }
            if (rest.Length == 0) continue;
            if (line.Length > 0 && line.Length + 1 + rest.Length > width) { result.Add(line.ToString()); line.Clear(); }
            if (line.Length > 0) line.Append(' ');
            line.Append(rest);
        }
        if (line.Length > 0) result.Add(line.ToString());
        if (result.Count == 0) result.Add(string.Empty);
        return result;
    }

    private static void AppendText(List<byte> bytes, TicketText t, int cols)
    {
        bytes.AddRange([0x1B, 0x61, t.Align switch { TicketAlign.Center => (byte)1, TicketAlign.End => (byte)2, _ => (byte)0 }]);
        if (t.Bold) bytes.AddRange([0x1B, 0x45, 1]);
        if (t.Large) bytes.AddRange([0x1D, 0x21, 0x11]);
        foreach (var l in Wrap(t.Text, t.Large ? cols / 2 : cols)) AppendLine(bytes, l);
        if (t.Large) bytes.AddRange([0x1D, 0x21, 0x00]);
        if (t.Bold) bytes.AddRange([0x1B, 0x45, 0]);
    }

    private static void AppendColumns(List<byte> bytes, TicketColumns c, int cols)
    {
        bytes.AddRange([0x1B, 0x61, 0]);
        if (c.Label.Length + 1 + c.Value.Length <= cols)
        {
            AppendLine(bytes, c.Label + new string(' ', cols - c.Label.Length - c.Value.Length) + c.Value);
            return;
        }
        foreach (var l in Wrap(c.Label, cols)) AppendLine(bytes, l);
        foreach (var l in Wrap(c.Value, cols)) AppendLine(bytes, l.PadLeft(cols));
    }

    private static void AppendLine(List<byte> bytes, string text)
    {
        bytes.AddRange(Pc858.GetBytes(text));
        bytes.Add(0x0A);
    }

    private static Encoding CreateEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(858, new EncoderReplacementFallback("?"), DecoderFallback.ReplacementFallback);
    }
}
