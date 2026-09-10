using RestaurantPos.Client.Maui.Models;

namespace RestaurantPos.Client.Maui.Controls;

public static class ResponsiveLayoutEngine
{
    public const double MinimumTouchTargetSizePt = 54.0;
    public const double RushItemTouchTargetSizePt = 68.0;

    public static int CalculateColumnCount(double availableWidthDip)
    {
        return availableWidthDip switch
        {
            < 700 => 2,
            <= 900 => 3,
            <= 1100 => 4,
            <= 1400 => 5,
            _ => 6
        };
    }

    public static double CalculateTileDimension(double availableWidthDip, int columnCount, double margin = 8.0)
    {
        double totalMargins = margin * (columnCount + 1);
        double computed = (availableWidthDip - totalMargins) / columnCount;
        return Math.Max(computed, MinimumTouchTargetSizePt);
    }
}
