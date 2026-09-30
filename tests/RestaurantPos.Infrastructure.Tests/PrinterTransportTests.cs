using System.Linq;
using FluentAssertions;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Printing;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class PrinterTransportTests
{
    private static PrinterConfiguration Printer(bool textMode) => new() { Name = "P", IpAddress = "10.0.0.1", PaperWidthMm = 80, TextMode = textMode };
    private static TicketDocument Doc(string lang) => new(lang, lang == "ar", [new TicketText(lang == "ar" ? "سفري" : "Crème")]);

    private static bool IsText(byte[] payload) => payload.Take(5).SequenceEqual(new byte[] { 0x1B, 0x40, 0x1B, 0x74, 19 });

    [Theory]
    [InlineData(true, "fr", true)]
    [InlineData(true, "en", true)]
    [InlineData(true, "ar", false)]
    [InlineData(false, "fr", false)]
    public void BuildPayload_ChoosesTextOnlyForLtrWhenEnabled(bool textMode, string lang, bool expectText) =>
        IsText(EscPosPrinterTransport.BuildPayload(Printer(textMode), Doc(lang), false)).Should().Be(expectText);
}
