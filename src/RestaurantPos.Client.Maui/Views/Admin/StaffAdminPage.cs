#if MAUI_UI
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using RestaurantPos.Client.Maui.ViewModels;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Client.Maui.Views.Admin;

/// <summary>
/// Page d'administration du personnel.
/// Cree, modifie et desactive les comptes operateurs avec code PIN.
/// </summary>
public class StaffAdminPage : ContentPage
{
    private readonly StaffAdminViewModel _vm;

    public StaffAdminPage(StaffAdminViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
    }

    /// <summary>Construit le contenu pour integration dans AdminShellPage.</summary>
    public View BuildContent()
    {
        if (_vm.StaffMembers.Count == 0)
            _ = _vm.LoadStaffCommand.ExecuteAsync(null);

        var panel = new Grid
        {
            BackgroundColor = Color.FromArgb("#0F172A"),
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star },
                new RowDefinition { Height = GridLength.Auto }
            },
            Padding = new Thickness(16)
        };

        // Header
        var headerRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };
        headerRow.Add(new Label
        {
            Text = "👤 Gestion du Personnel",
            TextColor = Colors.White,
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center
        });

        var addBtn = new Button
        {
            Text = "+ Nouveau Membre",
            BackgroundColor = Color.FromArgb("#3B82F6"),
            TextColor = Colors.White,
            FontSize = 14,
            HeightRequest = 42,
            CornerRadius = 10,
            Padding = new Thickness(16, 0),
            Command = _vm.ShowCreateFormCommand
        };
        Grid.SetColumn(addBtn, 1);
        headerRow.Add(addBtn);
        Grid.SetRow(headerRow, 0);

        // Formulaire creation (visible conditionnellement)
        var createForm = BuildCreateForm();
        createForm.SetBinding(IsVisibleProperty, nameof(StaffAdminViewModel.IsFormVisible));
        Grid.SetRow(createForm, 1);

        // Liste personnel
        var staffList = new CollectionView
        {
            ItemTemplate = new DataTemplate(() =>
            {
                var row = new Grid
                {
                    Padding = new Thickness(16, 12),
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = GridLength.Auto },
                        new ColumnDefinition { Width = GridLength.Star },
                        new ColumnDefinition { Width = GridLength.Auto },
                        new ColumnDefinition { Width = GridLength.Auto }
                    }
                };

                // Avatar / initiale
                var avatar = new Frame
                {
                    WidthRequest = 44,
                    HeightRequest = 44,
                    CornerRadius = 22,
                    BackgroundColor = Color.FromArgb("#3B82F6"),
                    HasShadow = false,
                    Padding = 0
                };
                var initLabel = new Label
                {
                    FontSize = 18,
                    TextColor = Colors.White,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                };
                initLabel.SetBinding(Label.TextProperty, new Binding("Name",
                    converter: new FirstLetterConverter()));
                avatar.Content = initLabel;

                var nameLabel = new Label
                {
                    FontSize = 15,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White,
                    VerticalOptions = LayoutOptions.Center
                };
                nameLabel.SetBinding(Label.TextProperty, "Name");

                var roleLabel = new Label
                {
                    FontSize = 12,
                    TextColor = Color.FromArgb("#94A3B8")
                };
                roleLabel.SetBinding(Label.TextProperty, "Role");

                var nameColumn = new VerticalStackLayout
                {
                    VerticalOptions = LayoutOptions.Center,
                    Spacing = 2,
                    Children = { nameLabel, roleLabel }
                };

                var statusDot = new BoxView
                {
                    WidthRequest = 10,
                    HeightRequest = 10,
                    CornerRadius = 5,
                    VerticalOptions = LayoutOptions.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                statusDot.SetBinding(BoxView.ColorProperty, new Binding("IsActive",
                    converter: new BoolToColorConverter(Color.FromArgb("#10B981"), Color.FromArgb("#EF4444"))));

                var deactivateBtn = new Button
                {
                    Text = "Desactiver",
                    BackgroundColor = Color.FromArgb("#7F1D1D"),
                    TextColor = Colors.White,
                    FontSize = 12,
                    HeightRequest = 36,
                    CornerRadius = 8,
                    Padding = new Thickness(10, 0)
                };
                deactivateBtn.SetBinding(Button.CommandProperty,
                    new Binding(nameof(StaffAdminViewModel.DeactivateStaffCommand), source: _vm));
                deactivateBtn.SetBinding(Button.CommandParameterProperty, new Binding("."));
                deactivateBtn.SetBinding(IsEnabledProperty, "IsActive");

                Grid.SetColumn(nameColumn, 1);
                Grid.SetColumn(statusDot, 2);
                Grid.SetColumn(deactivateBtn, 3);

                row.Add(avatar);
                row.Add(nameColumn);
                row.Add(statusDot);
                row.Add(deactivateBtn);

                return new Frame
                {
                    Padding = 0,
                    Margin = new Thickness(0, 4),
                    CornerRadius = 12,
                    HasShadow = false,
                    BackgroundColor = Color.FromArgb("#1E293B"),
                    Content = row
                };
            })
        };
        staffList.SetBinding(CollectionView.ItemsSourceProperty, nameof(StaffAdminViewModel.StaffMembers));
        Grid.SetRow(staffList, 2);

        // Barre statut
        var statusBar = new Label
        {
            TextColor = Color.FromArgb("#10B981"),
            FontSize = 13,
            Padding = new Thickness(0, 8),
            BackgroundColor = Color.FromArgb("#0F172A")
        };
        statusBar.SetBinding(Label.TextProperty, nameof(StaffAdminViewModel.StatusMessage));
        Grid.SetRow(statusBar, 3);

        panel.Add(headerRow);
        panel.Add(createForm);
        panel.Add(staffList);
        panel.Add(statusBar);

        return panel;
    }

    private View BuildCreateForm()
    {
        var form = new Frame
        {
            BackgroundColor = Color.FromArgb("#1E293B"),
            CornerRadius = 12,
            Padding = new Thickness(16),
            Margin = new Thickness(0, 8),
            HasShadow = false,
            IsVisible = false
        };
        form.SetBinding(IsVisibleProperty, nameof(StaffAdminViewModel.IsFormVisible));

        var nameEntry = new Entry
        {
            Placeholder = "Nom complet...",
            PlaceholderColor = Color.FromArgb("#475569"),
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#0F172A"),
            HeightRequest = 44
        };
        nameEntry.SetBinding(Entry.TextProperty, nameof(StaffAdminViewModel.NewMemberName));

        var pinEntry = new Entry
        {
            Placeholder = "Code PIN (4 chiffres)...",
            PlaceholderColor = Color.FromArgb("#475569"),
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#0F172A"),
            Keyboard = Keyboard.Numeric,
            IsPassword = true,
            HeightRequest = 44
        };
        pinEntry.SetBinding(Entry.TextProperty, nameof(StaffAdminViewModel.NewMemberPin));

        var confirmBtn = new Button
        {
            Text = "✔ Creer le Membre",
            BackgroundColor = Color.FromArgb("#059669"),
            TextColor = Colors.White,
            HeightRequest = 48,
            CornerRadius = 10,
            FontSize = 16,
            Command = _vm.CreateStaffCommand
        };

        var cancelBtn = new Button
        {
            Text = "Annuler",
            BackgroundColor = Color.FromArgb("#334155"),
            TextColor = Color.FromArgb("#94A3B8"),
            HeightRequest = 40,
            CornerRadius = 8,
            FontSize = 14,
            Command = _vm.HideFormCommand
        };

        form.Content = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                new Label { Text = "Nouveau Membre", TextColor = Colors.White, FontSize = 16, FontAttributes = FontAttributes.Bold },
                nameEntry,
                pinEntry,
                confirmBtn,
                cancelBtn
            }
        };

        return form;
    }

    private class FirstLetterConverter : IValueConverter
    {
        public object Convert(object? v, Type t, object? p, System.Globalization.CultureInfo c)
            => v is string s && s.Length > 0 ? s[0].ToString().ToUpper() : "?";
        public object ConvertBack(object? v, Type t, object? p, System.Globalization.CultureInfo c)
            => throw new NotImplementedException();
    }

    private class BoolToColorConverter : IValueConverter
    {
        private readonly Color _t, _f;
        public BoolToColorConverter(Color t, Color f) { _t = t; _f = f; }
        public object Convert(object? v, Type tt, object? p, System.Globalization.CultureInfo c)
            => v is true ? _t : _f;
        public object ConvertBack(object? v, Type tt, object? p, System.Globalization.CultureInfo c)
            => throw new NotImplementedException();
    }
}
#endif
