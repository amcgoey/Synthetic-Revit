using System;
using System.Collections.Generic;
using System.Linq;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Operations
{
    /// <summary>
    /// Enforces topological sort order on incoming ObjectModels to ensure dependency safety.
    /// Uses a bottom-up directed acyclic graph (DAG) sorting order.
    /// </summary>
    public static class ImportExecutionRunner
    {
        /// <summary>
        /// Sorts the incoming ObjectModels into a deterministic topological order.
        /// </summary>
        /// <param name="models">The incoming unsorted models.</param>
        /// <returns>A collection of sorted models.</returns>
        public static IEnumerable<ObjectModel> Sort(IEnumerable<ObjectModel> models)
        {
            if (models == null) throw new ArgumentNullException(nameof(models));

            return models
                .OrderBy(GetTypeTier)
                .ThenBy(m => (m is ElementModel em) ? em.Name : string.Empty);
        }

        private static int GetTypeTier(ObjectModel model)
        {
            if (model == null) return 8;

            Type type = model.GetType();

            // Tier 1: Primitives & Assets
            if (typeof(LinePatternElementModel).IsAssignableFrom(type) ||
                typeof(FillPatternElementModel).IsAssignableFrom(type) ||
                typeof(PropertySetElementModel).IsAssignableFrom(type))
            {
                return 1;
            }

            // Tier 2: Foundations
            if (typeof(MaterialModel).IsAssignableFrom(type))
            {
                return 2;
            }

            // Tier 3: Simple Annotations & Datums
            if (typeof(DimensionTypeModel).IsAssignableFrom(type) ||
                typeof(GridTypeModel).IsAssignableFrom(type) ||
                typeof(LevelTypeModel).IsAssignableFrom(type) ||
                typeof(ParameterElementModel).IsAssignableFrom(type) ||
                type.Name == "TextNoteTypeModel")
            {
                return 3;
            }

            // Tier 4: Categorization & Filtering
            if (typeof(CategoryModel).IsAssignableFrom(type) ||
                typeof(ParameterFilterElementModel).IsAssignableFrom(type))
            {
                return 4;
            }

            // Tier 5: System Families / Host Types
            if (typeof(HostObjTypeModel).IsAssignableFrom(type) ||
                typeof(MullionTypeModel).IsAssignableFrom(type) ||
                typeof(FasciaTypeModel).IsAssignableFrom(type) ||
                typeof(GutterTypeModel).IsAssignableFrom(type) ||
                type.Name == "CurtainSystemTypeModel")
            {
                return 5;
            }

            // Tier 6: Views & UI
            if (typeof(ViewModel).IsAssignableFrom(type) ||
                typeof(BrowserOrganizationModel).IsAssignableFrom(type))
            {
                return 6;
            }

            // Fallbacks for other generic models
            if (typeof(ElementTypeModel).IsAssignableFrom(type))
            {
                return 5; // Default unknown element types to system families tier
            }

            if (typeof(ElementModel).IsAssignableFrom(type))
            {
                return 7; // Default other elements to tier 7
            }

            return 8; // Fallback for other ObjectModels
        }
    }
}
