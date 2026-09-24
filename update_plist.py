import re

with open('src/RestaurantPos.Client.Flutter/ios/Runner/Info.plist', 'r') as f:
    text = f.read()

# Remove Portrait support
text = re.sub(r'<string>UIInterfaceOrientationPortrait</string>', '', text)
text = re.sub(r'<string>UIInterfaceOrientationPortraitUpsideDown</string>', '', text)

# Add UIRequiresFullScreen
if 'UIRequiresFullScreen' not in text:
    text = text.replace('</dict>\n</plist>', '    <key>UIRequiresFullScreen</key>\n    <true/>\n</dict>\n</plist>')

with open('src/RestaurantPos.Client.Flutter/ios/Runner/Info.plist', 'w') as f:
    f.write(text)

