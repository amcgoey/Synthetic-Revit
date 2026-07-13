using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Synthetic.Modules.FamilyManagement.Commands;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;

using Synthetic.Shared.RevitAPI;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.FamilyManagement.Utilities;
namespace Synthetic.Modules.FamilyManagement.Commands
{
    /// <summary>
    /// Availability class to ensure CmdTestAuditPurgeJournal is always available, even when no document is open.
    /// </summary>
    public class CmdTestAuditPurgeJournalAvailability : IExternalCommandAvailability
    {
        /// <summary>
        /// Determine if the command is active.
        /// </summary>
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
        {
            return true;
        }
    }

    /// <summary>
    /// Headless integration test command for Audit &amp; Purge.
    /// Opens the test model, measures family file sizes before and after purging,
    /// checks parameter override data integrity, and writes results to commandData.JournalData.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdTestAuditPurgeJournal : IExternalCommand
    {
        /// <summary>
        /// Execute the headless test command.
        /// </summary>
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Autodesk.Revit.ApplicationServices.Application app = uiapp.Application;

            string version = app.VersionNumber;

            // Resolve test model path dynamically
            string assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string dllDir = Path.GetDirectoryName(assemblyPath) ?? string.Empty;
            string repoDir = Path.GetDirectoryName(dllDir) ?? string.Empty;
            string testModelPath = Path.Combine(repoDir, "tests", "test_models", $"TestTemplate{version}.rvt");

            Document? doc = null;
            bool openedByUs = false;

            try
            {
                if (uidoc != null && uidoc.Document != null)
                {
                    doc = uidoc.Document;
                }
                else
                {
                    if (!File.Exists(testModelPath))
                    {
                        message = $"Test model not found at path: {testModelPath}";
                        return Result.Failed;
                    }

                    doc = app.OpenDocumentFile(testModelPath);
                    openedByUs = true;
                }

                if (doc == null)
                {
                    message = "Failed to open the test model.";
                    return Result.Failed;
                }

                app.WriteJournalComment("[TEST_AUDIT_PURGE] CmdTestAuditPurgeJournal execution started.", true);
                app.WriteJournalComment($"[TEST_AUDIT_PURGE] Active Document: {doc.Title} (Path: {doc.PathName})", true);

                // Collect editable, non-inplace families
                IList<Family> families = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .Where(f => f.IsEditable && !f.IsInPlace)
                    .ToList();

                app.WriteJournalComment($"[TEST_AUDIT_PURGE] Total editable families found: {families.Count}", true);

                // Find a family instance with some Comments value, or just the first family instance
                FamilyInstance? targetInstance = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .FirstOrDefault(fi =>
                    {
                        Parameter p = fi.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                        return p != null && !string.IsNullOrEmpty(p.AsString());
                    }) ?? new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilyInstance))
                        .Cast<FamilyInstance>()
                        .FirstOrDefault();

                Family? targetFamily = targetInstance?.Symbol?.Family;
                string originalParamValue = string.Empty;
                if (targetInstance != null)
                {
                    Parameter p = targetInstance.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    originalParamValue = p?.AsString() ?? string.Empty;
                    app.WriteJournalComment($"[TEST_AUDIT_PURGE] Target instance chosen: '{targetInstance.Name}' (ID: {targetInstance.Id}), Family: '{targetFamily?.Name ?? "None"}'", true);
                    app.WriteJournalComment($"[TEST_AUDIT_PURGE] Target initial Comments value: '{originalParamValue}'", true);
                }
                else
                {
                    app.WriteJournalComment("[TEST_AUDIT_PURGE] No family instances found in the active project.", true);
                }

                // Create temp directory for saving families
                string tempDir = Path.Combine(Path.GetTempPath(), "RevitAuditPurgeTest");
                if (!Directory.Exists(tempDir))
                {
                    Directory.CreateDirectory(tempDir);
                }

                app.WriteJournalComment($"[TEST_AUDIT_PURGE] Measuring pre-purge sizes. Temp directory: {tempDir}", true);

