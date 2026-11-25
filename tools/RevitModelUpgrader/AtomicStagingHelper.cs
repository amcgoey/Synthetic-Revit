using System;
using System.IO;

namespace Synthetic.Tools.ModelUpgrader
{
    /// <summary>
    /// Creates per-file temporary staging paths and atomically replaces verified upgrade outputs.
    /// </summary>
    internal static class AtomicStagingHelper
    {
        /// <summary>
        /// Creates an isolated staging directory adjacent to <paramref name="targetPath"/> so
        /// Revit backup files remain quarantined and final replacement stays on the same filesystem volume.
        /// </summary>
        public static void CreateStagingLocation(string targetPath, out string stagingDir, out string stagingFilePath)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                throw new ArgumentException("Target path cannot be empty.", nameof(targetPath));
            }

            string fullTargetPath = Path.GetFullPath(targetPath);
            string targetDir = Path.GetDirectoryName(fullTargetPath)
                ?? throw new InvalidOperationException($"Could not determine parent directory for '{fullTargetPath}'.");
            Directory.CreateDirectory(targetDir);

            string token = Guid.NewGuid().ToString("N").Substring(0, 12);
            stagingDir = Path.Combine(targetDir, $".synthetic_upgrade_staging_{token}");
            Directory.CreateDirectory(stagingDir);
            stagingFilePath = Path.Combine(stagingDir, Path.GetFileName(fullTargetPath));
        }

        /// <summary>
        /// Atomically moves or replaces <paramref name="targetPath"/> with verified <paramref name="stagingFilePath"/>.
        /// </summary>
        public static void AtomicMoveIntoPlace(string stagingFilePath, string targetPath)
        {
            if (string.IsNullOrWhiteSpace(stagingFilePath) || !File.Exists(stagingFilePath))
            {
                throw new FileNotFoundException("Verified staging file was not found.", stagingFilePath);
            }

            string fullTargetPath = Path.GetFullPath(targetPath);
            string targetDir = Path.GetDirectoryName(fullTargetPath)
                ?? throw new InvalidOperationException($"Could not determine parent directory for '{fullTargetPath}'.");
            Directory.CreateDirectory(targetDir);

            if (!File.Exists(fullTargetPath))
            {
                File.Move(stagingFilePath, fullTargetPath);
                return;
            }

            string backupPath = fullTargetPath + $".bak_{Guid.NewGuid():N}";
            try
            {
                File.Replace(stagingFilePath, fullTargetPath, backupPath, ignoreMetadataErrors: true);
            }
            finally
            {
                TryDeleteFile(backupPath);
            }
        }

        public static void TryDeleteFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch
            {
                // Best-effort backup cleanup
            }
        }

        public static void TryDeleteDirectory(string? dirPath)
        {
            if (string.IsNullOrWhiteSpace(dirPath) || !Directory.Exists(dirPath))
            {
                return;
            }

            try
            {
                Directory.Delete(dirPath, recursive: true);
            }
            catch
            {
                // Best-effort staging cleanup
            }
        }
    }
}
