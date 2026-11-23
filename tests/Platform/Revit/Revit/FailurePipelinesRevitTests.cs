using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using NUnit.Framework;
using Synthetic.Platform.Revit;
using SyntheticTests.Helpers;

namespace SyntheticTests.Platform.Revit
{
    [TestFixture]
    [RevitAdapterTest]
    public class FailurePipelinesRevitTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void OneTimeSetup(UIApplication uiapp)
        {
            _uiapp = uiapp;
            TestWorkerJournalMarker.EnsureWritten(uiapp);
        }

        [Test]
        public void ObserveOnly_RecordsNativeOverlapWarning_WithoutDeletingWarning()
        {
            Assert.IsNotNull(_uiapp);
            Document doc = _uiapp!.Application.NewProjectDocument(UnitSystem.Metric);
            try
            {
                Level level = FindFirstLevel(doc);
                FailureProcessingReport? capturedReport = null;
                var phase2ObservedIds = new List<Guid>();

                WithPhase2WarningCaptureCleanup(_uiapp, phase2ObservedIds, () =>
                {
                    doc.ExecuteRolledBackGroup("ObserveOnly Overlap Warning", () =>
                    {
                        CompositeFailuresPreprocessor preprocessor = FailurePipelines.ObserveOnly(out FailureProcessingReport report);
                        capturedReport = report;

                        doc.ExecuteTransaction("Create Overlapping Walls", preprocessor, () =>
                        {
                            CreateOverlappingWalls(doc, level.Id);
                        });
                    });
                });

                Assert.IsNotNull(capturedReport);
                Assert.IsFalse(capturedReport!.WarningTripped, "ObserveOnly must not trip WarningTripped.");
                Assert.IsTrue(
                    capturedReport.Records.Any(r =>
                        r.FailureDefinitionId == BuiltInFailures.OverlapFailures.WallsOverlap.Guid &&
                        r.Severity == FailureSeverityLevel.Warning &&
                        r.ActionTaken == FailureHandlingAction.PassedThrough),
                    "ObserveOnly must record WallsOverlap with PassedThrough action.");
                CollectionAssert.Contains(
                    phase2ObservedIds,
                    BuiltInFailures.OverlapFailures.WallsOverlap.Guid,
                    "ObserveOnly must leave WallsOverlap intact into Revit Phase 2 FailuresProcessing.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void DeleteAllWarningsRecorded_DeletesNativeOverlapWarning_AndSetsWarningTripped()
        {
            Assert.IsNotNull(_uiapp);
            Document doc = _uiapp!.Application.NewProjectDocument(UnitSystem.Metric);
            try
            {
                Level level = FindFirstLevel(doc);
                FailureProcessingReport? capturedReport = null;
                var phase2ObservedIds = new List<Guid>();

                WithPhase2WarningCaptureCleanup(_uiapp, phase2ObservedIds, () =>
                {
                    doc.ExecuteRolledBackGroup("DeleteAllWarningsRecorded Overlap Warning", () =>
                    {
                        CompositeFailuresPreprocessor preprocessor = FailurePipelines.DeleteAllWarningsRecorded(out FailureProcessingReport report);
                        capturedReport = report;

                        doc.ExecuteTransaction("Create Overlapping Walls", preprocessor, () =>
                        {
                            CreateOverlappingWalls(doc, level.Id);
                        });
                    });
                });

                Assert.IsNotNull(capturedReport);
                Assert.IsTrue(capturedReport!.WarningTripped, "DeleteAllWarningsRecorded must set WarningTripped.");
                Assert.IsTrue(
                    capturedReport.Records.Any(r =>
                        r.FailureDefinitionId == BuiltInFailures.OverlapFailures.WallsOverlap.Guid &&
                        r.Severity == FailureSeverityLevel.Warning &&
                        r.ActionTaken == FailureHandlingAction.Deleted),
                    "DeleteAllWarningsRecorded must record WallsOverlap as Deleted.");
                CollectionAssert.DoesNotContain(
                    phase2ObservedIds,
                    BuiltInFailures.OverlapFailures.WallsOverlap.Guid,
                    "DeleteAllWarningsRecorded must have deleted WallsOverlap before Phase 2 FailuresProcessing.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void SuppressConstraints_DeletesNativeGroupConstraintWarning_AndPassesThroughUnmatchedWarnings()
        {
            Assert.IsNotNull(_uiapp);
            Document doc = _uiapp!.Application.NewProjectDocument(UnitSystem.Metric);
            try
            {
                Level level = FindFirstLevel(doc);
                var phase2ObservedIds = new List<Guid>();

                WithPhase2WarningCaptureCleanup(_uiapp, phase2ObservedIds, () =>
                {
                    doc.ExecuteRolledBackGroup("SuppressConstraints Native Provocation", () =>
                    {
                        // 1. Verify unmatched warning passes through without tripping WarningTripped
                        CompositeFailuresPreprocessor unmatchedPreprocessor = FailurePipelines.SuppressConstraints(out FailureProcessingReport unmatchedReport);
                        doc.ExecuteTransaction("Unmatched Overlap Warning", unmatchedPreprocessor, () =>
                        {
                            CreateOverlappingWalls(doc, level.Id);
                        });

                        Assert.IsFalse(unmatchedReport.WarningTripped, "SuppressConstraints must not delete unrelated warnings.");
                        Assert.IsTrue(unmatchedReport.Records.Any(r =>
                            r.FailureDefinitionId == BuiltInFailures.OverlapFailures.WallsOverlap.Guid &&
                            r.ActionTaken == FailureHandlingAction.PassedThrough));
                        CollectionAssert.Contains(
                            phase2ObservedIds,
                            BuiltInFailures.OverlapFailures.WallsOverlap.Guid,
                            "Unmatched warning should reach Phase 2 FailuresProcessing untouched.");

                        // 2. Provoke the group constraint warning via Revit's native FailureMessage posting pipeline
                        // (Revit API does not expose group-edit/member-exclusion APIs to trigger CannotRemoveGroupMemberConstraintWarn
                        // without UI group-edit mode, while doc.PostFailure exercises the real native FailuresAccessor Phase 1/2 pipeline).
                        phase2ObservedIds.Clear();
                        CompositeFailuresPreprocessor constraintPreprocessor = FailurePipelines.SuppressConstraints(out FailureProcessingReport constraintReport);
                        doc.ExecuteTransaction("Post Group Constraint Warning", constraintPreprocessor, () =>
                        {
                            doc.PostFailure(new FailureMessage(BuiltInFailures.GroupFailures.CannotRemoveGroupMemberConstraintWarn));
                        });

                        Assert.IsTrue(constraintReport.WarningTripped, "SuppressConstraints must suppress native group constraint warning and set WarningTripped.");
                        Assert.IsTrue(
                            constraintReport.Records.Any(r =>
                                r.ActionTaken == FailureHandlingAction.Deleted &&
                                r.FailureDefinitionId == BuiltInFailures.GroupFailures.CannotRemoveGroupMemberConstraintWarn.Guid),
                            "SuppressConstraints must record Deleted CannotRemoveGroupMemberConstraintWarn.");
                        Assert.IsEmpty(phase2ObservedIds, "Suppressed constraint warning must not leak to Phase 2 FailuresProcessing.");
                    });
                });
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void FailurePipelineBuilder_OnFailureTargetedRule_DeletesTargetedRoomNotEnclosedWarning()
        {
            Assert.IsNotNull(_uiapp);
            Document doc = _uiapp!.Application.NewProjectDocument(UnitSystem.Metric);
            try
            {
                Level level = FindFirstLevel(doc);
                var phase2ObservedIds = new List<Guid>();

                WithPhase2WarningCaptureCleanup(_uiapp, phase2ObservedIds, () =>
                {
                    doc.ExecuteRolledBackGroup("Targeted RoomNotEnclosed Rule", () =>
                    {
                        CompositeFailuresPreprocessor preprocessor = new FailurePipelineBuilder()
                            .OnFailure(BuiltInFailures.RoomFailures.RoomNotEnclosed)
                            .Build();

                        doc.ExecuteTransaction("Create Unenclosed Room", preprocessor, () =>
                        {
                            doc.Create.NewRoom(level, new UV(0, 0));
                        });

                        Assert.IsTrue(preprocessor.Report.WarningTripped, "Targeted RoomNotEnclosed rule should suppress warning.");
                        Assert.IsTrue(
                            preprocessor.Report.Records.Any(r =>
                                r.FailureDefinitionId == BuiltInFailures.RoomFailures.RoomNotEnclosed.Guid &&
                                r.ActionTaken == FailureHandlingAction.Deleted),
                            "Expected RoomNotEnclosed to be recorded as Deleted.");
                        Assert.IsEmpty(phase2ObservedIds, "Deleted warning should not reach Phase 2.");
                    });
                });
            }
            finally
            {
                doc.Close(false);
            }
        }

        private static void WithPhase2WarningCaptureCleanup(
            UIApplication uiapp,
            List<Guid> phase2ObservedIds,
            Action action)
        {
            EventHandler<FailuresProcessingEventArgs> handler = (_, e) =>
            {
                FailuresAccessor fa = e.GetFailuresAccessor();
                IList<FailureMessageAccessor> messages = fa.GetFailureMessages();
                if (messages == null || messages.Count == 0)
                {
                    return;
                }

                bool deletedAny = false;
                foreach (FailureMessageAccessor msg in messages)
                {
                    FailureDefinitionId? defId = msg.GetFailureDefinitionId();
                    if (defId != null)
                    {
                        phase2ObservedIds.Add(defId.Guid);
                    }

                    if (msg.GetSeverity() == FailureSeverity.Warning)
                    {
                        fa.DeleteWarning(msg);
                        deletedAny = true;
                    }
                    else if (msg.GetSeverity() == FailureSeverity.Error && msg.HasResolutions())
                    {
                        fa.ResolveFailure(msg);
                        deletedAny = true;
                    }
                }

                if (deletedAny)
                {
                    e.SetProcessingResult(FailureProcessingResult.ProceedWithCommit);
                }
            };

            uiapp.Application.FailuresProcessing += handler;
            try
            {
                action();
            }
            finally
            {
                uiapp.Application.FailuresProcessing -= handler;
            }
        }

        private static Level FindFirstLevel(Document doc)
        {
            Level? level = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault();
            Assert.IsNotNull(level, "Metric project template must contain at least one Level.");
            return level!;
        }

        private static void CreateOverlappingWalls(Document doc, ElementId levelId)
        {
            Line line = Line.CreateBound(XYZ.Zero, new XYZ(10, 0, 0));
            Wall.Create(doc, line, levelId, false);
            Wall.Create(doc, line, levelId, false);
        }
    }
}
