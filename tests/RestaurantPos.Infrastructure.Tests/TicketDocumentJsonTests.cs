using System.Linq;
using FluentAssertions;
using RestaurantPos.Infrastructure.Printing;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class TicketDocumentJsonTests
{
    [Fact]
    public void RoundTrip_PreservesLineTypes()
    {
        var doc = new TicketDocument("ar", true,
        [
            new TicketText("سفري", TicketAlign.Center, Bold: true, Large: true),
            new TicketColumns("Total", "12.50"),
            new TicketSeparator(Cut: true)
        ]);
        var json = TicketDocumentJson.Serialize(doc);
        json.Should().Contain("\"type\":\"TicketText\"");
        json.Should().Contain("\"TicketColumns\"").And.Contain("\"TicketSeparator\"");
        var back = TicketDocumentJson.Deserialize(json);
        back.Language.Should().Be(doc.Language);
        back.RightToLeft.Should().Be(doc.RightToLeft);
        back.Lines.Select(l => l.GetType()).Should().Equal(typeof(TicketText), typeof(TicketColumns), typeof(TicketSeparator));
        for (var i = 0; i < doc.Lines.Count; i++) back.Lines[i].Should().Be(doc.Lines[i]);
    }
}
