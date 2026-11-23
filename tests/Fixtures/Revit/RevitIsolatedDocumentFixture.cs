using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NUnit.Framework;
using Synthetic.Platform.Host;
using SyntheticTests.Helpers;

namespace SyntheticTests.Fixtures
{
    /// <summary>
    /// Shared per-test temp isolation base class for <see cref="RevitIsolatedDocTestAttribute"/> suites.
    /// Copies <c>.rvt</c>/<c>.rfa</c> fixtures into <c>%TEMP%\SyntheticRevitTestDocs\&lt;RunGuid&gt;\&lt;TestId&gt;\</c>,
    /// initializes <see cref="HostSettingsRegistrar"/> and <see cref="TestWorkerJournalMarker"/>, closes any tracked
    /// documents and removes the per-test temp directory in <c>[TearDown]</c>, and asserts zero open-document leaks
    /// in <c>[OneTimeTearDown]</c>.
    /// </summary>
    [RevitIsolatedDocTest]
    public abstract class RevitIsolatedDocumentFixture
    {
        private UIApplication? _uiapp;
        private int _initialDocCount;
        private string _fixtureRunRoot = string.Empty;
        private string _currentTestTempDir = string.Empty;
        private readonly List<Document> _trackedDocuments = new List<Document>();

        /// <summary>
        /// Gets the active <see cref="UIApplication"/> injected by <c>ricaun.NUnit</c>.
        /// </summary>
        protected UIApplication UiApp =>
            _uiapp ?? throw new InvalidOperationException("UIApplication has not been initialized.");

        /// <summary>
        /// Gets the active Revit <see cref="Autodesk.Revit.ApplicationServices.Application"/>.
        /// </summary>
        protected Autodesk.Revit.ApplicationServices.Application App => UiApp.Application;

        /// <summary>
        /// Gets the per-test isolated directory under <c>%TEMP%\SyntheticRevitTestDocs\&lt;RunGuid&gt;\&lt;TestId&gt;\</c>.
        /// </summary>
        protected string CurrentTestTempDirectory
        {
            get
            {
                if (string.IsNullOrEmpty(_currentTestTempDir))
                {
                    _currentTestTempDir = CreatePerTestDirectory();
                }
                else if (!Directory.Exists(_currentTestTempDir))
                {
                    Directory.CreateDirectory(_currentTestTempDir);
                }

                return _currentTestTempDir;
            }
        }

        /// <summary>
        /// Initializes cold-worker state, captures initial open-document count, and creates the fixture run root directory.
        /// </summary>
        [OneTimeSetUp]
        public void BaseOneTimeSetUp(UIApplication uiapp)
        {
            _uiapp = uiapp ?? throw new ArgumentNullException(nameof(uiapp));
            HostSettingsRegistrar.EnsureRegistered();
            TestWorkerJournalMarker.EnsureWritten(uiapp);
            _initialDocCount = uiapp.Application.Documents.Size;
            _fixtureRunRoot = CreateIsolatedRunDirectory();
            OnFixtureSetUp(uiapp);
        }

        /// <summary>
        /// Creates a fresh per-test temp directory before each test.
        /// </summary>
        [SetUp]
        public void BaseSetUp()
        {
            _trackedDocuments.Clear();
            _currentTestTempDir = CreatePerTestDirectory();
            OnTestSetUp();
        }

        /// <summary>
        /// Closes all tracked documents opened/created during the test and deletes the per-test temp directory.
        /// </summary>
        [TearDown]
        public void BaseTearDown()
        {
            try
            {
                OnTestTearDown();
            }
            finally
            {
                CloseTrackedDocuments();
                TryDeleteDirectory(_currentTestTempDir);
                _currentTestTempDir = string.Empty;
            }
        }

        /// <summary>
        /// Cleans up the fixture run temp root and asserts that zero Revit documents leaked during the fixture.
        /// </summary>
        [OneTimeTearDown]
        public void BaseOneTimeTearDown()
        {
            try
            {
                OnFixtureTearDown();
            }
            finally
            {
                CloseTrackedDocuments();
                TryDeleteDirectory(_fixtureRunRoot);
                _fixtureRunRoot = string.Empty;

                if (_uiapp?.Application != null)
                {
                    Assert.AreEqual(
                        _initialDocCount,
                        _uiapp.Application.Documents.Size,
                        $"{GetType().Name} leaked open Revit document(s): expected {_initialDocCount}, actual {_uiapp.Application.Documents.Size}.");
                }
            }
        }

