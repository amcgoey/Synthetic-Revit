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
using Synthetic.Modules.StandardsManagement.Models;
using Synthetic.Modules.StandardsManagement.Utilities;
using Synthetic.Shared.RevitAPI;

namespace Synthetic.Modules.StandardsManagement.Engine
{
    public class RevitFamilyEnforcer : IFamilyEnforcer
    {
        private readonly IStandardSerializationEngine _serializationEngine;

        public RevitFamilyEnforcer(IStandardSerializationEngine serializationEngine)
        {
            _serializationEngine = serializationEngine ?? throw new ArgumentNullException(nameof(serializationEngine));
        }

        public void Enforce(
            Document doc,
            IEnumerable<ElementModel> standards,
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
#elif REVIT2024 || REVIT2025
                    if (options.CategoryFilter == "Title Blocks Only" && (family.FamilyCategory == null || family.FamilyCategory.Id.Value != (long)BuiltInCategory.OST_TitleBlocks))
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

                using (TransactionGroup familyTg = new TransactionGroup(familyDoc, "Update Family Standards"))
                {
                    familyTg.Start();

                    _serializationEngine.ToRevit(familyStandards, familyDoc, null, cancellationToken, new DeleteWarningsPreprocessor());

                    if (options.PurgeUnusedStyleTypes)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            familyTg.RollBack();
                            throw new OperationCanceledException();
                        }
                        try
                        {
#if REVIT2022
                            // Do nothing
#elif REVIT2023 || REVIT2024 || REVIT2025
                            DocumentUtil.Purge(parentDoc.Application, familyDoc);
#else
                            DocumentUtil.Purge(parentDoc.Application, familyDoc);
#endif
                        }
                        catch (Exception)
                        {
                        }
                    }

                    familyTg.Assimilate();
                }

                using (Transaction parentTx = new Transaction(parentDoc, "Load Family"))
                {
                    FailureHandlingOptions parentOptions = parentTx.GetFailureHandlingOptions();
                    parentOptions.SetFailuresPreprocessor(new DeleteWarningsPreprocessor());
                    parentTx.SetFailureHandlingOptions(parentOptions);

                    parentTx.Start();

                    try
                    {
                        Synthetic.Shared.RevitAPI.FamilyUtil.SetIsChanged(parentDoc, familyDoc);
                        familyDoc.LoadFamily(parentDoc, new ImportFamilyLoadOptions());
                    }
                    catch (Exception)
                    {
                    }

                    parentTx.Commit();
                }
            }
            finally
            {
                try
                {
                    familyDoc.Close(false);
                }
                catch (Exception)
                {
                }
            }
        }

        private class DeleteWarningsPreprocessor : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
            {
                IList<FailureMessageAccessor> failList = failuresAccessor.GetFailureMessages();

                if (failList.Count == 0)
                {
                    return FailureProcessingResult.Continue;
                }

                foreach (FailureMessageAccessor failure in failList)
                {
                    failuresAccessor.DeleteWarning(failure);
                }
                return FailureProcessingResult.ProceedWithCommit;
            }
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