                // Measure sizes before
                Dictionary<string, long> sizesBefore = new Dictionary<string, long>();
                foreach (Family family in families)
                {
                    string tempFilePath = Path.Combine(tempDir, family.Name + "_before.rfa");
                    Document? familyDoc = null;
                    try
                    {
                        familyDoc = doc.EditFamily(family);
                        SaveAsOptions saveOptions = new SaveAsOptions { OverwriteExistingFile = true };
                        familyDoc.SaveAs(tempFilePath, saveOptions);
                        long bytes = new FileInfo(tempFilePath).Length;
                        sizesBefore[family.Name] = bytes;
                        app.WriteJournalComment($"[TEST_AUDIT_PURGE] Pre-purge size of family '{family.Name}': {bytes} bytes", true);
                    }
                    catch (Exception ex)
                    {
                        app.WriteJournalComment($"[TEST_AUDIT_PURGE] Warning: Failed to measure pre-purge size of family '{family.Name}': {ex.Message}", true);
                    }
                    finally
                    {
                        if (familyDoc != null)
                        {
                            familyDoc.Close(false);
                            familyDoc.Dispose();
                        }
                    }
                }

                // Execute the Purge and safe load loop synchronously and measure sizes/parameters
                // within a transaction group that is rolled back at the end to keep the document clean.
                Dictionary<string, long> sizesAfter = new Dictionary<string, long>();
                string paramValueAfter = "NOT_FOUND";

                app.WriteJournalComment("[TEST_AUDIT_PURGE] Starting transaction group for Purge & Reload...", true);

                using (TransactionGroup tg = new TransactionGroup(doc, "Purge and Measure"))
                {
                    tg.Start();

                    foreach (Family family in families)
                    {
                        string familyName = family.Name;
                        Document? familyDoc = null;
                        try
                        {
                            app.WriteJournalComment($"[TEST_AUDIT_PURGE] Editing family '{familyName}' for purge...", true);
                            familyDoc = doc.EditFamily(family);

                            // Purge unused elements
#if !REVIT2022
                            bool purged = DocumentUtil.Purge(app, familyDoc);
                            app.WriteJournalComment($"[TEST_AUDIT_PURGE] Purged unused elements in family '{familyName}'. Success: {purged}", true);
#endif

                            // Reload family back into target project document using SafeFamilyLoadOptions
                            app.WriteJournalComment($"[TEST_AUDIT_PURGE] Reloading family '{familyName}' using SafeFamilyLoadOptions...", true);
                            using (Transaction trans = new Transaction(doc, $"Reload Family: {familyName}"))
                            {
                                FailureHandlingOptions options = trans.GetFailureHandlingOptions();
                                options.SetFailuresPreprocessor(new PurgeFailuresPreprocessor());
                                trans.SetFailureHandlingOptions(options);

                                trans.Start();
                                familyDoc.LoadFamily(doc, new SafeFamilyLoadOptions());
                                trans.Commit();
                            }
                            app.WriteJournalComment($"[TEST_AUDIT_PURGE] Reload of family '{familyName}' completed successfully.", true);
                        }
                        catch (Exception ex)
                        {
                            app.WriteJournalComment($"[TEST_AUDIT_PURGE] Error: Failed processing family '{familyName}': {ex.Message}", true);
                        }
                        finally
                        {
                            if (familyDoc != null)
                            {
                                familyDoc.Close(false);
                                familyDoc.Dispose();
                            }
                        }
                    }

                    // Measure sizes after
                    app.WriteJournalComment("[TEST_AUDIT_PURGE] Measuring post-purge sizes...", true);
                    foreach (Family family in families)
                    {
                        if (!sizesBefore.ContainsKey(family.Name)) continue;

                        string tempFilePath = Path.Combine(tempDir, family.Name + "_after.rfa");
                        Document? familyDoc = null;
                        try
                        {
                            familyDoc = doc.EditFamily(family);
                            SaveAsOptions saveOptions = new SaveAsOptions { OverwriteExistingFile = true };
                            familyDoc.SaveAs(tempFilePath, saveOptions);
                            long bytes = new FileInfo(tempFilePath).Length;
                            sizesAfter[family.Name] = bytes;
                            long reduction = sizesBefore[family.Name] - bytes;
                            app.WriteJournalComment($"[TEST_AUDIT_PURGE] Post-purge size of family '{family.Name}': {bytes} bytes (Reduction: {reduction} bytes)", true);
                        }
                        catch (Exception ex)
                        {
                            app.WriteJournalComment($"[TEST_AUDIT_PURGE] Warning: Failed to measure post-purge size of family '{family.Name}': {ex.Message}", true);
                        }
                        finally
                        {
                            if (familyDoc != null)
                            {
                                familyDoc.Close(false);
                                familyDoc.Dispose();
                            }
                        }
                    }

                    // Read parameter value after reloading
                    if (targetFamily != null)
                    {
                        FamilyInstance? updatedInstance = new FilteredElementCollector(doc)
                            .OfClass(typeof(FamilyInstance))
                            .Cast<FamilyInstance>()
                            .FirstOrDefault(fi => fi.Symbol?.Family?.Id == targetFamily.Id);

                        if (updatedInstance != null)
                        {
                            Parameter p = updatedInstance.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                            if (p != null)
                            {
                                paramValueAfter = p.AsString() ?? string.Empty;
                            }
                        }
                    }

                    app.WriteJournalComment($"[TEST_AUDIT_PURGE] Target final Comments value: '{paramValueAfter}'", true);
                    app.WriteJournalComment($"[TEST_AUDIT_PURGE] Parameter preservation check: {(originalParamValue == paramValueAfter ? "PASS" : "FAIL")}", true);

                    // Write results to JournalData map
                    IDictionary<string, string> journalData = commandData.JournalData;
                    journalData.Clear();

                    if (targetFamily != null && sizesBefore.ContainsKey(targetFamily.Name) && sizesAfter.ContainsKey(targetFamily.Name))
                    {
                        journalData.Add("Test_Family1_SizeBefore", sizesBefore[targetFamily.Name].ToString());
                        journalData.Add("Test_Family1_SizeAfter", sizesAfter[targetFamily.Name].ToString());
                        journalData.Add("Test_Family1_ParamValue", paramValueAfter);
                    }
                    else
                    {
                        // Fallback to first available family if targetInstance was missing
                        var firstFamilyName = sizesBefore.Keys.FirstOrDefault();
                        if (firstFamilyName != null && sizesAfter.ContainsKey(firstFamilyName))
                        {
                            journalData.Add("Test_Family1_SizeBefore", sizesBefore[firstFamilyName].ToString());
                            journalData.Add("Test_Family1_SizeAfter", sizesAfter[firstFamilyName].ToString());
                            journalData.Add("Test_Family1_ParamValue", paramValueAfter);
                        }
                    }

                    if (app.IsJournalPlaying())
                    {
                        app.WriteJournalComment("[TEST_AUDIT_PURGE] Rolling back transaction group (Journal playback mode)...", true);
                        tg.RollBack();
                    }
                    else
                    {
                        app.WriteJournalComment("[TEST_AUDIT_PURGE] Committing transaction group (Manual run mode)...", true);
                        tg.Commit();
                    }
                }

