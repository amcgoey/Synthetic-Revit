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

    public static class StandardsPipelineConstants
    {
        public const string TargetDatabase = "Database";
        public const string TargetFile = "File";

        public const string ActionCanceled = "Canceled";
        public const string ActionFailed = "Failed";
        public const string ActionSaveFailed = "Save Failed";
        public const string ActionSaved = "Saved";
        public const string ActionCreated = "Created";
        public const string ActionUpdated = "Updated";
        public const string ActionUnchanged = "Unchanged";
    }
}
