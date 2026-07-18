using System;
using System.Collections.Generic;
using System.Threading;
using Autodesk.Revit.DB;
using Synthetic.Modules.MergeDuplicates.Models;

namespace Synthetic.Modules.RevitDOM
{
    public interface IStandardSerializationEngine
    {
        IEnumerable<ObjectModel> ByRevit(IEnumerable<Element> elements, Document doc, bool isTemplate, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
        IEnumerable<DuplicateClusterModel> Analyze(IEnumerable<ObjectModel> models, Document doc, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
        IEnumerable<SerializationResultModel> ToRevit(IEnumerable<ObjectModel> models, Document doc, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
        ObjectModel? ExtractCategory(Category category, Document doc, bool isTemplate);
    }
}
