using System;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NUnit.Framework;
using Synthetic.Platform.Host;
using Synthetic.Platform.Revit;
using SyntheticTests.Helpers;

namespace SyntheticTests.Fixtures
{
    /// <summary>
    /// Baseline document source for <see cref="RevitTemplateRollbackFixture"/>.
    /// </summary>
    public enum RevitBaselineDocumentKind
    {
        /// <summary>
        /// Creates a cached in-memory metric project document via <see cref="UnitSystem.Metric"/>.
        /// </summary>
        MetricProject,

        /// <summary>
        /// Creates a cached in-memory imperial project document via <see cref="UnitSystem.Imperial"/>.
        /// </summary>
        ImperialProject,

        /// <summary>
        /// Opens an isolated temp copy of <c>TestTemplate&lt;Year&gt;.rvt</c> without touching the tracked fixture on disk.
        /// </summary>
        TestTemplateCopy,

        /// <summary>
        /// Skips baseline document creation for pure Revit API struct/value tests that still require cold-worker setup.
        /// </summary>
        None
    }

    /// <summary>
    /// Shared per-fixture baseline caching base class for <see cref="RevitAdapterTestAttribute"/> suites.
    /// Caches a baseline <see cref="Document"/> in <c>[OneTimeSetUp]</c> (after initializing
    /// <see cref="HostSettingsRegistrar"/> and <see cref="TestWorkerJournalMarker"/>), executes each test inside
    /// <see cref="DocumentTransactionExtensions.ExecuteRolledBackGroup(Document, string, Action{Document})"/>,
    /// recreates invalidated documents between tests, and asserts zero open-document leaks in <c>[OneTimeTearDown]</c>.
    /// </summary>
    [RevitAdapterTest]
    public abstract class RevitTemplateRollbackFixture
    {
        private UIApplication? _uiapp;
        private int _initialDocCount;
        private Document? _baselineDoc;
        private Document? _secondaryBaselineDoc;
        private string? _fixtureTempRoot;

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
        /// Gets the baseline document kind created once per fixture. Defaults to <see cref="RevitBaselineDocumentKind.MetricProject"/>.
        /// </summary>
        protected virtual RevitBaselineDocumentKind BaselineKind => RevitBaselineDocumentKind.MetricProject;

        /// <summary>
        /// Performs cold-worker initialization, captures the initial open-document count, and creates the fixture baseline document.
        /// </summary>
        [OneTimeSetUp]
        public void BaseOneTimeSetUp(UIApplication uiapp)
        {
            _uiapp = uiapp ?? throw new ArgumentNullException(nameof(uiapp));
            HostSettingsRegistrar.EnsureRegistered();
            TestWorkerJournalMarker.EnsureWritten(uiapp);
            _initialDocCount = uiapp.Application.Documents.Size;

            if (BaselineKind != RevitBaselineDocumentKind.None)
            {
                _baselineDoc = CreateBaselineDocument();
            }

            OnFixtureSetUp(uiapp);
        }

        /// <summary>
        /// Hook for subclasses requiring per-test setup before execution.
        /// </summary>
        [SetUp]
        public void BaseSetUp()
        {
            OnTestSetUp();
        }

        /// <summary>
        /// Ensures any invalidated or left-modifiable cached baseline document is recycled cleanly between tests.
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
                if (IsDocumentInvalidated(_baselineDoc))
                {
                    CloseDocumentQuietly(ref _baselineDoc);
                }

