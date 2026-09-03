#if MAUI_UI
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using RestaurantPos.Client.Maui.ViewModels;

namespace RestaurantPos.Client.Maui.Views.Admin;

/// <summary>
/// Hub du Back-Office : navigation par onglets entre Catalogue, Personnel, Imprimantes.
/// </summary>
public class AdminShellPage : ContentPage
{
    private readonly AdminHubViewModel _vm;
    private readonly CatalogAdminPage _catalogPage;
    private readonly StaffAdminPage _staffPage;

    public AdminShellPage(AdminHubViewModel vm, CatalogAdminPage catalogPage, StaffAdminPage staffPage)
    {
        _vm = vm;
        BindingContext = vm;
        _catalogPage = catalogPage;
        _staffPage = staffPage;
        BackgroundColor = Color.FromArgb("#0F172A");
        Shell.SetNavBarIsVisible(this, false);
        Build();
    }

    private void Build()
    {
        // Barre onglets
        var tabBar = new HorizontalStackLayout
        {
            BackgroundColor = Color.FromArgb("#1E293B"),
            Padding = new Thickness(12, 8),
            Spacing = 8
        };

        var tabs = new[]
        {
            ("📦 Catalogue", AdminSection.Catalog),
            ("👤 Personnel", AdminSection.Staff),
            ("🖨 Imprimantes", AdminSection.Printers),
            ("🗂 Disposition", AdminSection.Layout)
        };

        foreach (var (label, section) in tabs)
        {
            var btn = new Button
            {
                Text = label,
                HeightRequest = 42,
                CornerRadius = 8,
                FontSize = 14,
                Padding = new Thickness(14, 0),
                Command = _vm.SwitchSectionCommand,
                CommandParameter = section
            };
            btn.SetBinding(BackgroundColorProperty, new Binding(
                nameof(AdminHubViewModel.CurrentSection),
                converter: new SectionColorConverter(section)));
            btn.SetBinding(Button.TextColorProperty, new Binding(
                nameof(AdminHubViewModel.CurrentSection),
                converter: new SectionTextColorConverter(section)));
            tabBar.Add(btn);
        }

        // Bouton retour salle
        var backBtn = new Button
        {
            Text = "← Retour Salle",
            BackgroundColor = Color.FromArgb("#334155"),
            TextColor = Color.FromArgb("#94A3B8"),
            FontSize = 13,
            HeightRequest = 42,
            CornerRadius = 8,
            Padding = new Thickness(14, 0),
            HorizontalOptions = LayoutOptions.EndAndExpand,
            Command = new Command(async () => await Shell.Current.GoToAsync("//floor"))
        };

        var tabBarGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star }
            },
            BackgroundColor = Color.FromArgb("#1E293B"),
            Padding = new Thickness(12, 8)
        };
        Grid.SetColumn(tabBar, 0);
        Grid.SetColumn(backBtn, 1);
        tabBarGrid.Add(tabBar);
        tabBarGrid.Add(backBtn);

        // Zone de contenu (change selon l'onglet actif)
        var contentArea = new ContentView();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AdminHubViewModel.CurrentSection))
            {
                contentArea.Content = _vm.CurrentSection switch
                {
                    AdminSection.Catalog => _catalogPage.BuildContent(),
                    AdminSection.Staff => _staffPage.BuildContent(),
                    _ => new Label
                    {
                        Text = "Section en cours de developpement...",
                        TextColor = Color.FromArgb("#94A3B8"),
                        FontSize = 18,
                        HorizontalOptions = LayoutOptions.Center,
                        VerticalOptions = LayoutOptions.Center
                    }
                };
            }
        };

        // Initialiser avec catalogue
        contentArea.Content = _catalogPage.BuildContent();

        Content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star }
            },
            Children =
            {
                AddToGrid(tabBarGrid, 0),
                AddToGrid(contentArea, 1)
            }
        };
    }

    private static T AddToGrid<T>(T view, int row) where T : View
    {
        Grid.SetRow(view, row);
        return view;
    }

    private class SectionColorConverter : IValueConverter
    {
        private readonly AdminSection _target;
        public SectionColorConverter(AdminSection t) { _target = t; }
        public object Convert(object? v, Type t, object? p, System.Globalization.CultureInfo c)
            => v is AdminSection s && s == _target
                ? Color.FromArgb("#3B82F6")
                : Color.FromArgb("#334155");
        public object ConvertBack(object? v, Type t, object? p, System.Globalization.CultureInfo c)
            => throw new NotImplementedException();
    }

    private class SectionTextColorConverter : IValueConverter
    {
        private readonly AdminSection _target;
        public SectionTextColorConverter(AdminSection t) { _target = t; }
        public object Convert(object? v, Type t, object? p, System.Globalization.CultureInfo c)
            => v is AdminSection s && s == _target ? Colors.White : Color.FromArgb("#94A3B8");
        public object ConvertBack(object? v, Type t, object? p, System.Globalization.CultureInfo c)
            => throw new NotImplementedException();
    }
}
#endif
