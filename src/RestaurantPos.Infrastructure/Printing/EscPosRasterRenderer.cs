using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkiaSharp;
using Topten.RichTextKit;

namespace RestaurantPos.Infrastructure.Printing;

/// <summary>Image 1 bit prête pour GS v 0 : bit 1 = point noir, MSB = pixel de gauche.</summary>
public sealed record MonoImage(int Width, int Height, byte[] Bits)
{
    public int BytesPerRow => Width / 8;
}

/// <summary>Dessine un TicketDocument en image monochrome (mise en forme bidi et liaison arabe par RichTextKit/HarfBuzz).</summary>
public static class EscPosRasterRenderer
{
    private const int Margin = 8;
    private const float NormalSize = 24f;
    private const float LargeSize = 40f;
    private const string Latin = "Noto Sans";
    private const string Arabic = "Noto Sans Arabic";

    static EscPosRasterRenderer() => FontMapper.Default = EmbeddedFontMapper.Instance;

    public static int DotsFor(int paperWidthMm) => paperWidthMm == 58 ? 384 : 576;

    public static IReadOnlyList<MonoImage> Render(TicketDocument doc, int widthDots)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var segments = new List<List<TicketLine>> { new() };
        foreach (var line in doc.Lines)
        {
            if (line is TicketSeparator { Cut: true }) segments.Add([]);
            else segments[^1].Add(line);
        }
        return segments.Where(s => s.Count > 0).Select(s => RenderSegment(s, doc.RightToLeft, widthDots)).ToList();
    }

    private sealed record Block(float Height, Action<SKCanvas, float> Draw);

    private static MonoImage RenderSegment(List<TicketLine> lines, bool rtl, int width)
    {
        float content = width - 2 * Margin;
        var blocks = lines.Select(l => Layout(l, rtl, content)).ToList();
        var height = (int)Math.Ceiling(blocks.Sum(b => b.Height)) + 2 * Margin;
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            float y = Margin;
            foreach (var block in blocks)
            {
                block.Draw(canvas, y);
                y += block.Height;
            }
        }
        return Threshold(bitmap);
    }

    private static Block Layout(TicketLine line, bool rtl, float content) => line switch
    {
        TicketText t => TextBlockOf(Text(t.Text, t.Bold, t.Large, Map(t.Align, rtl), rtl, content), Margin),
        TicketColumns c => Columns(c, rtl, content),
        _ => new Block(16, (canvas, y) =>
        {
            using var paint = new SKPaint { Color = SKColors.Black, StrokeWidth = 2, PathEffect = SKPathEffect.CreateDash([6, 4], 0) };
            canvas.DrawLine(Margin, y + 8, Margin + content, y + 8, paint);
        })
    };

    private static Block TextBlockOf(TextBlock tb, float x) =>
        new(tb.MeasuredHeight, (canvas, y) => tb.Paint(canvas, new SKPoint(x, y)));

    private static Block Columns(TicketColumns c, bool rtl, float content)
    {
        var value = Text(c.Value, false, false, rtl ? TextAlignment.Left : TextAlignment.Right, rtl, content);
        var labelWidth = Math.Max(content / 3, content - value.MeasuredWidth - 16);
        var label = Text(c.Label, false, false, rtl ? TextAlignment.Right : TextAlignment.Left, rtl, labelWidth);
        var labelX = rtl ? Margin + content - labelWidth : Margin;
        return new Block(Math.Max(label.MeasuredHeight, value.MeasuredHeight), (canvas, y) =>
        {
            label.Paint(canvas, new SKPoint(labelX, y));
            value.Paint(canvas, new SKPoint(Margin, y));
        });
    }

    private static TextAlignment Map(TicketAlign align, bool rtl) => align switch
    {
        TicketAlign.Center => TextAlignment.Center,
        TicketAlign.End => rtl ? TextAlignment.Left : TextAlignment.Right,
        _ => rtl ? TextAlignment.Right : TextAlignment.Left
    };

    private static TextBlock Text(string text, bool bold, bool large, TextAlignment align, bool rtl, float maxWidth)
    {
        var tb = new TextBlock { MaxWidth = maxWidth, Alignment = align, BaseDirection = rtl ? TextDirection.RTL : TextDirection.LTR };
        foreach (var (run, arabic) in ScriptRuns(string.IsNullOrEmpty(text) ? " " : text))
        {
            tb.AddText(run, new Style
            {
                FontFamily = arabic ? Arabic : Latin,
                FontSize = large ? LargeSize : NormalSize,
                FontWeight = bold || large ? 700 : 400,
                TextColor = SKColors.Black
            });
        }
        return tb;
    }

    // Noto Sans n'a pas l'arabe et Noto Sans Arabic pas le latin : découpage en runs par écriture, les espaces suivent le run courant.
    private static IEnumerable<(string Run, bool Arabic)> ScriptRuns(string text)
    {
        var start = 0;
        var current = IsArabic(text[0]);
        for (var i = 1; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]) || IsArabic(text[i]) == current) continue;
            yield return (text[start..i], current);
            start = i;
            current = !current;
        }
        yield return (text[start..], current);
    }

    private static bool IsArabic(char ch) =>
        ch is (>= '؀' and <= 'ۿ') or (>= 'ݐ' and <= 'ݿ') or (>= 'ࢠ' and <= 'ࣿ')
            or (>= 'ﭐ' and <= '﷿') or (>= 'ﹰ' and <= '﻿');

    private static MonoImage Threshold(SKBitmap bitmap)
    {
        var width = bitmap.Width;
        var bytesPerRow = width / 8;
        var bits = new byte[bytesPerRow * bitmap.Height];
        var pixels = bitmap.GetPixelSpan();
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var p = (y * width + x) * 4; // BGRA
                var luminance = (pixels[p + 2] * 299 + pixels[p + 1] * 587 + pixels[p] * 114) / 1000;
                if (luminance < 128) bits[y * bytesPerRow + x / 8] |= (byte)(0x80 >> (x % 8));
            }
        }
        return new MonoImage(width, bitmap.Height, bits);
    }

    private sealed class EmbeddedFontMapper : FontMapper
    {
        public static readonly EmbeddedFontMapper Instance = new();
        private readonly Dictionary<string, SKTypeface> _faces = new(StringComparer.Ordinal)
        {
            [$"{Latin}|400"] = Load("NotoSans-Regular.ttf"),
            [$"{Latin}|700"] = Load("NotoSans-Bold.ttf"),
            [$"{Arabic}|400"] = Load("NotoSansArabic-Regular.ttf"),
            [$"{Arabic}|700"] = Load("NotoSansArabic-Bold.ttf")
        };

        public override SKTypeface TypefaceFromStyle(IStyle style, bool ignoreFontVariants) =>
            _faces[$"{(style.FontFamily == Arabic ? Arabic : Latin)}|{(style.FontWeight >= 600 ? 700 : 400)}"];

        private static SKTypeface Load(string file)
        {
            using var stream = typeof(EscPosRasterRenderer).Assembly.GetManifestResourceStream("RestaurantPos.Infrastructure.Printing.Fonts." + file)
                ?? throw new InvalidOperationException("Police absente : " + file);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return SKTypeface.FromData(SKData.CreateCopy(memory.ToArray()));
        }
    }
}
