using System;
using System.Collections.Generic;
using System.Threading;
using Autodesk.Revit.DB;
using Synthetic.Features.StandardsManagement;
using Synthetic.Features.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;

namespace SyntheticTests.Modules.StandardsManagement
{
    public class FakeFamilyEnforcer : IFamilyEnforcer
    {
        public Action<Document, IEnumerable<ElementModel>, StandardsExecutionOptions, Action<string, string, int>, List<SerializationResultModel>, CancellationToken>? EnforceHandler { get; set; }

        public void Enforce(
            Document doc,
            IEnumerable<ElementModel> standards,
            StandardsExecutionOptions options,
            Action<string, string, int> reportProgress,
            List<SerializationResultModel> dbResults,
            CancellationToken cancellationToken)
        {
            if (EnforceHandler != null)
            {
                EnforceHandler(doc, standards, options, reportProgress, dbResults, cancellationToken);
            }
        }
    }
}
