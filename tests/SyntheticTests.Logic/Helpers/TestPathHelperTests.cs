using System;
using System.IO;
using NUnit.Framework;
using SyntheticTests.Helpers;

namespace SyntheticTests.Helpers
{
    [TestFixture]
    public class TestPathHelperTests
    {
        private string _tempRoot = string.Empty;
        private string? _originalEnvRoot;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "TestPathHelperTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            _originalEnvRoot = Environment.GetEnvironmentVariable(TestPathHelper.ProjectRootEnvVar);
        }

        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable(TestPathHelper.ProjectRootEnvVar, _originalEnvRoot);
            if (!string.IsNullOrEmpty(_tempRoot) && Directory.Exists(_tempRoot))
            {
                try
                {
                    Directory.Delete(_tempRoot, recursive: true);
                }
                catch
                {
                    // Best-effort cleanup of temp test directory
                }
            }
        }

        [Test]
        public void GetProjectRoot_DefaultOverload_ResolvesRepositoryContainingSolution()
        {
            string root = TestPathHelper.GetProjectRoot();

            Assert.IsTrue(Directory.Exists(root), $"Resolved root should exist: {root}");
            Assert.IsTrue(File.Exists(Path.Combine(root, "src", "Synthetic.sln")),
                $"Resolved root should contain src/Synthetic.sln: {root}");
        }

        [Test]
        [NonParallelizable]
        public void GetProjectRoot_WhenMarkerAdjacentToAssemblyExists_TakesPrecedenceOverEnvVarAndWalk()
        {
            string assemblyDir = Path.Combine(_tempRoot, "assembly");
            string markerTarget = Path.Combine(_tempRoot, "marker-root");
            string envTarget = Path.Combine(_tempRoot, "env-root");
            string walkRoot = Path.Combine(_tempRoot, "walk-root");
            string testDir = Path.Combine(walkRoot, "tests", "bin");

            Directory.CreateDirectory(assemblyDir);
            Directory.CreateDirectory(markerTarget);
            Directory.CreateDirectory(envTarget);
            Directory.CreateDirectory(Path.Combine(walkRoot, "src"));
            Directory.CreateDirectory(testDir);
            File.WriteAllText(Path.Combine(walkRoot, "src", "Synthetic.sln"), string.Empty);
            File.WriteAllText(Path.Combine(assemblyDir, TestPathHelper.ProjectRootMarkerFileName), "  " + markerTarget + Environment.NewLine);

            Environment.SetEnvironmentVariable(TestPathHelper.ProjectRootEnvVar, envTarget);

            string resolved = TestPathHelper.GetProjectRoot(assemblyDir, testDir);

            Assert.AreEqual(markerTarget, resolved);
        }

        [Test]
        [NonParallelizable]
        public void GetProjectRoot_WhenMarkerAdjacentToTestDirectoryExists_ResolvesBeforeEnvVar()
        {
            string assemblyDir = Path.Combine(_tempRoot, "assembly");
            string markerTarget = Path.Combine(_tempRoot, "testdir-marker-root");
            string envTarget = Path.Combine(_tempRoot, "env-root");
            string testDir = Path.Combine(_tempRoot, "test-bin");

            Directory.CreateDirectory(assemblyDir);
            Directory.CreateDirectory(markerTarget);
            Directory.CreateDirectory(envTarget);
            Directory.CreateDirectory(testDir);
            File.WriteAllText(Path.Combine(testDir, TestPathHelper.ProjectRootMarkerFileName), markerTarget);

            Environment.SetEnvironmentVariable(TestPathHelper.ProjectRootEnvVar, envTarget);

            string resolved = TestPathHelper.GetProjectRoot(assemblyDir, testDir);

            Assert.AreEqual(markerTarget, resolved);
        }

        [Test]
        [NonParallelizable]
        public void GetProjectRoot_WhenMarkerMissingOrInvalidAndEnvVarSet_ReturnsEnvVarOverUpwardWalk()
        {
            string assemblyDir = Path.Combine(_tempRoot, "assembly");
            string missingTarget = Path.Combine(_tempRoot, "does-not-exist");
            string envTarget = Path.Combine(_tempRoot, "env-root");
            string walkRoot = Path.Combine(_tempRoot, "walk-root");
            string testDir = Path.Combine(walkRoot, "nested", "bin");

            Directory.CreateDirectory(assemblyDir);
            Directory.CreateDirectory(envTarget);
            Directory.CreateDirectory(Path.Combine(walkRoot, "src"));
            Directory.CreateDirectory(testDir);
            File.WriteAllText(Path.Combine(walkRoot, "src", "Synthetic.sln"), string.Empty);
            File.WriteAllText(Path.Combine(assemblyDir, TestPathHelper.ProjectRootMarkerFileName), missingTarget);

            Environment.SetEnvironmentVariable(TestPathHelper.ProjectRootEnvVar, envTarget);

            string resolved = TestPathHelper.GetProjectRoot(assemblyDir, testDir);

            Assert.AreEqual(envTarget, resolved);
        }

        [Test]
        [NonParallelizable]
        public void GetProjectRoot_WhenNoMarkerAndNoEnvVar_WalksUpwardForSolution()
        {
            string assemblyDir = Path.Combine(_tempRoot, "assembly");
            string walkRoot = Path.Combine(_tempRoot, "walk-root");
            string testDir = Path.Combine(walkRoot, "tests", "SyntheticTests.Logic", "bin");

            Directory.CreateDirectory(assemblyDir);
            Directory.CreateDirectory(Path.Combine(walkRoot, "src"));
            Directory.CreateDirectory(testDir);
            File.WriteAllText(Path.Combine(walkRoot, "src", "Synthetic.sln"), string.Empty);

            Environment.SetEnvironmentVariable(TestPathHelper.ProjectRootEnvVar, null);

            string resolved = TestPathHelper.GetProjectRoot(assemblyDir, testDir);

            Assert.AreEqual(walkRoot, resolved);
        }

        [Test]
        [NonParallelizable]
        public void GetProjectRoot_WhenNoStrategyResolves_ThrowsInvalidOperationException()
        {
            string assemblyDir = Path.Combine(_tempRoot, "isolated-assembly");
            string testDir = Path.Combine(_tempRoot, "isolated-testdir", "nested");

            Directory.CreateDirectory(assemblyDir);
            Directory.CreateDirectory(testDir);
            Environment.SetEnvironmentVariable(TestPathHelper.ProjectRootEnvVar, Path.Combine(_tempRoot, "missing-env-dir"));

            Assert.Throws<InvalidOperationException>(() => TestPathHelper.GetProjectRoot(assemblyDir, testDir));
        }
    }
}