                if (openedByUs)
                {
                    doc.Close(false);
                    openedByUs = false;
                }

                if (!app.IsJournalPlaying())
                {
                    string targetName = targetFamily?.Name ?? sizesBefore.Keys.FirstOrDefault() ?? "N/A";
                    string sizeBeforeStr = (targetName != "N/A" && sizesBefore.ContainsKey(targetName)) ? sizesBefore[targetName].ToString() : "N/A";
                    string sizeAfterStr = (targetName != "N/A" && sizesAfter.ContainsKey(targetName)) ? sizesAfter[targetName].ToString() : "N/A";

                    string info = $"Audit & Purge Test Completed:\n\n" +
                                  $"Target Family: {targetName}\n" +
                                  $"Size Before: {sizeBeforeStr} bytes\n" +
                                  $"Size After: {sizeAfterStr} bytes\n" +
                                  $"Param Value (Comments): {paramValueAfter}";
                    Autodesk.Revit.UI.TaskDialog.Show("Audit & Purge Test Results", info);
                }

                app.WriteJournalComment("[TEST_AUDIT_PURGE] CmdTestAuditPurgeJournal execution completed successfully.", true);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                if (app != null)
                {
                    app.WriteJournalComment($"[TEST_AUDIT_PURGE] Critical exception in CmdTestAuditPurgeJournal: {ex.Message}", true);
                }
                return Result.Failed;
            }
            finally
            {
                if (openedByUs && doc != null && doc.IsValidObject)
                {
                    doc.Close(false);
                }
            }
        }
    }
}
