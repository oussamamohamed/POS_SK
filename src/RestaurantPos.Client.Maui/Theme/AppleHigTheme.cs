#if MAUI_UI
using System;
using System.Windows.Input;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;

namespace RestaurantPos.Client.Maui.Theme;

/// <summary>
/// Système de design officiel inspiré des Apple Human Interface Guidelines (HIG) pour iPadOS.
/// Centralise la palette Apple Dark Mode, la typographie SF, les rayons de courbure squircle,
/// le retour haptique tactile et les composants visuels standardisés.
/// </summary>
public static class AppleHigTheme
{
    // =========================================================================
    // Palette Apple HIG (iOS / iPadOS Dark Mode & Light Mode Adaptatif)
    // =========================================================================
    public static readonly Color SystemBackground = Color.FromArgb("#000000");           // Noir pur OLED iPad
    public static readonly Color SecondarySystemBackground = Color.FromArgb("#1C1C1E");  // Cartes / Groupes
    public static readonly Color TertiarySystemBackground = Color.FromArgb("#2C2C2E");   // Boutons / Tuiles
    public static readonly Color QuaternarySystemFill = Color.FromArgb("#3A3A3C");        // Éléments inactifs

    public static readonly Color LabelPrimary = Color.FromArgb("#FFFFFF");                // Texte principal
    public static readonly Color LabelSecondary = Color.FromArgb("#8E8E93");              // Sous-titres
    public static readonly Color LabelTertiary = Color.FromArgb("#636366");               // Mentions discrètes
    public static readonly Color Separator = Color.FromArgb("#38383A");                   // Lignes de séparation

    // Accents Apple Système
    public static readonly Color SystemBlue = Color.FromArgb("#0A84FF");                  // Actions principales / Navigation
    public static readonly Color SystemGreen = Color.FromArgb("#30D158");                 // Succès / Encaissement / Table Libre
    public static readonly Color SystemOrange = Color.FromArgb("#FF9F0A");                // Table Occupée / KDS En cours
    public static readonly Color SystemRed = Color.FromArgb("#FF453A");                   // Suppressions / Retards / Addition
    public static readonly Color SystemIndigo = Color.FromArgb("#5E5CE6");                // Remises / Clôture
    public static readonly Color SystemTeal = Color.FromArgb("#64D2FF");                  // Transfert / Impression

    // =========================================================================
    // Typographie Apple San Francisco (Échelles de points HIG)
    // =========================================================================
    public const double LargeTitle = 28;
    public const double Title1 = 22;
    public const double Title2 = 20;
    public const double Title3 = 18;
    public const double Headline = 16;
    public const double Body = 15;
    public const double Subheadline = 14;
    public const double Footnote = 13;
    public const double Caption1 = 12;
    public const double Caption2 = 11;

    // =========================================================================
    // Rayons de Courbure & Dimensions Ergonomiques (Apple HIG)
    // =========================================================================
    public const double CornerRadiusSmall = 8;
    public const double CornerRadiusMedium = 12;
    public const double CornerRadiusLarge = 16;
    public const double CornerRadiusExtraLarge = 20;
    public const double CornerRadiusSheet = 24;
    public const double CornerRadiusPill = 999;
    public const double MinTouchTarget = 44;

    // Inset de sécurité pour la barre Home Indicator iPadOS
    public static readonly Thickness iPadBottomSafeArea = new(0, 0, 0, 10);

    // =========================================================================
    // Retours Haptiques Tactiles (iPad / iOS Touch Feedback)
    // =========================================================================
    /// <summary>
    /// Déclenche un retour tactile instantané pour les interactions de commande ou touches caisse.
    /// </summary>
    public static void PerformHapticClick()
    {
        try
        {
            Microsoft.Maui.Devices.HapticFeedback.Default.Perform(Microsoft.Maui.Devices.HapticFeedbackType.Click);
        }
        catch
        {
            // Ignoré si non supporté sur la plateforme ou en mode test
        }
    }

    /// <summary>
    /// Déclenche un retour tactile de confirmation forte (ex: encaissement réussi, impression, envoi cuisine).
    /// </summary>
    public static void PerformHapticSuccess()
    {
        try
        {
            Microsoft.Maui.Devices.HapticFeedback.Default.Perform(Microsoft.Maui.Devices.HapticFeedbackType.LongPress);
        }
        catch
        {
            // Ignoré si non supporté sur la plateforme ou en mode test
        }
    }

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
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(cornerRadius) },
            StrokeThickness = 1,
            Stroke = Separator,
            BackgroundColor = backgroundColor ?? SecondarySystemBackground,
            Padding = padding ?? new Thickness(16),
            Content = content
        };
    }

    /// <summary>
    /// Crée un bouton capsule (Pill Button) respectant la cible tactile minimale iPad (44pt).
    /// </summary>
    public static Button CreatePillButton(string text, Color backgroundColor, Color textColor, ICommand command, double height = 44, double fontSize = Subheadline)
    {
        return new Button
        {
            Text = text,
            BackgroundColor = backgroundColor,
            TextColor = textColor,
            CornerRadius = (int)(height / 2),
            HeightRequest = height,
            MinimumHeightRequest = MinTouchTarget,
            Padding = new Thickness(18, 0),
            FontSize = fontSize,
            FontAttributes = FontAttributes.Bold,
            Command = command
        };
    }

    /// <summary>
    /// Indicateur de préhension supérieur pour les feuilles modales iPad (Apple Modal Sheet Grabber).
    /// </summary>
    public static View CreateSheetGrabber()
    {
        return new BoxView
        {
            WidthRequest = 36,
            HeightRequest = 5,
            CornerRadius = 2.5,
            Color = QuaternarySystemFill,
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, 0, 12)
        };
    }

    /// <summary>
    /// Badge capsule (Pill Tag) avec fond translucide Apple.
    /// </summary>
    public static Border CreateBadge(string text, Color color, double fontSize = Caption1)
    {
        return new Border
        {
            BackgroundColor = Color.FromRgba(color.Red, color.Green, color.Blue, 0.18),
            Stroke = Color.FromRgba(color.Red, color.Green, color.Blue, 0.4),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(CornerRadiusPill) },
            Padding = new Thickness(8, 2),
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = text,
                TextColor = color,
                FontSize = fontSize,
                FontAttributes = FontAttributes.Bold,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };
    }

    /// <summary>
    /// Extension fluide pour configurer un BindableObject inline.
    /// </summary>
    public static T Also<T>(this T obj, Action<T> action) where T : BindableObject
    {
        action(obj);
        return obj;
    }
}
#endif
