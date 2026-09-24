import re

paths = [
    'src/RestaurantPos.Client.Maui/Platforms/iOS/Info.plist',
    'src/RestaurantPos.Client.Maui/Platforms/MacCatalyst/Info.plist'
]

replacement = """<key>UISupportedInterfaceOrientations</key>
    <array>
        <string>UIInterfaceOrientationLandscapeLeft</string>
        <string>UIInterfaceOrientationLandscapeRight</string>
    </array>
    <key>UISupportedInterfaceOrientations~ipad</key>
    <array>
        <string>UIInterfaceOrientationLandscapeLeft</string>
        <string>UIInterfaceOrientationLandscapeRight</string>
    </array>"""

for p in paths:
    try:
        with open(p, 'r') as f:
            content = f.read()
        
        pattern = r"<key>UISupportedInterfaceOrientations</key>.*?<key>UIViewControllerBasedStatusBarAppearance</key>"
        content = re.sub(pattern, replacement + "\n    <key>UIViewControllerBasedStatusBarAppearance</key>", content, flags=re.DOTALL)
        
        with open(p, 'w') as f:
            f.write(content)
    except FileNotFoundError:
        pass

