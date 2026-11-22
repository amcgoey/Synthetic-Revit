using System;
using System.IO;
using NUnit.Framework;

namespace SyntheticTests.Helpers
{
    public static class TestPathHelper
    {
        public static string GetProjectRoot()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string revitAddinsDir = Path.Combine(appData, "Autodesk", "Revit", "Addins");
            if (Directory.Exists(revitAddinsDir))
            {
                foreach (var versionDir in Directory.GetDirectories(revitAddinsDir))
                {
                    string[] addinFiles = Directory.GetFiles(versionDir, "Synthetic*.addin");
                    foreach (var addinPath in addinFiles)
                    {
                        if (File.Exists(addinPath))
                        {
                            string content = File.ReadAllText(addinPath);
                            var match = System.Text.RegularExpressions.Regex.Match(content, @"<Assembly>(.*?)\\output\\Synthetic\\Synthetic\d*\.dll", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (match.Success)
                            {
                                string root = match.Groups[1].Value;
                                if (Directory.Exists(root))
                                {
                                    return root;
                                }
                            }
                        }
                    }
                }
            }

            string envPath = Environment.GetEnvironmentVariable("SYNTHETIC_PROJECT_ROOT");
            if (!string.IsNullOrEmpty(envPath) && Directory.Exists(envPath))
            {
                return envPath;
            }

            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "tests")))
            {
                dir = Path.GetDirectoryName(dir);
            }
            return dir ?? TestContext.CurrentContext.TestDirectory;
        }
    }
}
