using System;

namespace RestaurantPos.Application.DTOs;

public class VoidReceiptRequest
{
    public string TerminalId { get; set; } = string.Empty;
    public Guid OperatorId { get; set; }
}
