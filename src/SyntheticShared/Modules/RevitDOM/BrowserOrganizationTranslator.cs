using System;
using Autodesk.Revit.DB;

namespace Synthetic.Modules.RevitDOM
{
    internal class BrowserOrganizationTranslator : IModelTranslator<BrowserOrganization, BrowserOrganizationModel>
    {
        public void ExtractSpecifics(BrowserOrganization revitElement, BrowserOrganizationModel model, Document doc)
        {
            // Empty. ParameterEngine handles standard parameters.
        }

        public BrowserOrganization? InjectSpecifics(BrowserOrganizationModel model, BrowserOrganization? revitElement, Document doc)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            if (revitElement != null)
            {
                return revitElement;
            }

            // Return null and log warning as programmatic creation is not allowed by Revit API
            SerializationResultModel.LogWarning("Revit API prevents the programmatic creation of new Browser Organizations.");
            return null;
        }

        #region Explicit IModelTranslator implementations

        void IModelTranslator.ExtractSpecifics(object revitElement, ObjectModel model, Document doc)
        {
            ExtractSpecifics((BrowserOrganization)revitElement, (BrowserOrganizationModel)model, doc);
        }

        object? IModelTranslator.InjectSpecifics(ObjectModel model, object? revitElement, Document doc)
        {
            return InjectSpecifics((BrowserOrganizationModel)model, (BrowserOrganization?)revitElement, doc);
        }

        #endregion
    }
}
