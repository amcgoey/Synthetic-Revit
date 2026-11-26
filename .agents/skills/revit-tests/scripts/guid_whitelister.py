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
    """Extract ProductCode, ClientId, and AddInId GUIDs from a bundle PackageContents.xml."""
    guids = set()
    try:
        if not os.path.exists(xml_path):
            return guids
        with open(xml_path, "r", encoding="utf-8") as f:
            content = f.read()
        # ProductCode attribute on ApplicationPackage element
        matches = re.findall(r'ProductCode\s*=\s*["\']([^"\']+)["\']', content, re.IGNORECASE)
        for m in matches:
            guid_cand = m.strip().strip("{}").strip()
            if len(guid_cand) >= 32:
                guids.add(guid_cand)
        # Also grab ClientId/AddInId from any embedded .addin fragments inside the XML
        matches2 = re.findall(r'<(?:ClientId|AddInId)>(.*?)</', content, re.IGNORECASE)
        for m in matches2:
            guid_cand = m.strip().strip("{}").strip()
            if len(guid_cand) >= 32:
                guids.add(guid_cand)
    except Exception:
        pass
    return guids


def get_bundle_and_addin_paths(
    version,
    env=None,
    program_files=None,
    program_data=None,
    app_data=None,
):
    env_map = os.environ if env is None else env
    pf = program_files or env_map.get("ProgramW6432") or env_map.get("ProgramFiles") or r"C:\Program Files"
    pd = program_data or env_map.get("ProgramData") or r"C:\ProgramData"
    ad = app_data or env_map.get("AppData") or os.path.expanduser("~\\AppData\\Roaming")

    ver_str = str(version)
    paths = [
        os.path.join(pf, "Autodesk", f"Revit {ver_str}", "AddIns"),
        os.path.join(pf, "Autodesk", "Revit", "Addins", ver_str),
        os.path.join(pd, "Autodesk", "Revit", "Addins", ver_str),
        os.path.join(ad, "Autodesk", "Revit", "Addins", ver_str),
    ]
    bundle_paths = [
        os.path.join(ad, "Autodesk", "ApplicationPlugins"),
        os.path.join(pd, "Autodesk", "ApplicationPlugins"),
        os.path.join(pf, "Autodesk", "ApplicationPlugins"),
    ]
    return paths, bundle_paths


def collect_whitelisted_guids(
    version,
    env=None,
    program_files=None,
    program_data=None,
    app_data=None,
):
    """Recursively scan all Revit add-in and ApplicationPlugins roots for publisher GUIDs."""
    guids_to_whitelist = set(KNOWN_FRAMEWORK_GUIDS)
    paths, bundle_paths = get_bundle_and_addin_paths(
        version,
        env=env,
        program_files=program_files,
        program_data=program_data,
        app_data=app_data,
    )

    for search_root in paths + bundle_paths:
        if not os.path.isdir(search_root):
            continue
        try:
            for root, _, files in os.walk(search_root):
                for filename in files:
                    lower_name = filename.lower()
                    full_path = os.path.join(root, filename)
                    if lower_name.endswith(".addin"):
                        guids_to_whitelist.update(extract_guids_from_file(full_path))
                    elif lower_name == "packagecontents.xml":
                        guids_to_whitelist.update(extract_guids_from_package_contents(full_path))
        except Exception:
            pass

    return guids_to_whitelist


def whitelist_existing_addins(
    version,
    env=None,
    program_files=None,
    program_data=None,
    app_data=None,
    reg_module=None,
):
    """
    Scans add-in directories recursively and persists code-signing GUIDs in HKCU registry.
    Returns the list of registry value names that were added.
    """
    reg = winreg if reg_module is None else reg_module
    if reg is None:
        print("[WARN] winreg module not available. Registry whitelisting skipped.")
        return []

    guids_to_whitelist = collect_whitelisted_guids(
        version,
        env=env,
        program_files=program_files,
        program_data=program_data,
        app_data=app_data,
    )

    added_guids = []
    if guids_to_whitelist:
        key_path = f"Software\\Autodesk\\Revit\\Autodesk Revit {version}\\CodeSigning"
        try:
            key = reg.CreateKey(reg.HKEY_CURRENT_USER, key_path)
            for g in sorted(guids_to_whitelist):
                for g_variant in (g.lower(), g.upper()):
                    try:
                        val, _ = reg.QueryValueEx(key, g_variant)
                        if val == 1:
                            continue
                    except FileNotFoundError:
                        pass
                    reg.SetValueEx(key, g_variant, 0, reg.REG_DWORD, 1)
                    if g_variant not in added_guids:
                        added_guids.append(g_variant)
                        print(f"[TESTS] Dynamically whitelisted new GUID {g_variant} in registry for Revit {version}.")
            reg.CloseKey(key)
        except Exception as e:
            print(f"[WARN] Failed to write whitelist to registry for Revit {version}: {e}")

    return added_guids
