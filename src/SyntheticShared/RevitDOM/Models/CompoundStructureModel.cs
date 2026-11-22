using System;
using System.Collections.Generic;

using Synthetic.Infrastructure.Serialization;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Models
{
    /// <summary>
    /// Model representing a Revit CompoundStructure (e.g. wall layers).
    /// </summary>
    public class CompoundStructureModel
    {
        /// <summary>
        /// Gets or sets the list of layers in the compound structure.
        /// </summary>
        public List<SerialCompoundStructureLayer> Layers { get; set; } = new List<SerialCompoundStructureLayer>();

        /// <summary>
        /// Gets or sets the wall sweeps.
        /// </summary>
        public List<string> WallSweeps { get; set; } = new List<string>();

        /// <summary>
        /// Initializes a new instance of the CompoundStructureModel class.
        /// </summary>
        public CompoundStructureModel () { }
    }

    /// <summary>
    /// Model representing a single layer in a CompoundStructure.
    /// </summary>
    public class SerialCompoundStructureLayer
    {
        /// <summary>
        /// Gets or sets the material ID model.
        /// </summary>
        public ElementIdModel MaterialId { get; set; } = new ElementIdModel();

        /// <summary>
        /// Gets or sets the layer function name.
        /// </summary>
        public string Function { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the width of the layer in feet.
        /// </summary>
        public double Width { get; set; }

        /// <summary>
        /// Gets or sets the deck embedding type.
        /// </summary>
        public string DeckEmbeddingType { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the deck profile ID model.
        /// </summary>
        public ElementIdModel DeckProfileId { get; set; } = new ElementIdModel();

        /// <summary>
        /// Gets or sets a value indicating whether the layer caps.
        /// </summary>
        public bool LayerCapFlag { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the layer is the structural material.
        /// </summary>
        public bool StructuralMaterial { get; set; }
#if REVIT2022 || REVIT2023 || REVIT2024 || REVIT2025
        // Legacy: Priority is not used
#else
        /// <summary>
        /// Gets or sets the priority of the layer (Revit 2026+).
        /// </summary>
        public int Priority { get; set; }
#endif

        /// <summary>
        /// Initializes a new instance of the SerialCompoundStructureLayer class.
        /// </summary>
        public SerialCompoundStructureLayer () { }
    }
}

