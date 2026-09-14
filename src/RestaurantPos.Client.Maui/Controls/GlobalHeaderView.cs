#if MAUI_UI
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

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
/// En-tête global réutilisable répliquant fidèlement le bandeau web (app-header).
/// </summary>
public class GlobalHeaderView : Grid
{
    public GlobalHeaderView(PosActiveViewTab activeTab)
    {
        BackgroundColor = Color.FromArgb("#1E293B");
        Padding = new Thickness(14, 8);
        HeightRequest = 58;
        ColumnSpacing = 12;

        ColumnDefinitions = new ColumnDefinitionCollection
        {
            new ColumnDefinition { Width = GridLength.Auto }, // Logo & status badge (~150px)
            new ColumnDefinition { Width = GridLength.Star }, // 5 Nav Tabs (~520px)
            new ColumnDefinition { Width = GridLength.Auto }  // Operator & Lock (~145px)
        };

        // Brand & Status Badge (Compact)
        var brandLayout = new HorizontalStackLayout
        {
            Spacing = 8,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Border
                {
                    Padding = 0,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(7) },
                    Stroke = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(1, 1),
                        GradientStops =
                        {
                            new GradientStop { Color = Color.FromArgb("#38BDF8"), Offset = 0.0f },
                            new GradientStop { Color = Color.FromArgb("#818CF8"), Offset = 1.0f }
                        }
                    },
                    StrokeThickness = 1.5,
                    WidthRequest = 28,
                    HeightRequest = 28,
                    Background = Color.FromArgb("#0F172A"),
                    VerticalOptions = LayoutOptions.Center,
                    Content = new Label
                    {
                        Text = "⚡",
                        TextColor = Color.FromArgb("#38BDF8"),
                        FontSize = 16,
                        HorizontalOptions = LayoutOptions.Center,
                        VerticalOptions = LayoutOptions.Center
                    }
                },
                new HorizontalStackLayout
                {
                    Spacing = 3,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Label { Text = "AGY", TextColor = Colors.White, FontSize = 16, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center },
                        new Label { Text = "POS", TextColor = Color.FromArgb("#3B82F6"), FontSize = 16, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center }
                    }
                },
                new Frame
                {
                    Padding = new Thickness(6, 2),
                    CornerRadius = 10,
                    HasShadow = false,
                    BackgroundColor = Color.FromRgba(16, 185, 129, 30),
                    BorderColor = Color.FromRgba(16, 185, 129, 75),
                    VerticalOptions = LayoutOptions.Center,
                    Content = new HorizontalStackLayout
                    {
                        Spacing = 4,
                        VerticalOptions = LayoutOptions.Center,
                        Children =
                        {
                            new Label { Text = "●", TextColor = Color.FromArgb("#10B981"), FontSize = 8, VerticalOptions = LayoutOptions.Center },
                            new Label { Text = "En ligne", TextColor = Color.FromArgb("#10B981"), FontSize = 10, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center }
                        }
                    }
                }
            }
        };
        Grid.SetColumn(brandLayout, 0);

        // Navigation Tabs (5 tabs matching web, with ample space)
        var navTabs = new HorizontalStackLayout
        {
            Spacing = 6,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                MakeTabButton("🛒 Caisse", activeTab == PosActiveViewTab.Pos, new Command(async () => await Shell.Current.GoToAsync("//pos"))),
                MakeTabButton("🗺️ Plan de Salle", activeTab == PosActiveViewTab.Floor, new Command(async () => await Shell.Current.GoToAsync("//floor"))),
                MakeTabButton("👨‍🍳 Cuisine KDS", activeTab == PosActiveViewTab.Kds, new Command(async () => await Shell.Current.GoToAsync("//kds"))),
                MakeTabButton("⚙️ Paramétrage", activeTab == PosActiveViewTab.Admin, new Command(async () => await Shell.Current.GoToAsync("admin"))),
                MakeTabButton("📜 Fiscalité NF525", activeTab == PosActiveViewTab.Fiscal, new Command(async () => await Shell.Current.GoToAsync("fiscal")))
            }
        };
        Grid.SetColumn(navTabs, 1);

        // Operator & Lock (Compact, prevents covering Fiscalité button)
        var userSection = new HorizontalStackLayout
        {
            Spacing = 8,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Frame
                {
                    Padding = new Thickness(8, 4),
                    CornerRadius = 7,
                    HasShadow = false,
                    BackgroundColor = Color.FromArgb("#334155"),
                    BorderColor = Color.FromRgba(255, 255, 255, 20),
                    VerticalOptions = LayoutOptions.Center,
                    Content = new HorizontalStackLayout
                    {
                        Spacing = 6,
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
                                    new Label { Text = "Alexandre D.", TextColor = Colors.White, FontSize = 12, FontAttributes = FontAttributes.Bold },
                                    new Label { Text = "FloorManager", TextColor = Color.FromArgb("#94A3B8"), FontSize = 9 }
                                }
                            }
                        }
                    }
                },
                new Button
                {
                    Text = "🔒",
                    BackgroundColor = Color.FromArgb("#334155"),
                    TextColor = Colors.White,
                    FontSize = 15,
                    WidthRequest = 36,
                    HeightRequest = 36,
                    CornerRadius = 7,
                    Padding = 0,
                    Command = new Command(async () => await Shell.Current.GoToAsync("//pin"))
                }
            }
        };
        Grid.SetColumn(userSection, 2);

        Children.Add(brandLayout);
        Children.Add(navTabs);
        Children.Add(userSection);
    }

    private static Button MakeTabButton(string text, bool isActive, Command? command)
    {
        return new Button
        {
            Text = text,
            BackgroundColor = isActive ? Color.FromArgb("#3B82F6") : Color.FromArgb("#334155"),
            TextColor = isActive ? Colors.White : Color.FromArgb("#94A3B8"),
            FontSize = 12,
            FontAttributes = isActive ? FontAttributes.Bold : FontAttributes.None,
            HeightRequest = 36,
            CornerRadius = 7,
            Padding = new Thickness(10, 0),
            Command = command
        };
    }
}
#endif
