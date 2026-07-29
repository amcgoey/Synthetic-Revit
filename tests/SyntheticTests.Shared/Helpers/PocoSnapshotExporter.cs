using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Synthetic.Modules.MergeDuplicates.Services;
using Synthetic.RevitDOM.Operations.Merge;
using Synthetic.RevitDOM.Translation;

namespace SyntheticTests.Helpers
{
    public static class PocoSnapshotExporter
    {
        public static void ExportPocoSnapshot(IEnumerable<Element> elements, string fileName)
        {
            string? gate = Environment.GetEnvironmentVariable("SYNTHETIC_EXPORT_SNAPSHOTS");
            if (!string.Equals(gate, "true", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(gate, "1", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string projectRoot = TestPathHelper.GetProjectRoot();
            string assetsDir = Path.Combine(projectRoot, "tests", "SyntheticTests.Shared", "Assets");
            if (!Directory.Exists(assetsDir))
            {
                Directory.CreateDirectory(assetsDir);
            }

            var models = elements
                .Where(el => el != null)
                .Select(el => RevitMergeDataCollector.ConvertToPoco(el.Document, el))
                .ToList();

            string json = JsonConvert.SerializeObject(models, Formatting.Indented);
            string filePath = Path.Combine(assetsDir, fileName);
            File.WriteAllText(filePath, json);
        }
    }
}
