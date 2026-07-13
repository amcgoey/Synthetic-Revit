import os
import re

def parse_cs_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()
    
    # Find namespace
    ns_match = re.search(r'namespace\s+([\w\.]+)', content)
    namespace = ns_match.group(1) if ns_match else "Synthetic"
    
    # Class name and summary
    class_match = re.search(r'///\s*<summary>\s*\n((?:///.*\n)*)///\s*</summary>\s*\n(?:\s*\[.*\]\s*\n)*\s*(?:public|internal|private)\s+class\s+(\w+)', content)
    if class_match:
        class_summary = "".join([line.strip().replace("///", "").strip() for line in class_match.group(1).split("\n")])
        class_name = class_match.group(2)
    else:
        class_match = re.search(r'(?:public|internal|private)\s+class\s+(\w+)', content)
        class_name = class_match.group(1) if class_match else None
        class_summary = ""
        
    if not class_name:
        return None
        
    # Clean up tags in class summary
    class_summary = re.sub(r'<see\s+cref="(\w+)"\s*/>', r'`\1`', class_summary)
    class_summary = re.sub(r'<see\s+cref=".*?:(\w+)"\s*/>', r'`\1`', class_summary)
    class_summary = re.sub(r'<see\s+langword="(\w+)"\s*/>', r'`\1`', class_summary)
    class_summary = re.sub(r'\s+', ' ', class_summary).strip()

    # Split content by '/// <summary>' to parse methods/properties
    parts = content.split('/// <summary>')
    methods = []
    fields = []
    
    for part in parts[1:]:
        summary_end = part.find('</summary>')
        if summary_end == -1:
            continue
        summary_lines = part[:summary_end].split('\n')
        summary = " ".join([l.replace('///', '').strip() for l in summary_lines if l.strip()])
        
        after_summary = part[summary_end + len('</summary>'):]
        lines = [l.strip() for l in after_summary.split('\n') if l.strip() and not l.strip().startswith('///') and not l.strip().startswith('[')]
        
        sig = ""
        for line in lines:
            sig += " " + line
            if '{' in line or ';' in line:
                break
        sig = sig.strip()
        
        # If it has parentheses, it is a method/constructor
        # (Exclude attribute usage or things like that)
        if '(' in sig and not sig.startswith('class '):
            # Clean up parameters and returns if any
            param_matches = re.findall(r'<param\s+name="(\w+)"\s*>(.*?)</param\s*>', summary)
            params = []
            for p_name, p_desc in param_matches:
                params.append((p_name, re.sub(r'\s+', ' ', p_desc).strip()))
                
            return_match = re.search(r'<returns\s*>(.*?)</returns\s*>', summary)
            returns_val = return_match.group(1).strip() if return_match else ""
            
            clean_summary = re.sub(r'<param.*?>.*?</param>', '', summary)
            clean_summary = re.sub(r'<returns.*?>.*?</returns>', '', clean_summary)
            clean_summary = re.sub(r'<see\s+cref="(\w+)"\s*/>', r'`\1`', clean_summary)
            clean_summary = re.sub(r'<see\s+cref=".*?:(\w+)"\s*/>', r'`\1`', clean_summary)
            clean_summary = re.sub(r'<see\s+langword="(\w+)"\s*/>', r'`\1`', clean_summary)
            clean_summary = re.sub(r'\s+', ' ', clean_summary).strip()
            
            # Format signature
            sig_clean = sig.split('{')[0].split(';')[0].strip()
            sig_clean = re.sub(r'\s+', ' ', sig_clean)
            
            methods.append({
                'signature': sig_clean,
                'summary': clean_summary,
                'params': params,
                'returns': returns_val
            })
        elif ('{' in sig or ';' in sig) and not sig.startswith('class '):
            # Property or Field
            clean_summary = re.sub(r'<see\s+cref="(\w+)"\s*/>', r'`\1`', summary)
            clean_summary = re.sub(r'<see\s+cref=".*?:(\w+)"\s*/>', r'`\1`', clean_summary)
            clean_summary = re.sub(r'<see\s+langword="(\w+)"\s*/>', r'`\1`', clean_summary)
            clean_summary = re.sub(r'\s+', ' ', clean_summary).strip()
            
            # Extract name
            sig_clean = sig.split('=')[0].split('{')[0].split(';')[0].strip()
            name_parts = sig_clean.split()
            var_name = name_parts[-1] if name_parts else "unknown"
            
            fields.append({
                'name': var_name,
                'signature': sig.replace('{ get; set; }', '').replace('{ get; }', '').replace('{ set; }', '').split('=')[0].strip(),
                'summary': clean_summary
            })
            
    return {
        'class_name': class_name,
        'class_summary': class_summary,
        'methods': methods,
        'fields': fields,
        'namespace': namespace,
        'file_rel_path': filepath.replace('\\', '/')
    }

def generate_markdown(class_data):
    lines = []
    lines.append(f"### Class: `{class_data['class_name']}`")
    lines.append(f"**File:** [{class_data['file_rel_path']}](../{class_data['file_rel_path']})")
    lines.append("")
    if class_data['class_summary']:
        lines.append(class_data['class_summary'])
        lines.append("")
        
    if class_data['methods']:
        lines.append("#### Methods")
        for m in class_data['methods']:
            lines.append(f"##### `{m['signature'].split('(')[0].split()[-1]}`")
            lines.append("```csharp")
            lines.append(m['signature'])
            lines.append("```")
            if m['summary']:
                lines.append(m['summary'])
            if m['params'] or m['returns']:
                lines.append("")
            if m['params']:
                lines.append("**Parameters:**")
                for p_name, p_desc in m['params']:
                    lines.append(f"- `{p_name}`: {p_desc}")
            if m['returns']:
                lines.append(f"**Returns:** {m['returns']}")
            lines.append("")
            
    if class_data['fields']:
        lines.append("#### Fields")
        for f in class_data['fields']:
            lines.append(f"- **`{f['name']}`**: `{f['signature']}`")
            if f['summary']:
                lines.append(f"  *Description:* {f['summary']}")
        lines.append("")
        
    lines.append("---")
    lines.append("")
    return "\n".join(lines)

