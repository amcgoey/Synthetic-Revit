using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.Models;
using Synthetic.Modules.StandardsManagement.Utilities;
using Synthetic.Shared.RevitAPI;
using Synthetic.Shared.UI;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.Modules.StandardsManagement.Engine
{
    public class StandardsExecutionPipeline : IStandardsExecutionPipeline
    {
        private readonly IStandardSerializationEngine _serializationEngine;
        private readonly IStandardsExportService _exportService;

        public StandardsExecutionPipeline(
            IStandardSerializationEngine serializationEngine,
            IStandardsExportService exportService)
        {
            _serializationEngine = serializationEngine ?? throw new ArgumentNullException(nameof(serializationEngine));
            _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        }

        public StandardsExecutionResult Execute(
            Document doc,
            IEnumerable<StandardsExecutionItem> items,
            StandardsExecutionOptions options,
            IProgress<ProgressState>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (options == null) throw new ArgumentNullException(nameof(options));

            var result = new StandardsExecutionResult
            {
                Items = items.ToList()
            };

            var allResults = new List<SerializationResultModel>();
            var dbResults = new List<SerializationResultModel>();
            bool dbPhaseSucceeded = true;
            string? actualFilePath = null;

            var itemsToEnforce = result.Items.Where(i => i.WillEnforce).ToList();

            if (options.WriteRevitDatabase)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    dbResults = RunRevitDbPhase(doc, itemsToEnforce, options, progress, cancellationToken);
                    allResults.AddRange(dbResults);
                }
                catch (OperationCanceledException)
                {
                    dbPhaseSucceeded = false;
                    foreach (var item in itemsToEnforce)
                    {
                        var r = new SerializationResultModel(item.Model, "Execution cancelled by user.")
                        {
                            OperationTarget = StandardsPipelineConstants.TargetDatabase,
                            Action = StandardsPipelineConstants.ActionCanceled,
                            Message = "Execution cancelled by user."
                        };
                        dbResults.Add(r);
                        allResults.Add(r);
                    }
                }
                catch (Exception ex)
                {
                    dbPhaseSucceeded = false;
                    foreach (var item in itemsToEnforce)
                      {
                        var r = new SerializationResultModel(item.Model, $"Database Write Failed: {ex.Message}", ex)
                        {
                            OperationTarget = StandardsPipelineConstants.TargetDatabase,
                            Action = StandardsPipelineConstants.ActionFailed,
                            Message = ex.Message
                        };
                        dbResults.Add(r);
                        allResults.Add(r);
                    }
                }
            }

            // Phase 2: File saving
            if (options.SaveLocalFiles && dbPhaseSucceeded)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var fileItems = result.Items.Where(i => i.WillSave).ToList();
                    if (fileItems.Count > 0)
                    {
                        var queueItems = new List<QueueItemModel>();
                        var modelMapping = new Dictionary<ObjectModel, ObjectModel>();
                        foreach (var item in fileItems)
                        {
                            var qItem = new QueueItemModel(item.Model, item.WillEnforce, item.WillSave);
                            queueItems.Add(qItem);
                            modelMapping[item.Model] = qItem.Model;
                        }

                        var dbResultsForExport = new List<SerializationResultModel>();
                        foreach (var r in dbResults)
                        {
                            var mappedR = r;
                            if (r.Model != null && modelMapping.TryGetValue(r.Model, out var clonedModel))
                            {
                                mappedR = r.Success
                                    ? new SerializationResultModel(clonedModel, r.ElementIdentity)
                                    {
                                        OperationTarget = r.OperationTarget,
                                        Action = r.Action,
                                        Message = r.Message
                                    }
                                    : new SerializationResultModel(clonedModel, r.ErrorMessage ?? "Database Write Failed", r.Exception)
                                    {
                                        OperationTarget = r.OperationTarget,
                                        Action = r.Action,
                                        Message = r.Message
                                    };
                            }
                            dbResultsForExport.Add(mappedR);
                        }

                        string? targetPath = options.StandardsFilePath;

                        var fileResults = _exportService.Export(
                            queueItems,
                            targetPath,
                            dbResultsForExport,
                            new HashSet<string>(options.ProtectedPaths ?? new System.Collections.Generic.List<string>(), StringComparer.OrdinalIgnoreCase),
                            out string? finalPathUsed);

                        var mappedFileResults = new List<SerializationResultModel>();
                        for (int i = 0; i < queueItems.Count; i++)
                        {
                            var originalItem = fileItems[i];
                            var clonedModel = queueItems[i].Model;
                            var resultsForClone = fileResults.Where(r => r.Model == clonedModel).ToList();
                            foreach (var r in resultsForClone)
                              {
                                var mappedResult = r.Success
                                    ? new SerializationResultModel(originalItem.Model, r.ElementIdentity)
                                    : new SerializationResultModel(originalItem.Model, r.ErrorMessage ?? "File Save Failed", r.Exception);
                                mappedResult.OperationTarget = StandardsPipelineConstants.TargetFile;
                                mappedResult.Action = r.Action;
                                mappedResult.Message = r.Message;
                                mappedFileResults.Add(mappedResult);
                            }
                        }

                        allResults.AddRange(mappedFileResults);
                        actualFilePath = finalPathUsed;
                    }
                }
                catch (OperationCanceledException)
                {
                    var fileItems = result.Items.Where(i => i.WillSave).ToList();
                    foreach (var item in fileItems)
                    {
                        var r = new SerializationResultModel(item.Model, "File Save Cancelled.")
                        {
                            OperationTarget = StandardsPipelineConstants.TargetFile,
                            Action = StandardsPipelineConstants.ActionCanceled,
                            Message = "File Save Cancelled."
                        };
                        allResults.Add(r);
                    }
                }
                catch (Exception ex)
                {
                    var fileItems = result.Items.Where(i => i.WillSave).ToList();
                    foreach (var item in fileItems)
                    {
                        var r = new SerializationResultModel(item.Model, $"File Save Failed: {ex.Message}", ex)
                        {
                            OperationTarget = StandardsPipelineConstants.TargetFile,
                            Action = StandardsPipelineConstants.ActionFailed,
                            Message = ex.Message
                        };
                        allResults.Add(r);
                    }
                }
            }

            // Build ImportLogItem collection from allResults
            var logItems = new List<ImportLogItem>();
            foreach (var res in allResults)
            {
                var model = res.Model;
                string action = StandardsPipelineConstants.ActionUpdated;
                if (!res.Success)
                {
                    if (res.Action == "Alias Swap Failed")
                    {
                        action = "Alias Fail";
                    }
                    else
                    {
                        action = res.OperationTarget == StandardsPipelineConstants.TargetFile ? StandardsPipelineConstants.ActionSaveFailed : StandardsPipelineConstants.ActionFailed;
                    }
                }
                else
                {
                    if (!string.IsNullOrEmpty(res.Action))
                    {
                        action = res.Action;
                    }
                    else if (res.OperationTarget == StandardsPipelineConstants.TargetFile)
                    {
                        action = StandardsPipelineConstants.ActionSaved;
                    }
                    else
                    {
                        var matchingItem = result.Items.FirstOrDefault(qi => qi.Model == model);
                        if (matchingItem != null && matchingItem.WillEnforce)
                        {
                            action = StandardsPipelineConstants.ActionCreated;
                        }
                        else
                        {
                            action = StandardsPipelineConstants.ActionUpdated;
                        }
                    }
                }

                string name = (model is ElementModel em) ? (em.Name ?? "Unnamed") : model.GetType().Name;
                string className = (model is ElementModel emClass) ? (emClass.Class ?? "Unknown") : model.GetType().Name;
                if (className.Contains("."))
                {
                    className = className.Split('.').Last();
                }

                string message = res.Success ? "Operation completed successfully." : (res.ErrorMessage ?? "Unknown error occurred.");
                if (!string.IsNullOrEmpty(res.Message))
                {
                    message = res.Message;
                }
                if (res.Warnings != null && res.Warnings.Count > 0)
                {
                    message += " Warnings: " + string.Join(", ", res.Warnings);
                }

                logItems.Add(new ImportLogItem
                {
                    Action = action,
                    Class = className,
                    ElementName = name,
                    Message = message
                });
            }

            // Generate report markdown
            string markdown = StandardsReportGenerator.GenerateMarkdown(logItems);
            result.ReportMarkdown = markdown;

            // Write markdown log file next to target path
            if (!string.IsNullOrEmpty(actualFilePath) && File.Exists(actualFilePath))
            {
                try
                {
                    string logPath = Path.ChangeExtension(actualFilePath, ".log.md");
                    File.WriteAllText(logPath, markdown);
                    result.LogFilePath = logPath;
                }
                catch (Exception logEx)
                {
                    Console.WriteLine($"Error writing automatic Markdown log next to target path: {logEx.Message}");
                }
            }

            // Populate results on individual items
            foreach (var item in result.Items)
            {
                var itemResults = allResults.Where(r => r.Model == item.Model).ToList();
                if (itemResults.Count > 0)
                {
                    var failedResult = itemResults.FirstOrDefault(r => !r.Success);
                    if (failedResult != null)
                    {
                        item.Action = failedResult.Action ?? (failedResult.OperationTarget == StandardsPipelineConstants.TargetFile ? StandardsPipelineConstants.ActionSaveFailed : StandardsPipelineConstants.ActionFailed);
                        item.Message = failedResult.ErrorMessage ?? "Error occurred.";
                    }
                    else
                    {
                        var lastResult = itemResults.Last();
                        item.Action = lastResult.Action ?? (lastResult.OperationTarget == StandardsPipelineConstants.TargetFile ? StandardsPipelineConstants.ActionSaved : (item.WillEnforce ? StandardsPipelineConstants.ActionCreated : StandardsPipelineConstants.ActionUpdated));
                        item.Message = lastResult.Message ?? "Operation completed successfully.";
                    }
                }
                else
                {
                    item.Action = StandardsPipelineConstants.ActionUnchanged;
                    item.Message = "Staged, but no database write or file save operations were performed.";
                }
            }

            // Overall success requires that the database writes succeeded (if attempted) and all input items are successful
            result.Success = dbPhaseSucceeded && result.Items.All(i => i.Action != StandardsPipelineConstants.ActionFailed && i.Action != StandardsPipelineConstants.ActionSaveFailed && i.Action != StandardsPipelineConstants.ActionCanceled);

            // Update progress as completed
            if (progress != null)
            {
                progress.Report(new ProgressState
                {
                    TaskDescription = "Consolidate Project Standards",
                    CurrentItemName = "Execution completed.",
                    ProgressIndex = result.Items.Count,
                    MaximumBounds = result.Items.Count,
                    IsCompleted = true
                });
            }

            result.RawResults = allResults;
            return result;
        }

        private List<SerializationResultModel> RunRevitDbPhase(
            Document doc,
            List<StandardsExecutionItem> dbItems,
            StandardsExecutionOptions options,
            IProgress<ProgressState>? progress,
            CancellationToken cancellationToken)
        {
            var dbResults = new List<SerializationResultModel>();
            if (doc == null) return dbResults;

            int totalWorkItems = dbItems.Count;

            void ReportProgress(string taskDesc, string itemName, int index)
            {
                if (progress != null)
                {
                    progress.Report(new ProgressState
                    {
                        TaskDescription = taskDesc,
                        CurrentItemName = itemName,
                        ProgressIndex = index,
                        MaximumBounds = totalWorkItems,
                        IsCompleted = false
                    });
                }
            }

            ReportProgress("Consolidate Project Standards", "Starting standard injection...", 0);

            if (options.UseTransactionGroup)
            {
                using (var txGroup = new TransactionGroup(doc, "Consolidate Project Standards"))
                {
                    txGroup.Start();
                    try
                    {
                        ProcessDbPhaseInternal(doc, dbItems, options, ReportProgress, dbResults, cancellationToken);
                        txGroup.Assimilate();
                    }
                    catch (Exception)
                    {
                        txGroup.RollBack();
                        throw;
                    }
                }
            }
            else
            {
                ProcessDbPhaseInternal(doc, dbItems, options, ReportProgress, dbResults, cancellationToken);
            }

            return dbResults;
        }

        private void ProcessDbPhaseInternal(
            Document doc,
            List<StandardsExecutionItem> dbItems,
            StandardsExecutionOptions options,
            Action<string, string, int> reportProgress,
            List<SerializationResultModel> dbResults,
            CancellationToken cancellationToken)
        {
            if (dbItems.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var elementPocos = dbItems.Select(q => q.Model).ToList();
                var results = _serializationEngine.ToRevit(elementPocos, doc, null, cancellationToken).ToList();
                dbResults.AddRange(results);
            }

            if (options.ProcessFamilies)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var elementPocos = dbItems.Select(q => q.Model).OfType<ElementModel>().ToList();
                ProcessFamilyUpdates(doc, elementPocos, options, reportProgress, dbResults, cancellationToken);
            }
        }

        private void ProcessFamilyUpdates(
            Document doc,
            List<ElementModel> standards,
            StandardsExecutionOptions options,
            Action<string, string, int> reportProgress,
            List<SerializationResultModel> dbResults,
            CancellationToken cancellationToken)
        {
            if (doc == null || !options.ProcessFamilies) return;

            cancellationToken.ThrowIfCancellationRequested();

            reportProgress("Consolidate Project Standards", "Collecting families to update...", dbResults.Count);

            IList<Family> allFamilies = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .ToList();

            List<Family> familiesToProcess = new List<Family>();
            foreach (Family family in allFamilies)
            {
                if (family.IsEditable)
                {
                    if (options.CategoryFilter == "Annotations Only" && (family.FamilyCategory == null || family.FamilyCategory.CategoryType != CategoryType.Annotation))
                        continue;
#if REVIT2022 || REVIT2023
                    if (options.CategoryFilter == "Title Blocks Only" && (family.FamilyCategory == null || family.FamilyCategory.Id.IntegerValue != (int)BuiltInCategory.OST_TitleBlocks))
#else
                    if (options.CategoryFilter == "Title Blocks Only" && (family.FamilyCategory == null || family.FamilyCategory.Id.Value != (long)BuiltInCategory.OST_TitleBlocks))
#endif
                        continue;

                    familiesToProcess.Add(family);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (doc.IsWorkshared && familiesToProcess.Count > 0)
            {
                reportProgress("Consolidate Project Standards", "Checking out family worksets...", dbResults.Count);
                List<WorksetId> worksetIds = familiesToProcess
                    .Select(f => f.WorksetId)
                    .Distinct()
                    .Where(id => id != WorksetId.InvalidWorksetId)
                    .ToList();

                if (worksetIds.Count > 0)
                {
                    WorksharingUtils.CheckoutWorksets(doc, worksetIds);
                }
            }

            List<string> familyNamesToProcess = familiesToProcess
                .Select(f => f.Name)
                .Distinct()
                .ToList();

            int familyIndex = 0;
            foreach (string familyName in familyNamesToProcess)
            {
                cancellationToken.ThrowIfCancellationRequested();

                familyIndex++;
                reportProgress(
                    "Consolidate Project Standards",
                    $"Updating family {familyIndex} of {familyNamesToProcess.Count}: {familyName}...",
                    dbResults.Count);

                Family? family = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .FirstOrDefault(f => f.Name == familyName);

                if (family != null && family.IsValidObject)
                {
                    try
                    {
                        UpdateFamilyRecursively(doc, family, standards, options, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        var failedModel = new ElementModel
                        {
                            Name = familyName,
                            Class = "Autodesk.Revit.DB.Family"
                        };
                        var result = new SerializationResultModel(failedModel, $"Family update failed for {familyName}: {ex.Message}", ex)
                        {
                            OperationTarget = StandardsPipelineConstants.TargetDatabase,
                            Action = StandardsPipelineConstants.ActionFailed,
                            Message = ex.Message
                        };
                        dbResults.Add(result);
                    }
                }
            }
        }

        private void UpdateFamilyRecursively(
            Document parentDoc,
            Family family,
            IEnumerable<ElementModel> standards,
            StandardsExecutionOptions options,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException();
            }

            if (family == null || !family.IsEditable) return;

            Document? familyDoc = null;
            try
            {
                familyDoc = parentDoc.EditFamily(family);
            }
            catch (Exception)
            {
                return;
            }

            if (familyDoc == null) return;

            parentDoc.Application.FailuresProcessing += ResolveWarnings;

            try
            {
                IList<Family> nestedFamilies = new FilteredElementCollector(familyDoc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .ToList();

                var nestedFamiliesInfo = nestedFamilies
                    .Select(nf => new { Id = nf.Id, Name = nf.Name, IsEditable = nf.IsEditable })
                    .ToList();

                foreach (var nfInfo in nestedFamiliesInfo)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException();
                    }
                    if (nfInfo.IsEditable)
                    {
                        Family? freshNestedFamily = new FilteredElementCollector(familyDoc)
                            .OfClass(typeof(Family))
                            .Cast<Family>()
                            .FirstOrDefault(nf => nf.Name == nfInfo.Name);
                        if (freshNestedFamily != null && freshNestedFamily.IsValidObject)
                        {
                            UpdateFamilyRecursively(familyDoc, freshNestedFamily, standards, options, cancellationToken);
                        }
                    }
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }

                var familyStandards = standards.Where(s =>
                {
                    if (s is MaterialModel) return true;
                    if (s is ElementTypeModel etModel)
                    {
                        if (familyDoc.IsFamilyDocument && etModel.Class == "Autodesk.Revit.DB.SpotDimensionType")
                        {
                            return false;
                        }
                        return true;
                    }
                    return false;
                }).ToList();

                foreach (var serialElement in familyStandards)
                {
                    if (serialElement is ElementTypeModel etModel)
                    {
                        etModel.ElementType = null;
                    }
                    serialElement.Element = null;
                    serialElement.Document = null;
                }

                _serializationEngine.ToRevit(familyStandards, familyDoc, null, cancellationToken);

                if (options.PurgeUnusedStyleTypes)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException();
                    }
                    try
                    {
#if !REVIT2022
                        DocumentUtil.Purge(parentDoc.Application, familyDoc);
#endif
                    }
                    catch (Exception)
                    {
                    }
                }

                try
                {
                    Synthetic.Shared.RevitAPI.FamilyUtil.SetIsChanged(parentDoc, familyDoc);
                    familyDoc.LoadFamily(parentDoc, new ImportFamilyLoadOptions());
                }
                catch (Exception)
                {
                }
            }
            finally
            {
                parentDoc.Application.FailuresProcessing -= ResolveWarnings;
                try
                {
                    familyDoc.Close(false);
                }
                catch (Exception)
                {
                }
            }
        }

        private static void ResolveWarnings(object? sender, Autodesk.Revit.DB.Events.FailuresProcessingEventArgs e)
        {
            FailuresAccessor fa = e.GetFailuresAccessor();
            IList<FailureMessageAccessor> failList = fa.GetFailureMessages();

            if (failList.Count == 0)
            {
                e.SetProcessingResult(FailureProcessingResult.Continue);
                return;
            }

            foreach (FailureMessageAccessor failure in failList)
            {
                fa.DeleteWarning(failure);
            }
            e.SetProcessingResult(FailureProcessingResult.ProceedWithCommit);
        }

        private class ImportFamilyLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = true;
                return true;
            }

            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = true;
                return true;
            }
        }
    }
}
