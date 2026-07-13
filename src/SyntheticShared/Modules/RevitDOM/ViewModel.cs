using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Newtonsoft.Json;
using Synthetic.Infrastructure.Serialization;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Autodesk.Revit.DB;
using View = Autodesk.Revit.DB.View;

namespace Synthetic.Modules.RevitDOM
{
    /// <summary>
    /// Represents a model representation of a Revit View, inheriting from ElementModel.
    /// </summary>
    public class ViewModel : ElementModel
    {
        #region Public Properties

        /// <summary>
        /// Key name for Category overrides.
        /// </summary>
        public const string CategoryKey = "Category";

        /// <summary>
        /// Key name for Hidden status.
        /// </summary>
        public const string HiddenKey = "Hidden";

        /// <summary>
        /// Key name for Override Graphic Settings.
        /// </summary>
        public const string OverrideKey = "OverrideGraphicSettings";
        

        /// <summary>
        /// Gets or sets a value indicating whether this model is a template view/template.
        /// </summary>
        public new bool IsTemplate { get; set; }

        /// <summary>
        /// Gets or sets the list of parameter IDs that are not controlled by this template.
        /// </summary>
        public List<int> NonControlledParameterIds { get; set; } = new List<int>();

        /// <summary>
        /// Gets or sets the associated Revit View Template object. Ignored in JSON.
        /// </summary>
        [JsonIgnoreAttribute]
        public object? ViewTemplate
        {
            get => this.View;
            set => this.View = value;
        }

        /// <summary>
        /// Gets or sets the display style of the view.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public EnumModel? DisplayStyle { get; set; }
        
        /// <summary>
        /// Gets or sets the shadow intensity.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? ShadowIntensity { get; set; }

        /// <summary>
        /// Gets or sets the sunlight intensity.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? SunlightIntensity { get; set; }

        /// <summary>
        /// Gets or sets the view scale.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? Scale { get; set; }

        /// <summary>
        /// Gets or sets the view detail level.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public EnumModel? DetailLevel { get; set; }

        /// <summary>
        /// Gets or sets the view discipline.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public EnumModel? Discipline { get; set; }

        /// <summary>
        /// Gets or sets the view phase.
        /// </summary>
        public ElementIdModel? Phase { get; set; }

        /// <summary>
        /// Gets or sets the view phase filter.
        /// </summary>
        public ElementIdModel? PhaseFilter { get; set; }

        /// <summary>
        /// Gets or sets the list of view filter overrides applied to this view.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<ViewFilterOverrideModel>? ViewFilterOverrides { get; set; }

        /// <summary>
        /// Gets or sets the list of graphic overrides applied to categories in this view.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<CategoryGraphicOverridesModel>? CategoryGraphicOverrides { get; set; }



        /// <summary>
        /// Gets or sets the associated Level ID (only applies to plan views).
        /// </summary>
        public ElementIdModel? LevelId { get; set; }

        /// <summary>
        /// Gets or sets the associated ViewFamilyType ID.
        /// </summary>
        public ElementIdModel? ViewFamilyTypeId { get; set; }

        /// <summary>
        /// Gets or sets the view crop box (only applies to section, elevation, and plan views).
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public BoundingBoxXYZModel? CropBox { get; set; }

        /// <summary>
        /// Gets or sets the associated Scope Box ID.
        /// </summary>
        public ElementIdModel? ScopeBoxId { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the crop box is active.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? CropBoxActive { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the crop box is visible.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? CropBoxVisible { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the annotation crop is active.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? AnnotationCropActive { get; set; }

        /// <summary>
        /// Gets or sets far clip active status.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? FarClipSettings { get; set; }

        /// <summary>
        /// Gets or sets far clip offset value.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public double? FarClipOffset { get; set; }



        /// <summary>
        /// Gets or sets the parts visibility enum value.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public EnumModel? PartsVisibility { get; set; }

        /// <summary>
        /// Gets or sets the associated Revit View. Ignored in JSON.
        /// </summary>
        [JsonIgnoreAttribute]
        public object? View { get; set; }

        /// <summary>
        /// Gets or sets the underlying Revit Element. Ignored in JSON.
        /// </summary>
        [JsonIgnoreAttribute]
        public override object? Element
        {
            get => this.View;
            set => this.View = value;
        }

        #endregion
        #region Public Constructors

        /// <summary>
        /// Initializes a new instance of the ViewModel class.
        /// </summary>
        public ViewModel () { }

        #endregion
    }

    public static class ViewExtensions
    {
        public static ViewModel ToModel(this View view, bool isTemplate = false)
        {
            if (view == null) return null;
            
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
            model.Class = view.GetType().FullName;

            translator.ExtractSpecifics(view, model, view.Document);
            return model;
        }

        public static ViewModel ToTemplateModel(this View view, bool isTemplate = true)
        {
            if (view == null) return null;
            
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
            model.Class = view.GetType().FullName;
            model.ViewTemplate = view;

            translator.ExtractSpecifics(view, model, view.Document);
            return model;
        }
    }
}

