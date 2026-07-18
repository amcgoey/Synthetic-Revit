namespace Synthetic.Modules.StandardsManagement.Models
{
    public class StandardsExecutionOptions
    {
        public bool ProcessFamilies { get; set; }
        public string CategoryFilter { get; set; } = string.Empty;
        public bool PurgeUnusedStyleTypes { get; set; }
        public string StandardsFilePath { get; set; } = string.Empty;
        public bool WriteRevitDatabase { get; set; } = true;
        public bool SaveLocalFiles { get; set; } = true;
        public bool UseTransactionGroup { get; set; } = true;
    }
}
