#if MAUI_UI
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using RestaurantPos.Client.Maui.ViewModels;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Client.Maui.Views.Admin;

/// <summary>
/// Page d'administration du catalogue (categories et articles).
/// CRUD complet : creer, modifier, archiver produits et categories.
/// </summary>
public class CatalogAdminPage : ContentPage
{
    private readonly CatalogAdminViewModel _vm;

    public CatalogAdminPage(CatalogAdminViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
    }

    /// <summary>Construit le contenu pour integration dans AdminShellPage.</summary>
    public View BuildContent()
    {
        var layout = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(240) },
                new ColumnDefinition { Width = GridLength.Star }
            },
            ColumnSpacing = 0
        };

        layout.Add(BuildCategoryPanel(), 0, 0);
        layout.Add(BuildProductPanel(), 1, 0);

        // Charger les categories si pas encore fait
        if (_vm.Categories.Count == 0)
        {
            _ = _vm.LoadCategoriesCommand.ExecuteAsync(null);
        }

        return layout;
    }

    private View BuildCategoryPanel()
    {
        var panel = new VerticalStackLayout
        {
            BackgroundColor = Color.FromArgb("#1E293B"),
            Spacing = 0
        };

        panel.Add(new Label
        {
            Text = "Familles",
            TextColor = Colors.White,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            Padding = new Thickness(16, 14)
        });

        // Champ creation categorie
        var newCatEntry = new Entry
        {
            Placeholder = "Nouvelle famille...",
            PlaceholderColor = Color.FromArgb("#475569"),
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#0F172A"),
            Margin = new Thickness(8),
            HeightRequest = 44
        };

        var addCatBtn = new Button
        {
            Text = "+ Ajouter",
            BackgroundColor = Color.FromArgb("#3B82F6"),
            TextColor = Colors.White,
            FontSize = 14,
            HeightRequest = 40,
            CornerRadius = 8,
            Margin = new Thickness(8, 0, 8, 8),
            Command = new Command(async () =>
            {
                if (!string.IsNullOrWhiteSpace(newCatEntry.Text))
                {
                    await _vm.CreateCategoryCommand.ExecuteAsync(newCatEntry.Text);
                    newCatEntry.Text = string.Empty;
                }
            })
        };

        var catList = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            ItemTemplate = new DataTemplate(() =>
            {
                var frame = new Frame
                {
                    Padding = new Thickness(12, 10),
                    Margin = new Thickness(6, 2),
                    CornerRadius = 8,
                    HasShadow = false
                };

                var label = new Label
                {
                    FontSize = 15,
                    TextColor = Colors.White
                };
                label.SetBinding(Label.TextProperty, "Name");

                frame.Content = label;

                var tap = new TapGestureRecognizer();
                tap.SetBinding(TapGestureRecognizer.CommandProperty,
                    new Binding(nameof(CatalogAdminViewModel.SelectCategoryCommand), source: _vm));
                tap.SetBinding(TapGestureRecognizer.CommandParameterProperty, new Binding("."));
                frame.GestureRecognizers.Add(tap);

                return frame;
            })
        };
        catList.SetBinding(CollectionView.ItemsSourceProperty, nameof(CatalogAdminViewModel.Categories));
        catList.SetBinding(CollectionView.SelectedItemProperty, nameof(CatalogAdminViewModel.SelectedCategory));

        panel.Add(newCatEntry);
        panel.Add(addCatBtn);
        panel.Add(new BoxView { HeightRequest = 1, Color = Color.FromArgb("#334155") });
        panel.Add(catList);

        return panel;
    }

    private View BuildProductPanel()
    {
        var panel = new Grid
        {
            BackgroundColor = Color.FromArgb("#0F172A"),
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star },
                new RowDefinition { Height = GridLength.Auto }
            }
        };

        // Header
        var catNameLabel = new Label
        {
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            Padding = new Thickness(16, 12)
        };
        catNameLabel.SetBinding(Label.TextProperty,
            new Binding($"{nameof(CatalogAdminViewModel.SelectedCategory)}.Name",
                stringFormat: "Articles : {0}"));

        var loadingLabel = new Label
        {
            Text = "Chargement...",
            TextColor = Color.FromArgb("#94A3B8"),
            FontSize = 13,
            IsVisible = false,
            HorizontalOptions = LayoutOptions.End,
            Margin = new Thickness(0, 0, 16, 0)
        };
        loadingLabel.SetBinding(IsVisibleProperty, nameof(CatalogAdminViewModel.IsLoading));

        var headerGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };
        headerGrid.Add(catNameLabel);
        Grid.SetColumn(loadingLabel, 1);
        headerGrid.Add(loadingLabel);
        Grid.SetRow(headerGrid, 0);

        // Champ creation article
        var newProdEntry = new Entry
        {
            Placeholder = "Nom du nouvel article...",
            PlaceholderColor = Color.FromArgb("#475569"),
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#1E293B"),
            Margin = new Thickness(12, 4),
            HeightRequest = 44
        };

        var addProdBtn = new Button
        {
            Text = "+ Ajouter Article",
            BackgroundColor = Color.FromArgb("#059669"),
            TextColor = Colors.White,
            FontSize = 15,
            HeightRequest = 44,
            CornerRadius = 10,
            Margin = new Thickness(12, 0, 12, 8),
            Command = new Command(async () =>
            {
                if (!string.IsNullOrWhiteSpace(newProdEntry.Text))
                {
                    await _vm.CreateProductCommand.ExecuteAsync(newProdEntry.Text);
                    newProdEntry.Text = string.Empty;
                }
            })
        };

        var addRow = new VerticalStackLayout { Children = { newProdEntry, addProdBtn } };
        Grid.SetRow(addRow, 1);

        // Liste articles
        var productList = new CollectionView
        {
            ItemTemplate = new DataTemplate(() =>
            {
                var row = new Grid
                {
                    Padding = new Thickness(16, 10),
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = GridLength.Star },
                        new ColumnDefinition { Width = GridLength.Auto },
                        new ColumnDefinition { Width = GridLength.Auto }
                    }
                };

                var nameLabel = new Label
                {
                    FontSize = 15,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White,
                    VerticalOptions = LayoutOptions.Center
                };
                nameLabel.SetBinding(Label.TextProperty, "Name");

                var priceLabel = new Label
                {
                    FontSize = 15,
                    TextColor = Color.FromArgb("#10B981"),
                    VerticalOptions = LayoutOptions.Center,
                    Margin = new Thickness(0, 0, 12, 0)
                };
                priceLabel.SetBinding(Label.TextProperty, "Price", stringFormat: "{0:F2} €");

                var archiveBtn = new Button
                {
                    Text = "🗑",
                    BackgroundColor = Color.FromArgb("#7F1D1D"),
                    TextColor = Colors.White,
                    FontSize = 14,
                    WidthRequest = 40,
                    HeightRequest = 36,
                    CornerRadius = 8,
                    Padding = 0
                };
                archiveBtn.SetBinding(Button.CommandProperty,
                    new Binding(nameof(CatalogAdminViewModel.ArchiveProductCommand), source: _vm));
                archiveBtn.SetBinding(Button.CommandParameterProperty, new Binding("."));

                Grid.SetColumn(priceLabel, 1);
                Grid.SetColumn(archiveBtn, 2);
                row.Add(nameLabel);
                row.Add(priceLabel);
                row.Add(archiveBtn);

                return new Frame
                {
                    Padding = 0,
                    Margin = new Thickness(8, 3),
                    CornerRadius = 10,
                    HasShadow = false,
                    BackgroundColor = Color.FromArgb("#1E293B"),
                    Content = row
                };
            })
        };
        productList.SetBinding(CollectionView.ItemsSourceProperty, nameof(CatalogAdminViewModel.Products));
        Grid.SetRow(productList, 2);

        // Barre de statut
        var statusBar = new Label
        {
            TextColor = Color.FromArgb("#10B981"),
            FontSize = 13,
            Padding = new Thickness(16, 8),
            BackgroundColor = Color.FromArgb("#1E293B")
        };
        statusBar.SetBinding(Label.TextProperty, nameof(CatalogAdminViewModel.StatusMessage));
        Grid.SetRow(statusBar, 3);

        panel.Add(headerGrid);
        panel.Add(addRow);
        panel.Add(productList);
        panel.Add(statusBar);

        return panel;
    }
}
#endif
