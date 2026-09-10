#if MAUI_UI
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using RestaurantPos.Client.Maui.ViewModels;

namespace RestaurantPos.Client.Maui.Views;

/// <summary>
/// Page modale de selection des modificateurs (cuissons, sauces, supplements).
/// Presentee en popup par-dessus la page POS.
/// </summary>
public class ModifiersPopupPage : ContentPage
{
    private readonly ModifiersViewModel _vm;

    public ModifiersPopupPage(ModifiersViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
        BackgroundColor = Color.FromArgb("CC000000");
        Shell.SetNavBarIsVisible(this, false);
        Build();
    }

    private void Build()
    {
        var card = new Frame
        {
            BackgroundColor = Color.FromArgb("#1E293B"),
            CornerRadius = 20,
            Padding = new Thickness(24),
            WidthRequest = 480,
            MaximumHeightRequest = 600,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            HasShadow = false,
            Content = BuildCardContent()
        };

        Content = new Grid
        {
            Children = { card }
        };
    }

    private View BuildCardContent()
    {
        // Titre et nom du produit
        var titleLabel = new Label
        {
            Text = "Modificateurs",
            TextColor = Colors.White,
            FontSize = 22,
            FontAttributes = FontAttributes.Bold
        };

        var productLabel = new Label { TextColor = Color.FromArgb("#94A3B8"), FontSize = 15 };
        productLabel.SetBinding(Label.TextProperty,
            new Binding($"{nameof(ModifiersViewModel.Product)}.Name", stringFormat: "Article : {0}"));

        var groupLabel = new Label { TextColor = Color.FromArgb("#F59E0B"), FontSize = 14, FontAttributes = FontAttributes.Bold };
        groupLabel.SetBinding(Label.TextProperty,
            new Binding($"{nameof(ModifiersViewModel.Group)}.GroupName"));

        // Options selectionnables
        var optionsList = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            ItemTemplate = new DataTemplate(() =>
            {
                var check = new Frame
                {
                    Padding = new Thickness(14, 10),
                    Margin = new Thickness(0, 4),
                    CornerRadius = 10,
                    HasShadow = false
                };

                var label = new Label { FontSize = 16, VerticalOptions = LayoutOptions.Center };
                label.SetBinding(Label.TextProperty, "Option.Name");
                label.SetBinding(Label.TextColorProperty, new Binding("IsSelected",
                    converter: new BoolToColorConverter(Colors.White, Color.FromArgb("#94A3B8"))));

                var icon = new Label { FontSize = 20, VerticalOptions = LayoutOptions.Center };
                icon.SetBinding(Label.TextProperty, new Binding("IsSelected",
                    converter: new BoolToStringConverter("✅", "⬜")));

                check.BackgroundColor = Color.FromArgb("#0F172A");
                check.Content = new HorizontalStackLayout
                {
                    Spacing = 12,
                    Children = { icon, label }
                };

                var tap = new TapGestureRecognizer();
                tap.SetBinding(TapGestureRecognizer.CommandProperty,
                    new Binding(nameof(ModifiersViewModel.ToggleOptionCommand), source: _vm));
                tap.SetBinding(TapGestureRecognizer.CommandParameterProperty, new Binding("."));
                check.GestureRecognizers.Add(tap);

                return check;
            })
        };
        optionsList.SetBinding(CollectionView.ItemsSourceProperty, nameof(ModifiersViewModel.SelectableOptions));

        // Champ instructions cuisine
        var instructionsEntry = new Entry
        {
            Placeholder = "Instructions specifiques (optionnel)...",
            PlaceholderColor = Color.FromArgb("#475569"),
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#0F172A"),
            FontSize = 14
        };
        instructionsEntry.SetBinding(Entry.TextProperty, nameof(ModifiersViewModel.SpecialInstructions));

        // Erreur
        var errorLabel = new Label
        {
            TextColor = Color.FromArgb("#EF4444"),
            FontSize = 13,
            IsVisible = false
        };
        errorLabel.SetBinding(Label.TextProperty, nameof(ModifiersViewModel.ValidationErrorMessage));
        errorLabel.SetBinding(IsVisibleProperty, new Binding(
            nameof(ModifiersViewModel.ValidationErrorMessage),
            converter: new StringToBoolConverter()));

        // Boutons
        var confirmBtn = new Button
        {
            Text = "✔ Confirmer",
            BackgroundColor = Color.FromArgb("#10B981"),
            TextColor = Colors.White,
            HeightRequest = 52,
            CornerRadius = 12,
            FontSize = 17,
            Command = _vm.ConfirmModifiersCommand
        };

        var cancelBtn = new Button
        {
            Text = "Annuler",
            BackgroundColor = Color.FromArgb("#334155"),
            TextColor = Color.FromArgb("#94A3B8"),
            HeightRequest = 44,
            CornerRadius = 10,
            FontSize = 15,
            Command = new Command(async () => await Shell.Current.GoToAsync(".."))
        };

        // Navigation auto apres confirmation
        _vm.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName == nameof(ModifiersViewModel.IsCompleted) && _vm.IsCompleted)
                await Shell.Current.GoToAsync("..");
        };

        return new VerticalStackLayout
        {
            Spacing = 14,
            Children =
            {
                titleLabel,
                productLabel,
                groupLabel,
                new BoxView { HeightRequest = 1, Color = Color.FromArgb("#334155") },
                optionsList,
                new BoxView { HeightRequest = 1, Color = Color.FromArgb("#334155") },
                new Label { Text = "Commentaire cuisine :", TextColor = Color.FromArgb("#94A3B8"), FontSize = 13 },
                instructionsEntry,
                errorLabel,
                confirmBtn,
                cancelBtn
            }
        };
    }

    private class BoolToColorConverter : IValueConverter
    {
        private readonly Color _trueColor;
        private readonly Color _falseColor;
        public BoolToColorConverter(Color t, Color f) { _trueColor = t; _falseColor = f; }
        public object Convert(object? v, Type t, object? p, System.Globalization.CultureInfo c)
            => v is true ? _trueColor : _falseColor;
        public object ConvertBack(object? v, Type t, object? p, System.Globalization.CultureInfo c)
            => throw new NotImplementedException();
    }

    private class BoolToStringConverter : IValueConverter
    {
        private readonly string _trueVal;
        private readonly string _falseVal;
        public BoolToStringConverter(string t, string f) { _trueVal = t; _falseVal = f; }
        public object Convert(object? v, Type t, object? p, System.Globalization.CultureInfo c)
            => v is true ? _trueVal : _falseVal;
        public object ConvertBack(object? v, Type t, object? p, System.Globalization.CultureInfo c)
            => throw new NotImplementedException();
    }

    private class StringToBoolConverter : IValueConverter
    {
        public object Convert(object? v, Type t, object? p, System.Globalization.CultureInfo c)
            => v is string s && !string.IsNullOrEmpty(s);
        public object ConvertBack(object? v, Type t, object? p, System.Globalization.CultureInfo c)
            => throw new NotImplementedException();
    }
}
#endif
