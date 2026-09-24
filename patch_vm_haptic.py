import re

with open('src/RestaurantPos.Client.Maui/ViewModels/PosTerminalViewModel.cs', 'r') as f:
    content = f.read()

def inject_haptic(method_def, method_body):
    return method_def + "\n    {" + f"""\n        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);"""

content = content.replace("public void RemoveItem(OrderItem item)\n    {", inject_haptic("public void RemoveItem(OrderItem item)", ""))
content = content.replace("public void IncrementQuantity(OrderItem item)\n    {", inject_haptic("public void IncrementQuantity(OrderItem item)", ""))
content = content.replace("public void DecrementQuantity(OrderItem item)\n    {", inject_haptic("public void DecrementQuantity(OrderItem item)", ""))
content = content.replace("public void ClearCart()\n    {", inject_haptic("public void ClearCart()", ""))
content = content.replace("public async Task SendKitchenAndResetAsync(ITableManagementService? tableService = null)\n    {", inject_haptic("public async Task SendKitchenAndResetAsync(ITableManagementService? tableService = null)", ""))
content = content.replace("public void AppendNumpadDigit(string digit)\n    {", inject_haptic("public void AppendNumpadDigit(string digit)", ""))

with open('src/RestaurantPos.Client.Maui/ViewModels/PosTerminalViewModel.cs', 'w') as f:
    f.write(content)
