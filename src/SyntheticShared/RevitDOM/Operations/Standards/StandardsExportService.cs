using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Platform.UI;

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

        private static bool IsValidModelForExport(ObjectModel? model)
        {
            if (model is ElementModel em)
            {
                return !string.IsNullOrWhiteSpace(em.Class) && !string.IsNullOrWhiteSpace(em.Name);
            }
            return model != null;
        }

        private static List<ObjectModel> CollectValidModelsForExport(IEnumerable<QueueItemModel> fileItems)
        {
            var validModels = new List<ObjectModel>();
            foreach (var item in fileItems)
            {
                if (IsValidModelForExport(item.Model))
                {
                    validModels.Add(item.Model);
                }
            }
            return validModels;
        }

        private static void AppendSuccessfulFileResults(List<SerializationResultModel> results, IEnumerable<ObjectModel> models)
        {
            foreach (var model in models)
            {
                var result = new SerializationResultModel(model, null);
                result.OperationTarget = "File";
                results.Add(result);
            }
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
                            var deserialized = StandardSerializationEngine.FromJson(existingJson);
                            if (deserialized != null)
                            {
                                existingElements = deserialized.ToList();
                            }
                        }

                        var validFileModels = CollectValidModelsForExport(fileItems);
                        var newElements = validFileModels.OfType<ElementModel>().ToList();

                        var mergedElements = StandardsMergeUtility.Merge(existingElements, newElements, overwriteDuplicates);
                        string serializedJson = StandardSerializationEngine.ToJson(mergedElements.Cast<ObjectModel>());
                        File.WriteAllText(fullTargetPath, serializedJson);

                        AppendSuccessfulFileResults(results, validFileModels);

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
                    var modelsToSave = CollectValidModelsForExport(fileItems);

                    if (modelsToSave.Count > 0)
                    {
                        string serializedJson = StandardSerializationEngine.ToJson(modelsToSave);
                        File.WriteAllText(targetPath, serializedJson);

                        AppendSuccessfulFileResults(results, modelsToSave);
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
