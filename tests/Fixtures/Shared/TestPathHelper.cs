using System;
using System.IO;
using NUnit.Framework;

namespace SyntheticTests.Helpers
{
    public static class TestPathHelper
    {
        public const string ProjectRootMarkerFileName = "synthetic_project_root.txt";
        public const string ProjectRootEnvVar = "SYNTHETIC_PROJECT_ROOT";

        public static string GetProjectRoot()
        {
            string? assemblyDir = Path.GetDirectoryName(typeof(TestPathHelper).Assembly.Location);
            string? testDir = TestContext.CurrentContext?.TestDirectory;
            return GetProjectRoot(assemblyDir, testDir);
        }

        public static string GetProjectRoot(string? assemblyDir, string? testDirectory)
        {
            string? markerRoot = TryReadMarkerFile(assemblyDir);
            if (markerRoot != null)
            {
                return markerRoot;
            }

            if (!string.IsNullOrEmpty(testDirectory) && !PathsEqual(assemblyDir, testDirectory))
            {
                markerRoot = TryReadMarkerFile(testDirectory);
                if (markerRoot != null)
                {
                    return markerRoot;
                }
            }

            string? envPath = Environment.GetEnvironmentVariable(ProjectRootEnvVar);
            if (!string.IsNullOrWhiteSpace(envPath))
            {
                string trimmedEnvPath = envPath.Trim();
                if (Directory.Exists(trimmedEnvPath))
                {
                    return trimmedEnvPath;
                }
            }

            string? dir = testDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                string slnCandidate = Path.Combine(dir, "src", "Synthetic.sln");
                if (File.Exists(slnCandidate))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException(
                "Could not locate Synthetic project root via synthetic_project_root.txt, SYNTHETIC_PROJECT_ROOT, or upward search for src/Synthetic.sln.");
        }

        private static string? TryReadMarkerFile(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return null;
            }

            string markerPath = Path.Combine(directory, ProjectRootMarkerFileName);
            if (!File.Exists(markerPath))
            {
                return null;
            }

            string candidate = File.ReadAllText(markerPath).Trim();
            if (!string.IsNullOrEmpty(candidate) && Directory.Exists(candidate))
            {
                return candidate;
            }

            return null;
        }

        private static bool PathsEqual(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            string normalizedLeft = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedRight = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }
    }
}
