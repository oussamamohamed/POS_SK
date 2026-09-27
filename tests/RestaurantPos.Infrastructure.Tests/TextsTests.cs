using System.Globalization;
using FluentAssertions;
using RestaurantPos.Infrastructure.Localization;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class TextsTests
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr");
    private static readonly CultureInfo Ar = CultureInfo.GetCultureInfo("ar");
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de");

    [Fact]
    public void Get_ReturnsValueForEachLanguage()
    {
        Texts.Get(En, "errors.pairing_code_invalid").Should().Be("Invalid or expired code");
        Texts.Get(Fr, "errors.pairing_code_invalid").Should().Be("Code invalide ou expiré");
        Texts.Get(Ar, "errors.pairing_code_invalid").Should().Be("رمز غير صالح أو منتهي الصلاحية");
    }

    [Fact]
    public void Get_UnsupportedCulture_FallsBackToEnglish() =>
        Texts.Get(De, "errors.pairing_code_invalid").Should().Be("Invalid or expired code");

    [Fact]
    public void Get_UnknownKey_ReturnsKey() =>
        Texts.Get(Fr, "errors.does_not_exist").Should().Be("errors.does_not_exist");

    [Fact]
    public void Get_ReplacesNamedPlaceholders_WithInvariantFormatting()
    {
        Texts.Get(Ar, "errors.rate_limited_seconds", ("seconds", 12.0))
            .Should().Contain("12").And.NotContain("{seconds}");
    }

    [Fact]
    public void Get_MissingArgument_LeavesPlaceholder() =>
        Texts.Get(En, "errors.rate_limited_seconds").Should().Contain("{seconds}");

    [Fact]
    public void EveryEnglishKey_ExistsInFrenchAndArabic_AndIsNotEmpty()
    {
        var en = Texts.Keys(En);
        en.Should().NotBeEmpty();
        foreach (var culture in new[] { Fr, Ar })
        {
            Texts.Keys(culture).Should().BeEquivalentTo(en, $"culture {culture.Name}");
            foreach (var key in en) Texts.Get(culture, key).Should().NotBeNullOrWhiteSpace();
        }
    }
}
