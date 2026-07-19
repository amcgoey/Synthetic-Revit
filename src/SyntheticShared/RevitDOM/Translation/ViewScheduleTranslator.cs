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
    /// Translator governing the extraction and injection of Revit ViewSchedule elements, inheriting from ViewTranslator.
    /// </summary>
    internal class ViewScheduleTranslator : ViewTranslator, IModelTranslator<ViewSchedule, ViewScheduleModel>
    {
        private readonly IIdentityService _identityService;

        public ViewScheduleTranslator(IIdentityService identityService) : base(identityService)
        {
            _identityService = identityService ?? throw new ArgumentNullException(nameof(identityService));
        }

        public void ExtractSpecifics(ViewSchedule viewSchedule, ViewScheduleModel model, Document doc)
        {
            if (viewSchedule == null) throw new ArgumentNullException(nameof(viewSchedule));
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            // Extract base class properties first
            base.ExtractSpecifics(viewSchedule, model, doc);
        }

        public ViewSchedule InjectSpecifics(ViewScheduleModel model, ViewSchedule? revitElement, Document doc)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            if (revitElement == null)
            {
                ElementId catId = new ElementId((int)BuiltInCategory.OST_Rooms);
                revitElement = ViewSchedule.CreateSchedule(doc, catId);
            }

            // Inject base class properties first
            revitElement = (ViewSchedule?)base.InjectSpecifics(model, revitElement, doc);
            return revitElement!;
        }

        #region Explicit IModelTranslator implementations

        void IModelTranslator.ExtractSpecifics(object revitElement, ObjectModel model, Document doc)
        {
            ExtractSpecifics((ViewSchedule)revitElement, (ViewScheduleModel)model, doc);
        }

        object? IModelTranslator.InjectSpecifics(ObjectModel model, object? revitElement, Document doc)
        {
            return InjectSpecifics((ViewScheduleModel)model, (ViewSchedule?)revitElement, doc);
        }

        #endregion
    }
}
