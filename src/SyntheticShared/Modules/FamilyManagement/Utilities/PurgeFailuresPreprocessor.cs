using Autodesk.Revit.DB;
using System.Collections.Generic;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace Synthetic.Modules.FamilyManagement.Utilities
{
    /// <summary>
    /// Silently preprocesses and handles failures triggered during family audit and purge operations.
    /// </summary>
    public class PurgeFailuresPreprocessor : IFailuresPreprocessor
    {
        /// <summary>
        /// Preprocesses failures to delete warnings and attempt resolution of errors.
        /// </summary>
        /// <param name="failuresAccessor">The failures accessor.</param>
        /// <returns>FailureProcessingResult.</returns>
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            IList<FailureMessageAccessor> failureMessages = failuresAccessor.GetFailureMessages();

            foreach (FailureMessageAccessor failure in failureMessages)
            {
                FailureSeverity severity = failure.GetSeverity();
                if (severity == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(failure);
                }
                else if (severity == FailureSeverity.Error)
                {
                    failuresAccessor.ResolveFailure(failure);
                }
            }
            return FailureProcessingResult.ProceedWithCommit;
        }
    }
}
