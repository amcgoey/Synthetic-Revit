using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Synthetic.Platform.IO;

namespace SyntheticTests.Platform.IO
{
    [TestFixture]
    [UnitTest]
    public class FileUtilTests
    {
        [Test]
        public void CopyFiles_ExistingFilesWithRootNames_CopiesAndPreservesRelativeStructure()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string sourceFile = Path.Combine(tempRoot, "Source", "Materials", "Wood", "oak.png");
                Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
                File.WriteAllText(sourceFile, "oak-bitmap");

                string destRoot = Path.Combine(tempRoot, "Dest");
                var rootNames = new List<string> { "Materials", "Maps" };

                var results = FileUtil.CopyFiles(new List<string> { sourceFile }, destRoot, rootNames);

                var copied = results["Files Copied"];
                var notCopied = results["Files Not Copied"];
                Assert.IsTrue(copied.ContainsKey("oak.png"));
                Assert.IsEmpty(notCopied);

                string expectedDest = Path.Combine(destRoot + "\\wood", "oak.png");
                Assert.IsTrue(File.Exists(expectedDest));
                Assert.AreEqual("oak-bitmap", File.ReadAllText(expectedDest));
            }
            finally
            {
                DeleteDirectorySafe(tempRoot);
            }
        }

        [Test]
        public void CopyFiles_OverwriteFalseWhenDestinationExists_SkipsAndReportsInNotCopied()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string sourceFile = Path.Combine(tempRoot, "src", "tile.png");
                Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
                File.WriteAllText(sourceFile, "new-content");

                string destRoot = Path.Combine(tempRoot, "dest");
                Directory.CreateDirectory(destRoot);
                string destFile = Path.Combine(destRoot, "tile.png");
                File.WriteAllText(destFile, "original-content");

                var results = FileUtil.CopyFiles(new List<string> { sourceFile }, destRoot, rootNames: null, overwrite: false);

                Assert.IsEmpty(results["Files Copied"]);
                Assert.IsTrue(results["Files Not Copied"].ContainsKey("tile.png"));
                Assert.AreEqual("original-content", File.ReadAllText(destFile));
            }
            finally
            {
                DeleteDirectorySafe(tempRoot);
            }
        }

        [Test]
        public void CopyFiles_MissingSourceResolvedViaExplicitAlternatePaths_CopiesFromAlternateRoot()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string missingPrefix = Path.Combine(tempRoot, "oldserver", "maps").ToLowerInvariant();
                string missingPath = Path.Combine(missingPrefix, "brick.png");

                string alternatePrefix = Path.Combine(tempRoot, "newserver", "maps");
                string actualAlternateFile = Path.Combine(alternatePrefix, "brick.png");
                Directory.CreateDirectory(Path.GetDirectoryName(actualAlternateFile)!);
                File.WriteAllText(actualAlternateFile, "resolved-brick");

                var alternatePaths = new Dictionary<string, List<string>>
                {
                    { missingPrefix, new List<string> { alternatePrefix } }
                };

                string destRoot = Path.Combine(tempRoot, "dest");
                var results = FileUtil.CopyFiles(
                    new List<string> { missingPath },
                    destRoot,
                    rootNames: null,
                    overwrite: true,
                    archiveDirectories: null,
                    alternatePaths: alternatePaths);

                Assert.IsTrue(results["Files Copied"].ContainsKey("brick.png"));
                Assert.IsEmpty(results["Files Not Copied"]);
                Assert.AreEqual("resolved-brick", File.ReadAllText(Path.Combine(destRoot, "brick.png")));
            }
            finally
            {
                DeleteDirectorySafe(tempRoot);
            }
        }

        [Test]
        public void CopyFiles_MissingBimProjectPathResolvedViaExplicitArchiveDirectories_CopiesFromArchiveDesignFolder()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string missingPath = Path.Combine(tempRoot, "bim", "alpha tower", "textures", "stone.png");

                string archiveRoot = Path.Combine(tempRoot, "ArchiveRoot");
                string archivedFile = Path.Combine(archiveRoot, "2024_AlphaTower", "2 Design", "textures", "stone.png");
                Directory.CreateDirectory(Path.GetDirectoryName(archivedFile)!);
                File.WriteAllText(archivedFile, "archived-stone");

                string destRoot = Path.Combine(tempRoot, "dest");
                var results = FileUtil.CopyFiles(
                    new List<string> { missingPath },
                    destRoot,
                    rootNames: null,
                    overwrite: true,
                    archiveDirectories: new[] { archiveRoot },
                    alternatePaths: null);

                Assert.IsTrue(results["Files Copied"].ContainsKey("stone.png"));
                Assert.IsEmpty(results["Files Not Copied"]);
                Assert.AreEqual("archived-stone", File.ReadAllText(Path.Combine(destRoot, "stone.png")));
            }
            finally
            {
                DeleteDirectorySafe(tempRoot);
            }
        }

        [Test]
        public void CopyFiles_MissingFileWithoutFallback_ReportsInNotCopied()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string missingPath = Path.Combine(tempRoot, "nonexistent", "missing.png");
                string destRoot = Path.Combine(tempRoot, "dest");

                var results = FileUtil.CopyFiles(new List<string> { missingPath }, destRoot);

                Assert.IsEmpty(results["Files Copied"]);
                Assert.IsTrue(results["Files Not Copied"].ContainsKey("missing.png"));
            }
            finally
            {
                DeleteDirectorySafe(tempRoot);
            }
        }

        [Test]
        public void SeparatePath_MatchesLastRootOccurrence_AndHandlesNullOrUnmatched()
        {
            var roots = new List<string> { "Materials", "Maps" };

            Assert.AreEqual(string.Empty, FileUtil.SeparatePath(null, roots));
            Assert.AreEqual(string.Empty, FileUtil.SeparatePath(@"C:\Other\Textures", roots));
            Assert.AreEqual(@"\Wood\Oak", FileUtil.SeparatePath(@"C:\Assets\Materials\Wood\Oak", roots));
        }

        private static string CreateTempDirectory()
        {
            string dir = Path.Combine(Path.GetTempPath(), "SyntheticFileUtilTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void DeleteDirectorySafe(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch
            {
                // Ignore transient cleanup failures in parallel temp directories
            }
        }
    }
}
