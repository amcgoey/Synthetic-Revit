using System.Collections.Generic;

namespace Synthetic.Modules.StandardsManagement.Models
{
    public class StandardsExecutionResult
    {
        public bool Success { get; set; }
        public List<StandardsExecutionItem> Items { get; set; } = new List<StandardsExecutionItem>();
        public List<Synthetic.Modules.RevitDOM.SerializationResultModel> RawResults { get; set; } = new List<Synthetic.Modules.RevitDOM.SerializationResultModel>();
        public string ReportMarkdown { get; set; } = string.Empty;
        public string LogFilePath { get; set; } = string.Empty;
    }
}
