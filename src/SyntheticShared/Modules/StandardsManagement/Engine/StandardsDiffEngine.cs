using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;

using Synthetic.Modules.RevitDOM;
using Synthetic.Infrastructure.Serialization;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.MergeDuplicates.Models;
using Synthetic.Modules.StandardsManagement.Engine;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;

namespace Synthetic.Modules.StandardsManagement.Engine
{
    /// <summary>
    /// Pre-execution analysis engine that compares incoming deserialized JSON standard models
    /// against live elements in the active Revit document.
    /// </summary>
    public static class StandardsDiffEngine
    {
        /// <summary>
        /// Runs a deep comparison of deserialized JSON standard models against live elements in the document.
        /// Safely ignores Revit System Families and unsupported system types (e.g. WallTypes, FloorTypes)
        /// when processing within a Family Document (doc.IsFamilyDocument is true) to prevent API execution failures.
        /// </summary>
        /// <param name="doc">The active Revit document context (can be a project or family document).</param>
        /// <param name="incomingModels">The collection of deserialized JSON standard element models to analyze.</param>
        /// <returns>An ObservableCollection of DuplicateClusterModel objects summarizing the resolved parameter conflicts.</returns>
        public static ObservableCollection<DuplicateClusterModel> RunDeepScan(Document doc, IEnumerable<ElementModel> incomingModels, IStandardSerializationEngine? engine = null)
        {
            if (incomingModels == null) return new ObservableCollection<DuplicateClusterModel>();
            engine = engine ?? new StandardSerializationEngine();
            var clusters = engine.Analyze(incomingModels, doc);
            return new ObservableCollection<DuplicateClusterModel>(clusters);
        }
    }
}
