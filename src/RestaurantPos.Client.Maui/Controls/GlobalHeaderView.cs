#if MAUI_UI
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using RestaurantPos.Client.Maui.Theme;

namespace RestaurantPos.Client.Maui.Controls;

public enum PosActiveViewTab
{
    Pos,
    Floor,
    Kds,
    Admin,
    Fiscal
}

/// <summary>
/// Barre de navigation supérieure globale conforme aux Apple Human Interface Guidelines (HIG) pour iPadOS.
/// Présente un contrôle segmenté Apple (Pill bar), des badges capsules translucides
/// et des boutons d'actions aux dimensions tactiles ergonomiques (min 44pt).
/// </summary>
public class GlobalHeaderView : Grid
{
    public GlobalHeaderView(PosActiveViewTab activeTab)
    {
        BackgroundColor = AppleHigTheme.SecondarySystemBackground;
        Padding = new Thickness(16, 8);
        HeightRequest = 62;
        ColumnSpacing = 16;

        ColumnDefinitions = new ColumnDefinitionCollection
        {
            new ColumnDefinition { Width = GridLength.Auto }, // Brand & Badge (~170px)
            new ColumnDefinition { Width = GridLength.Star }, // Segmented Control (~550px)
            new ColumnDefinition { Width = GridLength.Auto }  // Operator & Lock (~160px)
        };

        // Ligne de séparation inférieure subtile Apple HIG (1px)
        var bottomSeparator = new BoxView
        {
            HeightRequest = 1,
            Color = AppleHigTheme.Separator,
            VerticalOptions = LayoutOptions.End
        };
        Grid.SetColumnSpan(bottomSeparator, 3);
        Children.Add(bottomSeparator);

        // 1. Logo & Badge de statut Apple
        var brandLayout = new HorizontalStackLayout
        {
            Spacing = 10,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Border
                {
                    Padding = 0,
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(AppleHigTheme.CornerRadiusSmall) },
                    Stroke = AppleHigTheme.SystemBlue,
                    StrokeThickness = 1.2,
                    WidthRequest = 32,
                    HeightRequest = 32,
                    BackgroundColor = AppleHigTheme.TertiarySystemBackground,
                    VerticalOptions = LayoutOptions.Center,
                    Content = new Label
                    {
                        Text = "⚡",
                        TextColor = AppleHigTheme.SystemBlue,
                        FontSize = 17,
                        HorizontalOptions = LayoutOptions.Center,
                        VerticalOptions = LayoutOptions.Center
                    }
                },
                new HorizontalStackLayout
                {
                    Spacing = 4,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Label { Text = "AGY", TextColor = AppleHigTheme.LabelPrimary, FontSize = 17, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center },
                        new Label { Text = "POS", TextColor = AppleHigTheme.SystemBlue, FontSize = 17, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center }
                    }
                },
                // Capsule statut "En ligne"
                new Border
                {
                    Padding = new Thickness(8, 3),
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(AppleHigTheme.CornerRadiusPill) },
                    BackgroundColor = Color.FromRgba(48, 209, 88, 28),
                    Stroke = Color.FromRgba(48, 209, 88, 60),
                    StrokeThickness = 1,
                    VerticalOptions = LayoutOptions.Center,
                    Content = new HorizontalStackLayout
                    {
                        Spacing = 5,
                        VerticalOptions = LayoutOptions.Center,
                        Children =
                        {
                            new Label { Text = "●", TextColor = AppleHigTheme.SystemGreen, FontSize = 9, VerticalOptions = LayoutOptions.Center },
                            new Label { Text = "En ligne", TextColor = AppleHigTheme.SystemGreen, FontSize = 11, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center }
                        }
                    }
                }
            }
        };
        Grid.SetColumn(brandLayout, 0);

        // 2. Contrôle segmenté Apple HIG (Navigation Tabs)
        var segmentedBar = new Border
        {
            BackgroundColor = AppleHigTheme.TertiarySystemBackground,
            Stroke = AppleHigTheme.Separator,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(2),
            HeightRequest = 42,
            VerticalOptions = LayoutOptions.Center,
            Content = new ScrollView
            {
                Orientation = ScrollOrientation.Horizontal,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
                Content = new HorizontalStackLayout
                {
                    Spacing = 2,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        MakeSegmentButton("🛒 Caisse", activeTab == PosActiveViewTab.Pos, new Command(async () => await Shell.Current.GoToAsync("//pos"))),
                        MakeSegmentButton("🗺️ Salle", activeTab == PosActiveViewTab.Floor, new Command(async () => await Shell.Current.GoToAsync("//floor"))),
                        MakeSegmentButton("👨‍🍳 KDS", activeTab == PosActiveViewTab.Kds, new Command(async () => await Shell.Current.GoToAsync("//kds"))),
                        MakeSegmentButton("⚙️ Admin", activeTab == PosActiveViewTab.Admin, new Command(async () => await Shell.Current.GoToAsync("admin"))),
                        MakeSegmentButton("📜 Fiscal", activeTab == PosActiveViewTab.Fiscal, new Command(async () => await Shell.Current.GoToAsync("//fiscal")))
                    }
                }
            }
        };
        Grid.SetColumn(segmentedBar, 1);

        // 3. Profil Opérateur & Verrouillage Apple Toolbar
        var userSection = new HorizontalStackLayout
        {
            Spacing = 10,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Border
                {
                    Padding = new Thickness(10, 5),
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(AppleHigTheme.CornerRadiusSmall) },
                    BackgroundColor = AppleHigTheme.TertiarySystemBackground,
                    Stroke = AppleHigTheme.Separator,
                    StrokeThickness = 1,
                    VerticalOptions = LayoutOptions.Center,
                    Content = new HorizontalStackLayout
                    {
                        Spacing = 7,
                        VerticalOptions = LayoutOptions.Center,
                        Children =
                        {
                            new Label { Text = "👤", FontSize = 14, VerticalOptions = LayoutOptions.Center },
                            new VerticalStackLayout
                            {
                                Spacing = 0,
                                VerticalOptions = LayoutOptions.Center,
                                Children =
                                {
                                    new Label { Text = "Alexandre D.", TextColor = AppleHigTheme.LabelPrimary, FontSize = 12, FontAttributes = FontAttributes.Bold },
                                    new Label { Text = "FloorManager", TextColor = AppleHigTheme.LabelSecondary, FontSize = 10 }
                                }
                            }
                        }
                    }
                },
                new Button
                {
                    Text = "🔒",
                    BackgroundColor = AppleHigTheme.TertiarySystemBackground,
                    TextColor = AppleHigTheme.LabelPrimary,
                    FontSize = 16,
                    WidthRequest = 42,
                    HeightRequest = 42,
                    MinimumWidthRequest = 42,
                    MinimumHeightRequest = 42,
                    CornerRadius = (int)AppleHigTheme.CornerRadiusSmall,
                    Padding = 0,
                    BorderColor = AppleHigTheme.Separator,
                    BorderWidth = 1,
                    Command = new Command(async () => await Shell.Current.GoToAsync("//pin"))
                }
            }
        };
        Grid.SetColumn(userSection, 2);

        Children.Add(brandLayout);
        Children.Add(segmentedBar);
        Children.Add(userSection);
    }

    private static Button MakeSegmentButton(string text, bool isActive, Command? command)
    {
        return new Button
        {
            Text = text,
            BackgroundColor = isActive ? AppleHigTheme.SystemBlue : Colors.Transparent,
            TextColor = isActive ? Colors.White : AppleHigTheme.LabelSecondary,
            FontSize = 12,
            FontAttributes = isActive ? FontAttributes.Bold : FontAttributes.None,
            HeightRequest = 36,
            CornerRadius = 8,
            Padding = new Thickness(8, 0),
            Command = command
        };
    }
}
#endif
