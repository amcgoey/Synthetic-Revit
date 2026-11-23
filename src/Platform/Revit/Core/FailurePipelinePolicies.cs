using System;
using System.Collections.Generic;

namespace Synthetic.Platform.Revit
{
    /// <summary>
    /// Factory providing pure in-process <see cref="FailurePipelinePolicy"/> presets.
    /// </summary>
    public static class FailurePipelinePolicies
    {
        /// <summary>
        /// Creates a non-mutating diagnostic policy that observes and records all failures without deleting or resolving any.
        /// </summary>
        /// <returns>A configured <see cref="FailurePipelinePolicy"/> instance.</returns>
        public static FailurePipelinePolicy ObserveOnly()
        {
            return ObserveOnly(new FailureProcessingReport());
        }

        /// <summary>
        /// Creates a non-mutating diagnostic policy that observes and records all failures into the specified report.
        /// </summary>
        /// <param name="report">The report to populate.</param>
        /// <returns>A configured <see cref="FailurePipelinePolicy"/> instance.</returns>
        public static FailurePipelinePolicy ObserveOnly(FailureProcessingReport? report)
        {
            return new FailurePipelinePolicy(report)
            {
                AllowBlanketSuppression = false
            };
        }

        /// <summary>
        /// Creates a policy configured for blanket warning suppression with full failure recording.
        /// </summary>
        /// <returns>A configured <see cref="FailurePipelinePolicy"/> instance.</returns>
        public static FailurePipelinePolicy DeleteAllWarningsRecorded()
        {
            return DeleteAllWarningsRecorded(new FailureProcessingReport());
        }

        /// <summary>
        /// Creates a policy configured for blanket warning suppression recording into the specified report.
        /// </summary>
        /// <param name="report">The report to populate.</param>
        /// <returns>A configured <see cref="FailurePipelinePolicy"/> instance.</returns>
        public static FailurePipelinePolicy DeleteAllWarningsRecorded(FailureProcessingReport? report)
        {
            return new FailurePipelinePolicy(report)
            {
                AllowBlanketSuppression = true
            };
        }

        /// <summary>
        /// Creates a policy targeting the specified failure definition GUIDs for purge or constraint suppression.
        /// </summary>
        /// <param name="targetIds">The target failure definition GUIDs.</param>
        /// <param name="report">Optional diagnostic report to populate.</param>
        /// <returns>A configured <see cref="FailurePipelinePolicy"/> instance.</returns>
        public static FailurePipelinePolicy ForTargetIds(IEnumerable<Guid>? targetIds, FailureProcessingReport? report = null)
        {
            var policy = new FailurePipelinePolicy(report)
            {
                AllowBlanketSuppression = false
            };

            if (targetIds != null)
            {
                var rule = new TargetIdFailurePolicyRule(targetIds);
                if (rule.TargetIds.Count > 0)
                {
                    policy.AddRule(rule);
                }
            }

            return policy;
        }
    }
}
