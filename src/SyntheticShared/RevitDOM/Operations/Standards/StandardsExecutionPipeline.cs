using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Shared.RevitAPI;
using Synthetic.Shared.UI;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.RevitDOM.Operations.Standards
{
    public class StandardsExecutionPipeline : IStandardsExecutionPipeline
    {
        private readonly IStandardSerializationEngine _serializationEngine;
        private readonly IStandardsExportService _exportService;
        private readonly IFamilyEnforcer _familyEnforcer;

        public StandardsExecutionPipeline(
            IStandardSerializationEngine serializationEngine,
            IStandardsExportService exportService,
            IFamilyEnforcer familyEnforcer)
        {
            _serializationEngine = serializationEngine ?? throw new ArgumentNullException(nameof(serializationEngine));
            _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
            _familyEnforcer = familyEnforcer ?? throw new ArgumentNullException(nameof(familyEnforcer));
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
                _familyEnforcer.Enforce(doc, elementPocos, options, reportProgress, dbResults, cancellationToken);
            }
        }


    }
}
