import re

with open('src/RestaurantPos.Client.Maui/Services/PlatformEnvironmentService.cs', 'r') as f:
    content = f.read()

content = content.replace("Application.Current", "Microsoft.Maui.Controls.Application.Current")

with open('src/RestaurantPos.Client.Maui/Services/PlatformEnvironmentService.cs', 'w') as f:
    f.write(content)

