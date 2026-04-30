import os
import glob
import re

files = glob.glob('src/**/*.cs', recursive=True)
view_models = [f for f in files if 'ViewModel' in f]

for f in view_models:
    with open(f, 'r', encoding='utf-8') as file:
        content = file.read()
    
    # skip ViewComponents ViewModels
    if 'Views/Shared/Components' in f:
        continue

    # find class definition
    class_match = re.search(r'public class (\w+ViewModel)(?:\s*:\s*([\w<>,\s]+))?', content)
    if not class_match:
        continue
    
    class_name = class_match.group(1)
    base_class = class_match.group(2)
    
    is_ui_stack = base_class and 'UiStackLayoutViewModel' in base_class
    has_page_title = 'PageTitle' in content
    
    if not is_ui_stack or not has_page_title:
        print(f"{f}: {class_name} | is_ui_stack: {bool(is_ui_stack)} | has_page_title: {has_page_title}")

