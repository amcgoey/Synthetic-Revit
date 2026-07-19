using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.UI;

namespace Synthetic.RevitDOM.Operations.Standards
{
    /// <summary>
    /// Service that encapsulates Phase 2 (File I/O) execution and protected file overwrite guardrails.
    /// </summary>
    public class StandardsExportService : IStandardsExportService
    {
        private readonly IGuardrailPromptService _guardrailService;
        private readonly IFileDialogService _dialogService;

        /// <summary>
        /// Initializes a new instance of the <see cref="StandardsExportService"/> class.
        /// </summary>
        public StandardsExportService(IGuardrailPromptService guardrailService, IFileDialogService dialogService)
        {
            _guardrailService = guardrailService ?? throw new ArgumentNullException(nameof(guardrailService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        }

        /// <summary>
        /// Exports the queued standards to a JSON file.
        /// </summary>
        public List<SerializationResultModel> Export(
            List<QueueItemModel> fileItems,
            string? targetPath,
            List<SerializationResultModel> dbResults,
            HashSet<string> protectedPaths,
            out string? finalPathUsed)
        {
            var results = new List<SerializationResultModel>();
            finalPathUsed = null;

            if (fileItems == null || fileItems.Count == 0)
            {
                return results;
            }

            if (string.IsNullOrEmpty(targetPath))
            {
                targetPath = _dialogService.SaveFileDialog("JSON Files (*.json)|*.json", "Save Standards JSON File", "ProjectStandards.json");
            }

            if (string.IsNullOrEmpty(targetPath))
            {
                foreach (var item in fileItems)
                {
                    var result = new SerializationResultModel(item.Model, "Save aborted: No save path selected.");
                    result.OperationTarget = "File";
                    results.Add(result);
                }
                return results;
            }

            finalPathUsed = targetPath;
            string fullTargetPath = Path.GetFullPath(targetPath);
            bool isProtected = protectedPaths.Contains(fullTargetPath);
            bool proceedWithSave = true;

            if (isProtected)
            {
                var guardrailChoice = _guardrailService.PromptProtectedFileOverwrite(fullTargetPath);
                if (guardrailChoice == GuardrailResult.SaveAs)
                {
                    string? newPath = _dialogService.SaveFileDialog("JSON Files (*.json)|*.json", "Save Standard As", Path.GetFileName(fullTargetPath));
                    if (!string.IsNullOrEmpty(newPath))
                    {
                        targetPath = newPath;
                        finalPathUsed = newPath;
                        fullTargetPath = Path.GetFullPath(targetPath);
                    }
                    else
                    {
                        proceedWithSave = false;
                    }
                }
                else if (guardrailChoice == GuardrailResult.Cancel)
                {
                    proceedWithSave = false;
                }
                else if (guardrailChoice == GuardrailResult.MergeOverwrite || guardrailChoice == GuardrailResult.MergePreserve)
                {
                    bool overwriteDuplicates = (guardrailChoice == GuardrailResult.MergeOverwrite);
                    try
                    {
                        List<ElementModel> existingElements = new List<ElementModel>();
                        if (File.Exists(fullTargetPath))
                        {
                            string existingJson = File.ReadAllText(fullTargetPath);
                            var deserialized = ModelsToSerialize.DeserializeByJson(existingJson);
                            if (deserialized != null)
                            {
                                existingElements = deserialized.ToList();
                            }
                        }

                        var newElements = new List<ElementModel>();
                        foreach (var item in fileItems)
                        {
                            var dbMatch = dbResults.FirstOrDefault(r => r.Model == item.Model);
                            if (dbMatch != null && !dbMatch.Success)
                            {
                                continue;
                            }
                            if (item.Model is ElementModel em)
                            {
                                newElements.Add(em);
                            }
                        }

                        var mergedElements = StandardsMergeUtility.Merge(existingElements, newElements, overwriteDuplicates);
                        string serializedJson = ModelsToSerialize.SerializeToJson(mergedElements.Cast<ObjectModel>().ToList());
                        File.WriteAllText(fullTargetPath, serializedJson);

                        foreach (var item in fileItems)
                        {
                            var dbMatch = dbResults.FirstOrDefault(r => r.Model == item.Model);
                            if (dbMatch != null && !dbMatch.Success)
                            {
                                continue;
                            }
                            var result = new SerializationResultModel(item.Model, null);
                            result.OperationTarget = "File";
                            results.Add(result);
                        }

                        proceedWithSave = false;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error merging standard file: {ex.Message}");
                        foreach (var item in fileItems)
                        {
                            var result = new SerializationResultModel(item.Model, $"File Merge Failed: {ex.Message}", ex);
                            result.OperationTarget = "File";
                            results.Add(result);
                        }
                        proceedWithSave = false;
                    }
                }
            }

            if (!proceedWithSave && results.Count == 0)
            {
                // Aborted at overwrite prompt with no discrete results registered yet
                foreach (var item in fileItems)
                {
                    var result = new SerializationResultModel(item.Model, "Save aborted by user at guardrail prompt.");
                    result.OperationTarget = "File";
                    results.Add(result);
                }
            }

            if (proceedWithSave)
            {
                try
                {
                    var modelsToSave = new List<ObjectModel>();
                    foreach (var item in fileItems)
                    {
                        var dbMatch = dbResults.FirstOrDefault(r => r.Model == item.Model);
                        if (dbMatch != null && !dbMatch.Success)
                        {
                            continue;
                        }
                        modelsToSave.Add(item.Model);
                    }

                    if (modelsToSave.Count > 0)
                    {
                        string serializedJson = ModelsToSerialize.SerializeToJson(modelsToSave);
                        File.WriteAllText(targetPath, serializedJson);

                        foreach (var model in modelsToSave)
                        {
                            var result = new SerializationResultModel(model, null);
                            result.OperationTarget = "File";
                            results.Add(result);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error writing standard file: {ex.Message}");
                    foreach (var item in fileItems)
                    {
                        var result = new SerializationResultModel(item.Model, $"File Write Failed: {ex.Message}", ex);
                        result.OperationTarget = "File";
                        results.Add(result);
                    }
                }
            }

            return results;
        }
    }
}
