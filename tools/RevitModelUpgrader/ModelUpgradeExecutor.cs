using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace Synthetic.Tools.ModelUpgrader
{
    /// <summary>
    /// Executes standalone <c>.rvt</c>, <c>.rte</c>, and <c>.rfa</c> model upgrades inside Revit
    /// with <c>BasicFileInfo</c>-safe <see cref="OpenOptions"/>, dialog suppression, explicit
    /// <c>FailuresProcessing</c> reporting, compacted temporary staging, open-document leak checks,
    /// post-close format verification, and atomic file replacement.
    /// </summary>
    internal static class ModelUpgradeExecutor
    {
        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".rvt",
            ".rte",
            ".rfa"
        };

        public static FileUpgradeResult UpgradeFile(UIApplication uiapp, UpgradeJobSpec job, string expectedTargetVersion)
        {
            if (uiapp == null) throw new ArgumentNullException(nameof(uiapp));
            if (job == null) throw new ArgumentNullException(nameof(job));
            if (string.IsNullOrWhiteSpace(expectedTargetVersion))
            {
                throw new ArgumentException("Target version cannot be empty.", nameof(expectedTargetVersion));
            }

            Application app = uiapp.Application;
            int initialDocCount = app.Documents.Size;

            var result = new FileUpgradeResult
            {
                SourcePath = Path.GetFullPath(job.SourcePath),
                TargetPath = Path.GetFullPath(job.TargetPath),
                Audit = job.Audit,
                FileExtension = Path.GetExtension(job.SourcePath) ?? string.Empty
            };

            string? stagingDir = null;
            Document? openedDoc = null;
            bool handlersAttached = false;

            void OnDialogBoxShowing(object? sender, DialogBoxShowingEventArgs e)
            {
                HandleDialogBoxShowing(e, result);
            }

            void OnFailuresProcessing(object? sender, FailuresProcessingEventArgs e)
            {
                HandleFailuresProcessing(e, result);
            }

            try
            {
                if (!File.Exists(result.SourcePath))
                {
                    throw new FileNotFoundException($"Source model file not found: '{result.SourcePath}'.", result.SourcePath);
                }

                if (!SupportedExtensions.Contains(result.FileExtension))
                {
                    throw new InvalidOperationException(
                        $"Unsupported model extension '{result.FileExtension}' for '{result.SourcePath}'. Expected .rvt, .rte, or .rfa.");
                }

                bool isRvt = string.Equals(result.FileExtension, ".rvt", StringComparison.OrdinalIgnoreCase);

                bool sourceIsWorkshared;
                using (BasicFileInfo sourceInfo = BasicFileInfo.Extract(result.SourcePath))
                {
                    result.SourceFormat = (sourceInfo.Format ?? string.Empty).Trim();
                    sourceIsWorkshared = sourceInfo.IsWorkshared;
                    result.WasWorkshared = sourceIsWorkshared;

                    if (sourceInfo.IsSavedInLaterVersion)
                    {
                        throw new InvalidOperationException(
                            $"Cannot upgrade '{result.SourcePath}' (Format '{result.SourceFormat}'): file was saved in a newer Revit version than {expectedTargetVersion}.");
                    }
                }

                var openOptions = new OpenOptions
                {
                    Audit = job.Audit
                };
                if (isRvt && sourceIsWorkshared)
                {
                    openOptions.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;
                }

                AtomicStagingHelper.CreateStagingLocation(result.TargetPath, out stagingDir, out string stagingFilePath);

                uiapp.DialogBoxShowing += OnDialogBoxShowing;
                app.FailuresProcessing += OnFailuresProcessing;
                handlersAttached = true;

                ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(result.SourcePath);
                openedDoc = app.OpenDocumentFile(modelPath, openOptions);
                if (openedDoc == null || !openedDoc.IsValidObject)
                {
                    throw new InvalidOperationException($"Revit returned an invalid document for '{result.SourcePath}'.");
                }

                if (job.MinLoadableFamilies > 0)
                {
                    int loadableFamilies = new FilteredElementCollector(openedDoc)
                        .OfClass(typeof(Family))
                        .Cast<Family>()
                        .Count(f => !f.IsInPlace);
                    result.LoadableFamilyCount = loadableFamilies;

                    if (loadableFamilies < job.MinLoadableFamilies)
                    {
                        throw new InvalidOperationException(
                            $"Expected at least {job.MinLoadableFamilies} loadable families in '{result.SourcePath}', found {loadableFamilies}.");
                    }
                }

                var saveOptions = new SaveAsOptions
                {
                    Compact = true,
                    OverwriteExistingFile = true,
                    MaximumBackups = 1
                };
                if (isRvt && openedDoc.IsWorkshared)
                {
                    var worksharingOptions = new WorksharingSaveAsOptions
                    {
                        SaveAsCentral = true,
                        ClearTransmitted = true
                    };
                    saveOptions.SetWorksharingOptions(worksharingOptions);
                }

                openedDoc.SaveAs(stagingFilePath, saveOptions);

                CloseDocumentAndAssertZeroLeaks(app, ref openedDoc, initialDocCount, result.SourcePath);

                if (handlersAttached)
                {
                    uiapp.DialogBoxShowing -= OnDialogBoxShowing;
                    app.FailuresProcessing -= OnFailuresProcessing;
                    handlersAttached = false;
                }

                if (result.ErrorCount > 0)
                {
                    throw new InvalidOperationException(
                        $"Upgrade recorded {result.ErrorCount} Revit failure error(s) for '{result.SourcePath}'; aborting before target replacement.");
                }

                if (!File.Exists(stagingFilePath))
                {
                    throw new FileNotFoundException($"Compacted staging output missing after SaveAs: '{stagingFilePath}'.", stagingFilePath);
                }

                using (BasicFileInfo stagedInfo = BasicFileInfo.Extract(stagingFilePath))
                {
                    result.VerifiedFormat = (stagedInfo.Format ?? string.Empty).Trim();
                }

                if (!string.Equals(result.VerifiedFormat, expectedTargetVersion.Trim(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Verified BasicFileInfo.Format '{result.VerifiedFormat}' did not match target Revit version '{expectedTargetVersion}' for '{stagingFilePath}'.");
                }

                AtomicStagingHelper.AtomicMoveIntoPlace(stagingFilePath, result.TargetPath);
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }
            finally
            {
                if (handlersAttached)
                {
                    uiapp.DialogBoxShowing -= OnDialogBoxShowing;
                    app.FailuresProcessing -= OnFailuresProcessing;
                }

                if (openedDoc != null)
                {
                    try
                    {
                        CloseDocumentAndAssertZeroLeaks(app, ref openedDoc, initialDocCount, result.SourcePath);
                    }
                    catch (Exception closeEx)
                    {
                        result.Success = false;
                        result.ErrorMessage = string.IsNullOrEmpty(result.ErrorMessage)
                            ? closeEx.Message
                            : $"{result.ErrorMessage} | Cleanup error: {closeEx.Message}";
                    }
                }

                AtomicStagingHelper.TryDeleteDirectory(stagingDir);
            }

            return result;
        }

        private static void CloseDocumentAndAssertZeroLeaks(
            Application app,
            ref Document? doc,
            int initialDocCount,
            string sourcePath)
        {
            Document? current = doc;
            doc = null;
            if (current != null && current.IsValidObject)
            {
                current.Close(false);
            }

            int actualCount = app.Documents.Size;
            if (actualCount != initialDocCount)
            {
                throw new InvalidOperationException(
                    $"Open Revit document leak after upgrading '{sourcePath}': expected {initialDocCount}, actual {actualCount}.");
            }
        }

        private static void HandleDialogBoxShowing(DialogBoxShowingEventArgs e, FileUpgradeResult result)
        {
            string dialogId = e.DialogId ?? string.Empty;
            string message = e is TaskDialogShowingEventArgs td ? (td.Message ?? string.Empty) : string.Empty;

            int[] candidateCodes = GetCandidateOverrideCodes(dialogId);
            int chosenCode = candidateCodes[0];
            bool accepted = false;

            for (int i = 0; i < candidateCodes.Length; i++)
            {
                if (e.OverrideResult(candidateCodes[i]))
                {
                    chosenCode = candidateCodes[i];
                    accepted = true;
                    break;
                }
            }

            result.SuppressedDialogs.Add(new RecordedDialog
            {
                DialogId = dialogId,
                Message = message,
                OverrideResultCode = chosenCode,
                OverrideAccepted = accepted
            });
        }

        private static int[] GetCandidateOverrideCodes(string dialogId)
        {
            if (string.Equals(dialogId, "TaskDialog_Unresolved_References", StringComparison.OrdinalIgnoreCase))
            {
                return new[]
                {
                    (int)TaskDialogResult.CommandLink2,
                    (int)TaskDialogResult.Close,
                    (int)TaskDialogResult.Cancel
                };
            }

            if (string.Equals(dialogId, "TaskDialog_Missing_Third_Party_Updater", StringComparison.OrdinalIgnoreCase))
            {
                return new[]
                {
                    (int)TaskDialogResult.CommandLink1,
                    (int)TaskDialogResult.Ok,
                    (int)TaskDialogResult.Close
                };
            }

            return new[]
            {
                (int)TaskDialogResult.Ok,
                (int)TaskDialogResult.Close,
                (int)TaskDialogResult.Yes,
                (int)TaskDialogResult.CommandLink1,
                (int)TaskDialogResult.Cancel
            };
        }

        private static void HandleFailuresProcessing(FailuresProcessingEventArgs e, FileUpgradeResult result)
        {
            FailuresAccessor accessor = e.GetFailuresAccessor();
            if (accessor == null)
            {
                return;
            }

            IList<FailureMessageAccessor>? messages = accessor.GetFailureMessages();
            if (messages == null || messages.Count == 0)
            {
                return;
            }

            foreach (FailureMessageAccessor msg in messages)
            {
                FailureSeverity severity = msg.GetSeverity();
                FailureDefinitionId? defId = msg.GetFailureDefinitionId();
                Guid guid = defId != null ? defId.Guid : Guid.Empty;
                string description = msg.GetDescriptionText() ?? string.Empty;

                var failingIds = new List<long>();
                ICollection<ElementId>? elementIds = msg.GetFailingElementIds();
                if (elementIds != null)
                {
                    foreach (ElementId id in elementIds)
                    {
                        if (id == null)
                        {
                            continue;
                        }
#if REVIT2023
                        failingIds.Add(id.IntegerValue);
#else
                        failingIds.Add(id.Value);
#endif
                    }
                }

                string resolutionAction = "Recorded";
                if (severity == FailureSeverity.Warning)
                {
                    result.WarningCount++;
                    accessor.DeleteWarning(msg);
                    resolutionAction = "RecordedAndDeletedWarning";
                }
                else if (severity == FailureSeverity.Error || severity == FailureSeverity.DocumentCorruption)
                {
                    result.ErrorCount++;
                    resolutionAction = "RecordedError";
                }

                result.Failures.Add(new RecordedFailure
                {
                    FailureDefinitionGuid = guid.ToString(),
                    Severity = severity.ToString(),
                    Description = description,
                    FailingElementIds = failingIds,
                    ResolutionAction = resolutionAction
                });
            }
        }
    }
}
