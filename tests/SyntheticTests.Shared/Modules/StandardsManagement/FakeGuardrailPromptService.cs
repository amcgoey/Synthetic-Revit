using System;
using System.Collections.Generic;
using Synthetic.Shared.UI;

namespace SyntheticTests.Modules.StandardsManagement
{
    /// <summary>
    /// Test implementation of IGuardrailPromptService that records invocations and returns configured results.
    /// </summary>
    public class FakeGuardrailPromptService : IGuardrailPromptService
    {
        private readonly GuardrailResult _configuredResult;
        
        /// <summary>
        /// Gets the list of file paths that were prompted.
        /// </summary>
        public List<string> PromptedPaths { get; } = new List<string>();

        /// <summary>
        /// Initializes a new instance of FakeGuardrailPromptService.
        /// </summary>
        /// <param name="configuredResult">The result to return when prompted.</param>
        public FakeGuardrailPromptService(GuardrailResult configuredResult = GuardrailResult.Cancel)
        {
            _configuredResult = configuredResult;
        }

        /// <inheritdoc/>
        public GuardrailResult PromptProtectedFileOverwrite(string filePath)
        {
            PromptedPaths.Add(filePath);
            return _configuredResult;
        }
    }
}
