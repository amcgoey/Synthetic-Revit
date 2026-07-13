using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;

namespace Synthetic.Modules.RevitDOM
{
    /// <summary>
    /// Static engine to translate and manage nested Material Appearance, Structural, and Thermal assets in Revit.
    /// Does not implement IModelTranslator as it operates on nested models of Material rather than top-level dispatched elements.
    /// </summary>
    internal static class MaterialAssetEngine
    {
        #region Extraction Methods

        /// <summary>
        /// Extracts rendering/visual properties from the Material's assigned AppearanceAssetElement.
        /// </summary>
        public static AppearanceAssetModel? ExtractAppearance(Material material)
        {
            if (material == null) return null;
            var assetId = material.AppearanceAssetId;
            if (assetId == ElementId.InvalidElementId) return null;

            var assetElem = material.Document.GetElement(assetId) as AppearanceAssetElement;
            if (assetElem == null) return null;

            var model = new AppearanceAssetModel { Name = assetElem.Name };

            var renderAsset = assetElem.GetRenderingAsset();
            if (renderAsset != null)
            {
                // Extract Color
                var colorProp = renderAsset.FindByName("generic_diffuse") as AssetPropertyDoubleArray4d;
                if (colorProp != null)
                {
                    model.Color = colorProp.GetValueAsColor().ToModel();
                }

                // Extract Transparency
                var transProp = renderAsset.FindByName("generic_transparency") as AssetPropertyDouble;
                if (transProp != null)
                {
                    model.Transparency = transProp.Value;
                }

                // Extract Smoothness (Glossiness)
                var glossProp = renderAsset.FindByName("generic_glossiness") as AssetPropertyDouble;
                if (glossProp != null)
                {
                    model.Smoothness = glossProp.Value;
                }
            }

            return model;
        }