def discover_cs_files(src_root="src"):
    """
    Recursively discovers all C# source files under src_root.
    Excludes build output directories (obj/, bin/) and auto-generated
    files (*.g.cs, *.designer.cs) that contain no hand-authored docs.
    This script must be run from the project root directory so that
    src_root resolves correctly.
    """
    excluded_dirs = {"obj", "bin", ".vs"}
    excluded_suffixes = (".g.cs", ".designer.cs")
    cs_files = []
    for dirpath, dirnames, filenames in os.walk(src_root):
        # Prune excluded directories in-place to stop os.walk descending into them
        dirnames[:] = [d for d in dirnames if d.lower() not in excluded_dirs]
        for filename in filenames:
            if filename.endswith(".cs") and not filename.endswith(excluded_suffixes):
                cs_files.append(os.path.join(dirpath, filename).replace("\\", "/"))
    return cs_files


def merge_docs():
    src_root = "src"
    if not os.path.exists(src_root):
        print(f"[ERROR] Source directory not found: '{src_root}'")
        print(f"[ERROR] Ensure this script is run from the project root directory.")
        return

    cs_files = discover_cs_files(src_root)
    print(f"[INFO] Discovered {len(cs_files)} C# source files under '{src_root}/'.")

    parsed_classes = []
    skipped = 0
    for f in cs_files:
        data = parse_cs_file(f)
        if data:
            parsed_classes.append(data)
        else:
            skipped += 1

    print(f"[INFO] Parsed {len(parsed_classes)} classes ({skipped} files skipped — no class/doc comment found).")

    doc_path = "docs/SyntheticShared_API_Documentation.md"

    # Group all parsed classes by namespace
    parsed_by_ns = {}
    for c in parsed_classes:
        parsed_by_ns.setdefault(c['namespace'], []).append(c)

    if not os.path.exists(doc_path):
        # --- Fresh generation: no existing file to merge into ---
        print(f"[INFO] No existing doc file found. Generating fresh: {doc_path}")
        new_doc = "# SyntheticShared API Documentation\n\n"
        new_doc += "_Auto-generated by generate_docs.py. Do not edit namespace or class headers manually._\n\n"
        for ns_name in sorted(parsed_by_ns.keys()):
            new_doc += f"\n## Namespace: `{ns_name}`\n\n"
            for new_c in sorted(parsed_by_ns[ns_name], key=lambda x: x['class_name']):
                new_doc += generate_markdown(new_c)
        with open(doc_path, 'w', encoding='utf-8') as f:
            f.write(new_doc)
        print(f"[INFO] Fresh documentation written to {doc_path}.")
        print(f"[INFO] Namespaces written: {len(parsed_by_ns)} | Classes written: {len(parsed_classes)}")
        return

    # --- Merge mode: update/insert into the existing file ---
    with open(doc_path, 'r', encoding='utf-8') as f:
        doc_content = f.read()

    # Split the document by namespace section headers
    ns_sections = re.split(r'(## Namespace: `[\w\.]+`)', doc_content)

    # ns_sections[0] is the document header (title, intro text, etc.)
    header = ns_sections[0]
    new_doc = header

    # Track which namespaces already exist in the doc so we can append new ones later
    written_namespaces = set()

    i = 1
    while i < len(ns_sections):
        ns_header = ns_sections[i]
        ns_body = ns_sections[i + 1] if i + 1 < len(ns_sections) else ""

        ns_name_match = re.search(r'## Namespace: `([\w\.]+)`', ns_header)
        ns_name = ns_name_match.group(1) if ns_name_match else ""
        written_namespaces.add(ns_name)

        # Split this namespace body by class section headers
        class_parts = re.split(r'(### Class: `\w+`)', ns_body)

        class_map = {}
        intro = class_parts[0]  # Namespace intro text before the first class

        j = 1
        while j < len(class_parts):
            c_header = class_parts[j]
            c_body = class_parts[j + 1] if j + 1 < len(class_parts) else ""
            c_name_match = re.search(r'### Class: `(\w+)`', c_header)
            c_name = c_name_match.group(1) if c_name_match else ""
            class_map[c_name] = c_header + c_body
            j += 2

        # Insert or update classes for this namespace from the freshly parsed data
        if ns_name in parsed_by_ns:
            for new_c in parsed_by_ns[ns_name]:
                class_map[new_c['class_name']] = generate_markdown(new_c)

        # Re-sort classes alphabetically within the namespace
        new_body = intro
        for c_name in sorted(class_map.keys()):
            new_body += class_map[c_name]

        new_doc += ns_header + new_body
        i += 2

    # Append entirely new namespace sections not previously in the document
    new_namespaces = sorted(set(parsed_by_ns.keys()) - written_namespaces)
    for ns_name in new_namespaces:
        print(f"[INFO] Adding new namespace section: {ns_name}")
        new_doc += f"\n## Namespace: `{ns_name}`\n\n"
        for new_c in sorted(parsed_by_ns[ns_name], key=lambda x: x['class_name']):
            new_doc += generate_markdown(new_c)

    with open(doc_path, 'w', encoding='utf-8') as f:
        f.write(new_doc)

    print(f"[INFO] Documentation successfully updated at {doc_path}.")
    if new_namespaces:
        print(f"[INFO] New namespace sections added: {', '.join(new_namespaces)}")


if __name__ == "__main__":
    merge_docs()

