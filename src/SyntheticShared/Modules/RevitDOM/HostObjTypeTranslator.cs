using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Synthetic.Modules.RevitDOM
{
    /// <summary>
    /// Translator governing the extraction and injection of Revit system family types (derived from ElementType),
    /// managing the nested CompoundStructureModel layers with material identity resolution and priority settings.
    /// </summary>
    internal class HostObjTypeTranslator : TemplateDuplicatingTranslator<ElementType, HostObjTypeModel>
    {
        private readonly IIdentityService _identityService;

        public HostObjTypeTranslator(IIdentityService identityService)
        {
            _identityService = identityService ?? throw new ArgumentNullException(nameof(identityService));
        }

        /// <summary>
        /// Extracts CompoundStructure from the native ElementType (if it is a HostObjAttributes) and maps it to the model.
        /// </summary>
        public override void ExtractSpecifics(ElementType revitElement, HostObjTypeModel model, Document doc)
        {
            if (revitElement == null) throw new ArgumentNullException(nameof(revitElement));
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            if (revitElement is HostObjAttributes hostType)
            {
                CompoundStructure cs = hostType.GetCompoundStructure();
                if (cs != null)
                {
                    model.Structure = ToModel(cs, doc);
                }
            }
        }

        /// <summary>
        /// Injects the CompoundStructure from the model into the native ElementType (if it supports compound structures).
        /// </summary>
        public override ElementType? InjectSpecifics(HostObjTypeModel model, ElementType? revitElement, Document doc)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            // Call base class to ensure the native ElementType exists (duplicating from template if needed)
            var element = base.InjectSpecifics(model, revitElement, doc);
            if (element == null) return null;

            if (element is HostObjAttributes hostType && model.Structure != null)
            {
                var cs = CreateCompoundStructure(model.Structure, doc, model);
                if (cs != null)
                {
                    // Revit enforces logical/geometric validation (e.g. core layer thickness > 0).
                    // We let native ArgumentException/InvalidOperationException bubble up directly (hard abort).
                    hostType.SetCompoundStructure(cs);
                }
            }

            return element;
        }

        /// <summary>
        /// Reconstructs a native Revit CompoundStructure from a CompoundStructureModel,
        /// handling material resolution with graceful degradation and 2026+ layer priorities.
        /// </summary>
        private CompoundStructure CreateCompoundStructure(CompoundStructureModel structureModel, Document doc, HostObjTypeModel hostModel)
        {
            IList<CompoundStructureLayer> csLayers = new List<CompoundStructureLayer>();
            int structuralIndex = -1;

            for (int i = 0; i < structureModel.Layers.Count; i++)
            {
                SerialCompoundStructureLayer layerModel = structureModel.Layers[i];
                var layer = new CompoundStructureLayer();

                // Deck embedding type
                if (!string.IsNullOrEmpty(layerModel.DeckEmbeddingType))
                {
                    layer.DeckEmbeddingType = (StructDeckEmbeddingType)Enum.Parse(
                         typeof(StructDeckEmbeddingType),
                         layerModel.DeckEmbeddingType);
                }

                // Deck profile
                if (layerModel.DeckProfileId != null)
                {
                    var deckId = _identityService.ResolveElementId(layerModel.DeckProfileId, doc);
                    layer.DeckProfileId = deckId;
                }

                // Layer function
                if (!string.IsNullOrEmpty(layerModel.Function))
                {
                    layer.Function = (MaterialFunctionAssignment)Enum.Parse(
                         typeof(MaterialFunctionAssignment),
                         layerModel.Function);
                }

                layer.LayerCapFlag = layerModel.LayerCapFlag;

                // Material ID resolution with Graceful Degradation
                if (layerModel.MaterialId != null)
                {
                    var matId = _identityService.ResolveElementId(layerModel.MaterialId, doc);
                    if (matId != ElementId.InvalidElementId)
                    {
                        layer.MaterialId = matId;
                    }
                    else
                    {
                        layer.MaterialId = ElementId.InvalidElementId;
                        SerializationResultModel.LogWarning($"Could not resolve Material: {layerModel.MaterialId.Name} for layer {i} of system family '{hostModel.Name}'. Downgrading to <By Category>.");
                    }
                }
                else
                {
                    layer.MaterialId = ElementId.InvalidElementId;
                }

                layer.Width = layerModel.Width;
                csLayers.Add(layer);

                if (layerModel.StructuralMaterial)
                {
                    structuralIndex = i;
                }
            }

            CompoundStructure cs = CompoundStructure.CreateSimpleCompoundStructure(csLayers);
            if (structuralIndex >= 0 && structuralIndex < csLayers.Count)
            {
                cs.StructuralMaterialIndex = structuralIndex;
            }

            // Inverted Conditioning for Revit 2026+ Layer Priority
#if REVIT2022 || REVIT2023 || REVIT2024 || REVIT2025
            // Priority is not supported in legacy versions
#else
            for (int i = 0; i < structureModel.Layers.Count; i++)
            {
                int priority = structureModel.Layers[i].Priority;
                if (priority > 0)
                {
                    cs.SetLayerPriority(i, priority);
                }
            }
#endif

            return cs;
        }

        private List<string> ExtractWallSweeps(CompoundStructure cs)
        {
            var wallSweeps = new List<string>();
            // Wall sweeps info extraction logic can be added here if supported in future releases
            return wallSweeps;
        }

        private CompoundStructureModel ToModel(CompoundStructure cs, Document doc)
        {
            if (cs == null) return null;
            var model = new CompoundStructureModel();
            model.Layers = new List<SerialCompoundStructureLayer>();
            model.WallSweeps = ExtractWallSweeps(cs);
            IList<CompoundStructureLayer> csLayers = cs.GetLayers();

            int structuralIndex = cs.StructuralMaterialIndex;
            for (int i = 0; i < csLayers.Count; i++)
            {
                CompoundStructureLayer csLayer = csLayers[i];
                var layerModel = ToModel(csLayer, doc);
                layerModel.StructuralMaterial = (i == structuralIndex);
#if REVIT2022 || REVIT2023 || REVIT2024 || REVIT2025
                // Priority is not supported in legacy versions
#else
                layerModel.Priority = cs.GetLayerPriority(i);
#endif
                model.Layers.Add(layerModel);
            }
            return model;
        }

        private SerialCompoundStructureLayer ToModel(CompoundStructureLayer layer, Document doc)
        {
            if (layer == null) return null;
            return new SerialCompoundStructureLayer
            {
                MaterialId = _identityService.ToModel(layer.MaterialId, doc, false),
                Function = layer.Function.ToString(),
                Width = layer.Width,
                DeckEmbeddingType = layer.DeckEmbeddingType.ToString(),
                DeckProfileId = _identityService.ToModel(layer.DeckProfileId, doc, false),
                LayerCapFlag = layer.LayerCapFlag
            };
        }
    }
}
