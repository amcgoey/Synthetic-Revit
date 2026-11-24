using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NUnit.Framework;
using Synthetic.Features.SharedCoordinates.Adapters;
using Synthetic.Features.SharedCoordinates.Models;
using Synthetic.Platform.Revit;
using SyntheticTests.Fixtures;

namespace SyntheticTests.Features.SharedCoordinates
{
    [TestFixture]
    [RevitAdapterTest]
    public class RevitSharedCoordinatesAdapterRevitTests : RevitTemplateRollbackFixture
    {
        private string? _fixtureTempDir;
        private string? _singleSiteUnitModelPath;
        private string? _multiSiteUnitModelPath;
        private string _singleSiteName = "Internal";

        protected override void OnFixtureSetUp(UIApplication uiapp)
        {
            _fixtureTempDir = RevitIsolatedDocumentFixture.CreateIsolatedRunDirectory();
            _singleSiteUnitModelPath = Path.Combine(_fixtureTempDir, "SharedCoordsSingleSiteUnit.rvt");
            _multiSiteUnitModelPath = Path.Combine(_fixtureTempDir, "SharedCoordsMultiSiteUnit.rvt");

            // 1. Single-location unit model (AcquireCoordinates requires exactly one ProjectLocation)
            Document singleDoc = App.NewProjectDocument(UnitSystem.Imperial);
            try
            {
                singleDoc.ExecuteTransaction("Configure Single Unit Site", FailurePipelines.ObserveOnly(), () =>
                {
                    ProjectLocation activeLoc = singleDoc.ActiveProjectLocation;
                    Assert.IsNotNull(activeLoc, "New project document must have an ActiveProjectLocation.");

                    activeLoc.SetProjectPosition(
                        XYZ.Zero,
                        CreateProjectPosition(singleDoc, 25.0, 15.0, 5.0, 0.0));
                    _singleSiteName = activeLoc.Name;
                });

                Assert.AreEqual(1, singleDoc.ProjectLocations.Size, "Single-site unit model must contain exactly one ProjectLocation.");
                singleDoc.SaveAs(_singleSiteUnitModelPath);
            }
            finally
            {
                if (singleDoc.IsValidObject)
                {
                    singleDoc.Close(false);
                }
            }

            // 2. Multi-location unit model for unshared fallback tests
            Document multiDoc = App.NewProjectDocument(UnitSystem.Imperial);
            try
            {
                multiDoc.ExecuteTransaction("Seed Multi Unit Sites", FailurePipelines.ObserveOnly(), () =>
                {
                    ProjectLocation activeLoc = multiDoc.ActiveProjectLocation;
                    Assert.IsNotNull(activeLoc, "New project document must have an ActiveProjectLocation.");

                    ProjectLocation floor2Loc = activeLoc.Duplicate("Floor 2");
                    floor2Loc.SetProjectPosition(XYZ.Zero, CreateProjectPosition(multiDoc, 0.0, 0.0, 0.0, 0.0));

                    ProjectLocation floor3Loc = activeLoc.Duplicate("Floor 3");
                    floor3Loc.SetProjectPosition(XYZ.Zero, CreateProjectPosition(multiDoc, 10.0, 20.0, 30.0, 0.0));
                });

                multiDoc.SaveAs(_multiSiteUnitModelPath);
            }
            finally
            {
                if (multiDoc.IsValidObject)
                {
                    multiDoc.Close(false);
                }
            }
        }

        protected override void OnFixtureTearDown()
        {
            if (!string.IsNullOrWhiteSpace(_fixtureTempDir) && Directory.Exists(_fixtureTempDir))
            {
                try
                {
                    Directory.Delete(_fixtureTempDir, recursive: true);
                }
                catch
                {
                    // Best-effort fixture temp cleanup
                }
            }

            _fixtureTempDir = null;
            _singleSiteUnitModelPath = null;
            _multiSiteUnitModelPath = null;
        }

