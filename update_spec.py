import re

with open('specs/023-modern-ipad-ui/spec.md', 'r') as f:
    content = f.read()

# Q1 Edge case replacement
q1_pattern = r"What happens if the iPad is rotated rapidly \(Portrait vs Landscape\)\? \[NEEDS CLARIFICATION: Does the UI fully support both Portrait and Landscape, or is it strictly locked to Landscape mode\?\]"
q1_replacement = "How does the system handle device rotation? The application is strictly locked to Landscape mode to preserve classical POS ergonomics and prevent layout shifts during rush interactions."
content = re.sub(q1_pattern, q1_replacement, content)

# Q2 Requirement replacement
q2_pattern = r"- \*\*FR-006\*\*: System MUST layout the interface dynamically for left-handed vs right-handed operators\. \[NEEDS CLARIFICATION: Is left/right-handed layout switching required in this phase\?\]"
q2_replacement = "- **FR-006**: System MUST dynamically adapt the layout for left-handed and right-handed operators, easily mirroring the cart and numeric keypad position to match user dominance."
content = re.sub(q2_pattern, q2_replacement, content)

# Q3 Requirement replacement
q3_pattern = r"- \*\*FR-007\*\*: System MUST support iOS multitasking\. \[NEEDS CLARIFICATION: Should the app support Split View / Slide Over side-by-side with other apps, or enforce Full Screen exclusive kiosk mode\?\]"
q3_replacement = "- **FR-007**: System MUST enforce an exclusive Full Screen (Kiosk) mode, disregarding or restricting iOS multitasking (Split View / Slide Over) to maximize stability and prevent accidental exits during ordering."
content = re.sub(q3_pattern, q3_replacement, content)

with open('specs/023-modern-ipad-ui/spec.md', 'w') as f:
    f.write(content)
