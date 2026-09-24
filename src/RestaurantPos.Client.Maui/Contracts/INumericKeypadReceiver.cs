namespace RestaurantPos.Client.Maui.Contracts;

public interface INumericKeypadReceiver
{
    void OnDigitPressed(int digit);
    void OnBackspacePressed();
    void OnClearPressed();
    void OnDecimalSeparatorPressed();
    void OnEnterPressed();
}
