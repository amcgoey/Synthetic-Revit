using System;
using System.Collections.Generic;
using System.Threading;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Operations.Standards;
using Synthetic.Shared.UI;

namespace Synthetic.RevitDOM.Operations.Standards
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
