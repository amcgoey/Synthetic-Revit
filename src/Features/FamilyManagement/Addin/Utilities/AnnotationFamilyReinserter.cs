using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;

namespace Synthetic.Features.FamilyManagement.Utilities
{
    /// <summary>
    /// Forces annotation families in a Revit project to re-mark as changed and reload cleanly.
    /// </summary>
    public static class AnnotationFamilyReinserter
    {
        /// <summary>
        /// Forces all annotation families in the project to be reinserted/reloaded to ensure they are up to date.
        /// </summary>
        /// <param name="doc">The Revit Document.</param>
        public static void ForceReinsertAnnotation(Document doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            IList<Element> families = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .ToElements();

            IFamilyLoadOptions opt = new ForceReinsertFamilyLoadOptions();
            var familiesToReinsert = new List<Tuple<Family, ElementId>>();

            foreach (Family family in families)
            {
                if (family.FamilyCategory != null && family.FamilyCategory.CategoryType == CategoryType.Annotation)
                {
                    if (doc.IsWorkshared)
                    {
                        IList<WorksetId> worksetIds = new List<WorksetId> { family.WorksetId };
                        WorksharingUtils.CheckoutWorksets(doc, worksetIds);
                    }
                    familiesToReinsert.Add(new Tuple<Family, ElementId>(family, family.Id));
                }
            }

            foreach (Tuple<Family, ElementId> familyTuple in familiesToReinsert)
            {
                Family family = familyTuple.Item1;
                ElementId familyId = familyTuple.Item2;
                Element searchElem = doc.GetElement(familyId);
                if (searchElem != null && family.IsEditable)
                {
                    Document familyDoc = doc.EditFamily(family);
                    EventHandler<FailuresProcessingEventArgs> warningsHandler = ResolveWarnings;
                    doc.Application.FailuresProcessing += warningsHandler;
                    try
                    {
                        Synthetic.Platform.Revit.FamilyUtil.SetIsChanged(doc, familyDoc);
                        familyDoc.LoadFamily(doc, opt);
                    }
                    finally
                    {
                        doc.Application.FailuresProcessing -= warningsHandler;
                        familyDoc.Close(false);
                    }
                }
            }
        }

        private static void ResolveWarnings(object? sender, FailuresProcessingEventArgs e)
        {
            FailuresAccessor fa = e.GetFailuresAccessor();
            IList<FailureMessageAccessor> failList = fa.GetFailureMessages();

            if (failList == null || failList.Count == 0)
            {
                e.SetProcessingResult(FailureProcessingResult.Continue);
                return;
            }

            bool deletedAny = false;
            foreach (FailureMessageAccessor failure in failList)
            {
                FailureDefinitionId failID = failure.GetFailureDefinitionId();
                if (failID == BuiltInFailures.RoomFailures.RoomNotEnclosed)
                {
                    fa.DeleteWarning(failure);
                    deletedAny = true;
                }
            }

            e.SetProcessingResult(deletedAny ? FailureProcessingResult.ProceedWithCommit : FailureProcessingResult.Continue);
        }

        private sealed class ForceReinsertFamilyLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = true;
                return true;
            }

            public bool OnSharedFamilyFound(
                Family sharedFamily,
                bool familyInUse,
                out FamilySource source,
                out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = true;
                return true;
            }
        }
    }
}
