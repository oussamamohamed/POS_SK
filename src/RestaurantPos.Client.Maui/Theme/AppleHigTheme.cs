#if MAUI_UI
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace RestaurantPos.Client.Maui.Theme;

/// <summary>
/// Système de design officiel inspiré des Apple Human Interface Guidelines (HIG) pour iPadOS.
/// Centralise la palette Apple Dark Mode, la typographie SF, les rayons de courbure squircle
/// et les composants visuels standardisés (cartes inset grouped, segmented controls, modal sheets).
/// </summary>
public static class AppleHigTheme
{
    // =========================================================================
    // Couleurs Système Apple (Dark Mode Palette)
    // =========================================================================
    public static readonly Color SystemBackground = Color.FromArgb("#000000");            // Noir pur OLED
    public static readonly Color SecondarySystemBackground = Color.FromArgb("#1C1C1E");   // Cartes & Panneaux
    public static readonly Color TertiarySystemBackground = Color.FromArgb("#2C2C2E");    // Sous-cartes & Champs
    public static readonly Color QuaternarySystemFill = Color.FromArgb("#3A3A3C");        // Remplissage & Chips
    public static readonly Color Separator = Color.FromArgb("#38383A");                    // Lignes de séparation
    public static readonly Color OpaqueSeparator = Color.FromArgb("#545458");              // Bordures accentuées

    public static readonly Color LabelPrimary = Colors.White;                             // Titres & Textes forts
    public static readonly Color LabelSecondary = Color.FromArgb("#8E8E93");              // Labels secondaires (System Gray)
    public static readonly Color LabelTertiary = Color.FromArgb("#636366");               // Mentions discrètes

    // Tints Sémantiques Apple
    public static readonly Color SystemBlue = Color.FromArgb("#0A84FF");                  // Accent principal / Actions
    public static readonly Color SystemGreen = Color.FromArgb("#30D158");                 // Succès / Validation / Espèces
    public static readonly Color SystemOrange = Color.FromArgb("#FF9F0A");                // Attente / Avertissement
    public static readonly Color SystemRed = Color.FromArgb("#FF453A");                   // Annulation / Erreur / Supprimer
    public static readonly Color SystemIndigo = Color.FromArgb("#5E5CE6");                // Mode Fiscal / Avancé
    public static readonly Color SystemTeal = Color.FromArgb("#64D2FF");                  // Info / Cyan
    public static readonly Color SystemYellow = Color.FromArgb("#FFD60A");                // Étoiles / Alertes
    public static readonly Color SystemPurple = Color.FromArgb("#BF5AF2");                // Catégories spéciales

    // =========================================================================
    // Échelle Typographique Apple HIG
    // =========================================================================
    public const double LargeTitle = 34;
    public const double Title1 = 28;
    public const double Title2 = 22;
    public const double Title3 = 20;
    public const double Headline = 17;
    public const double Body = 17;
    public const double Callout = 16;
    public const double Subheadline = 15;
    public const double Footnote = 13;
    public const double Caption1 = 12;
    public const double Caption2 = 11;

    // =========================================================================
    // Rayons de Courbure & Dimensions Ergonomiques
    // =========================================================================
    public const double CornerRadiusSmall = 8;
    public const double CornerRadiusMedium = 12;
    public const double CornerRadiusLarge = 16;
    public const double CornerRadiusExtraLarge = 20;
    public const double CornerRadiusSheet = 18;
    public const double CornerRadiusPill = 999;
    public const double MinTouchTarget = 44;

    // =========================================================================
    // Constructeurs de Composants Standards Apple HIG
    // =========================================================================

    /// <summary>
    /// Crée une carte de style Apple Inset Grouped.
    /// </summary>
    public static Border CreateCard(View content, Thickness? padding = null, Color? backgroundColor = null, double cornerRadius = CornerRadiusLarge)
    {
        return new Border
        {
            Padding = padding ?? new Thickness(16),
            BackgroundColor = backgroundColor ?? SecondarySystemBackground,
            Stroke = Separator,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(cornerRadius) },
            Content = content
        };
    }

    /// <summary>
    /// Crée un bouton ergonomique Apple HIG (cible tactile minimum 44pt).
    /// </summary>
    public static Button CreatePillButton(string text, Color backgroundColor, Color textColor, System.Windows.Input.ICommand? command = null, double height = 44, double fontSize = 15, FontAttributes fontAttributes = FontAttributes.Bold)
    {
        return new Button
        {
            Text = text,
            BackgroundColor = backgroundColor,
            TextColor = textColor,
            FontSize = fontSize,
            FontAttributes = fontAttributes,
            HeightRequest = height,
            MinimumHeightRequest = MinTouchTarget,
            MinimumWidthRequest = MinTouchTarget,
            CornerRadius = (int)(height / 2),
            Padding = new Thickness(16, 0),
            Command = command
        };
    }

    /// <summary>
    /// Poignée de préhension ("grabber handle") caractéristique des modales Apple Sheet.
    /// </summary>
    public static View CreateSheetGrabber()
    {
        return new BoxView
        {
            WidthRequest = 40,
            HeightRequest = 5,
            CornerRadius = 2.5f,
            Color = LabelTertiary,
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 8, 0, 12)
        };
    }

    /// <summary>
    /// Badge capsule discret au style Apple (fond teinté translucide + texte coloré).
    /// </summary>
    public static View CreateCapsuleBadge(string text, Color tintColor, double fontSize = 11)
    {
        return new Border
        {
            Padding = new Thickness(8, 3),
            BackgroundColor = Color.FromRgba(tintColor.Red, tintColor.Green, tintColor.Blue, 0.18),
            Stroke = Color.FromRgba(tintColor.Red, tintColor.Green, tintColor.Blue, 0.4),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(CornerRadiusPill) },
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = text,
                TextColor = tintColor,
                FontSize = fontSize,
                FontAttributes = FontAttributes.Bold,
                VerticalOptions = LayoutOptions.Center
            }
        };
    }
    /// <summary>
    /// Extension fluide pour configurer un BindableObject inline.
    /// </summary>
    public static T Also<T>(this T obj, Action<T> configure) where T : BindableObject
    {
        configure(obj);
        return obj;
    }
}
#endif
