using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using View = Autodesk.Revit.DB.View;

namespace Synthetic.RevitDOM.Translation
{
    public static class ViewExtensions
    {
        public static ViewModel ToModel(this View view, bool isTemplate = false)
        {
            if (view == null) return null!;

            ViewModel model;
            IModelTranslator translator;

            if (view is ViewPlan)
            {
                model = new ViewPlanModel();
                translator = new ViewPlanTranslator(new RevitIdentityService());
            }
            else if (view is ViewSheet)
            {
                model = new ViewSheetModel();
                translator = new ViewSheetTranslator(new RevitIdentityService());
            }
            else if (view is ViewSchedule)
            {
                model = new ViewScheduleModel();
                translator = new ViewScheduleTranslator(new RevitIdentityService());
            }
            else
            {
                model = new ViewModel();
                translator = new ViewTranslator(new RevitIdentityService());
            }

            model.Populate(view, isTemplate);
            model.Name = view.Name;
            model.Class = view.GetType().FullName ?? string.Empty;

            translator.ExtractSpecifics(view, model, view.Document);
            return model;
        }

        public static ViewModel ToTemplateModel(this View view, bool isTemplate = true)
        {
            if (view == null) return null!;

            ViewModel model;
            IModelTranslator translator;

            if (view is ViewPlan)
            {
                model = new ViewPlanModel();
                translator = new ViewPlanTranslator(new RevitIdentityService());
            }
            else if (view is ViewSheet)
            {
                model = new ViewSheetModel();
                translator = new ViewSheetTranslator(new RevitIdentityService());
            }
            else if (view is ViewSchedule)
            {
                model = new ViewScheduleModel();
                translator = new ViewScheduleTranslator(new RevitIdentityService());
            }
            else
            {
                model = new ViewModel();
                translator = new ViewTranslator(new RevitIdentityService());
            }

            model.Populate(view, isTemplate);
            model.Name = view.Name;
            model.Class = view.GetType().FullName ?? string.Empty;
            model.ViewTemplate = view;

            translator.ExtractSpecifics(view, model, view.Document);
            return model;
        }
    }
}