                if (IsDocumentInvalidated(_secondaryBaselineDoc))
                {
                    CloseDocumentQuietly(ref _secondaryBaselineDoc);
                }
            }
        }

        /// <summary>
        /// Closes all cached baseline documents, cleans up any fixture temp directory, and asserts zero leaked documents.
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
                CloseDocumentQuietly(ref _secondaryBaselineDoc);
                CloseDocumentQuietly(ref _baselineDoc);
                TryDeleteDirectory(_fixtureTempRoot);
                _fixtureTempRoot = null;

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
        /// Executes <paramref name="action"/> against the cached baseline document wrapped inside
        /// <see cref="DocumentTransactionExtensions.ExecuteRolledBackGroup(Document, string, Action{Document})"/>.
        /// </summary>
        protected void WithRolledBackDocument(Action<Document> action, string? groupName = null)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            Document doc = EnsureValidBaselineDocument();
            string resolvedGroupName = groupName ?? (TestContext.CurrentContext?.Test?.Name ?? "RevitTemplateRollbackFixture");

            try
            {
                doc.ExecuteRolledBackGroup(resolvedGroupName, action);
            }
            finally
            {
                if (IsDocumentInvalidated(_baselineDoc))
                {
                    CloseDocumentQuietly(ref _baselineDoc);
                }
            }
        }

        /// <summary>
        /// Executes <paramref name="func"/> against the cached baseline document wrapped inside
        /// <see cref="DocumentTransactionExtensions.ExecuteRolledBackGroup(Document, string, Action{Document})"/> and returns its result.
        /// </summary>
        protected T WithRolledBackDocument<T>(Func<Document, T> func, string? groupName = null)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));

            T result = default!;
            WithRolledBackDocument(doc => { result = func(doc); }, groupName);
            return result;
        }

        /// <summary>
        /// Executes <paramref name="action"/> against two cached baseline documents, each wrapped inside
        /// <see cref="DocumentTransactionExtensions.ExecuteRolledBackGroup(Document, string, Action{Document})"/>.
        /// </summary>
        protected void WithRolledBackDocumentPair(Action<Document, Document> action, string? groupName = null)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            Document primaryDoc = EnsureValidBaselineDocument();
            Document secondaryDoc = EnsureValidSecondaryBaselineDocument();
            string resolvedGroupName = groupName ?? (TestContext.CurrentContext?.Test?.Name ?? "RevitTemplateRollbackFixturePair");

            try
            {
                primaryDoc.ExecuteRolledBackGroup(resolvedGroupName, p =>
                {
                    secondaryDoc.ExecuteRolledBackGroup(resolvedGroupName + "_Secondary", s => action(p, s));
                });
            }
            finally
            {
                if (IsDocumentInvalidated(_secondaryBaselineDoc))
                {
                    CloseDocumentQuietly(ref _secondaryBaselineDoc);
                }

                if (IsDocumentInvalidated(_baselineDoc))
                {
                    CloseDocumentQuietly(ref _baselineDoc);
                }
            }
        }

        /// <summary>
        /// Returns a valid cached baseline document (recreating it if previously invalidated), for read-only
        /// tests or callers that must verify raw <see cref="TransactionGroup"/> behavior directly.
        /// </summary>
        protected Document EnsureBaselineDocument() => EnsureValidBaselineDocument();

        /// <summary>
        /// Forces recreation of the cached baseline document.
        /// </summary>
        protected void RecreateBaselineDocument()
        {
            CloseDocumentQuietly(ref _baselineDoc);
            if (BaselineKind != RevitBaselineDocumentKind.None)
            {
                _baselineDoc = CreateBaselineDocument();
            }
        }

        private Document EnsureValidBaselineDocument()
        {
            if (IsDocumentInvalidated(_baselineDoc))
            {
                CloseDocumentQuietly(ref _baselineDoc);
                _baselineDoc = CreateBaselineDocument();
            }

            return _baselineDoc!;
        }

        private Document EnsureValidSecondaryBaselineDocument()
        {
            if (IsDocumentInvalidated(_secondaryBaselineDoc))
            {
                CloseDocumentQuietly(ref _secondaryBaselineDoc);
                _secondaryBaselineDoc = CreateBaselineDocument();
            }

            return _secondaryBaselineDoc!;
        }

        private Document CreateBaselineDocument()
        {
            switch (BaselineKind)
            {
                case RevitBaselineDocumentKind.MetricProject:
                    return App.NewProjectDocument(UnitSystem.Metric);
                case RevitBaselineDocumentKind.ImperialProject:
                    return App.NewProjectDocument(UnitSystem.Imperial);
                case RevitBaselineDocumentKind.TestTemplateCopy:
                    _fixtureTempRoot ??= RevitIsolatedDocumentFixture.CreateIsolatedRunDirectory();
                    string templatePath = RevitIsolatedDocumentFixture.GetTestTemplatePath(App);
                    return RevitIsolatedDocumentFixture.OpenIsolatedModelCopy(
                        App,
                        templatePath,
                        _fixtureTempRoot,
                        detachFromCentral: true);
                default:
                    throw new InvalidOperationException(
                        $"Cannot create a baseline document when {nameof(BaselineKind)} is {BaselineKind}.");
            }
        }

        private static bool IsDocumentInvalidated(Document? doc)
        {
            if (doc == null)
            {
                return true;
            }

            try
            {
                return !doc.IsValidObject || doc.IsModifiable;
            }
            catch
            {
                return true;
            }
        }

        private static void CloseDocumentQuietly(ref Document? doc)
        {
            Document? current = doc;
            doc = null;
            if (current == null)
            {
                return;
            }

            try
            {
                if (current.IsValidObject)
                {
                    current.Close(false);
                }
            }
            catch
            {
                // Best-effort cleanup; BaseOneTimeTearDown asserts final Documents.Size
            }
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
