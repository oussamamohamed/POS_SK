import re

with open('src/RestaurantPos.Client.Maui/Views/PosTerminalPage.cs', 'r') as f:
    content = f.read()

# I will find the main Grid construction and set its FlowDirection binding.
handedness_code = """
        Content.SetBinding(VisualElement.FlowDirectionProperty, new Binding(nameof(PosTerminalViewModel.IsLeftHandedMode), converter: new BoolToFlowDirectionConverter()));
"""
# Find "Content = new Grid" and insert underneath
pattern = r'Content = new Grid\s*\{\s*Children =\s*\{'
replacement = """Content = new Grid
        {
            Children =
            {"""
content = re.sub(pattern, replacement, content)

# But we need BoolToFlowDirectionConverter
converter_code = """
    private class BoolToFlowDirectionConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool isLeftHanded && isLeftHanded)
                return FlowDirection.RightToLeft;
            return FlowDirection.LeftToRight;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
    }
"""

if "BoolToFlowDirectionConverter" not in content:
    content = content.replace("private class SelectedModifiersToStringConverter : IValueConverter", converter_code + "\n    private class SelectedModifiersToStringConverter : IValueConverter")

# Now inject the set binding near the end of constructor
content = content.replace("Content = new Grid\n        {", "Content = new Grid\n        {")
# Wait, let's just insert it before "if (Application.Current != null)" or end of constructor.
ctor_end = "if (Application.Current != null)"
content = content.replace(ctor_end, handedness_code + "\n        " + ctor_end)

with open('src/RestaurantPos.Client.Maui/Views/PosTerminalPage.cs', 'w') as f:
    f.write(content)
