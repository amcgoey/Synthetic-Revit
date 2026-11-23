using System;
using System.Collections.Generic;
using System.Threading;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;

namespace Synthetic.Features.StandardsManagement
{
    public interface IFamilyEnforcer
    {
        void Enforce(
            Document doc,
            IEnumerable<ElementModel> standards,
            StandardsExecutionOptions options,
            Action<string, string, int> reportProgress,
            List<SerializationResultModel> dbResults,
            CancellationToken cancellationToken);
    }
}
