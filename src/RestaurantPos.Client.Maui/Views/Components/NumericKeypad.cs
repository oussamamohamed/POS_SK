#if MAUI_UI
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using RestaurantPos.Client.Maui.Contracts;

namespace RestaurantPos.Client.Maui.Views.Components;

public class NumericKeypad : ContentView
{
    public static readonly BindableProperty ReceiverProperty = BindableProperty.Create(
        nameof(Receiver),
        typeof(INumericKeypadReceiver),
        typeof(NumericKeypad),
        null);

    public INumericKeypadReceiver Receiver
    {
        get => (INumericKeypadReceiver)GetValue(ReceiverProperty);
        set => SetValue(ReceiverProperty, value);
    }

    public NumericKeypad()
    {
        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Star },
                new RowDefinition { Height = GridLength.Star },
                new RowDefinition { Height = GridLength.Star },
                new RowDefinition { Height = GridLength.Star }
            },
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star } // For side buttons (Enter, Backspace)
            },
            RowSpacing = 5,
            ColumnSpacing = 5
        };

        void CreateButton(string text, int row, int col, int rowSpan = 1, int colSpan = 1, Action? action = null)
        {
            var btn = new Button
            {
                Text = text,
                FontSize = 24,
                FontAttributes = FontAttributes.Bold,
                BackgroundColor = Color.FromArgb("#334155"),
                TextColor = Colors.White,
                CornerRadius = 8
            };
            btn.Clicked += (s, e) =>
            {
                if (action != null) action();
                else if (int.TryParse(text, out int num)) Receiver?.OnDigitPressed(num);
                Microsoft.Maui.Devices.HapticFeedback.Default.Perform(Microsoft.Maui.Devices.HapticFeedbackType.Click);
            };
            grid.Add(btn, col, row);
            Grid.SetRowSpan(btn, rowSpan);
            Grid.SetColumnSpan(btn, colSpan);
        }

        // Row 0
        CreateButton("7", 0, 0);
        CreateButton("8", 0, 1);
        CreateButton("9", 0, 2);
        CreateButton("⌫", 0, 3, action: () => Receiver?.OnBackspacePressed());

        // Row 1
        CreateButton("4", 1, 0);
        CreateButton("5", 1, 1);
        CreateButton("6", 1, 2);
        CreateButton("C", 1, 3, action: () => Receiver?.OnClearPressed()); // Clear

        // Row 2
        CreateButton("1", 2, 0);
        CreateButton("2", 2, 1);
        CreateButton("3", 2, 2);
        var enterBtn = new Button { Text = "↵", FontSize = 24, BackgroundColor = Color.FromArgb("#10B981"), TextColor = Colors.White, CornerRadius = 8 };
        enterBtn.Clicked += (s, e) => 
        {
            Receiver?.OnEnterPressed();
            Microsoft.Maui.Devices.HapticFeedback.Default.Perform(Microsoft.Maui.Devices.HapticFeedbackType.Click);
        };
        grid.Add(enterBtn, 3, 2);
        Grid.SetRowSpan(enterBtn, 2);

        // Row 3
        CreateButton("0", 3, 0, colSpan: 2);
        CreateButton(".", 3, 2, action: () => Receiver?.OnDecimalSeparatorPressed());

        Content = grid;
    }
}

#endif