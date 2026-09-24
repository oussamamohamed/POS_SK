import re

with open('src/RestaurantPos.Client.Maui/ViewModels/PosTerminalViewModel.cs', 'r') as f:
    content = f.read()

# Add INumericKeypadReceiver to interface implementation
if 'INumericKeypadReceiver' not in content:
    content = content.replace("public partial class PosTerminalViewModel : ObservableObject", 
                              "public partial class PosTerminalViewModel : ObservableObject, Contracts.INumericKeypadReceiver")

# Add InputBufferState property and INumericKeypadReceiver methods
if 'public InputBufferState InputBuffer' not in content:
    buffer_impl = """
    [ObservableProperty]
    private InputBufferState _inputBuffer = new();

    public void OnDigitPressed(int digit)
    {
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
        if (InputBuffer.ActiveField != InputBufferTarget.None)
        {
            InputBuffer.AppendDigit(digit);
            OnPropertyChanged(nameof(InputBuffer));
        }
    }

    public void OnBackspacePressed()
    {
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
        if (InputBuffer.ActiveField != InputBufferTarget.None)
        {
            InputBuffer.Backspace();
            OnPropertyChanged(nameof(InputBuffer));
        }
    }

    public void OnClearPressed()
    {
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
        if (InputBuffer.ActiveField != InputBufferTarget.None)
        {
            InputBuffer.Clear();
            OnPropertyChanged(nameof(InputBuffer));
        }
    }

    public void OnDecimalSeparatorPressed()
    {
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
        if (InputBuffer.ActiveField != InputBufferTarget.None && !InputBuffer.CurrentBuffer.Contains('.'))
        {
            InputBuffer.CurrentBuffer += ".";
            OnPropertyChanged(nameof(InputBuffer));
        }
    }

    public void OnEnterPressed()
    {
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
        // Process buffer based on active field
        if (InputBuffer.ActiveField == InputBufferTarget.Quantity && int.TryParse(InputBuffer.CurrentBuffer, out int qty))
        {
            // Apply quantity to currently selected generic item if applicable
        }
        InputBuffer.Clear();
        InputBuffer.ActiveField = InputBufferTarget.None;
        OnPropertyChanged(nameof(InputBuffer));
    }
"""
    # Insert it right before "public PosTerminalViewModel("
    content = content.replace("public PosTerminalViewModel(", buffer_impl + "\n    public PosTerminalViewModel(")


with open('src/RestaurantPos.Client.Maui/ViewModels/PosTerminalViewModel.cs', 'w') as f:
    f.write(content)
