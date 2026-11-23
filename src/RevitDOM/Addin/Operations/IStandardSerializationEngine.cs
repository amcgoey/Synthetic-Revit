using System;
using System.Collections.Generic;
using System.Threading;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;

namespace Synthetic.RevitDOM.Operations
{
    public interface IStandardSerializationEngine
    {
        IEnumerable<ObjectModel> ByRevit(IEnumerable<Element> elements, Document doc, bool isTemplate, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
        IEnumerable<DuplicateClusterModel> Analyze(IEnumerable<ObjectModel> models, Document doc, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
        IEnumerable<SerializationResultModel> ToRevit(IEnumerable<ObjectModel> models, Document doc, IProgress<string>? progress = null, CancellationToken cancellationToken = default, IFailuresPreprocessor? failuresPreprocessor = null);
        ObjectModel? ExtractCategory(Category category, Document doc, bool isTemplate);
        IEnumerable<Element> GatherRootElements(Document doc, IReadOnlyCollection<string>? selectedGroupings = null, CancellationToken cancellationToken = default);
    }
}
