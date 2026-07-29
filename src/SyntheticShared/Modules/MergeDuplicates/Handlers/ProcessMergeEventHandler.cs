using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Infrastructure.Serialization;
using Synthetic.Shared.UI;
using Synthetic.Modules.MergeDuplicates.ViewModels;
using Synthetic.RevitDOM.Operations.Merge;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Modules.MergeDuplicates.Handlers
{
    /// <summary>
    /// Tracks the summary results of a merge cluster execution for Step 6.
    /// </summary>
    public class MergeExecutionReport
    {
        /// <summary>
        /// Gets or sets the name of the merge cluster.
        /// </summary>
        public string ClusterName { get; set; } = string.Empty;
        /// <summary>
        /// Gets or sets the count of instances swapped during the merge.
        /// </summary>
        public int InstancesSwappedCount { get; set; }
        /// <summary>
        /// Gets or sets the count of duplicate types purged.
        /// </summary>
        public int DuplicateTypesPurged { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether the merge execution succeeded.
        /// </summary>
        public bool ExecutionSuccessStatus { get; set; }
        /// <summary>
        /// Gets or sets the error message if the execution failed.
        /// </summary>
        public string? ErrorMessage { get; set; }
    }
    /// <summary>
    /// Standard family load options to automatically overwrite parameter values.
    /// </summary>
    public class ProcessMergeFamilyLoadOptions : IFamilyLoadOptions
    {
        /// <summary>
        /// Called when a family is found in the target document. Overwrites parameter values.
        /// </summary>
        /// <param name="familyInUse">Indicates if the family is currently in use.</param>
        /// <param name="overwriteParameterValues">Output parameter indicating if parameter values should be overwritten.</param>
        /// <returns>True to load the family.</returns>
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = true;
            return true;
        }
        /// <summary>
        /// Called when a shared family is found in the target document. Overwrites parameter values.
        /// </summary>
        /// <param name="sharedFamily">The shared family being loaded.</param>
        /// <param name="familyInUse">Indicates if the shared family is currently in use.</param>
        /// <param name="source">Output parameter indicating the source of the family.</param>
        /// <param name="overwriteParameterValues">Output parameter indicating if parameter values should be overwritten.</param>
        /// <returns>True to load the shared family.</returns>
        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = true;
            return true;
        }
    }
    /// <summary>
    /// External event handler to process the active merge queue on the Revit API thread.
    /// </summary>
    public class ProcessMergeEventHandler : IExternalEventHandler
    {
        private readonly object _lock = new object();
        private MergeQueueViewModel? _queueVM;
        private Window? _parentWindow;
        private MergeDuplicatesViewModel? _mainVM;
        private CancellationToken _cancellationToken;
        /// <summary>
        /// Queues a merge execution request with its corresponding UI/VM contexts.
        /// </summary>
        public void QueueRequest(MergeQueueViewModel? queueVM, Window? window, MergeDuplicatesViewModel? mainVM, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                _queueVM = queueVM;
                _parentWindow = window;
                _mainVM = mainVM;
                _cancellationToken = cancellationToken;
            }
        }
        /// <summary>
        /// Executes the merge processing on the Revit API thread.
        /// </summary>
        public void Execute(UIApplication app)
        {
            MergeQueueViewModel? currentQueue;
            Window? currentWindow;
            MergeDuplicatesViewModel? currentMainVM;
            CancellationToken currentToken;
            lock (_lock)
            {
                currentQueue = _queueVM;
                currentWindow = _parentWindow;
                currentMainVM = _mainVM;
                currentToken = _cancellationToken;
                // Reset state
                _queueVM = null;
                _parentWindow = null;
                _mainVM = null;
                _cancellationToken = CancellationToken.None;
            }
            if (currentQueue == null || currentQueue.QueuedClusters.Count == 0) return;
            Document? doc = null;
            var primItem = currentQueue.QueuedClusters.FirstOrDefault()?.SelectedPrimary;
            if (primItem != null && app.Application.Documents != null)
            {
                var targetId = primItem.RevitElementId.ToElementId();
                foreach (Document openDoc in app.Application.Documents)
                {
                    if (!openDoc.IsFamilyDocument)
                    {
                        try
                        {
                            if (openDoc.GetElement(targetId) != null)
                            {
                                doc = openDoc;
                                break;
                            }
                        }
                        catch {}
                    }
                }
            }
            if (doc == null)
            {
                doc = app.ActiveUIDocument?.Document;
            }
            if (doc == null && app.Application.Documents != null)
            {
                foreach (Document openDoc in app.Application.Documents)
                {
                    if (!openDoc.IsFamilyDocument)
                    {
                        doc = openDoc;
                        break;
                    }
                }
            }
            if (doc == null) return;
            // 1. Disable UI during processing to prevent concurrent modifications
            if (currentWindow != null)
            {
                currentWindow.Dispatcher.Invoke(() => currentWindow.IsEnabled = false);
            }
            var reports = new List<MergeExecutionReport>();
            var clustersToProcess = currentQueue.QueuedClusters.ToList();
            bool wasCancelled = false;
            using (var transGroup = new TransactionGroup(doc, "Merge Duplicates"))
            {
                transGroup.Start();
                foreach (var cluster in clustersToProcess)
                {
                    // Check cancellation token boundary
                    if (currentToken.IsCancellationRequested)
                    {
                        wasCancelled = true;
                        transGroup.RollBack();
                        break;
                    }
                    var report = new MergeExecutionReport
                    {
                        ClusterName = cluster.ClusterName,
                        ExecutionSuccessStatus = false
                    };
                    var primaryItem = cluster.SelectedPrimary;
                    if (primaryItem == null)
                    {
                        reports.Add(report);
                        continue;
                    }
                    var duplicateItems = cluster.Items
                        .Where(i => i != primaryItem && i.IsIncludedForMerge)
                        .ToList();
                    var typeMap = new Dictionary<ElementId, ElementId>();
                    using (var trans = new Transaction(doc, $"Merge Cluster Step 1: {cluster.ClusterName}"))
                    {
                        trans.Start();
                        try
                        {
                            Element primElement = doc.GetElement(primaryItem.RevitElementId.ToElementId());
                            if (primElement != null)
                            {
                                // 1. Schema Parameter Injection
                                if (primElement is Family primFamily)
                                {
                                    var parametersToInject = new List<string>();
                                    foreach (var mapping in cluster.TypeMappings)
                                    {
                                        if (mapping.RecommendedAction == RecommendedAction.Exclude) continue;
                                        foreach (var row in mapping.ParameterResolutions)
                                        {
                                            if (row.InjectParameter && !parametersToInject.Contains(row.ParameterName))
                                            {
                                                parametersToInject.Add(row.ParameterName);
                                            }
                                        }
                                    }
                                    if (parametersToInject.Count > 0)
                                    {
                                        Document famDoc = doc.EditFamily(primFamily);
                                        if (famDoc != null)
                                        {
                                            bool anyInjected = false;
                                            using (Transaction famTrans = new Transaction(famDoc, "Inject Parameters"))
                                            {
                                                famTrans.Start();
                                                FamilyManager famManager = famDoc.FamilyManager;
                                                foreach (var paramName in parametersToInject)
                                                {
                                                    bool paramExists = false;
                                                    foreach (FamilyParameter fp in famManager.Parameters)
                                                    {
                                                        if (fp.Definition.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase))
                                                        {
                                                            paramExists = true;
                                                            break;
                                                        }
                                                    }
                                                    if (!paramExists)
                                                    {
                                                        ParameterDefinitionSpec? spec = null; bool foundSource = false;
                                                        foreach (var mapping in cluster.TypeMappings)
                                                        {
                                                            if (mapping.RecommendedAction == RecommendedAction.Exclude) continue;
                                                            if (mapping.SourceType == null) continue;
                                                            FamilySymbol? sourceSymbol = doc.GetElement(mapping.SourceType.RevitTypeId.ToElementId()) as FamilySymbol;
                                                            if (sourceSymbol != null)
                                                            {
                                                                Parameter sourceParam = sourceSymbol.LookupParameter(paramName);
                                                                if (sourceParam != null)
                                                                {
                                                                    spec = GetParamGroupAndTypeFromSource(sourceParam); if (spec != null && spec.Group != null && spec.SpecType != null) { foundSource = true; break; }
                                                                    if (foundSource) break;
                                                                }
                                                            }
                                                        }
                                                        if (!foundSource || spec == null || spec.Group == null || spec.SpecType == null) { spec = GetDefaultGroupAndType(); }
                                                        if (InjectParameterToFamily(famManager, paramName, spec))
                                                        {
                                                            anyInjected = true;
                                                        }
                                                    }
                                                }
                                                famTrans.Commit();
                                            }
                                            if (anyInjected)
                                            {
                                                primFamily = famDoc.LoadFamily(doc, new ProcessMergeFamilyLoadOptions());
                                            }
                                            famDoc.Close(false);
                                            // Re-retrieve primary element reference in case LoadFamily updated it
                                            primElement = doc.GetElement(primaryItem.RevitElementId.ToElementId());
                                        }
                                    }
                                }
                                // 2. Type Mapping & Value Copying
                                foreach (var mapping in cluster.TypeMappings)
                                {
                                    if (mapping.RecommendedAction == RecommendedAction.Exclude) continue;
                                    if (mapping.SourceType == null || mapping.TargetType == null) continue;
                                    ElementId resolvedTargetTypeId = mapping.TargetType.RevitTypeId.ToElementId();
                                    if (mapping.RecommendedAction == RecommendedAction.Migrate && primElement is Family)
                                    {
                                        ElementType? targetTypeElement = doc.GetElement(mapping.TargetType.RevitTypeId.ToElementId()) as ElementType;
                                        if (targetTypeElement != null)
                                        {
                                            string targetName = mapping.MigrateRenameText;
                                            if (string.IsNullOrWhiteSpace(targetName))
                                            {
                                                targetName = mapping.SourceType.Name;
                                            }
                                            targetName = SanitizeRevitTypeName(targetName);
                                            FamilySymbol? existingSymbol = null;
                                            if (primElement is Family f)
                                            {
                                                foreach (ElementId symbolId in f.GetFamilySymbolIds())
                                                {
                                                    FamilySymbol? sym = doc.GetElement(symbolId) as FamilySymbol;
                                                    if (sym != null && sym.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase))
                                                    {
                                                        existingSymbol = sym;
                                                        break;
                                                    }
                                                }
                                            }
                                            if (existingSymbol != null)
                                            {
                                                resolvedTargetTypeId = existingSymbol.Id;
                                            }
                                            else
                                            {
                                                ElementType? duplicatedType = null;
                                                try
                                                {
                                                    duplicatedType = targetTypeElement.Duplicate(targetName);
                                                }
                                                catch (Exception)
                                                {
                                                    string fallbackName = SanitizeRevitTypeName(targetName + "_Migrated");
                                                    try
                                                    {
                                                        duplicatedType = targetTypeElement.Duplicate(fallbackName);
                                                    }
                                                    catch (Exception exInner)
                                                    {
                                                        int suffixCounter = 1;
                                                        while (duplicatedType == null && suffixCounter <= 10)
                                                        {
                                                            try
                                                            {
                                                                duplicatedType = targetTypeElement.Duplicate($"{targetName}_Migrated_{suffixCounter}");
                                                            }
                                                            catch
                                                            {
                                                                suffixCounter++;
                                                            }
                                                        }
                                                        if (duplicatedType == null)
                                                        {
                                                            throw new InvalidOperationException($"Failed to duplicate type '{targetName}' after multiple suffix attempts.", exInner);
                                                        }
                                                    }
                                                }
                                                if (duplicatedType != null)
                                                {
                                                    resolvedTargetTypeId = duplicatedType.Id;
                                                    // Copy all parameters from source type to the duplicated type
                                                    Element sourceSymbol = doc.GetElement(mapping.SourceType.RevitTypeId.ToElementId());
                                                    if (sourceSymbol != null)
                                                    {
                                                        foreach (Parameter sourceParam in sourceSymbol.Parameters)
                                                        {
                                                            if (sourceParam.IsReadOnly || !sourceParam.HasValue) continue;
                                                            Parameter targetParam = duplicatedType.LookupParameter(sourceParam.Definition.Name);
                                                            if (targetParam != null && !targetParam.IsReadOnly)
                                                            {
                                                                CopyParameterValue(sourceParam, targetParam);
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    // Apply resolved parameters
                                    Element targetTypeObj = doc.GetElement(resolvedTargetTypeId);
                                    if (targetTypeObj != null)
                                    {
                                        foreach (var row in mapping.ParameterResolutions)
                                        {
                                            Element winningElement = doc.GetElement(row.WinningValueElementId.ToElementId());
                                            if (winningElement != null)
                                            {
                                                Parameter winningParam = winningElement.LookupParameter(row.ParameterName);
                                                Parameter targetParam = targetTypeObj.LookupParameter(row.ParameterName);
                                                if (winningParam != null && targetParam != null && !targetParam.IsReadOnly)
                                                {
                                                    CopyParameterValue(winningParam, targetParam);
                                                }
                                            }
                                        }
                                    }
                                    typeMap[mapping.SourceType.RevitTypeId.ToElementId()] = resolvedTargetTypeId;
                                }
                                // 3. Swap Instances
                                int instancesSwapped = 0;
                                var dupTypeIds = typeMap.Keys.ToList();
                                if (dupTypeIds.Count > 0)
                                {
                                    if (primElement is Family)
                                    {
                                        var instances = new FilteredElementCollector(doc)
                                            .OfClass(typeof(FamilyInstance))
                                            .Cast<FamilyInstance>()
                                            .Where(fi => dupTypeIds.Contains(fi.GetTypeId()))
                                            .ToList();
                                        foreach (var instance in instances)
                                        {
                                            ElementId targetTypeId = typeMap[instance.GetTypeId()];
                                            FamilySymbol? targetSymbol = doc.GetElement(targetTypeId) as FamilySymbol;
                                            if (targetSymbol != null)
                                            {
                                                if (!targetSymbol.IsActive)
                                                {
                                                    targetSymbol.Activate();
                                                }
                                                instance.Symbol = targetSymbol;
                                                instancesSwapped++;
                                            }
                                        }
                                    }
                                    else if (primElement is GroupType)
                                    {
                                        var groups = new FilteredElementCollector(doc)
                                            .OfClass(typeof(Group))
                                            .Cast<Group>()
                                            .Where(g => dupTypeIds.Contains(g.GetTypeId()))
                                            .ToList();
                                        foreach (var group in groups)
                                        {
                                            ElementId targetTypeId = typeMap[group.GetTypeId()];
                                            GroupType? targetGroupType = doc.GetElement(targetTypeId) as GroupType;
                                            if (targetGroupType != null)
                                            {
                                                group.GroupType = targetGroupType;
                                                instancesSwapped++;
                                            }
                                        }
                                    }
                                    else if (primElement is AssemblyType)
                                    {
                                        var assemblies = new FilteredElementCollector(doc)
                                            .OfClass(typeof(AssemblyInstance))
                                            .Cast<AssemblyInstance>()
                                            .Where(a => dupTypeIds.Contains(a.GetTypeId()))
                                            .ToList();
                                        foreach (var assembly in assemblies)
                                        {
                                            ElementId targetTypeId = typeMap[assembly.GetTypeId()];
                                            assembly.ChangeTypeId(targetTypeId);
                                            instancesSwapped++;
                                        }
                                    }
                                }
                                report.InstancesSwappedCount = instancesSwapped;
                            }
                            trans.Commit();
                        }
                        catch (Exception ex)
                        {
                            trans.RollBack();
                            report.ExecutionSuccessStatus = false;
                            report.ErrorMessage = ex.Message;
                            app.Application.WriteJournalComment($"[MERGE_ERROR] Error processing cluster step 1 '{cluster.ClusterName}': {ex.Message}", true);
                            reports.Add(report);
                            continue;
                        }
                    }
                    // 3.5 AliasSwapEngine deep scan and swap (outside any active transaction!)
                    try
                    {
                        foreach (var kvp in typeMap)
                        {
                            AliasSwapEngine.SwapElementReferences(doc, kvp.Key, kvp.Value);
                        }
                    }
                    catch (Exception ex)
                    {
                        app.Application.WriteJournalComment($"[MERGE_ERROR] Error swapping alias references outside transaction: {ex.Message}", true);
                    }
                    // 4. Redundancy Deletion in Transaction 2
                    using (var trans2 = new Transaction(doc, $"Merge Cluster Step 2: {cluster.ClusterName}"))
                    {
                        trans2.Start();
                        try
                        {
                            Element primElement = doc.GetElement(primaryItem.RevitElementId.ToElementId());
                            int purgedCount = 0;
                            foreach (var dupItem in duplicateItems)
                            {
                                bool allTypesMergedOrMigrated = true;
                                foreach (var t in dupItem.Types)
                                {
                                    var mapping = cluster.TypeMappings.FirstOrDefault(m => m.SourceType != null && m.SourceType.RevitTypeId.ToElementId() == t.RevitTypeId.ToElementId());
                                    if (mapping != null && mapping.RecommendedAction == RecommendedAction.Exclude)
                                    {
                                        allTypesMergedOrMigrated = false;
                                        break;
                                    }
                                }
                                if (allTypesMergedOrMigrated)
                                {
                                    try
                                    {
                                        doc.Delete(dupItem.RevitElementId.ToElementId());
                                        purgedCount++;
                                    }
                                     catch (Exception ex)
                                     {
                                         try
                                         {
                                             System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "synthetic_test_error.txt"), ex.ToString());
                                         }
                                         catch {}
                                         report.ExecutionSuccessStatus = false;
                                         report.ErrorMessage = "Family deletion failed: " + ex.Message + " | " + ex.StackTrace;
                                     }
                                }
                                else
                                {
                                    if (primElement is Family || primElement is GroupType || primElement is AssemblyType)
                                    {
                                        foreach (var t in dupItem.Types)
                                        {
                                            var mapping = cluster.TypeMappings.FirstOrDefault(m => m.SourceType != null && m.SourceType.RevitTypeId.ToElementId() == t.RevitTypeId.ToElementId());
                                            if (mapping != null && mapping.RecommendedAction != RecommendedAction.Exclude)
                                            {
                                                try
                                                {
                                                    doc.Delete(t.RevitTypeId.ToElementId());
                                                    purgedCount++;
                                                }
                                                catch (Exception)
                                                {
                                                    // Silently handle symbol deletion error
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            report.DuplicateTypesPurged = purgedCount;
                            trans2.Commit();
                            report.ExecutionSuccessStatus = true;
                        }
                        catch (Exception ex)
                        {
                            trans2.RollBack();
                            report.ExecutionSuccessStatus = false;
                            report.ErrorMessage = ex.Message;
                            app.Application.WriteJournalComment($"[MERGE_ERROR] Error processing cluster step 2 '{cluster.ClusterName}': {ex.Message}", true);
                        }
                    }
                    reports.Add(report);
                    // Safely update UI Queue collections on Dispatcher
                    if (currentWindow != null && report.ExecutionSuccessStatus)
                    {
                        currentWindow.Dispatcher.Invoke(() =>
                        {
                            currentQueue.QueuedClusters.Remove(cluster);
                            if (currentMainVM != null && currentMainVM.ScannedClusters.Contains(cluster))
                            {
                                currentMainVM.ScannedClusters.Remove(cluster);
                            }
                        });
                    }
                }
                if (!wasCancelled)
                {
                    transGroup.Assimilate();
                }
            }
            // Export execution summary to JSON file
            ExportStep6ResultsSummary(reports);
            // Re-enable UI window
            if (currentWindow != null)
            {
                currentWindow.Dispatcher.Invoke(() =>
                {
                    currentWindow.IsEnabled = true;
                    if (wasCancelled)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Merge Process Cancelled", "The merge process was cancelled. All changes in this batch have been rolled back.");
                    }
                    else
                    {
                        int successCount = reports.Count(r => r.ExecutionSuccessStatus);
                        Autodesk.Revit.UI.TaskDialog.Show("Merge Duplicates Completed", $"{successCount} cluster(s) merged successfully.\nResults written to:\nMergeDuplicates_Step6_ExecutionResult.json");
                    }
                });
            }
        }
        private static void CopyParameterValue(Parameter sourceParam, Parameter targetParam)
        {
            if (sourceParam == null || targetParam == null || targetParam.IsReadOnly || !sourceParam.HasValue) return;
            switch (sourceParam.StorageType)
            {
                case StorageType.Double:
                    targetParam.Set(sourceParam.AsDouble());
                    break;
                case StorageType.Integer:
                    targetParam.Set(sourceParam.AsInteger());
                    break;
                case StorageType.String:
                    targetParam.Set(sourceParam.AsString());
                    break;
                case StorageType.ElementId:
                    targetParam.Set(sourceParam.AsElementId());
                    break;
            }
        }
                private static ParameterDefinitionSpec GetDefaultGroupAndType()
        {
            return ParameterDefinitionSpec.CreateDefault();
        }

        private static ParameterDefinitionSpec GetParamGroupAndTypeFromSource(Parameter sourceParam)
        {
            if (sourceParam == null) return GetDefaultGroupAndType();

            object? group = null;
            object? specType = null;

            Definition def = sourceParam.Definition;
            if (def != null)
            {
#if REVIT2022
                group = def.ParameterGroup;
                specType = def.ParameterType;
#elif REVIT2023
                group = def.ParameterGroup;
                specType = def.GetDataType();
#else
                // Default baseline: Revit 2024+
                group = def.GetGroupTypeId();
                specType = def.GetDataType();
#endif

                // Reflection fallbacks for cross-version / mock execution contexts
                if (group == null)
                {
                    var groupProp = def.GetType().GetProperty("ParameterGroup");
                    if (groupProp != null)
                    {
                        group = groupProp.GetValue(def);
                    }
                    else
                    {
                        var groupMethod = def.GetType().GetMethod("GetGroupTypeId");
                        if (groupMethod != null)
                        {
                            group = groupMethod.Invoke(def, null);
                        }
                    }
                }

                if (specType == null)
                {
                    var typeProp = def.GetType().GetProperty("ParameterType");
                    if (typeProp != null)
                    {
                        specType = typeProp.GetValue(def);
                    }
                    else
                    {
                        var typeMethod = def.GetType().GetMethod("GetDataType");
                        if (typeMethod != null)
                        {
                            specType = typeMethod.Invoke(def, null);
                        }
                    }
                }
            }

            if (group == null || specType == null)
            {
                var defaultSpec = GetDefaultGroupAndType();
                group ??= defaultSpec.Group;
                specType ??= defaultSpec.SpecType;
            }

            return new ParameterDefinitionSpec(group, specType);
        }

        private static bool InjectParameterToFamily(FamilyManager famManager, string paramName, ParameterDefinitionSpec spec)
        {
            if (famManager == null || spec == null || spec.Group == null || spec.SpecType == null) return false;

            var addParamMethod = famManager.GetType().GetMethods()
                .FirstOrDefault(m => m.Name == "AddParameter" && m.GetParameters().Length == 4);

            if (addParamMethod != null)
            {
                try
                {
                    addParamMethod.Invoke(famManager, new object[] { paramName, spec.Group, spec.SpecType, false });
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }
            return false;
        }

        private void ExportStep6ResultsSummary(List<MergeExecutionReport> summaries)
        {
            try
            {
                var settings = new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented
                };
                string json = JsonConvert.SerializeObject(summaries, settings);
                string assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string? assemblyDir = Path.GetDirectoryName(assemblyPath);
                if (assemblyDir != null)
                {
                    string logPath = Path.Combine(assemblyDir, "MergeDuplicates_Step6_ExecutionResult.json");
                    File.WriteAllText(logPath, json);
                }
            }
            catch (Exception)
            {
                // Fail silently
            }
        }
        private string SanitizeRevitTypeName(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "Unnamed_Type";
            char[] illegalChars = new char[] { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '\'', '~' };
            string sanitized = input;
            foreach (char c in illegalChars)
            {
                sanitized = sanitized.Replace(c.ToString(), string.Empty);
            }
            sanitized = sanitized.Trim();
            return string.IsNullOrWhiteSpace(sanitized) ? "Unnamed_Type" : sanitized;
        }
        /// <summary>
        /// Gets the name of the external event handler.
        /// </summary>
        /// <returns>A string name of the handler.</returns>
        public string GetName()
        {
            return "Process Merge External Event Handler";
        }
    }
}



