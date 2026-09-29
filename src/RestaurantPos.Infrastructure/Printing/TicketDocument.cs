using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RestaurantPos.Infrastructure.Printing;

public enum TicketAlign { Start, Center, End }

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TicketText), nameof(TicketText))]
[JsonDerivedType(typeof(TicketColumns), nameof(TicketColumns))]
[JsonDerivedType(typeof(TicketSeparator), nameof(TicketSeparator))]
public abstract record TicketLine;

public sealed record TicketText(string Text, TicketAlign Align = TicketAlign.Start, bool Bold = false, bool Large = false) : TicketLine;

public sealed record TicketColumns(string Label, string Value) : TicketLine;

public sealed record TicketSeparator(bool Cut = false) : TicketLine;

public sealed record TicketDocument(string Language, bool RightToLeft, IReadOnlyList<TicketLine> Lines);
