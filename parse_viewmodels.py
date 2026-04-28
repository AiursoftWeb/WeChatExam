import os
import re

dir_path = 'src/Aiursoft.WeChatExam/Models'
viewmodels_to_fix = []

# Regex to find class definition and inheritance
class_def_re = re.compile(r'public\s+class\s+(\w+)\s*:\s*([^{]+)\s*\{')

for root, _, files in os.walk(dir_path):
    for file in files:
        if file.endswith('.cs'):
            filepath = os.path.join(root, file)
            with open(filepath, 'r', encoding='utf-8') as f:
                content = f.read()
                
            # Check if it has PageTitle = "..." in a constructor
            has_page_title = 'PageTitle =' in content or 'PageTitle=' in content
            
            # Find classes
            for match in class_def_re.finditer(content):
                class_name = match.group(1)
                base_classes = match.group(2)
                
                # Assume if it inherits from anything it might be a ViewModel
                if 'ViewModel' in class_name or 'UiStackLayoutViewModel' in base_classes:
                    if not has_page_title:
                        viewmodels_to_fix.append((filepath, class_name))

print("Total to fix:", len(viewmodels_to_fix))
for path, name in viewmodels_to_fix:
    print(f"{path}: {name}")
