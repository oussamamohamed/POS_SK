using System;

namespace RestaurantPos.Application.DTOs;

public class VoidReceiptRequest
{
    public string TerminalId { get; set; } = string.Empty;
    public Guid OperatorId { get; set; }
    /// <summary>Remet en stock les ingrédients de la commande annulée (produit non consommé) ; faux par défaut.</summary>
    public bool Restock { get; set; }

    public VoidReceiptRequest() { }

    public VoidReceiptRequest(string terminalId, Guid operatorId)
    {
        TerminalId = terminalId;
        OperatorId = operatorId;
    }
}