        [Test]
        public void Constructor_NullDoc_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new RevitSharedCoordinatesAdapter(null!));
        }

        [Test]
        public void FromProjectPosition_PositiveNonZeroAngleAndOffsets_MatchesLiveProjectLocationGetTotalTransform()
        {
            WithRolledBackDocument(doc =>
            {
                VerifyProjectPositionParity(
                    doc,
                    siteName: "Parity_Positive_Acute",
                    eastWest: 125.5,
                    northSouth: -42.25,
                    elevation: 37.75,
                    angleRadians: Math.PI / 6.0);

                VerifyProjectPositionParity(
                    doc,
                    siteName: "Parity_Positive_Obtuse",
                    eastWest: 45.0,
                    northSouth: 310.75,
                    elevation: 18.5,
                    angleRadians: 2.0 * Math.PI / 3.0);
            });
        }

        [Test]
        public void FromProjectPosition_NegativeNonZeroAngleAndOffsets_MatchesLiveProjectLocationGetTotalTransform()
        {
            WithRolledBackDocument(doc =>
            {
                VerifyProjectPositionParity(
                    doc,
                    siteName: "Parity_Negative_Acute",
                    eastWest: -80.25,
                    northSouth: 210.5,
                    elevation: -14.75,
                    angleRadians: -Math.PI / 3.0);

                VerifyProjectPositionParity(
                    doc,
                    siteName: "Parity_Negative_Obtuse",
                    eastWest: -240.5,
                    northSouth: -95.0,
                    elevation: 62.25,
                    angleRadians: -3.0 * Math.PI / 4.0);
            });
        }

        [Test]
        public void GetActiveModelLevels_ReturnsLevelsOrderedByElevation()
        {
            WithRolledBackDocument(doc =>
            {
                doc.ExecuteTransaction("Seed Levels", FailurePipelines.ObserveOnly(), () =>
                {
                    Level low = Level.Create(doc, -10.0);
                    low.Name = "Adapter_Basement";

                    Level high = Level.Create(doc, 30.0);
                    high.Name = "Adapter_Roof";

                    Level mid = Level.Create(doc, 5.0);
                    mid.Name = "Adapter_Mezzanine";
                });

                var adapter = new RevitSharedCoordinatesAdapter(doc);
                var levels = adapter.GetActiveModelLevels();

                Assert.IsTrue(levels.Count >= 3);
                for (int i = 1; i < levels.Count; i++)
                {
                    Assert.IsTrue(levels[i].Elevation >= levels[i - 1].Elevation, "Levels must be ordered ascending by elevation.");
                }

                int basementIdx = IndexOfLevel(levels, "Adapter_Basement");
                int mezzIdx = IndexOfLevel(levels, "Adapter_Mezzanine");
                int roofIdx = IndexOfLevel(levels, "Adapter_Roof");
                Assert.IsTrue(basementIdx >= 0 && mezzIdx > basementIdx && roofIdx > mezzIdx);
            });
        }

        [Test]
        public void GetLinkedModelLevels_InvalidOrNonExistentId_ReturnsEmptyList()
        {
            Document doc = EnsureBaselineDocument();
            var adapter = new RevitSharedCoordinatesAdapter(doc);

            Assert.IsEmpty(adapter.GetLinkedModelLevels("not-a-number"));
            Assert.IsEmpty(adapter.GetLinkedModelLevels("99999999"));
        }

        [Test]
        public void SyncSharedSites_NullRequestOrMissingReferenceDatum_ThrowsExpectedException()
        {
            Document doc = EnsureBaselineDocument();
            var adapter = new RevitSharedCoordinatesAdapter(doc);

            Assert.Throws<ArgumentNullException>(() => adapter.SyncSharedSites(null!));
            Assert.Throws<ArgumentException>(() => adapter.SyncSharedSites(new SyncSharedSitesRequest { ReferenceDatumLevel = null! }));
        }

        [Test]
        public void SyncSharedSites_UpdatesExistingAndCreatesNewSites_PreservingEastWestNorthSouthAndAngle()
        {
            WithRolledBackDocument(doc =>
            {
                ProjectLocation? existingLoc = null;
                doc.ExecuteTransaction("Seed Existing Site", FailurePipelines.ObserveOnly(), () =>
                {
                    existingLoc = doc.ActiveProjectLocation.Duplicate("Level 1");
                    existingLoc.SetProjectPosition(XYZ.Zero, CreateProjectPosition(doc, 100.0, 200.0, 5.0, 0.25));
                });

                var adapter = new RevitSharedCoordinatesAdapter(doc);
                var request = new SyncSharedSitesRequest
                {
                    ReferenceDatumLevel = new LevelItem
                    {
                        Name = "Reference Datum",
                        Elevation = 10.0,
                        SharedElevation = 10.0
                    },
                    TargetSites = new List<SharedSiteSyncTarget>
                    {
                        new SharedSiteSyncTarget { LevelName = "Level 1", SharedElevation = 100.0, IsChecked = true },
                        new SharedSiteSyncTarget { LevelName = "Level 2", SharedElevation = 150.0, IsChecked = true },
                        new SharedSiteSyncTarget { LevelName = "Level 3", SharedElevation = 200.0, IsChecked = false }
                    }
                };

                var summary = adapter.SyncSharedSites(request);

                Assert.IsEmpty(summary.ErrorMessages);
                Assert.AreEqual(1, summary.UpdatedCount);
                Assert.AreEqual(1, summary.CreatedCount);
                CollectionAssert.Contains(summary.UpdatedSiteNames, "Level 1");
                CollectionAssert.Contains(summary.CreatedSiteNames, "Level 2");

                ProjectPosition updatedPos = existingLoc!.GetProjectPosition(XYZ.Zero);
                Assert.AreEqual(90.0, updatedPos.Elevation, 1e-6);
                Assert.AreEqual(100.0, updatedPos.EastWest, 1e-6);
                Assert.AreEqual(200.0, updatedPos.NorthSouth, 1e-6);
                Assert.AreEqual(0.25, updatedPos.Angle, 1e-6);

                var siteNames = adapter.GetExistingSharedSiteNames();
                CollectionAssert.Contains(siteNames, "Level 2");
                CollectionAssert.DoesNotContain(siteNames, "Level 3");
            });
        }

        [Test]
        public void SyncSharedSites_OneSiteFailsCommit_OtherSiteCommits_AssimilatesGroup()
        {
            WithRolledBackDocument(doc =>
            {
                var adapter = new RevitSharedCoordinatesAdapter(doc);
                var request = new SyncSharedSitesRequest
                {
                    ReferenceDatumLevel = new LevelItem { Name = "Ref", Elevation = 10.0 },
                    TargetSites = new List<SharedSiteSyncTarget>
                    {
                        new SharedSiteSyncTarget { LevelName = "Failing_NaN_Site", SharedElevation = double.NaN, IsChecked = true },
                        new SharedSiteSyncTarget { LevelName = "Valid_Synced_Site", SharedElevation = 150.0, IsChecked = true }
                    }
                };

                var summary = adapter.SyncSharedSites(request);

                Assert.AreEqual(1, summary.CreatedCount);
                CollectionAssert.Contains(summary.CreatedSiteNames, "Valid_Synced_Site");
                CollectionAssert.DoesNotContain(summary.CreatedSiteNames, "Failing_NaN_Site");
                Assert.IsNotEmpty(summary.ErrorMessages, "Failing site creation error must be surfaced in ErrorMessages.");

                var siteNames = adapter.GetExistingSharedSiteNames();
                CollectionAssert.Contains(siteNames, "Valid_Synced_Site");
                CollectionAssert.DoesNotContain(siteNames, "Failing_NaN_Site");
                Assert.IsFalse(doc.IsModifiable);
            });
        }

        [Test]
        public void SyncSharedSites_ZeroSitesCommit_RollsBackBatchGroup()
        {
            WithRolledBackDocument(doc =>
            {
                var adapter = new RevitSharedCoordinatesAdapter(doc);
                int siteCountBefore = adapter.GetExistingSharedSiteNames().Count;

                var request = new SyncSharedSitesRequest
                {
                    ReferenceDatumLevel = new LevelItem { Name = "Ref", Elevation = 10.0 },
                    TargetSites = new List<SharedSiteSyncTarget>
                    {
                        new SharedSiteSyncTarget { LevelName = "Failing_NaN_Only_Site", SharedElevation = double.NaN, IsChecked = true }
                    }
                };

                var summary = adapter.SyncSharedSites(request);

                Assert.AreEqual(0, summary.CreatedCount);
                Assert.AreEqual(0, summary.UpdatedCount);
                Assert.IsNotEmpty(summary.ErrorMessages);
                Assert.AreEqual(siteCountBefore, adapter.GetExistingSharedSiteNames().Count);
                Assert.IsFalse(doc.IsModifiable);
            });
        }

        [Test]
        public void ExecuteCommittedBatchGroup_FuncBool_SurfacesWarningsOnBothCommitAndRollback()
        {
            WithRolledBackDocument(doc =>
            {
                Level level = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .First();

                // 1. Predicate returns false -> group rolls back, yet recorded warnings surface cleanly
                FailureProcessingReport? rollbackReport = null;
                bool assimilatedOnFalse = doc.ExecuteCommittedBatchGroup("Predicate Rollback With Warning", () =>
                {
                    CompositeFailuresPreprocessor preprocessor = FailurePipelines.DeleteAllWarningsRecorded(out FailureProcessingReport report);
                    preprocessor.SetContext("SyncSite:RollbackWarn");
                    rollbackReport = report;

                    doc.ExecuteTransaction("Create Site And Post Warning", preprocessor, () =>
                    {
                        doc.ActiveProjectLocation.Duplicate("RolledBackWarnSite");
                        doc.PostFailure(new FailureMessage(BuiltInFailures.RoomFailures.RoomNotEnclosed));
                    });

                    return false;
                });

                Assert.IsFalse(assimilatedOnFalse);
                Assert.IsNotNull(rollbackReport);
                Assert.IsTrue(rollbackReport!.WarningTripped, "DeleteAllWarningsRecorded must capture warning before rollback.");
                Assert.IsTrue(rollbackReport.Records.Any(r => r.ContextTag == "SyncSite:RollbackWarn" && r.ActionTaken == FailureHandlingAction.Deleted));
                CollectionAssert.DoesNotContain(
                    new RevitSharedCoordinatesAdapter(doc).GetExistingSharedSiteNames(),
                    "RolledBackWarnSite",
                    "Returning false must roll back inner committed site creation.");

                // 2. Predicate returns true -> group assimilates, site persists, warnings surface
                FailureProcessingReport? commitReport = null;
                bool assimilatedOnTrue = doc.ExecuteCommittedBatchGroup("Predicate Commit With Warning", () =>
                {
                    CompositeFailuresPreprocessor preprocessor = FailurePipelines.DeleteAllWarningsRecorded(out FailureProcessingReport report);
                    preprocessor.SetContext("SyncSite:CommitWarn");
                    commitReport = report;

                    doc.ExecuteTransaction("Create Room And Post Warning", preprocessor, () =>
                    {
                        doc.Create.NewRoom(level, new UV(0, 0));
                        doc.ActiveProjectLocation.Duplicate("CommittedWarnSite");
                    });

                    return true;
                });

                Assert.IsTrue(assimilatedOnTrue);
                Assert.IsNotNull(commitReport);
                Assert.IsTrue(commitReport!.WarningTripped);
                Assert.IsTrue(commitReport.Records.Any(r => r.ContextTag == "SyncSite:CommitWarn" && r.ActionTaken == FailureHandlingAction.Deleted));
                CollectionAssert.Contains(
                    new RevitSharedCoordinatesAdapter(doc).GetExistingSharedSiteNames(),
                    "CommittedWarnSite");
            });
        }

        [Test]
        public void GetAssignedSiteName_GeoLocationAssignedInLinkDoc_ResolvesSiteName()
        {
            WithRolledBackDocument(doc =>
            {
                RevitLinkInstance linkInst = CreateLinkedInstance(doc, _singleSiteUnitModelPath!);
                doc.ExecuteTransaction("Acquire Shared Coordinates", FailurePipelines.ObserveOnly(), () =>
                {
                    doc.AcquireCoordinates(linkInst.Id);
                });

                var adapter = new RevitSharedCoordinatesAdapter(doc);
                Document? linkDoc = linkInst.GetLinkDocument();
                Assert.IsNotNull(linkDoc, "Linked document must be loaded.");

                string? resolved = adapter.GetAssignedSiteName(linkInst, linkDoc);
                Assert.AreEqual(_singleSiteName, resolved);
            });
        }

        [Test]
        public void GetAssignedSiteName_GeoLocationAssignedWithNullLinkDoc_ResolvesSiteNameFromHostOrValueString()
        {
            WithRolledBackDocument(doc =>
            {
                RevitLinkInstance linkInst = CreateLinkedInstance(doc, _singleSiteUnitModelPath!);
                doc.ExecuteTransaction("Acquire Shared Coordinates", FailurePipelines.ObserveOnly(), () =>
                {
                    doc.AcquireCoordinates(linkInst.Id);
                });

                var adapter = new RevitSharedCoordinatesAdapter(doc);
                string? resolved = adapter.GetAssignedSiteName(linkInst, null);
                Assert.AreEqual(_singleSiteName, resolved);
            });
        }

        [Test]
        public void GetAssignedSiteName_GeoLocationNotShared_FallsBackToInstanceNameLocation()
        {
            WithRolledBackDocument(doc =>
            {
                RevitLinkInstance linkInst = CreateLinkedInstance(doc, _multiSiteUnitModelPath!);
                doc.ExecuteTransaction("Set Instance Name Location Fallback", FailurePipelines.ObserveOnly(), () =>
                {
                    SetLinkInstanceMark(linkInst, "1 : location Floor 3");
                });

                Parameter? geoParam = linkInst.get_Parameter(BuiltInParameter.GEO_LOCATION);
                Assert.IsTrue(
                    geoParam == null ||
                    geoParam.AsElementId() == ElementId.InvalidElementId ||
                    (geoParam.AsValueString()?.IndexOf("<Not Shared>", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0,
                    "Unshared link instance must not report a shared GEO_LOCATION assignment.");

                var adapter = new RevitSharedCoordinatesAdapter(doc);
                string? resolved = adapter.GetAssignedSiteName(linkInst, linkInst.GetLinkDocument());
                Assert.AreEqual("Floor 3", resolved);
            });
        }

        [Test]
        public void GetLinkRepositionGroups_MultiStepResolution_CorrectlyClassifiesInstances()
        {
            WithRolledBackDocument(doc =>
            {
                ElementId linkTypeId = CreateLinkedType(doc, _singleSiteUnitModelPath!);
                RevitLinkInstance inst1 = null!;
                RevitLinkInstance inst2 = null!;
                RevitLinkInstance inst3 = null!;

                // 1. Place Instance 1 and share coordinates via AcquireCoordinates (GEO_LOCATION -> _singleSiteName)
                doc.ExecuteTransaction("Acquire Coordinates For Instance 1", FailurePipelines.ObserveOnly(), () =>
                {
                    inst1 = RevitLinkInstance.Create(doc, linkTypeId);
                    doc.AcquireCoordinates(inst1.Id);
                });

                // 2. Shift host ActiveProjectLocation so Instance 1 becomes out of sync without mutating link site positions,
                //    then place Instance 2 (fallback " : location ") aligned to the updated host transform, and Instance 3 (unassigned).
                doc.ExecuteTransaction("Shift Host Site And Place Remaining Instances", FailurePipelines.ObserveOnly(), () =>
                {
                    doc.ActiveProjectLocation.SetProjectPosition(
                        XYZ.Zero,
                        CreateProjectPosition(doc, 100.0, 200.0, 50.0, 0.0));

                    inst2 = RevitLinkInstance.Create(doc, linkTypeId);
                    SetLinkInstanceMark(inst2, $"2 : location {_singleSiteName}");

                    Document? linkDoc2 = inst2.GetLinkDocument();
                    Assert.IsNotNull(linkDoc2);
                    ProjectLocation linkLoc2 = new FilteredElementCollector(linkDoc2)
                        .OfClass(typeof(ProjectLocation))
                        .Cast<ProjectLocation>()
                        .First(loc => string.Equals(loc.Name, _singleSiteName, StringComparison.OrdinalIgnoreCase));

                    RigidTransform3D expected2 = SharedCoordinatesMath.ComputeExpectedTransform(
                        ToRigidTransform3D(doc.ActiveProjectLocation.GetTotalTransform()),
                        ToRigidTransform3D(linkLoc2.GetTotalTransform()));
                    XYZ delta2 = new XYZ(expected2.Origin.X, expected2.Origin.Y, expected2.Origin.Z) - inst2.GetTransform().Origin;
                    if (delta2.GetLength() > doc.Application.ShortCurveTolerance)
                    {
                        ElementTransformUtils.MoveElement(doc, inst2.Id, delta2);
                    }

                    inst3 = RevitLinkInstance.Create(doc, linkTypeId);
                    SetLinkInstanceMark(inst3, "3");
                });

                var adapter = new RevitSharedCoordinatesAdapter(doc);
                var groups = adapter.GetLinkRepositionGroups();

                Assert.AreEqual(1, groups.Count);
                var group = groups[0];
                Assert.AreEqual(3, group.TotalInstanceCount);
                Assert.AreEqual(1, group.OutOfSyncCount);
                Assert.AreEqual(1, group.UnassignedCount);

                var summary = adapter.RepositionLinks(new RepositionLinksRequest
                {
                    SelectedTypeIds = new List<string> { linkTypeId.ToString() }
                });

                Assert.IsFalse(
                    summary.ErrorMessages.Any(m => !m.StartsWith("[Warning]", StringComparison.OrdinalIgnoreCase)),
                    $"Unexpected RepositionLinks error(s): {string.Join("; ", summary.ErrorMessages)}");
                Assert.AreEqual(1, summary.RepositionedCount);
                Assert.AreEqual(1, summary.AlreadyAlignedCount);
                Assert.AreEqual(1, summary.SkippedCount);
                CollectionAssert.Contains(summary.RepositionedInstanceNames, inst1.Name);
                CollectionAssert.Contains(summary.SkippedInstanceNames, inst3.Name);
            });
        }

        private static RigidTransform3D ToRigidTransform3D(Transform transform)
        {
            return new RigidTransform3D(
                new Vector3D(transform.Origin.X, transform.Origin.Y, transform.Origin.Z),
                new Vector3D(transform.BasisX.X, transform.BasisX.Y, transform.BasisX.Z),
                new Vector3D(transform.BasisY.X, transform.BasisY.Y, transform.BasisY.Z),
                new Vector3D(transform.BasisZ.X, transform.BasisZ.Y, transform.BasisZ.Z));
        }

        private static ElementId CreateLinkedType(Document doc, string modelFilePath)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(modelFilePath));
            ElementId linkTypeId = ElementId.InvalidElementId;

            doc.ExecuteTransaction("Load Link Type", FailurePipelines.ObserveOnly(), () =>
            {
                ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(modelFilePath);
                var loadResult = RevitLinkType.Create(doc, modelPath, new RevitLinkOptions(false));
                linkTypeId = loadResult.ElementId;
            });

            Assert.AreNotEqual(ElementId.InvalidElementId, linkTypeId);
            return linkTypeId;
        }

        private static RevitLinkInstance CreateLinkedInstance(Document doc, string modelFilePath)
        {
            ElementId linkTypeId = CreateLinkedType(doc, modelFilePath);
            RevitLinkInstance? linkInst = null;

            doc.ExecuteTransaction("Place Link Instance", FailurePipelines.ObserveOnly(), () =>
            {
                linkInst = RevitLinkInstance.Create(doc, linkTypeId);
            });

            Assert.IsNotNull(linkInst);
            return linkInst!;
        }

        private static void SetLinkInstanceMark(RevitLinkInstance linkInstance, string value)
        {
            Parameter? nameParam = linkInstance.get_Parameter(BuiltInParameter.RVT_LINK_INSTANCE_NAME);
            Assert.IsNotNull(nameParam, "Expected BuiltInParameter.RVT_LINK_INSTANCE_NAME on RevitLinkInstance.");
            Assert.IsFalse(nameParam!.IsReadOnly, "Expected RVT_LINK_INSTANCE_NAME to be writable.");
            nameParam.Set(value);
        }

        private static int IndexOfLevel(IReadOnlyList<LevelItem> levels, string name)
        {
            for (int i = 0; i < levels.Count; i++)
            {
                if (string.Equals(levels[i].Name, name, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private void VerifyProjectPositionParity(
            Document doc,
            string siteName,
            double eastWest,
            double northSouth,
            double elevation,
            double angleRadians)
        {
            ProjectLocation? createdLocation = null;

            doc.ExecuteTransaction($"Create {siteName}", FailurePipelines.ObserveOnly(), () =>
            {
                ProjectLocation activeLoc = doc.ActiveProjectLocation;
                Assert.IsNotNull(activeLoc, "Baseline document must expose ActiveProjectLocation.");

                createdLocation = activeLoc.Duplicate(siteName);
                ProjectPosition pos = CreateProjectPosition(doc, eastWest, northSouth, elevation, angleRadians);
                createdLocation.SetProjectPosition(XYZ.Zero, pos);
            });

            Assert.IsNotNull(createdLocation, $"Failed to create ProjectLocation '{siteName}'.");

            ProjectPosition actualPosition = createdLocation!.GetProjectPosition(XYZ.Zero);
            Transform actualTotal = createdLocation.GetTotalTransform();

            RigidTransform3D computedFromInputs = RigidTransform3D.FromProjectPosition(
                eastWest,
                northSouth,
                elevation,
                angleRadians);
            RigidTransform3D computedFromRevitPosition = RigidTransform3D.FromProjectPosition(
                actualPosition.EastWest,
                actualPosition.NorthSouth,
                actualPosition.Elevation,
                actualPosition.Angle);

            AssertTransformParity(computedFromInputs, actualTotal, 1e-6, $"{siteName} (input parameters)");
            AssertTransformParity(computedFromRevitPosition, actualTotal, 1e-6, $"{siteName} (GetProjectPosition(XYZ.Zero))");
        }

        private static ProjectPosition CreateProjectPosition(
            Document doc,
            double eastWest,
            double northSouth,
            double elevation,
            double angleRadians)
        {
#if REVIT2023
            return doc.Application.Create.NewProjectPosition(eastWest, northSouth, elevation, angleRadians);
#else
            return new ProjectPosition(eastWest, northSouth, elevation, angleRadians);
#endif
        }

        private static void AssertTransformParity(
            RigidTransform3D expected,
            Transform actual,
            double tolerance,
            string context)
        {
            Assert.AreEqual(expected.Origin.X, actual.Origin.X, tolerance, $"{context}: Origin.X mismatch");
            Assert.AreEqual(expected.Origin.Y, actual.Origin.Y, tolerance, $"{context}: Origin.Y mismatch");
            Assert.AreEqual(expected.Origin.Z, actual.Origin.Z, tolerance, $"{context}: Origin.Z mismatch");

            Assert.AreEqual(expected.BasisX.X, actual.BasisX.X, tolerance, $"{context}: BasisX.X mismatch");
            Assert.AreEqual(expected.BasisX.Y, actual.BasisX.Y, tolerance, $"{context}: BasisX.Y mismatch");
            Assert.AreEqual(expected.BasisX.Z, actual.BasisX.Z, tolerance, $"{context}: BasisX.Z mismatch");

            Assert.AreEqual(expected.BasisY.X, actual.BasisY.X, tolerance, $"{context}: BasisY.X mismatch");
            Assert.AreEqual(expected.BasisY.Y, actual.BasisY.Y, tolerance, $"{context}: BasisY.Y mismatch");
            Assert.AreEqual(expected.BasisY.Z, actual.BasisY.Z, tolerance, $"{context}: BasisY.Z mismatch");

            Assert.AreEqual(expected.BasisZ.X, actual.BasisZ.X, tolerance, $"{context}: BasisZ.X mismatch");
            Assert.AreEqual(expected.BasisZ.Y, actual.BasisZ.Y, tolerance, $"{context}: BasisZ.Y mismatch");
            Assert.AreEqual(expected.BasisZ.Z, actual.BasisZ.Z, tolerance, $"{context}: BasisZ.Z mismatch");
        }
    }
}
