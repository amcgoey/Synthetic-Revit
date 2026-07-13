using System;
using Autodesk.Revit.DB;

namespace Synthetic.Modules.RevitDOM
{
    internal class MaterialTranslator : IModelTranslator<Material, MaterialModel>
    {
        private readonly IIdentityService _identityService;

        public MaterialTranslator(IIdentityService identityService)
        {
            _identityService = identityService ?? throw new ArgumentNullException(nameof(identityService));
        }

        public void ExtractSpecifics(Material revitElement, MaterialModel model, Document doc)
        {
            if (revitElement == null) throw new ArgumentNullException(nameof(revitElement));
            if (model == null) throw new ArgumentNullException(nameof(model));

            // Extract explicit color properties
            model.Color = revitElement.Color?.ToModel();
            model.CutForegroundPatternColor = revitElement.CutForegroundPatternColor?.ToModel();
            model.CutBackgroundPatternColor = revitElement.CutBackgroundPatternColor?.ToModel();
            model.SurfaceForegroundPatternColor = revitElement.SurfaceForegroundPatternColor?.ToModel();
            model.SurfaceBackgroundPatternColor = revitElement.SurfaceBackgroundPatternColor?.ToModel();

            // Extract explicit pattern ID properties
            model.CutForegroundPatternId = _identityService.ToModel(revitElement.CutForegroundPatternId, doc, false);
            model.CutBackgroundPatternId = _identityService.ToModel(revitElement.CutBackgroundPatternId, doc, false);
            model.SurfaceForegroundPatternId = _identityService.ToModel(revitElement.SurfaceForegroundPatternId, doc, false);
            model.SurfaceBackgroundPatternId = _identityService.ToModel(revitElement.SurfaceBackgroundPatternId, doc, false);

            // Extract AppearanceAssetId
            model.AppearanceAssetId = _identityService.ToModel(revitElement.AppearanceAssetId, doc, false);

            // Extract nested assets via MaterialAssetEngine delegation
            model.AppearanceAsset = MaterialAssetEngine.ExtractAppearance(revitElement);
            model.StructuralAsset = MaterialAssetEngine.ExtractStructural(revitElement);
            model.ThermalAsset = MaterialAssetEngine.ExtractThermal(revitElement);
        }

        public Material? InjectSpecifics(MaterialModel model, Material? revitElement, Document doc)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            if (revitElement == null)
            {
                var matId = Material.Create(doc, model.Name);
                revitElement = doc.GetElement(matId) as Material;
                if (revitElement == null)
                {
                    throw new InvalidOperationException($"Failed to create material '{model.Name}'.");
                }
            }

            // Map and assign explicit color properties
            if (model.Color != null)
            {
                revitElement.Color = model.Color.ToColor();
            }
            if (model.CutForegroundPatternColor != null)
            {
                revitElement.CutForegroundPatternColor = model.CutForegroundPatternColor.ToColor();
            }
            if (model.CutBackgroundPatternColor != null)
            {
                revitElement.CutBackgroundPatternColor = model.CutBackgroundPatternColor.ToColor();
            }
            if (model.SurfaceForegroundPatternColor != null)
            {
                revitElement.SurfaceForegroundPatternColor = model.SurfaceForegroundPatternColor.ToColor();
            }
            if (model.SurfaceBackgroundPatternColor != null)
            {
                revitElement.SurfaceBackgroundPatternColor = model.SurfaceBackgroundPatternColor.ToColor();
            }

            // Resolve and map explicit pattern ID properties
            if (model.CutForegroundPatternId != null)
            {
                revitElement.CutForegroundPatternId = _identityService.ResolveElementId(model.CutForegroundPatternId, doc);
            }
            if (model.CutBackgroundPatternId != null)
            {
                revitElement.CutBackgroundPatternId = _identityService.ResolveElementId(model.CutBackgroundPatternId, doc);
            }
            if (model.SurfaceForegroundPatternId != null)
            {
                revitElement.SurfaceForegroundPatternId = _identityService.ResolveElementId(model.SurfaceForegroundPatternId, doc);
            }
            if (model.SurfaceBackgroundPatternId != null)
            {
                revitElement.SurfaceBackgroundPatternId = _identityService.ResolveElementId(model.SurfaceBackgroundPatternId, doc);
            }

            // Map AppearanceAssetId if it is present and not null (redundant but safe)
            if (model.AppearanceAssetId != null)
            {
                revitElement.AppearanceAssetId = _identityService.ResolveElementId(model.AppearanceAssetId, doc);
            }

            // Delegate nested asset mutations to MaterialAssetEngine
            if (model.AppearanceAsset != null)
            {
                MaterialAssetEngine.InjectAppearance(revitElement, model.AppearanceAsset, doc);
            }
            if (model.StructuralAsset != null)
            {
                MaterialAssetEngine.InjectStructural(revitElement, model.StructuralAsset, doc);
            }
            if (model.ThermalAsset != null)
            {
                MaterialAssetEngine.InjectThermal(revitElement, model.ThermalAsset, doc);
            }

            return revitElement;
        }

        #region Explicit IModelTranslator implementations

        void IModelTranslator.ExtractSpecifics(object revitElement, ObjectModel model, Document doc)
        {
            ExtractSpecifics((Material)revitElement, (MaterialModel)model, doc);
        }

        object? IModelTranslator.InjectSpecifics(ObjectModel model, object? revitElement, Document doc)
        {
            return InjectSpecifics((MaterialModel)model, (Material?)revitElement, doc);
        }

        #endregion
    }
}
