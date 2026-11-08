using System;
using System.Collections.Generic;

namespace RestaurantPos.Domain.ValueObjects;

/// <summary>
/// Les 14 allergènes à déclaration obligatoire (règlement UE 1169/2011), codés en bits 0 à 13.
/// L'ordre est figé : le masque est stocké en base et échangé avec le web et l'iPad (miroir Swift : Allergens.swift).
/// </summary>
public static class Allergens
{
    private static readonly string[] _names =
    [
        "gluten", "crustaceans", "eggs", "fish", "peanuts", "soy", "milk",
        "nuts", "celery", "mustard", "sesame", "sulphites", "lupin", "molluscs"
    ];

    public static IReadOnlyList<string> Names => _names;

    public static int AllMask => (1 << _names.Length) - 1;

    public static bool IsValidMask(int mask) => mask >= 0 && mask <= AllMask;

    public static int Union(int a, int b) => a | b;

    public static bool Contains(int mask, string name)
    {
        var bit = Array.IndexOf(_names, name);
        return bit >= 0 && (mask & (1 << bit)) != 0;
    }
}
