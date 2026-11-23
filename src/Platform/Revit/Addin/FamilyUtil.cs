using System;
using System.Linq;
using Autodesk.Revit.DB;
using RevitView = Autodesk.Revit.DB.View;

namespace Synthetic.Platform.Revit
{
    /// <summary>
    /// Shared Revit family utility helpers.
    /// </summary>
    public class FamilyUtil
    {
        /// <summary>
        /// Marks a family document as changed/dirty by creating and deleting a temporary text note
        /// through <see cref="DocumentTransactionExtensions"/> with <see cref="FailurePipelines.ObserveOnly()"/>.
        /// </summary>
        /// <param name="doc">The host Revit document (retained for caller compatibility).</param>
        /// <param name="familyDoc">The family document to mark as changed.</param>
        public static void SetIsChanged(Document doc, Document familyDoc)
        {
            if (familyDoc == null) throw new ArgumentNullException(nameof(familyDoc));

            ElementId? viewId = new FilteredElementCollector(familyDoc)
                .OfClass(typeof(ViewPlan))
                .ToElementIds()
                .FirstOrDefault();

            if (viewId == null || viewId == ElementId.InvalidElementId)
            {
                viewId = new FilteredElementCollector(familyDoc)
                    .OfClass(typeof(ViewSheet))
                    .ToElementIds()
                    .FirstOrDefault();
            }

            if (viewId == null || viewId == ElementId.InvalidElementId)
            {
                viewId = new FilteredElementCollector(familyDoc)
                    .OfClass(typeof(RevitView))
                    .Cast<RevitView>()
                    .Where(v => !v.IsTemplate)
                    .Select(v => v.Id)
                    .FirstOrDefault();
            }

            if (viewId == null || viewId == ElementId.InvalidElementId)
            {
                return;
            }

            ElementId textTypeId = new FilteredElementCollector(familyDoc)
                .OfClass(typeof(TextNoteType))
                .ToElementIds()
                .FirstOrDefault() ?? ElementId.InvalidElementId;

            if (textTypeId == ElementId.InvalidElementId)
            {
                return;
            }

            ElementId? noteId = null;
            try
            {
                familyDoc.ExecuteTransaction("Temp text note", FailurePipelines.ObserveOnly(), () =>
                {
                    TextNote note = TextNote.Create(familyDoc, viewId, XYZ.Zero, "test", textTypeId);
                    noteId = note.Id;
                });
            }
            catch
            {
                return;
            }

            if (noteId != null && noteId != ElementId.InvalidElementId)
            {
                try
                {
                    familyDoc.ExecuteTransaction("Delete temp text note", FailurePipelines.ObserveOnly(), () =>
                    {
                        familyDoc.Delete(noteId);
                    });
                }
                catch
                {
                }
            }
        }
    }
}
