import os
import re

# Since registry actions are Windows specific, import winreg
try:
    import winreg
except ImportError:
    winreg = None

# GUIDs for permanently installed test-framework addins that must always be whitelisted.
# ricaun.RevitTest.Application (ApplicationPlugins bundle, all versions)
KNOWN_FRAMEWORK_GUIDS = {
    "65f304b3-8efb-464d-b08a-12cfd61a1986",  # ricaun.RevitTest.Application v1.x
}

def extract_guids_from_file(full_path):
    guids = set()
    try:
        if not os.path.exists(full_path):
            return guids
        with open(full_path, "r", encoding="utf-8") as f:
            content = f.read()
        matches = re.findall(r'<(?:ClientId|AddInId)>(.*?)</', content, re.IGNORECASE)
        for m in matches:
            guid_cand = m.strip().strip("{}").strip()
            if len(guid_cand) >= 32:
                guids.add(guid_cand)
    except Exception:
        pass
    return guids

def extract_guids_from_package_contents(xml_path):
    """Extract ProductCode GUIDs from a bundle PackageContents.xml."""
    guids = set()
    try:
        if not os.path.exists(xml_path):
            return guids
        with open(xml_path, "r", encoding="utf-8") as f:
            content = f.read()
        # ProductCode attribute on ApplicationPackage element
        matches = re.findall(r'ProductCode=["\']([^"\'\']+)["\']', content, re.IGNORECASE)
        for m in matches:
            guid_cand = m.strip().strip("{}").strip()
            if len(guid_cand) >= 32:
                guids.add(guid_cand)
        # Also grab AddInId from any embedded .addin fragments inside the XML
        matches2 = re.findall(r'<(?:ClientId|AddInId)>(.*?)</', content, re.IGNORECASE)
        for m in matches2:
            guid_cand = m.strip().strip("{}").strip()
            if len(guid_cand) >= 32:
                guids.add(guid_cand)
    except Exception:
        pass
    return guids

def get_bundle_and_addin_paths(version):
    programdata = os.environ.get("ProgramData", r"C:\ProgramData")
    appdata = os.environ.get("AppData", os.path.expanduser("~\\AppData\\Roaming"))
    
    paths = [
        os.path.join(programdata, "Autodesk", "Revit", "Addins", version),
        os.path.join(appdata, "Autodesk", "Revit", "Addins", version)
    ]
    bundle_paths = [
        os.path.join(appdata, "Autodesk", "ApplicationPlugins"),
        os.path.join(programdata, "Autodesk", "ApplicationPlugins"),
    ]
    return paths, bundle_paths

def whitelist_existing_addins(version):
    """
    Scans add-in directories and whitelists code-signing GUIDs in registry.
    Returns the list of registry value names that were added.
    """
    if winreg is None:
        print("[WARN] winreg module not available. Registry whitelisting skipped.")
        return []

    guids_to_whitelist = set(KNOWN_FRAMEWORK_GUIDS)
    paths, bundle_paths = get_bundle_and_addin_paths(version)

    # Scan standard .addin files
    for path in paths:
        if not os.path.exists(path):
            continue
        try:
            for file in os.listdir(path):
                if file.endswith(".addin"):
                    full_path = os.path.join(path, file)
                    guids = extract_guids_from_file(full_path)
                    guids_to_whitelist.update(guids)
        except Exception:
            pass

    # Scan ApplicationPlugins bundles (PackageContents.xml)
    for bundle_root in bundle_paths:
        if not os.path.exists(bundle_root):
            continue
        try:
            for entry in os.listdir(bundle_root):
                if entry.endswith(".bundle"):
                    pkg_xml = os.path.join(bundle_root, entry, "PackageContents.xml")
                    guids_to_whitelist.update(extract_guids_from_package_contents(pkg_xml))
                    # Also walk .addin files inside the bundle
                    for root, _, files in os.walk(os.path.join(bundle_root, entry)):
                        for f in files:
                            if f.endswith(".addin"):
                                guids_to_whitelist.update(
                                    extract_guids_from_file(os.path.join(root, f))
                                )
        except Exception:
            pass

    added_guids = []
    if guids_to_whitelist:
        key_path = f"Software\\Autodesk\\Revit\\Autodesk Revit {version}\\CodeSigning"
        try:
            key = winreg.CreateKey(winreg.HKEY_CURRENT_USER, key_path)
            for g in guids_to_whitelist:
                for g_variant in (g.lower(), g.upper()):
                    try:
                        val, _ = winreg.QueryValueEx(key, g_variant)
                        if val == 1:
                            continue
                    except FileNotFoundError:
                        pass
                    winreg.SetValueEx(key, g_variant, 0, winreg.REG_DWORD, 1)
                    if g_variant not in added_guids:
                        added_guids.append(g_variant)
                        print(f"[TESTS] Dynamically whitelisted new GUID {g_variant} in registry for Revit {version}.")
            winreg.CloseKey(key)
        except Exception as e:
            print(f"[WARN] Failed to write whitelist to registry for Revit {version}: {e}")
            
    return added_guids

def cleanup_whitelisted_registry_entries(version, added_guids):
    """
    Cleans up whitelisted registry entries that were added during the run.
    """
    if winreg is None or not added_guids:
        return

    key_path = f"Software\\Autodesk\\Revit\\Autodesk Revit {version}\\CodeSigning"
    try:
        key = winreg.OpenKey(winreg.HKEY_CURRENT_USER, key_path, 0, winreg.KEY_SET_VALUE)
        for g in set(added_guids):
            try:
                winreg.DeleteValue(key, g)
            except Exception:
                pass
        winreg.CloseKey(key)
        print(f"[TESTS] Cleaned up whitelisted registry entries for Revit {version}.")
    except Exception as e:
        print(f"[WARN] Failed to clean up registry whitelist for Revit {version}: {e}")
