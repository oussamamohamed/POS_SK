import re

with open('src/RestaurantPos.Client.Maui/Contracts/IPlatformEnvironmentService.cs', 'r') as f:
    content = f.read()

content = content.replace("Error = 3", "Error = 3,\n    LongPress = 4")

with open('src/RestaurantPos.Client.Maui/Contracts/IPlatformEnvironmentService.cs', 'w') as f:
    f.write(content)
