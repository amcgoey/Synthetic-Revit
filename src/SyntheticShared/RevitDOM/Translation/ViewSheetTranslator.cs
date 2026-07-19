using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Synthetic.Shared;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Translation
{
    /// <summary>
    /// Translator governing the extraction and injection of Revit ViewSheet elements, inheriting from ViewTranslator.
    /// </summary>
    internal class ViewSheetTranslator : ViewTranslator, IModelTranslator<ViewSheet, ViewSheetModel>
    {
        private readonly IIdentityService _identityService;

        public ViewSheetTranslator(IIdentityService identityService) : base(identityService)
        {
            _identityService = identityService ?? throw new ArgumentNullException(nameof(identityService));
        }

        public void ExtractSpecifics(ViewSheet viewSheet, ViewSheetModel model, Document doc)
        {
            if (viewSheet == null) throw new ArgumentNullException(nameof(viewSheet));
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            // Extract base class properties first
            base.ExtractSpecifics(viewSheet, model, doc);
        }

        public ViewSheet InjectSpecifics(ViewSheetModel model, ViewSheet? revitElement, Document doc)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            if (revitElement == null)
            {
                revitElement = ViewSheet.Create(doc, ElementId.InvalidElementId);
            }

            // Inject base class properties first
            revitElement = (ViewSheet?)base.InjectSpecifics(model, revitElement, doc);
            return revitElement!;
        }

        #region Explicit IModelTranslator implementations

        void IModelTranslator.ExtractSpecifics(object revitElement, ObjectModel model, Document doc)
        {
            ExtractSpecifics((ViewSheet)revitElement, (ViewSheetModel)model, doc);
        }

        object? IModelTranslator.InjectSpecifics(ObjectModel model, object? revitElement, Document doc)
        {
            return InjectSpecifics((ViewSheetModel)model, (ViewSheet?)revitElement, doc);
        }

        #endregion
    }
}
