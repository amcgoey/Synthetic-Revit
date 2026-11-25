import os
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
TEST_EXECUTOR_DIR = os.path.join(REPO_ROOT, ".agents", "skills", "revit-tests", "scripts")
if TEST_EXECUTOR_DIR not in sys.path:
    sys.path.insert(0, TEST_EXECUTOR_DIR)

import test_executor as te

UNIFIED_CLIENT_ID = "4fd5b26e-1325-460f-97b1-878804070868"
EXPECTED_FULL_CLASS = "Synthetic.Bootstrap.BootstrapApp"


class TestAddinManifestGeneration(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.env = os.environ.copy()
        sdk_info = te.configure_dotnet_environment(env=cls.env)
        cls.dotnet = te.resolve_dotnet_exe(sdk_info)

    def _generate_manifest(self, revit_version, configuration, out_path):
        source_csproj = os.path.join(
            REPO_ROOT,
            "src",
            f"Synthetic{revit_version if int(revit_version) <= 2026 else '2026'}",
            f"Synthetic{revit_version if int(revit_version) <= 2026 else '2026'}.csproj",
        )
        cmd = [
            self.dotnet,
            "build",
            source_csproj,
            "-t:GenerateRevitAddinManifest",
            "-c",
            configuration,
            "-p:Platform=x64",
            f"-p:RevitVersion={revit_version}",
            "-p:SkipDeployAddin=true",
            f"-p:AddinManifestOutputPath={out_path}",
            "-v:minimal",
        ]
        res = subprocess.run(cmd, env=self.env, capture_output=True, text=True)
        self.assertEqual(res.returncode, 0, msg=res.stdout + res.stderr)
        self.assertTrue(os.path.isfile(out_path), f"Expected manifest at {out_path}")
        return ET.parse(out_path).getroot()

    def test_manifest_matrix_across_versions_and_configurations(self):
        repo_output_root = os.path.abspath(os.path.join(REPO_ROOT, "output"))
        cases = [
            ("2023", "Debug", False, os.path.join(repo_output_root, "Synthetic", "Bootstrap", "v1", "Synthetic.Bootstrap2023.dll")),
            ("2024", "Debug", False, os.path.join(repo_output_root, "Synthetic", "Bootstrap", "v1", "Synthetic.Bootstrap2024.dll")),
            ("2025", "Debug", False, os.path.join(repo_output_root, "Synthetic", "Bootstrap", "v1", "Synthetic.Bootstrap2025.dll")),
            ("2026", "Debug", True, os.path.join(repo_output_root, "Synthetic", "Bootstrap", "v1", "Synthetic.Bootstrap2026.dll")),
            ("2027", "Debug", True, os.path.join(repo_output_root, "Synthetic", "Bootstrap", "v1", "Synthetic.Bootstrap2027.dll")),
            ("2023", "Release", False, r"C:\ProgramData\Autodesk\Revit\Addins\Synthetic\Bootstrap\v1\Synthetic.Bootstrap2023.dll"),
            ("2024", "Release", False, r"C:\ProgramData\Autodesk\Revit\Addins\Synthetic\Bootstrap\v1\Synthetic.Bootstrap2024.dll"),
            ("2025", "Release", False, r"C:\ProgramData\Autodesk\Revit\Addins\Synthetic\Bootstrap\v1\Synthetic.Bootstrap2025.dll"),
            ("2026", "Release", True, r"C:\ProgramData\Autodesk\Revit\Addins\Synthetic\Bootstrap\v1\Synthetic.Bootstrap2026.dll"),
            ("2027", "Release", True, r"C:\Program Files\Autodesk\Revit\Addins\Synthetic\Bootstrap\v1\Synthetic.Bootstrap2027.dll"),
        ]

        with tempfile.TemporaryDirectory() as tmp_dir:
            for revit_ver, config, expect_use_revit_context, expected_assembly in cases:
                with self.subTest(revit_version=revit_ver, configuration=config):
                    out_path = os.path.join(tmp_dir, config, f"Synthetic{revit_ver}.addin")
                    root = self._generate_manifest(revit_ver, config, out_path)

                    self.assertEqual(root.tag, "RevitAddIns")
                    addin = root.find("AddIn")
                    self.assertIsNotNone(addin)
                    self.assertEqual(addin.attrib.get("Type"), "Application")
                    self.assertEqual(addin.findtext("Name"), "Synthetic")
                    self.assertEqual(addin.findtext("Assembly"), expected_assembly)
                    self.assertEqual(addin.findtext("FullClassName"), EXPECTED_FULL_CLASS)
                    self.assertEqual(addin.findtext("ClientId"), UNIFIED_CLIENT_ID)

                    manifest_settings = root.find("ManifestSettings")
                    if expect_use_revit_context:
                        self.assertIsNotNone(manifest_settings, f"Expected <ManifestSettings> for Revit {revit_ver}")
                        self.assertEqual(manifest_settings.findtext("UseRevitContext"), "True")
                    else:
                        self.assertIsNone(manifest_settings, f"Did not expect <ManifestSettings> for Revit {revit_ver}")


if __name__ == "__main__":
    unittest.main()