        /// <summary>
        /// Extracts structural properties from the Material's assigned structural PropertySetElement.
        /// </summary>
        public static StructuralAssetModel? ExtractStructural(Material material)
        {
            if (material == null) return null;
            var assetId = material.StructuralAssetId;
            if (assetId == ElementId.InvalidElementId) return null;

            var pse = material.Document.GetElement(assetId) as PropertySetElement;
            if (pse == null) return null;

            try
            {
                var asset = pse.GetStructuralAsset();
                if (asset == null) return null;

                return new StructuralAssetModel
                {
                    Name = pse.Name,
                    Behavior = asset.Behavior.ToString(),
                    StructuralAssetClass = asset.StructuralAssetClass.ToString(),
                    Density = asset.Density,
                    YoungModulus = asset.YoungModulus.X,
                    PoissonRatio = asset.PoissonRatio.X
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts thermal properties from the Material's assigned thermal PropertySetElement.
        /// </summary>
        public static ThermalAssetModel? ExtractThermal(Material material)
        {
            if (material == null) return null;
            var assetId = material.ThermalAssetId;
            if (assetId == ElementId.InvalidElementId) return null;

            var pse = material.Document.GetElement(assetId) as PropertySetElement;
            if (pse == null) return null;

            try
            {
                var asset = pse.GetThermalAsset();
                if (asset == null) return null;

                return new ThermalAssetModel
                {
                    Name = pse.Name,
                    ThermalMaterialType = asset.ThermalMaterialType.ToString(),
                    Density = asset.Density,
                    ThermalConductivity = asset.ThermalConductivity,
                    SpecificHeat = asset.SpecificHeat,
                    Emissivity = asset.Emissivity
                };
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Injection Methods

        /// <summary>
        /// Injects visual rendering properties from the model into the Material's AppearanceAssetElement.
        /// </summary>
        public static void InjectAppearance(Material material, AppearanceAssetModel? model, Document doc)
        {
            if (material == null || model == null || doc == null) return;

            AppearanceAssetElement? assetElem = null;

            // 1. In-place Mutation: Check if material already has an assigned asset
            if (material.AppearanceAssetId != ElementId.InvalidElementId)
            {
                assetElem = doc.GetElement(material.AppearanceAssetId) as AppearanceAssetElement;
            }

            // 2. Name-matching (De Facto Shared): Search for existing asset matching POCO name
            if (assetElem == null)
            {
                assetElem = new FilteredElementCollector(doc)
                    .OfClass(typeof(AppearanceAssetElement))
                    .Cast<AppearanceAssetElement>()
                    .FirstOrDefault(ae => ae.Name == model.Name);
            }

            // 3. Duplication/Creation: Duplicate template or create one
            if (assetElem == null)
            {
                var template = new FilteredElementCollector(doc)
                    .OfClass(typeof(AppearanceAssetElement))
                    .Cast<AppearanceAssetElement>()
                    .FirstOrDefault();

                if (template != null)
                {
                    assetElem = template.Duplicate(model.Name);
                }
                else
                {
                    throw new InvalidOperationException($"No template AppearanceAssetElement found in the document to duplicate for '{model.Name}'.");
                }
            }

            if (assetElem == null)
            {
                throw new InvalidOperationException($"Could not resolve or duplicate AppearanceAssetElement for '{model.Name}'.");
            }

            // Assign to material if not already assigned
            if (material.AppearanceAssetId != assetElem.Id)
            {
                material.AppearanceAssetId = assetElem.Id;
            }

            // Mutate in-place
            using (AppearanceAssetEditScope editScope = new AppearanceAssetEditScope(doc))
            {
                Asset editableAsset = editScope.Start(assetElem.Id);
                if (editableAsset != null)
                {
                    assetElem.Name = model.Name;

                    if (model.Color != null)
                    {
                        var colorProp = editableAsset.FindByName("generic_diffuse") as AssetPropertyDoubleArray4d;
                        if (colorProp != null && !colorProp.IsReadOnly)
                        {
                            colorProp.SetValueAsColor(model.Color.ToColor());
                        }
                    }

                    var transProp = editableAsset.FindByName("generic_transparency") as AssetPropertyDouble;
                    if (transProp != null && !transProp.IsReadOnly)
                    {
                        transProp.Value = model.Transparency;
                    }

                    var glossProp = editableAsset.FindByName("generic_glossiness") as AssetPropertyDouble;
                    if (glossProp != null && !glossProp.IsReadOnly)
                    {
                        glossProp.Value = model.Smoothness;
                    }

                    editScope.Commit(true);
                }
            }
        }

        /// <summary>
        /// Injects structural physical properties from the model into the Material's structural PropertySetElement.
        /// </summary>
        public static void InjectStructural(Material material, StructuralAssetModel? model, Document doc)
        {
            if (material == null || model == null || doc == null) return;

            PropertySetElement? pse = null;

            // 1. In-place Mutation
            if (material.StructuralAssetId != ElementId.InvalidElementId)
            {
                pse = doc.GetElement(material.StructuralAssetId) as PropertySetElement;
            }

            // 2. Name-matching
            if (pse == null)
            {
                pse = new FilteredElementCollector(doc)
                    .OfClass(typeof(PropertySetElement))
                    .Cast<PropertySetElement>()
                    .FirstOrDefault(e => e.Name == model.Name && HasStructuralAsset(e));
            }

            // 3. Duplication/Creation
            if (pse == null)
            {
                var template = new FilteredElementCollector(doc)
                    .OfClass(typeof(PropertySetElement))
                    .Cast<PropertySetElement>()
                    .FirstOrDefault(e => HasStructuralAsset(e));

                if (template != null)
                {
                    pse = template.Duplicate(doc, model.Name);
                }
                else
                {
                    StructuralAssetClass assetClass = (StructuralAssetClass)Enum.Parse(typeof(StructuralAssetClass), model.StructuralAssetClass);
                    StructuralAsset asset = new StructuralAsset(model.Name, assetClass);
                    asset.Behavior = (StructuralBehavior)Enum.Parse(typeof(StructuralBehavior), model.Behavior);
                    asset.Density = model.Density;
                    asset.SetYoungModulus(model.YoungModulus);
                    asset.SetPoissonRatio(model.PoissonRatio);
                    pse = PropertySetElement.Create(doc, asset);
                }
            }

            if (pse == null)
            {
                throw new InvalidOperationException($"Could not resolve or duplicate PropertySetElement for structural asset '{model.Name}'.");
            }

            // Temporarily unassign to avoid Revit "in-use" lock
            bool wasAssigned = (material.StructuralAssetId == pse.Id);
            if (wasAssigned)
            {
                material.SetMaterialAspectByPropertySet(MaterialAspect.Structural, ElementId.InvalidElementId);
            }

            // Mutate in-place
            pse.Name = model.Name;

            bool parameterSetSuccessful = false;
            try
            {
                var densityParam = pse.get_Parameter(BuiltInParameter.PHY_MATERIAL_PARAM_STRUCTURAL_DENSITY);
                if (densityParam != null && !densityParam.IsReadOnly)
                {
                    densityParam.Set(model.Density);
                    
                    var youngParam = pse.get_Parameter(BuiltInParameter.PHY_MATERIAL_PARAM_YOUNG_MOD1);
                    if (youngParam != null && !youngParam.IsReadOnly) youngParam.Set(model.YoungModulus);

                    var youngIsoParam = pse.get_Parameter(BuiltInParameter.PHY_MATERIAL_PARAM_YOUNG_MOD);
                    if (youngIsoParam != null && !youngIsoParam.IsReadOnly) youngIsoParam.Set(model.YoungModulus);

                    var poissonParam = pse.get_Parameter(BuiltInParameter.PHY_MATERIAL_PARAM_POISSON_MOD1);
                    if (poissonParam != null && !poissonParam.IsReadOnly) poissonParam.Set(model.PoissonRatio);

                    var poissonIsoParam = pse.get_Parameter(BuiltInParameter.PHY_MATERIAL_PARAM_POISSON_MOD);
                    if (poissonIsoParam != null && !poissonIsoParam.IsReadOnly) poissonIsoParam.Set(model.PoissonRatio);

                    var behaviorParam = pse.get_Parameter(BuiltInParameter.PHY_MATERIAL_PARAM_BEHAVIOR);
                    if (behaviorParam != null && !behaviorParam.IsReadOnly)
                    {
                        if (Enum.TryParse<StructuralBehavior>(model.Behavior, out var behaviorVal))
                        {
                            behaviorParam.Set((int)behaviorVal);
                        }
                    }

                    var classParam = pse.get_Parameter(BuiltInParameter.PHY_MATERIAL_PARAM_CLASS);
                    if (classParam != null && !classParam.IsReadOnly)
                    {
                        if (Enum.TryParse<StructuralAssetClass>(model.StructuralAssetClass, out var classVal))
                        {
                            classParam.Set((int)classVal);
                        }
                    }

                    var nameParam = pse.get_Parameter(BuiltInParameter.PROPERTY_SET_NAME);
                    if (nameParam != null && !nameParam.IsReadOnly) nameParam.Set(model.Name);

                    parameterSetSuccessful = true;
                }
            }
            catch
            {
                // Ignore parameter errors and fall back to API mutation
            }

            if (!parameterSetSuccessful)
            {
                var originalAsset = pse.GetStructuralAsset();
                if (originalAsset != null)
                {
                    var assetClass = originalAsset.StructuralAssetClass;
                    if (!string.IsNullOrEmpty(model.StructuralAssetClass))
                    {
                        assetClass = (StructuralAssetClass)Enum.Parse(typeof(StructuralAssetClass), model.StructuralAssetClass);
                    }

                    var newAsset = new StructuralAsset(model.Name, assetClass);
                    newAsset.Behavior = (StructuralBehavior)Enum.Parse(typeof(StructuralBehavior), model.Behavior);
                    newAsset.Density = model.Density;
                    newAsset.SetYoungModulus(model.YoungModulus);
                    newAsset.SetPoissonRatio(model.PoissonRatio);
                    pse.SetStructuralAsset(newAsset);
                }
            }

            // Reassign or assign
            if (wasAssigned || material.StructuralAssetId != pse.Id)
            {
                material.SetMaterialAspectByPropertySet(MaterialAspect.Structural, pse.Id);
            }
        }

        public static void InjectThermal(Material material, ThermalAssetModel? model, Document doc)
        {
            if (material == null || model == null || doc == null) return;

            PropertySetElement? pse = null;

            // 1. In-place Mutation
            if (material.ThermalAssetId != ElementId.InvalidElementId)
            {
                pse = doc.GetElement(material.ThermalAssetId) as PropertySetElement;
            }

            // 2. Name-matching
            if (pse == null)
            {
                pse = new FilteredElementCollector(doc)
                    .OfClass(typeof(PropertySetElement))
                    .Cast<PropertySetElement>()
                    .FirstOrDefault(e => e.Name == model.Name && HasThermalAsset(e));
            }

            // 3. Duplication/Creation
            if (pse == null)
            {
                var template = new FilteredElementCollector(doc)
                    .OfClass(typeof(PropertySetElement))
                    .Cast<PropertySetElement>()
                    .FirstOrDefault(e => HasThermalAsset(e));

                if (template != null)
                {
                    pse = template.Duplicate(doc, model.Name);
                }
                else
                {
                    ThermalMaterialType matType = (ThermalMaterialType)Enum.Parse(typeof(ThermalMaterialType), model.ThermalMaterialType);
                    ThermalAsset asset = new ThermalAsset(model.Name, matType);
                    asset.Density = model.Density;
                    asset.ThermalConductivity = model.ThermalConductivity;
                    asset.SpecificHeat = model.SpecificHeat;
                    asset.Emissivity = model.Emissivity;
                    pse = PropertySetElement.Create(doc, asset);
                }
            }

            if (pse == null)
            {
                throw new InvalidOperationException($"Could not resolve or duplicate PropertySetElement for thermal asset '{model.Name}'.");
            }

            // Temporarily unassign to avoid Revit "in-use" lock
            bool wasAssigned = (material.ThermalAssetId == pse.Id);
            if (wasAssigned)
            {
                material.SetMaterialAspectByPropertySet(MaterialAspect.Thermal, ElementId.InvalidElementId);
            }

            // Mutate in-place
            pse.Name = model.Name;

            bool parameterSetSuccessful = false;
            try
            {
                var densityParam = pse.get_Parameter(BuiltInParameter.PHY_MATERIAL_PARAM_STRUCTURAL_DENSITY);
                if (densityParam != null && !densityParam.IsReadOnly)
                {
                    densityParam.Set(model.Density);

                    var condParam = pse.get_Parameter(BuiltInParameter.PHY_MATERIAL_PARAM_THERMAL_CONDUCTIVITY);
                    if (condParam != null && !condParam.IsReadOnly) condParam.Set(model.ThermalConductivity);

                    var specHeatParam = pse.get_Parameter(BuiltInParameter.PHY_MATERIAL_PARAM_STRUCTURAL_SPECIFIC_HEAT);
                    if (specHeatParam != null && !specHeatParam.IsReadOnly) specHeatParam.Set(model.SpecificHeat);

                    var emissParam = pse.get_Parameter(BuiltInParameter.THERMAL_MATERIAL_PARAM_EMISSIVITY);
                    if (emissParam != null && !emissParam.IsReadOnly) emissParam.Set(model.Emissivity);

                    var classParam = pse.get_Parameter(BuiltInParameter.PHY_MATERIAL_PARAM_CLASS);
                    if (classParam != null && !classParam.IsReadOnly)
                    {
                        if (Enum.TryParse<ThermalMaterialType>(model.ThermalMaterialType, out var typeVal))
                        {
                            classParam.Set((int)typeVal);
                        }
                    }

                    var nameParam = pse.get_Parameter(BuiltInParameter.PROPERTY_SET_NAME);
                    if (nameParam != null && !nameParam.IsReadOnly) nameParam.Set(model.Name);

                    parameterSetSuccessful = true;
                }
            }
            catch
            {
                // Ignore and fall back to API mutation
            }

            if (!parameterSetSuccessful)
            {
                var originalAsset = pse.GetThermalAsset();
                if (originalAsset != null)
                {
                    var matType = originalAsset.ThermalMaterialType;
                    if (!string.IsNullOrEmpty(model.ThermalMaterialType))
                    {
                        matType = (ThermalMaterialType)Enum.Parse(typeof(ThermalMaterialType), model.ThermalMaterialType);
                    }

                    var newAsset = new ThermalAsset(model.Name, matType);
                    newAsset.Density = model.Density;
                    newAsset.ThermalConductivity = model.ThermalConductivity;
                    newAsset.SpecificHeat = model.SpecificHeat;
                    newAsset.Emissivity = model.Emissivity;
                    pse.SetThermalAsset(newAsset);
                }
            }

            // Reassign or assign
            if (wasAssigned || material.ThermalAssetId != pse.Id)
            {
                material.SetMaterialAspectByPropertySet(MaterialAspect.Thermal, pse.Id);
            }
        }

        #endregion

        #region Helpers

        private static bool HasStructuralAsset(PropertySetElement pse)
        {
            try
            {
                return pse.GetStructuralAsset() != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasThermalAsset(PropertySetElement pse)
        {
            try
            {
                return pse.GetThermalAsset() != null;
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}
