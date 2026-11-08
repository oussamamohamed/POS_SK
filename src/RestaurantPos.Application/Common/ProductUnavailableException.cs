using System;

namespace RestaurantPos.Application.Common;

/// <summary>Ajout d'un produit en rupture à une commande (traduit en 409 <c>product_unavailable</c> par l'API).</summary>
public sealed class ProductUnavailableException(Guid productId)
    : InvalidOperationException($"Produit indisponible : {productId}")
{
    public Guid ProductId { get; } = productId;
}
