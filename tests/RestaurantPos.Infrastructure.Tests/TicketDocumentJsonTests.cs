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
        TicketDocumentJson.Deserialize(json).Should().BeEquivalentTo(doc);
    }
}
