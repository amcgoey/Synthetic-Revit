using System;
using System.Collections.Generic;

namespace Synthetic.Tools.ModelUpgrader
{
    /// <summary>
    /// JSON batch manifest passed by the Python CLI driver to <see cref="UpgradeRunnerTests"/>.
    /// </summary>
    public sealed class UpgradeManifest
    {
        public string TargetVersion { get; set; } = string.Empty;
        public string ReportPath { get; set; } = string.Empty;
        public List<UpgradeJobSpec> Jobs { get; set; } = new List<UpgradeJobSpec>();
    }

    /// <summary>
    /// Single standalone model/template/family upgrade specification.
    /// </summary>
    public sealed class UpgradeJobSpec
    {
        public string SourcePath { get; set; } = string.Empty;
        public string TargetPath { get; set; } = string.Empty;
        public bool Audit { get; set; }
        public int MinLoadableFamilies { get; set; }
    }

    /// <summary>
    /// Explicit batch upgrade report persisted as JSON after execution.
    /// </summary>
    public sealed class UpgradeBatchReport
    {
        public string TargetVersion { get; set; } = string.Empty;
        public string ExecutedAtUtc { get; set; } = string.Empty;
        public List<FileUpgradeResult> Jobs { get; set; } = new List<FileUpgradeResult>();
    }

    /// <summary>
    /// Per-file upgrade outcome capturing format verification, loadable family counts,
    /// suppressed modal dialogs, and all recorded <c>FailuresProcessing</c> warnings/errors.
    /// </summary>
    public sealed class FileUpgradeResult
    {
        public string SourcePath { get; set; } = string.Empty;
        public string TargetPath { get; set; } = string.Empty;
        public bool Success { get; set; }
        public bool Audit { get; set; }
        public bool WasWorkshared { get; set; }
        public string FileExtension { get; set; } = string.Empty;
        public string SourceFormat { get; set; } = string.Empty;
        public string VerifiedFormat { get; set; } = string.Empty;
        public int? LoadableFamilyCount { get; set; }
        public int WarningCount { get; set; }
        public int ErrorCount { get; set; }
        public List<RecordedDialog> SuppressedDialogs { get; set; } = new List<RecordedDialog>();
        public List<RecordedFailure> Failures { get; set; } = new List<RecordedFailure>();
        public string ErrorMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// Recorded modal dialog suppressed during headless upgrade via <c>DialogBoxShowing</c>.
    /// </summary>
    public sealed class RecordedDialog
    {
        public string DialogId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public int OverrideResultCode { get; set; }
        public bool OverrideAccepted { get; set; }
    }

    /// <summary>
    /// Recorded failure message observed in <c>FailuresProcessing</c>.
    /// </summary>
    public sealed class RecordedFailure
    {
        public string FailureDefinitionGuid { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<long> FailingElementIds { get; set; } = new List<long>();
        public string ResolutionAction { get; set; } = string.Empty;
    }
}
