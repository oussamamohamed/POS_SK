#if MAUI_UI
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using RestaurantPos.Client.Maui.Theme;
using RestaurantPos.Client.Maui.ViewModels;

namespace RestaurantPos.Client.Maui.Views;

/// <summary>
/// Page modale de sélection des modificateurs conforme aux Apple Human Interface Guidelines (HIG) pour iPadOS.
/// Présentation Apple Sheet avec grabber handle, cellules de sélection Inset et boutons ergonomiques.
/// </summary>
public class ModifiersPopupPage : ContentPage
{
    private readonly ModifiersViewModel _vm;

    public ModifiersPopupPage(ModifiersViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
        BackgroundColor = Color.FromArgb("#AA000000");
        Shell.SetNavBarIsVisible(this, false);
        Build();
    }

    private void Build()
    {
        var card = new Border
        {
            BackgroundColor = AppleHigTheme.SecondarySystemBackground,
            Stroke = AppleHigTheme.Separator,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(AppleHigTheme.CornerRadiusSheet) },
            Padding = new Thickness(26, 16),
            WidthRequest = 480,
            MaximumHeightRequest = 640,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            Content = BuildCardContent()
        };

        Content = new Grid
        {
            Children = { card }
        };
    }

    private View BuildCardContent()
    {
        // 1. Titre et informations produit (Vérifié par Apple Vision OCR : "Modificateurs", "Article")
        var titleLabel = new Label
        {
            Text = "Modificateurs",
            TextColor = AppleHigTheme.LabelPrimary,
            FontSize = AppleHigTheme.Title2,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center
        };

        var productLabel = new Label
        {
            TextColor = AppleHigTheme.LabelSecondary,
            FontSize = AppleHigTheme.Subheadline,
            HorizontalOptions = LayoutOptions.Center
        };
        productLabel.SetBinding(Label.TextProperty,
            new Binding($"{nameof(ModifiersViewModel.Product)}.Name", stringFormat: "Article : {0}"));

        var groupLabel = new Label
        {
            TextColor = AppleHigTheme.SystemOrange,
            FontSize = AppleHigTheme.Headline,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center
        };
        groupLabel.SetBinding(Label.TextProperty,
            new Binding($"{nameof(ModifiersViewModel.Group)}.GroupName"));

        // 2. Options sélectionnables (Cellules Inset)
        var optionsList = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            ItemTemplate = new DataTemplate(() =>
            {
                var check = new Border
                {
                    Padding = new Thickness(14, 12),
                    Margin = new Thickness(0, 3),
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(AppleHigTheme.CornerRadiusMedium) },
                    Stroke = AppleHigTheme.Separator,
                    StrokeThickness = 1,
                    BackgroundColor = AppleHigTheme.TertiarySystemBackground
                };

                var label = new Label
                {
                    FontSize = AppleHigTheme.Headline,
                    VerticalOptions = LayoutOptions.Center
                };
                label.SetBinding(Label.TextProperty, "Option.Name");
                label.SetBinding(Label.TextColorProperty, new Binding("IsSelected",
                    converter: new BoolToColorConverter(AppleHigTheme.LabelPrimary, AppleHigTheme.LabelSecondary)));

                var icon = new Label { FontSize = 18, VerticalOptions = LayoutOptions.Center };
                icon.SetBinding(Label.TextProperty, new Binding("IsSelected",
                    converter: new BoolToStringConverter("✅", "⬜")));

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

        // 3. Champ commentaire cuisine
        var instructionsEntry = new Border
        {
            Padding = new Thickness(12, 0),
            BackgroundColor = AppleHigTheme.TertiarySystemBackground,
            Stroke = AppleHigTheme.Separator,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
            Content = new Entry
            {
                Placeholder = "Instructions spécifiques (optionnel)...",
                PlaceholderColor = AppleHigTheme.LabelTertiary,
                TextColor = AppleHigTheme.LabelPrimary,
                BackgroundColor = Colors.Transparent,
                FontSize = AppleHigTheme.Subheadline,
                HeightRequest = 42
            }.Also(e => e.SetBinding(Entry.TextProperty, nameof(ModifiersViewModel.SpecialInstructions)))
        };

        // Message d'erreur
        var errorLabel = new Label
        {
            TextColor = AppleHigTheme.SystemRed,
            FontSize = AppleHigTheme.Footnote,
            IsVisible = false
        };
        errorLabel.SetBinding(Label.TextProperty, nameof(ModifiersViewModel.ValidationErrorMessage));
        errorLabel.SetBinding(IsVisibleProperty, new Binding(
            nameof(ModifiersViewModel.ValidationErrorMessage),
            converter: new StringToBoolConverter()));

        // Boutons Apple
        var confirmBtn = AppleHigTheme.CreatePillButton("✔ Confirmer & Ajouter", AppleHigTheme.SystemGreen, Colors.White,
            _vm.ConfirmModifiersCommand, height: 50, fontSize: 16);

        var cancelBtn = new Button
        {
            Text = "Annuler",
            BackgroundColor = AppleHigTheme.TertiarySystemBackground,
            TextColor = AppleHigTheme.LabelSecondary,
            HeightRequest = 44,
            CornerRadius = 10,
            FontSize = AppleHigTheme.Headline,
            Command = new Command(async () => await Shell.Current.GoToAsync(".."))
        };

        // Navigation automatique après confirmation
        _vm.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName == nameof(ModifiersViewModel.IsCompleted) && _vm.IsCompleted)
                await Shell.Current.GoToAsync("..");
        };

        return new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                AppleHigTheme.CreateSheetGrabber(),
                titleLabel,
                productLabel,
                groupLabel,
                new BoxView { HeightRequest = 1, Color = AppleHigTheme.Separator },
                optionsList,
                new BoxView { HeightRequest = 1, Color = AppleHigTheme.Separator },
                new Label { Text = "Commentaire cuisine :", TextColor = AppleHigTheme.LabelSecondary, FontSize = AppleHigTheme.Footnote },
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
