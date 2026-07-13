using System;
using System.Collections.Generic;
using Synthetic.Modules.RevitDOM;

namespace Synthetic.Modules.StandardsManagement.Utilities
{
    /// <summary>
    /// Service that executes case-insensitive batch find and replace operations on ElementModels and ParameterModels.
    /// </summary>
    public class FindReplaceService : IFindReplaceService
    {
        /// <inheritdoc/>
        public HashSet<ElementModel> Execute(
            IEnumerable<ElementModel> elements,
            string findText,
            string replaceText,
            bool searchElementNames,
            bool searchParameterValues)
        {
            var modified = new HashSet<ElementModel>();
            if (string.IsNullOrEmpty(findText)) return modified;

            string replace = replaceText ?? string.Empty;

            foreach (var element in elements)
            {
                if (element == null) continue;

                bool isDirty = false;

                if (searchElementNames)
                {
                    if (!string.IsNullOrEmpty(element.Name))
                    {
                        string newName = ReplaceCaseInsensitive(element.Name, findText, replace);
                        if (newName != element.Name)
                        {
                            element.Name = newName;
                            isDirty = true;
                        }
                    }
                }

                if (searchParameterValues)
                {
                    if (element.Parameters != null)
                    {
                        foreach (var param in element.Parameters)
                        {
                            if (param != null && !param.IsReadOnly && !string.IsNullOrEmpty(param.Value))
                            {
                                string newVal = ReplaceCaseInsensitive(param.Value, findText, replace);
                                if (newVal != param.Value)
                                {
                                    param.Value = newVal;
                                    isDirty = true;
                                }
                            }
                        }
                    }
                }

                if (isDirty)
                {
                    modified.Add(element);
                }
            }

            return modified;
        }

        private string ReplaceCaseInsensitive(string input, string search, string replace)
        {
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(search)) return input;

            var sb = new System.Text.StringBuilder();
            int lastPos = 0;
            int pos = input.IndexOf(search, StringComparison.OrdinalIgnoreCase);

            while (pos >= 0)
            {
                sb.Append(input.Substring(lastPos, pos - lastPos));
                sb.Append(replace);
                lastPos = pos + search.Length;
                pos = input.IndexOf(search, lastPos, StringComparison.OrdinalIgnoreCase);
            }

            sb.Append(input.Substring(lastPos));
            return sb.ToString();
        }
    }
}
