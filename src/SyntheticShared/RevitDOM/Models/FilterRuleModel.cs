using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Synthetic.Infrastructure.Serialization;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Models
{
    /// <summary>
    /// Represents a lightweight data structure to represent a single node in a filter's logical tree.
    /// Supports recursive logical nesting and parameter evaluation.
    /// </summary>
    public class FilterRuleModel : ObjectModel
    {
        /// <summary>
        /// Gets or sets the rule type (e.g., "LogicalAnd", "LogicalOr", "ParameterFilter", "HasValue").
        /// </summary>
        public string RuleType { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the parameter being evaluated (if applicable).
        /// </summary>
        public ElementIdModel? ParameterId { get; set; }

        /// <summary>
        /// Gets or sets the evaluator type encoded with rule type (e.g. "FilterStringRule:FilterStringEquals").
        /// </summary>
        public string? Evaluator { get; set; }

        /// <summary>
        /// Gets or sets the threshold value to check against.
        /// </summary>
        public string? RuleValue { get; set; }

        /// <summary>
        /// Gets or sets a recursive list of inner rules (for logical filters).
        /// </summary>
        public List<FilterRuleModel> InnerRules { get; set; } = new List<FilterRuleModel>();
    }
}
