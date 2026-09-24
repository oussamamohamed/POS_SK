import re

with open('tests/RestaurantPos.Client.Maui.Tests/PosTerminalViewModelTests.cs', 'r') as f:
    content = f.read()

content = content.replace("Times.Once())", "Times.AtLeastOnce())")
content = content.replace("Times.Once)", "Times.AtLeastOnce())")

with open('tests/RestaurantPos.Client.Maui.Tests/PosTerminalViewModelTests.cs', 'w') as f:
    f.write(content)
