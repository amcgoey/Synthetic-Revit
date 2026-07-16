import os

def get_addin_paths(version):
    """
    Computes standard Revit add-in paths for a given version.
    """
    programdata = os.environ.get("ProgramData", r"C:\ProgramData")
    appdata = os.environ.get("AppData", os.path.expanduser("~\\AppData\\Roaming"))
    
    return [
        os.path.join(programdata, "Autodesk", "Revit", "Addins", version),
        os.path.join(appdata, "Autodesk", "Revit", "Addins", version)
    ]

def rename_non_essential_addins(version):
    """
    Renames non-essential third-party add-ins to .bak to avoid loading them.
    Returns a list of tuples containing (original_path, new_path) of renamed files.
    """
    renamed_files = []
    paths = get_addin_paths(version)
    
    for path in paths:
        if not os.path.exists(path):
            continue
        try:
            for file in os.listdir(path):
                if file.endswith(".addin"):
                    lower_file = file.lower()
                    if "synthetic" in lower_file or "revittest" in lower_file or "ricaun" in lower_file:
                        continue
                    
                    full_path = os.path.join(path, file)
                    new_path = full_path + ".bak"
                    try:
                        if os.path.exists(new_path):
                            os.remove(new_path)
                        os.rename(full_path, new_path)
                        renamed_files.append((full_path, new_path))
                    except Exception:
                        # If rename fails (e.g. read-only ProgramData), we'll rely on whitelisting
                        pass
        except Exception:
            pass
            
    return renamed_files

def restore_renamed_addins(renamed_files):
    """
    Restores previously renamed .bak add-in files to their original paths.
    """
    for full_path, new_path in renamed_files:
        try:
            if os.path.exists(new_path):
                if os.path.exists(full_path):
                    os.remove(full_path)
                os.rename(new_path, full_path)
        except Exception as e:
            print(f"[ERROR] Failed to restore addin from '{new_path}': {e}")
