using System;
using System.Collections.Generic;
using System.Threading;
using Autodesk.Revit.DB;
using Synthetic.Modules.StandardsManagement.Models;
using Synthetic.Shared.UI;

namespace Synthetic.Modules.StandardsManagement.Engine
{
    public interface IStandardsExecutionPipeline
    {
        StandardsExecutionResult Execute(
            Document doc,
            IEnumerable<StandardsExecutionItem> items,
            StandardsExecutionOptions options,
            IProgress<ProgressState>? progress = null,
            CancellationToken cancellationToken = default);
    }
}
