using System;
using Synthetic.Modules.RevitDOM;

namespace Synthetic.Modules.StandardsManagement.Models
{
    public class StandardsExecutionItem
    {
        public ObjectModel Model { get; set; }
        public string Action { get; set; } = "Pending";
        public string Message { get; set; } = string.Empty;

        public StandardsExecutionItem(ObjectModel model)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
        }
    }
}
