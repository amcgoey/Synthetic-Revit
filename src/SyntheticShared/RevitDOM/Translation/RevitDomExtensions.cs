using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Translation
{
    public static class RevitDomExtensions
    {
        public static MaterialModel ToModel(this Material material, Document doc, bool isTemplate = false, IIdentityService? identityService = null)
        {
            if (material == null) return null;
            var model = new MaterialModel();
            model.Populate(material, isTemplate);
            var translator = new MaterialTranslator(identityService ?? new RevitIdentityService());
            translator.ExtractSpecifics(material, model, doc);
            return model;
        }

        public static PlanViewRangeModel ToModel(this PlanViewRange viewRange, Document doc)
        {
            if (viewRange == null) return null;
            var model = new PlanViewRangeModel();
            model.TopOffset = viewRange.GetOffset(PlanViewPlane.TopClipPlane);
            model.TopLevelId = viewRange.GetLevelId(PlanViewPlane.TopClipPlane).ToModel(doc, false);

            model.CutPlaneOffset = viewRange.GetOffset(PlanViewPlane.CutPlane);
            model.CutPlaneLevelId = viewRange.GetLevelId(PlanViewPlane.CutPlane).ToModel(doc, false);

            model.BottomOffset = viewRange.GetOffset(PlanViewPlane.BottomClipPlane);
            model.BottomLevelId = viewRange.GetLevelId(PlanViewPlane.BottomClipPlane).ToModel(doc, false);

            model.ViewDepthOffset = viewRange.GetOffset(PlanViewPlane.ViewDepthPlane);
            model.ViewDepthLevelId = viewRange.GetLevelId(PlanViewPlane.ViewDepthPlane).ToModel(doc, false);

            return model;
        }

        public static ParameterFilterElementModel ToModel(this ParameterFilterElement filter, Document doc, IIdentityService? identityService = null)
        {
            if (filter == null) return null;
            var model = new ParameterFilterElementModel();
            model.Populate(filter, false);
            model.Name = filter.Name;
            model.Class = "Autodesk.Revit.DB.ParameterFilterElement";

            var translator = new ParameterFilterElementTranslator(identityService ?? new RevitIdentityService());
            translator.ExtractSpecifics(filter, model, doc);

            return model;
        }

        public static FilledRegionTypeModel ToModel(this FilledRegionType regionType, bool isTemplate = false, IIdentityService? identityService = null)
        {
            if (regionType == null) return null;
            var model = new FilledRegionTypeModel();
            model.Populate(regionType, isTemplate);
            var translator = new FilledRegionTypeTranslator(identityService ?? new RevitIdentityService());
            translator.ExtractSpecifics(regionType, model, regionType.Document);
            return model;
        }

        public static HostObjTypeModel ToModel(this HostObjAttributes hostType, bool isTemplate = false, IIdentityService? identityService = null)
        {
            if (hostType == null) return null;
            var model = new HostObjTypeModel();
            model.Populate(hostType, isTemplate);
            model.Name = hostType.Name;
            model.Class = hostType.GetType().FullName;

            var translator = new HostObjTypeTranslator(identityService ?? new RevitIdentityService());
            translator.ExtractSpecifics(hostType, model, hostType.Document);

            return model;
        }

        public static ListModel ToModel(this IEnumerable<Element> elements, bool isTemplate)
        {
            if (elements == null) return null;
            var model = new ListModel();
            model.Elements = elements.Select(e => e.ToModel(isTemplate)).ToList();
            return model;
        }

        public static ListMaterialModel ToModel(this IEnumerable<Material> materials, bool isTemplate, IIdentityService? identityService = null)
        {
            if (materials == null) return null;
            var model = new ListMaterialModel();
            model.Elements = materials.Select(m => m.ToModel(m.Document, isTemplate, identityService)).ToList();
            return model;
        }

        public static GridTypeModel ToModel(this GridType type, bool isTemplate = false)
        {
            if (type == null) return null;
            var model = new GridTypeModel();
            model.Populate(type, isTemplate);
            var translator = new GridTypeTranslator();
            translator.ExtractSpecifics(type, model, type.Document);
            return model;
        }

        public static LevelTypeModel ToModel(this LevelType type, bool isTemplate = false)
        {
            if (type == null) return null;
            var model = new LevelTypeModel();
            model.Populate(type, isTemplate);
            var translator = new LevelTypeTranslator();
            translator.ExtractSpecifics(type, model, type.Document);
            return model;
        }

        public static CurtainSystemTypeModel ToModel(this CurtainSystemType type, bool isTemplate = false, IIdentityService? identityService = null)
        {
            if (type == null) return null;
            var model = new CurtainSystemTypeModel();
            model.Populate(type, isTemplate);
            model.Name = type.Name;
            model.Class = "Autodesk.Revit.DB.CurtainSystemType";

            var translator = new HostObjTypeTranslator(identityService ?? new RevitIdentityService());
            translator.ExtractSpecifics(type, model, type.Document);
            return model;
        }

        public static MullionTypeModel ToModel(this MullionType type, bool isTemplate = false, IIdentityService? identityService = null)
        {
            if (type == null) return null;
            var model = new MullionTypeModel();
            model.Populate(type, isTemplate);
            model.Name = type.Name;
            model.Class = "Autodesk.Revit.DB.MullionType";

            var translator = new HostObjTypeTranslator(identityService ?? new RevitIdentityService());
            translator.ExtractSpecifics(type, model, type.Document);
            return model;
        }

        public static FasciaTypeModel ToModel(this FasciaType type, bool isTemplate = false, IIdentityService? identityService = null)
        {
            if (type == null) return null;
            var model = new FasciaTypeModel();
            model.Populate(type, isTemplate);
            model.Name = type.Name;
            model.Class = "Autodesk.Revit.DB.FasciaType";

            var translator = new HostObjTypeTranslator(identityService ?? new RevitIdentityService());
            translator.ExtractSpecifics(type, model, type.Document);
            return model;
        }

        public static GutterTypeModel ToModel(this GutterType type, bool isTemplate = false, IIdentityService? identityService = null)
        {
            if (type == null) return null;
            var model = new GutterTypeModel();
            model.Populate(type, isTemplate);
            model.Name = type.Name;
            model.Class = "Autodesk.Revit.DB.GutterType";

            var translator = new HostObjTypeTranslator(identityService ?? new RevitIdentityService());
            translator.ExtractSpecifics(type, model, type.Document);
            return model;
        }

        public static ViewFamilyTypeModel ToModel(this ViewFamilyType type, bool isTemplate = false)
        {
            if (type == null) return null;
            var model = new ViewFamilyTypeModel();
            model.Populate(type, isTemplate);
            return model;
        }

        public static ElementTypeModel ToModel(this ElementType type, bool isTemplate = false)
        {
            if (type == null) return null;
            var model = new ElementTypeModel();
            model.Populate(type, isTemplate);
            return model;
        }

        public static DimensionTypeModel ToModel(this DimensionType type, bool isTemplate = false)
        {
            if (type == null) return null;
            var model = new DimensionTypeModel();
            model.Populate(type, isTemplate);
            model.DimensionStyle = type.StyleType.ToString();
            model.DimensionType = type;
            return model;
        }

        public static FillPatternElementModel ToModel(this FillPatternElement type, bool isTemplate = false)
        {
            if (type == null) return null;
            var model = new FillPatternElementModel();
            model.Populate(type, isTemplate);
            var translator = new FillPatternTranslator();
            translator.ExtractSpecifics(type, model, type.Document);
            return model;
        }

        public static LinePatternElementModel ToModel(this LinePatternElement type, bool isTemplate = false)
        {
            if (type == null) return null;
            var model = new LinePatternElementModel();
            model.Populate(type, isTemplate);
            var translator = new LinePatternTranslator();
            translator.ExtractSpecifics(type, model, type.Document);
            return model;
        }

        public static PropertySetElementModel ToModel(this PropertySetElement type, bool isTemplate = false)
        {
            if (type == null) return null;
            var model = new PropertySetElementModel();
            model.Populate(type, isTemplate);
            return model;
        }

        public static BrowserOrganizationModel ToModel(this BrowserOrganization type, bool isTemplate = false)
        {
            if (type == null) return null;
            var model = new BrowserOrganizationModel();
            model.Populate(type, isTemplate);
            var translator = new BrowserOrganizationTranslator();
            translator.ExtractSpecifics(type, model, type.Document);
            return model;
        }

        public static ParameterElementModel ToModel(this ParameterElement type, bool isTemplate = false)
        {
            if (type == null) return null;
            var model = new ParameterElementModel();
            model.Populate(type, isTemplate);
            var translator = new ParameterElementTranslator();
            translator.ExtractSpecifics(type, model, type.Document);
            return model;
        }

        public static T DeepClone<T>(this T source) where T : ObjectModel
        {
            if (source == null) return default;
            string json = Infrastructure.Serialization.Json.EncodeMinimal(source);
            return (T)JsonConvert.DeserializeObject(json, source.GetType())!;
        }
    }
}
