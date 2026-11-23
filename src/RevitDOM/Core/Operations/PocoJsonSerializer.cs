using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Synthetic.RevitDOM.Models;

namespace Synthetic.RevitDOM.Operations
{
    /// <summary>
    /// Pure headless JSON serialization and polymorphic hydration for <see cref="ObjectModel"/> / <see cref="ElementModel"/> graphs.
    /// </summary>
    public static class PocoJsonSerializer
    {
        /// <summary>
        /// Pure headless serialization: emits indented JSON top-level bucket dictionaries in canonical order driven by <see cref="StandardTypeMetadataRegistry"/>.
        /// Uses 'FilledRegionTypes', omits empty buckets, preserves 'Elements' list if non-empty,
        /// sorts bucket keys deterministically (OrdinalIgnoreCase then Ordinal), and resolves duplicate keys via last-in-wins with a logged warning.
        /// </summary>
        public static string ToJson(IEnumerable<ObjectModel> models)
        {
            if (models == null) throw new ArgumentNullException(nameof(models));
            SerializationResultModel.ClearWarnings();

            var descriptors = StandardTypeMetadataRegistry.GetAllDescriptors();
            var bucketMap = new Dictionary<string, Dictionary<string, ElementModel>>(StringComparer.Ordinal);
            foreach (var desc in descriptors)
            {
                bucketMap[desc.BucketName] = new Dictionary<string, ElementModel>(StringComparer.Ordinal);
            }

            var fallbackElements = new List<ElementModel>();

            foreach (var model in models)
            {
                if (model == null) continue;

                var desc = StandardTypeMetadataRegistry.GetDescriptorByModel(model);
                if (desc != null)
                {
                    // Exclude internal dimension style name on ToJson per spec
                    if (desc.BucketName == "DimensionTypes" &&
                        string.Equals((model as ElementModel)?.Name, DimensionTypeModel.InternalDimStyleName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (model is ElementModel em)
                    {
                        string key = desc.GetKey(model);
                        var bucketDict = bucketMap[desc.BucketName];
                        if (bucketDict.ContainsKey(key))
                        {
                            SerializationResultModel.LogWarning($"Duplicate key '{key}' encountered in bucket '{desc.BucketName}'. Overwriting previous entry with last-in-wins.");
                        }
                        bucketDict[key] = em;
                    }
                }
                else if (model is ElementModel em)
                {
                    fallbackElements.Add(em);
                }
            }

            // Construct output in canonical registry order using JObject
            var root = new JObject();

            foreach (var desc in descriptors)
            {
                var bucketDict = bucketMap[desc.BucketName];
                if (bucketDict.Count > 0)
                {
                    var sortedKeys = bucketDict.Keys
                        .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(k => k, StringComparer.Ordinal);

                    var bucketObj = new JObject();
                    foreach (var key in sortedKeys)
                    {
                        var entry = bucketDict[key];
                        bucketObj[key] = JToken.FromObject(entry);
                    }
                    root[desc.BucketName] = bucketObj;
                }
            }

            // Preserve Elements list if non-empty
            if (fallbackElements.Count > 0)
            {
                var sortedFallback = fallbackElements
                    .OrderBy(x => x.Class ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase);

                var array = new JArray();
                foreach (var em in sortedFallback)
                {
                    array.Add(JToken.FromObject(em));
                }
                root["Elements"] = array;
            }

            return root.ToString(Formatting.Indented);
        }

        /// <summary>
        /// Pure headless deserialization: reads values across current and legacy bucket names (including 'FilledRegionTypes' and 'FillRegionTypes'),
        /// ignores key formatting, infers default .Class from bucket when omitted, promotes legacy TextElementType inside ElementTypes,
        /// sets IsTemplate = true for ViewTemplates, and hydrates exact registered ModelType subclasses polymorphically via .Class.
        /// </summary>
        public static IEnumerable<ElementModel> FromJson(string json)
        {
            SerializationResultModel.ClearWarnings();

            if (string.IsNullOrWhiteSpace(json))
            {
                return Enumerable.Empty<ElementModel>();
            }

            var results = new List<ElementModel>();
            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch
            {
                return Enumerable.Empty<ElementModel>();
            }

            var serializer = new JsonSerializer();

            foreach (var prop in root.Properties())
            {
                string bucketName = prop.Name;
                if (prop.Value == null) continue;

                // Handle generic Elements fallback array
                if (string.Equals(bucketName, "Elements", StringComparison.OrdinalIgnoreCase))
                {
                    if (prop.Value is JArray array)
                    {
                        foreach (var token in array)
                        {
                            if (token is JObject itemObj)
                            {
                                var model = HydrateModel(itemObj, null, bucketName, serializer);
                                if (model != null)
                                {
                                    results.Add(model);
                                }
                            }
                        }
                    }
                    continue;
                }

                // Handle standard/legacy bucket dictionaries
                if (prop.Value is JObject dictObj)
                {
                    var desc = StandardTypeMetadataRegistry.GetDescriptorByBucket(bucketName);

                    foreach (var entryProp in dictObj.Properties())
                    {
                        if (entryProp.Value is JObject itemObj)
                        {
                            var model = HydrateModel(itemObj, desc, bucketName, serializer);
                            if (model != null)
                            {
                                results.Add(model);
                            }
                        }
                    }
                }
            }

            return results;
        }

        private static ElementModel? HydrateModel(
            JObject itemObj,
            StandardTypeDescriptor? desc,
            string bucketName,
            JsonSerializer serializer)
        {
            try
            {
                string? explicitClass = itemObj["Class"]?.ToString();
                string? className = explicitClass;

                if (string.IsNullOrEmpty(className) && desc != null)
                {
                    className = desc.DefaultClass;
                }

                Type targetType = ResolveHydrationTargetType(className, desc, bucketName);

                var model = (ElementModel?)itemObj.ToObject(targetType, serializer);
                if (model == null) return null;

                if (string.IsNullOrEmpty(model.Class) && !string.IsNullOrEmpty(className))
                {
                    model.Class = className!;
                }

                // Force IsTemplate = true for ViewTemplates
                if (string.Equals(bucketName, "ViewTemplates", StringComparison.OrdinalIgnoreCase) ||
                    (desc != null && desc.BucketName == "ViewTemplates"))
                {
                    model.IsTemplate = true;
                    if (model is ViewModel vm)
                    {
                        vm.IsTemplate = true;
                    }
                }

                return model;
            }
            catch (Exception ex)
            {
                SerializationResultModel.LogWarning($"Skipped invalid or corrupt element in bucket '{bucketName}': {ex.Message}");
                return null;
            }
        }

        private static Type ResolveHydrationTargetType(
            string? className,
            StandardTypeDescriptor? desc,
            string bucketName)
        {
            // View polymorphic hydration based on Class
            if (!string.IsNullOrEmpty(className))
            {
                if (string.Equals(className, "Autodesk.Revit.DB.ViewPlan", StringComparison.OrdinalIgnoreCase))
                    return typeof(ViewPlanModel);
                if (string.Equals(className, "Autodesk.Revit.DB.ViewSheet", StringComparison.OrdinalIgnoreCase))
                    return typeof(ViewSheetModel);
                if (string.Equals(className, "Autodesk.Revit.DB.ViewSchedule", StringComparison.OrdinalIgnoreCase))
                    return typeof(ViewScheduleModel);
            }

            if (string.Equals(bucketName, "Views", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(bucketName, "ViewTemplates", StringComparison.OrdinalIgnoreCase) ||
                (desc != null && (desc.BucketName == "Views" || desc.BucketName == "ViewTemplates")))
            {
                return typeof(ViewModel);
            }

            // Try exact class resolution from registry
            if (!string.IsNullOrEmpty(className))
            {
                var classDesc = StandardTypeMetadataRegistry.GetDescriptorByClass(className);
                if (classDesc != null)
                {
                    return classDesc.ModelType;
                }
            }

            // Fallback to bucket descriptor model type
            if (desc != null)
            {
                return desc.ModelType;
            }

            return typeof(ElementModel);
        }
    }
}
