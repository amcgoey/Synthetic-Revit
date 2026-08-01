using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Synthetic.Infrastructure.FailureProcessing
{
    /// <summary>
    /// Factory providing pre-configured failure processing pipeline presets.
    /// </summary>
    public static class FailurePipelines
    {
        /// <summary>
        /// Creates a failure preprocessor preset configured for purge and element deletion operations.
        /// Targets BuiltInFailures.EditingFailures failure IDs for warning deletion.
        /// </summary>
        /// <returns>A configured CompositeFailuresPreprocessor instance.</returns>
        public static CompositeFailuresPreprocessor Purge()
        {
            CompositeFailuresPreprocessor preprocessor = new CompositeFailuresPreprocessor();
            List<FailureDefinitionId> targetIds = new List<FailureDefinitionId>();

            if (BuiltInFailures.EditingFailures.ElementsWillBeDeleted != null)
            {
                targetIds.Add(BuiltInFailures.EditingFailures.ElementsWillBeDeleted);
            }

            if (BuiltInFailures.EditingFailures.ElementsDeleted != null)
            {
                targetIds.Add(BuiltInFailures.EditingFailures.ElementsDeleted);
            }

            if (targetIds.Count > 0)
            {
                preprocessor.AddRule(new TargetIdFailureRule(targetIds));
            }

            return preprocessor;
        }

        /// <summary>
        /// Creates a failure preprocessor preset configured for blanket warning suppression.
        /// </summary>
        /// <returns>A configured CompositeFailuresPreprocessor instance.</returns>
        public static CompositeFailuresPreprocessor DeleteWarnings()
        {
            return new CompositeFailuresPreprocessor
            {
                AllowBlanketSuppression = true
            };
        }
    }
}