        /// <summary>
        /// Optional hook executed at the end of <see cref="BaseOneTimeSetUp"/>.
        /// </summary>
        protected virtual void OnFixtureSetUp(UIApplication uiapp)
        {
        }

        /// <summary>
        /// Optional hook executed during <see cref="BaseSetUp"/>.
        /// </summary>
        protected virtual void OnTestSetUp()
        {
        }

        /// <summary>
        /// Optional hook executed during <see cref="BaseTearDown"/>.
        /// </summary>
        protected virtual void OnTestTearDown()
        {
        }

        /// <summary>
        /// Optional hook executed at the start of <see cref="BaseOneTimeTearDown"/>.
        /// </summary>
        protected virtual void OnFixtureTearDown()
        {
        }

        /// <summary>
        /// Registers a <see cref="Document"/> for automatic <c>Close(false)</c> cleanup in <see cref="BaseTearDown"/>.
        /// Uses reference equality because <see cref="Document.Equals(object)"/> throws if an earlier tracked document was closed.
        /// </summary>
        protected Document TrackDocument(Document doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (!ContainsDocumentReference(doc))
            {
                _trackedDocuments.Add(doc);
            }
            return doc;
        }

        private bool ContainsDocumentReference(Document doc)
        {
            for (int i = 0; i < _trackedDocuments.Count; i++)
            {
                if (ReferenceEquals(_trackedDocuments[i], doc))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns an isolated path inside <see cref="CurrentTestTempDirectory"/> for <paramref name="fileName"/>.
        /// </summary>
        protected string GetTempFilePath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("File name cannot be empty.", nameof(fileName));
            return Path.Combine(CurrentTestTempDirectory, fileName);
        }

        /// <summary>
        /// Resolves the tracked <c>tests/Assets/test_models/TestTemplate&lt;Year&gt;.rvt</c> path for the active Revit version.
        /// </summary>
        protected string GetTestTemplatePath() => GetTestTemplatePath(App);

        /// <summary>
        /// Copies <paramref name="sourcePath"/> into <see cref="CurrentTestTempDirectory"/> and returns the isolated copy path.
        /// </summary>
        protected string CopyAssetToTestTemp(string sourcePath, string? targetFileName = null)
        {
            if (string.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentException("Source path cannot be empty.", nameof(sourcePath));
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("Test fixture asset not found: " + sourcePath, sourcePath);
            }

            string fileName = string.IsNullOrWhiteSpace(targetFileName)
                ? Path.GetFileName(sourcePath)
                : targetFileName!;
            string destPath = Path.Combine(CurrentTestTempDirectory, fileName);
            File.Copy(sourcePath, destPath, overwrite: true);
            return destPath;
        }

        /// <summary>
        /// Copies <paramref name="sourceModelPath"/> into <see cref="CurrentTestTempDirectory"/> and opens the isolated copy in Revit.
        /// </summary>
        protected Document OpenIsolatedModelCopy(
            string sourceModelPath,
            bool detachFromCentral = true,
            string? targetFileName = null)
        {
            Document doc = OpenIsolatedModelCopy(App, sourceModelPath, CurrentTestTempDirectory, detachFromCentral, targetFileName);
            return TrackDocument(doc);
        }

        /// <summary>
        /// Copies <c>TestTemplate&lt;Year&gt;.rvt</c> into <see cref="CurrentTestTempDirectory"/> and opens the isolated copy in Revit.
        /// </summary>
        protected Document OpenIsolatedTestTemplateCopy(
            bool detachFromCentral = true,
            string? targetFileName = null)
        {
            return OpenIsolatedModelCopy(GetTestTemplatePath(), detachFromCentral, targetFileName);
        }

        /// <summary>
        /// Creates a new project document and registers it for automatic cleanup in <see cref="BaseTearDown"/>.
        /// </summary>
        protected Document NewTrackedProjectDocument(UnitSystem unitSystem = UnitSystem.Metric)
        {
            Document doc = App.NewProjectDocument(unitSystem);
            return TrackDocument(doc);
        }

        /// <summary>
        /// Creates a unique run directory under <c>%TEMP%\SyntheticRevitTestDocs\&lt;RunGuid&gt;</c>.
        /// </summary>
        public static string CreateIsolatedRunDirectory()
        {
            string runRoot = Path.Combine(
                Path.GetTempPath(),
                "SyntheticRevitTestDocs",
                Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(runRoot);
            return runRoot;
        }

        /// <summary>
        /// Resolves the tracked <c>tests/Assets/test_models/TestTemplate&lt;Year&gt;.rvt</c> path for <paramref name="app"/>.
        /// </summary>
        public static string GetTestTemplatePath(Autodesk.Revit.ApplicationServices.Application app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            string projectRoot = TestPathHelper.GetProjectRoot();
            string testModelName = "TestTemplate" + app.VersionNumber + ".rvt";
            string modelPathStr = Path.Combine(projectRoot, "tests", "Assets", "test_models", testModelName);
            if (!File.Exists(modelPathStr))
            {
                throw new FileNotFoundException("Test template model not found: " + modelPathStr, modelPathStr);
            }

            return modelPathStr;
        }

        /// <summary>
        /// Copies <paramref name="sourceModelPath"/> into <paramref name="destinationDirectory"/> with a unique filename if needed
        /// and opens the isolated copy cleanly without touching the tracked source file on disk.
        /// </summary>
        public static Document OpenIsolatedModelCopy(
            Autodesk.Revit.ApplicationServices.Application app,
            string sourceModelPath,
            string destinationDirectory,
            bool detachFromCentral = true,
            string? targetFileName = null)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (string.IsNullOrWhiteSpace(sourceModelPath)) throw new ArgumentException("Source model path cannot be empty.", nameof(sourceModelPath));
            if (string.IsNullOrWhiteSpace(destinationDirectory)) throw new ArgumentException("Destination directory cannot be empty.", nameof(destinationDirectory));
            if (!File.Exists(sourceModelPath))
            {
                throw new FileNotFoundException("Test model not found: " + sourceModelPath, sourceModelPath);
            }

            Directory.CreateDirectory(destinationDirectory);

            string ext = Path.GetExtension(sourceModelPath);
            string baseName = Path.GetFileNameWithoutExtension(sourceModelPath);
            string fileName = string.IsNullOrWhiteSpace(targetFileName)
                ? $"{baseName}_{Guid.NewGuid():N}{ext}"
                : targetFileName!;

            string copyPath = Path.Combine(destinationDirectory, fileName);
            File.Copy(sourceModelPath, copyPath, overwrite: true);

            ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(copyPath);
            if (detachFromCentral)
            {
                var openOptions = new OpenOptions
                {
                    DetachFromCentralOption = DetachFromCentralOption.DetachAndDiscardWorksets
                };
                return app.OpenDocumentFile(modelPath, openOptions);
            }

            return app.OpenDocumentFile(modelPath, new OpenOptions());
        }

        private string CreatePerTestDirectory()
        {
            if (string.IsNullOrEmpty(_fixtureRunRoot))
            {
                _fixtureRunRoot = CreateIsolatedRunDirectory();
            }

            string rawName = TestContext.CurrentContext?.Test?.Name ?? "Test";
            string sanitized = SanitizePathSegment(rawName);
            if (sanitized.Length > 12)
            {
                sanitized = sanitized.Substring(0, 12);
            }

            string dir = Path.Combine(
                _fixtureRunRoot,
                $"{sanitized}_{Guid.NewGuid().ToString("N").Substring(0, 6)}");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private void CloseTrackedDocuments()
        {
            for (int i = _trackedDocuments.Count - 1; i >= 0; i--)
            {
                Document doc = _trackedDocuments[i];
                try
                {
                    if (doc != null && doc.IsValidObject)
                    {
                        doc.Close(false);
                    }
                }
                catch
                {
                    // Best-effort cleanup; BaseOneTimeTearDown verifies final Documents.Size
                }
            }

            _trackedDocuments.Clear();
        }

        private static string SanitizePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Test";
            }

            char[] invalidChars = Path.GetInvalidFileNameChars();
            char[] buffer = value.ToCharArray();
            for (int i = 0; i < buffer.Length; i++)
            {
                if (Array.IndexOf(invalidChars, buffer[i]) >= 0 || char.IsWhiteSpace(buffer[i]))
                {
                    buffer[i] = '_';
                }
            }

            return new string(buffer);
        }

        private static void TryDeleteDirectory(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return;
            }

            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch
            {
                // Best-effort temp cleanup
            }
        }
    }
}
