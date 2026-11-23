using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Synthetic.Platform.IO;

namespace SyntheticTests.Platform.IO
{
    [TestFixture]
    [UnitTest]
    public class SearchPathsTests
    {
        [Test]
        public void Constructor_PrioritizesFirstSearchPathForDuplicateFileNames()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string primary = Path.Combine(tempRoot, "Primary");
                string secondary = Path.Combine(tempRoot, "Secondary");
                Directory.CreateDirectory(primary);
                Directory.CreateDirectory(secondary);

                string primaryFile = Path.Combine(primary, "shared.png");
                string secondaryFile = Path.Combine(secondary, "shared.png");
                string secondaryOnly = Path.Combine(secondary, "unique.png");
                File.WriteAllText(primaryFile, "first");
                File.WriteAllText(secondaryFile, "second");
                File.WriteAllText(secondaryOnly, "unique");

                var searchPaths = new SearchPaths(new List<string> { primary, secondary });

                Assert.IsTrue(searchPaths.ContainsPath(primary));
                Assert.IsTrue(searchPaths.ContainsFile("shared.png"));
                Assert.IsTrue(searchPaths.ContainsFile("unique.png"));
                Assert.IsFalse(searchPaths.ContainsFile("missing.png"));
                Assert.AreEqual(primaryFile, searchPaths.GetFilePath("shared.png"));
                Assert.AreEqual(secondaryOnly, searchPaths.GetFilePath("unique.png"));
                Assert.IsNull(searchPaths.GetFilePath("missing.png"));
            }
            finally
            {
                DeleteDirectorySafe(tempRoot);
            }
        }

        [Test]
        public void AddPaths_AddsNewDirectoriesAndIndexesNewFilesWithoutDuplicates()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string first = Path.Combine(tempRoot, "First");
                string second = Path.Combine(tempRoot, "Second");
                Directory.CreateDirectory(first);
                Directory.CreateDirectory(second);

                File.WriteAllText(Path.Combine(first, "a.png"), "a");
                string secondFile = Path.Combine(second, "b.png");
                File.WriteAllText(secondFile, "b");

                var searchPaths = new SearchPaths(new List<string> { first });
                searchPaths.AddPaths(new List<string> { first, second });

                Assert.AreEqual(2, searchPaths.Paths.Count);
                Assert.IsTrue(searchPaths.ContainsFile("b.png"));
                Assert.AreEqual(secondFile, searchPaths.GetFilePath("b.png"));
            }
            finally
            {
                DeleteDirectorySafe(tempRoot);
            }
        }

        [Test]
        public void CopyFiles_CopiesFoundFilesAndRespectsOverwriteFlag()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string sourceRoot = Path.Combine(tempRoot, "Source");
                string subDir = Path.Combine(sourceRoot, "Sub");
                Directory.CreateDirectory(subDir);

                string sourceFile = Path.Combine(subDir, "texture.png");
                File.WriteAllText(sourceFile, "v2");

                string destRoot = Path.Combine(tempRoot, "Dest");
                Directory.CreateDirectory(Path.Combine(destRoot, "Sub"));
                string destFile = Path.Combine(destRoot, "Sub", "texture.png");
                File.WriteAllText(destFile, "v1");

                var searchPaths = new SearchPaths(new List<string> { sourceRoot });

                var skipResult = searchPaths.CopyFiles(new List<string> { "texture.png", "absent.png" }, destRoot, Overwrite: false);
                CollectionAssert.IsEmpty((List<string>)skipResult["Files Copied"]!);
                CollectionAssert.AreEquivalent(new[] { "texture.png", "absent.png" }, (List<string>)skipResult["Files Not Copied"]!);
                Assert.AreEqual("v1", File.ReadAllText(destFile));

                var overwriteResult = searchPaths.CopyFiles(new List<string> { "texture.png", "absent.png" }, destRoot, Overwrite: true);
                CollectionAssert.AreEqual(new[] { "texture.png" }, (List<string>)overwriteResult["Files Copied"]!);
                CollectionAssert.AreEqual(new[] { "absent.png" }, (List<string>)overwriteResult["Files Not Copied"]!);
                Assert.AreEqual("v2", File.ReadAllText(destFile));
            }
            finally
            {
                DeleteDirectorySafe(tempRoot);
            }
        }

        private static string CreateTempDirectory()
        {
            string dir = Path.Combine(Path.GetTempPath(), "SyntheticSearchPathsTests_" + Guid.NewGuid().ToString("N"));
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
