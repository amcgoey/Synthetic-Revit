using System;
using System.Linq;
using Autodesk.Revit.DB;

namespace Synthetic.Modules.RevitDOM
{
    /// <summary>
    /// Translator governing the extraction and injection of FilledRegionType properties,
    /// implementing the "Elevate and Purge" strategy for graphic pattern and color overrides.
    /// </summary>
    internal class FilledRegionTypeTranslator : TemplateDuplicatingTranslator<FilledRegionType, FilledRegionTypeModel>
    {
        private readonly IIdentityService _identityService;

        public FilledRegionTypeTranslator(IIdentityService identityService)
        {
            _identityService = identityService ?? throw new ArgumentNullException(nameof(identityService));
        }

        /// <summary>
        /// Extracts graphic override properties from a native Revit FilledRegionType,
        /// populates the POCO properties, and purges redundant raw parameters.
        /// </summary>
        public override void ExtractSpecifics(FilledRegionType revitElement, FilledRegionTypeModel model, Document doc)
        {
            if (revitElement == null) throw new ArgumentNullException(nameof(revitElement));
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            // Elevate graphic override properties to explicit POCO properties
            model.ForegroundPatternColor = revitElement.ForegroundPatternColor.ToModel();
            model.BackgroundPatternColor = revitElement.BackgroundPatternColor.ToModel();

            model.ForegroundPatternId = _identityService.ToModel(revitElement.ForegroundPatternId, doc, model.IsTemplate);
            model.BackgroundPatternId = _identityService.ToModel(revitElement.BackgroundPatternId, doc, model.IsTemplate);

            // Aggressively purge redundant raw parameters to prevent duplicate/conflicting data in JSON standard
            if (model.Parameters != null)
            {
                model.Parameters.RemoveAll(p =>
                    p.Id == (long)BuiltInParameter.FOREGROUND_PATTERN_COLOR_PARAM ||
                    p.Id == (long)BuiltInParameter.BACKGROUND_PATTERN_COLOR_PARAM ||
                    p.Id == (long)BuiltInParameter.FOREGROUND_ANY_PATTERN_ID_PARAM ||
                    p.Id == (long)BuiltInParameter.BACKGROUND_DRAFT_PATTERN_ID_PARAM);
            }
        }

        /// <summary>
        /// Injects graphic override properties from the POCO model into a native Revit FilledRegionType.
        /// Duplicates from template if it does not yet exist.
        /// </summary>
        public override FilledRegionType? InjectSpecifics(FilledRegionTypeModel model, FilledRegionType? revitElement, Document doc)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            // Call base class to ensure a native FilledRegionType element exists (duplicating if needed)
            var element = base.InjectSpecifics(model, revitElement, doc);
            if (element == null) return null;

            // Apply elevated properties back to Revit FilledRegionType
            if (model.ForegroundPatternColor != null)
            {
                element.ForegroundPatternColor = model.ForegroundPatternColor.ToColor();
            }

            if (model.BackgroundPatternColor != null)
            {
                element.BackgroundPatternColor = model.BackgroundPatternColor.ToColor();
            }

            // Resolve and set Foreground Pattern ID
            if (model.ForegroundPatternId != null)
            {
                var fpId = _identityService.ResolveElementId(model.ForegroundPatternId, doc);
                if (fpId != ElementId.InvalidElementId)
                {
                    element.ForegroundPatternId = fpId;
                }
                else
                {
                    SerializationResultModel.LogWarning($"Could not resolve Foreground Pattern: {model.ForegroundPatternId.Name}");
                }
            }
            else
            {
                element.ForegroundPatternId = ElementId.InvalidElementId;
            }

            // Resolve and set Background Pattern ID
            if (model.BackgroundPatternId != null)
            {
                var bpId = _identityService.ResolveElementId(model.BackgroundPatternId, doc);
                if (bpId != ElementId.InvalidElementId)
                {
                    element.BackgroundPatternId = bpId;
                }
                else
                {
                    SerializationResultModel.LogWarning($"Could not resolve Background Pattern: {model.BackgroundPatternId.Name}");
                }
            }
            else
            {
                element.BackgroundPatternId = ElementId.InvalidElementId;
            }

            return element;
        }
    }
}
