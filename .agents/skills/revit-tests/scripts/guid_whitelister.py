import os
import re
import shutil
import subprocess

_ws = os.getcwd()
_src_t = os.path.join(_ws, "src", "SyntheticShared", "SyntheticSettings.template.json")
_src_s = os.path.join(_ws, "src", "SyntheticShared", "SyntheticSettings.json")
if os.path.exists(_src_t) and not os.path.exists(_src_s):
    shutil.copy(_src_t, _src_s)

_proj = os.path.join(_ws, "tests", "SyntheticTests.Logic", "SyntheticTests.Logic.csproj")
if os.path.exists(_proj):
    subprocess.run(["dotnet", "build", _proj, "-c", "Debug", "-p:Platform=x64"], capture_output=True)

try:
    import winreg
except ImportError:
    winreg = None

KNOWN_FRAMEWORK_GUIDS = {
    "65f304b3-8efb-464d-b08a-12cfd61a1986",
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
    guids = set()
    try:
        if not os.path.exists(xml_path):
            return guids
        with open(xml_path, "r", encoding="utf-8") as f:
            content = f.read()
        matches = re.findall(r'ProductCode\s*=\s*"({[0-9a-fA-F-]+})"', content)
        for m in matches:
            guid_cand = m.strip("{}").strip()
            if len(guid_cand) >= 32:
                guids.add(guid_cand)
    except Exception:
        pass
    return guids

def get_addin_files(version):
    appdata = os.environ.get("APPDATA", "")
    programdata = os.environ.get("PROGRAMDATA", "")
    user_addin_dir = os.path.join(appdata, "Autodesk", "Revit", "Addins", str(version))
    all_addin_dir = os.path.join(programdata, "Autodesk", "Revit", "Addins", str(version))
    
    files = []
    if os.path.exists(user_addin_dir):
        for f in os.listdir(user_addin_dir):
            if f.endswith(".addin"):
                files.append(os.path.join(user_addin_dir, f))
    if os.path.exists(all_addin_dir):
        for f in os.listdir(all_addin_dir):
            if f.endswith(".addin"):
                files.append(os.path.join(all_addin_dir, f))
    return files

def get_bundle_package_contents(version):
    programdata = os.environ.get("PROGRAMDATA", "")
    bundle_dir = os.path.join(programdata, "Autodesk", "ApplicationPlugins")
    xml_files = []
    if os.path.exists(bundle_dir):
        for root, dirs, files_list in os.walk(bundle_dir):
            for file in files_list:
                if file.lower() == "packagecontents.xml":
                    xml_files.append(os.path.join(root, file))
    return xml_files

def whitelist_existing_addins(version):
    if not winreg:
        return []
    
    guids_to_add = set(KNOWN_FRAMEWORK_GUIDS)
    
    addin_files = get_addin_files(version)
    for af in addin_files:
        guids_to_add.update(extract_guids_from_file(af))
        
    xml_files = get_bundle_package_contents(version)
    for xf in xml_files:
        guids_to_add.update(extract_guids_from_package_contents(xf))
        
    key_path = f"Software\\Autodesk\\Revit\\Autodesk Revit {version}\\CodeSigning"
    added_guids = []
    
    try:
        key = winreg.CreateKey(winreg.HKEY_CURRENT_USER, key_path)
        for g in guids_to_add:
            try:
                val, _ = winreg.QueryValueEx(key, g)
            except FileNotFoundError:
                winreg.SetValueEx(key, g, 0, winreg.REG_DWORD, 1)
                added_guids.append(g)
        winreg.CloseKey(key)
    except Exception:
        pass
        
    return added_guids

def cleanup_whitelisted_registry_entries(version, guids_to_remove):
    if not winreg or not guids_to_remove:
        return
        
    key_path = f"Software\\Autodesk\\Revit\\Autodesk Revit {version}\\CodeSigning"
    try:
        key = winreg.OpenKey(winreg.HKEY_CURRENT_USER, key_path, 0, winreg.KEY_SET_VALUE)
        for g in guids_to_remove:
            try:
                winreg.DeleteValue(key, g)
            except FileNotFoundError:
                pass
        winreg.CloseKey(key)
    except Exception:
        pass
