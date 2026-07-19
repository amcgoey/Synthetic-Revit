using System;
using System.Collections.Generic;
using System.Threading;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.RevitDOM.Operations.Merge;

namespace SyntheticTests.Shared.Modules.RevitDOM
{
    public class FakeStandardSerializationEngine : IStandardSerializationEngine
    {
        public IEnumerable<ObjectModel> ByRevit(IEnumerable<Element> elements, Document doc, bool isTemplate, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            return new List<ObjectModel>();
        }

        public IEnumerable<DuplicateClusterModel> Analyze(IEnumerable<ObjectModel> models, Document doc, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            return new List<DuplicateClusterModel>();
        }

        public IEnumerable<SerializationResultModel> ToRevit(IEnumerable<ObjectModel> models, Document doc, IProgress<string>? progress = null, CancellationToken cancellationToken = default, IFailuresPreprocessor? failuresPreprocessor = null)
        {
            return new List<SerializationResultModel>();
        }

        public ObjectModel? ExtractCategory(Category category, Document doc, bool isTemplate)
        {
            return null;
        }
    }
}
