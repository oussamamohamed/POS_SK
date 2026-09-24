import re
from datetime import datetime

with open(".specify/memory/constitution.md", "r") as f:
    text = f.read()

# Strip old Sync Impact Report
text = re.sub(r'<!--\nSync Impact Report.*?-->\n\n', '', text, flags=re.DOTALL)

# Update version and date
text = re.sub(r'\*\*Version\*\*: \d+\.\d+\.\d+', '**Version**: 2.0.0', text)
date_str = datetime.now().strftime("%Y-%m-%d")
text = re.sub(r'\*\*Last Amended\*\*: \d{4}-\d{2}-\d{2}', f'**Last Amended**: {date_str}', text)

# Rewrite principles for Flutter
text = text.replace('.NET MAUI iOS/iPadOS client', 'Flutter / Dart iOS/iPadOS client')
text = text.replace('frontend MUST utilize modern MVVM architecture (`CommunityToolkit.Mvvm`).', 'frontend MUST utilize modern reactive state management (e.g. BLoC or Riverpod).')
text = text.replace('single iPad executing .NET MAUI', 'Single iPad executing Flutter')
text = text.replace('.NET MAUI (C# / XAML or C# Markup)', 'Flutter (Dart)')
text = text.replace('`.NET MAUI`', '`Flutter`')

new_content = f"""<!--
Sync Impact Report
- Version change: 1.3.0 → 2.0.0 (MAJOR)
- List of modified principles:
  * II. Clean Architecture & Centralized Multi-POS Backend (Replaced .NET MAUI and MVVM Toolkit with Flutter/Dart and BLoC/Riverpod state management)
  * Deployment Topologies, Apple Ecosystem & Enterprise Stack (Replaced .NET MAUI with Flutter/Dart)
- Added sections: None
- Removed sections: None
- Follow-up TODOs: Implement the new Flutter frontend.
-->

{text}"""

with open(".specify/memory/constitution.md", "w") as f:
    f.write(new_content)
