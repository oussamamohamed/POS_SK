using System.Text.Json;

namespace RestaurantPos.Infrastructure.Printing;

public static class TicketDocumentJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(TicketDocument document) => JsonSerializer.Serialize(document, Options);

    public static TicketDocument Deserialize(string json) =>
        JsonSerializer.Deserialize<TicketDocument>(json, Options) ?? throw new JsonException("Ticket vide");
}
