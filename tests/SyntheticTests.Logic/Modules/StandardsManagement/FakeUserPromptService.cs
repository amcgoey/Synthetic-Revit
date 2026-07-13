using System;
using System.Collections.Generic;
using Synthetic.Shared.UI;

namespace SyntheticTests.Modules.StandardsManagement
{
    /// <summary>
    /// Test fake implementing IUserPromptService to avoid showing dialog windows during tests.
    /// </summary>
    public class FakeUserPromptService : IUserPromptService
    {
        public List<string> ShownMessages { get; } = new List<string>();
        public List<string> ConfirmedPrompts { get; } = new List<string>();
        public bool ConfirmationResult { get; set; } = true;

        public void ShowMessage(string message, string title)
        {
            ShownMessages.Add(message);
        }

        public bool ConfirmAction(string message, string title)
        {
            ConfirmedPrompts.Add(message);
            return ConfirmationResult;
        }
    }
}
