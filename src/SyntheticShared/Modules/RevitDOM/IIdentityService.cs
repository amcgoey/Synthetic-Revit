using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Synthetic.Modules.RevitDOM
{
    /// <summary>
    /// Governs identity extraction (mapping Revit ElementId to ElementIdModel)
    /// and resolution (finding live Revit elements/IDs from an ElementIdModel).
    /// </summary>
    public interface IIdentityService
    {
        /// <summary>
        /// Extracts an ElementId into an ElementIdModel state container.
        /// </summary>
        ElementIdModel ToModel(ElementId id, Document doc, bool isTemplate = false);

        /// <summary>
        /// Resolves an ElementIdModel to its native Revit ElementId.
        /// </summary>
        ElementId ResolveElementId(ElementIdModel model, Document doc);

        /// <summary>
        /// Resolves an ElementIdModel to a native Revit Element.
        /// </summary>
        Element ResolveElement(ElementIdModel model, Document doc);

        /// <summary>
        /// Resolves a collection of ElementIdModels into their native Revit Elements.
        /// </summary>
        IEnumerable<Element> GetElementsByElementIdModels(Document doc, IEnumerable<ElementIdModel> identifiers);
    }
}
