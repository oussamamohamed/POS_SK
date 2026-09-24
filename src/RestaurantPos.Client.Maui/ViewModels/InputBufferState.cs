namespace RestaurantPos.Client.Maui.ViewModels;

public enum InputBufferTarget
{
    None = 0,
    Quantity = 1,
    CustomPrice = 2,
    Barcode = 3
}

public class InputBufferState
{
    public string CurrentBuffer { get; set; } = string.Empty;
    public InputBufferTarget ActiveField { get; set; } = InputBufferTarget.None;

    public void Clear()
    {
        CurrentBuffer = string.Empty;
    }

    public void AppendDigit(int digit)
    {
        CurrentBuffer += digit.ToString();
    }

    public void Backspace()
    {
        if (CurrentBuffer.Length > 0)
        {
            CurrentBuffer = CurrentBuffer.Substring(0, CurrentBuffer.Length - 1);
        }
    }
}
