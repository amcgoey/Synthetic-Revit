using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using Autodesk.Revit.DB;

namespace SyntheticTests.Helpers
{
    public static class MockFailureFactory
    {
        private static void IgnoreIfRevitApiLoaded()
        {
#if REVIT2023 || REVIT2024 || REVIT2025 || REVIT2026
            Assert.Ignore("Mock failure preprocessor tests run in headless Logic test suite.");
#endif
        }

        public static FailureMessageAccessor CreateFailureMessage(FailureDefinitionId id, FailureSeverity severity = FailureSeverity.Warning, string description = "")
        {
            IgnoreIfRevitApiLoaded();
            try
            {
                var ctor = typeof(FailureMessageAccessor).GetConstructor(new[] { typeof(FailureDefinitionId), typeof(FailureSeverity), typeof(string) });
                if (ctor != null)
                {
                    return (FailureMessageAccessor)ctor.Invoke(new object[] { id, severity, description ?? "" });
                }
            }
            catch { }

            var msg = (FailureMessageAccessor)FormatterServices.GetUninitializedObject(typeof(FailureMessageAccessor));
            SetFieldOrProperty(msg, "DefinitionId", id, "m_id");
            SetFieldOrProperty(msg, "Severity", severity, "m_severity");
            SetFieldOrProperty(msg, "DescriptionText", description ?? "", "m_description");
            return msg;
        }

        public static FailuresAccessor CreateFailuresAccessor(IEnumerable<FailureMessageAccessor> messages)
        {
            IgnoreIfRevitApiLoaded();
            var list = messages != null ? new List<FailureMessageAccessor>(messages) : new List<FailureMessageAccessor>();
            
            try
            {
                var ctor = typeof(FailuresAccessor).GetConstructor(new[] { typeof(IEnumerable<FailureMessageAccessor>) });
                if (ctor != null)
                {
                    return (FailuresAccessor)ctor.Invoke(new object[] { list });
                }
            }
            catch { }

            var accessor = (FailuresAccessor)FormatterServices.GetUninitializedObject(typeof(FailuresAccessor));
            var deletedList = new List<FailureMessageAccessor>();
            var resolvedList = new List<FailureMessageAccessor>();

            SetFieldOrProperty(accessor, "_messages", list, "m_messages", "_failures");
            SetFieldOrProperty(accessor, "DeletedWarnings", deletedList, "_deletedWarnings", "m_deletedWarnings");
            SetFieldOrProperty(accessor, "ResolvedFailures", resolvedList, "_resolvedFailures", "m_resolvedFailures");
            return accessor;
        }

        public static List<FailureMessageAccessor> GetDeletedWarnings(FailuresAccessor accessor)
        {
            return GetFieldOrProperty<List<FailureMessageAccessor>>(accessor, "DeletedWarnings", "_deletedWarnings", "m_deletedWarnings") ?? new List<FailureMessageAccessor>();
        }

        public static List<FailureMessageAccessor> GetResolvedFailures(FailuresAccessor accessor)
        {
            return GetFieldOrProperty<List<FailureMessageAccessor>>(accessor, "ResolvedFailures", "_resolvedFailures", "m_resolvedFailures") ?? new List<FailureMessageAccessor>();
        }

        private static void SetFieldOrProperty(object obj, string name1, object value, string? name2 = null, string? name3 = null)
        {
            if (obj == null) return;
            Type type = obj.GetType();
            string?[] names = new[] { name1, name2, name3 };
            foreach (string? name in names)
            {
                if (string.IsNullOrEmpty(name)) continue;
                PropertyInfo? prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null && prop.CanWrite)
                {
                    try { prop.SetValue(obj, value); return; } catch { }
                }
                FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    try { field.SetValue(obj, value); return; } catch { }
                }
            }
        }

        private static T? GetFieldOrProperty<T>(object obj, params string[] names) where T : class
        {
            if (obj == null) return null;
            Type type = obj.GetType();
            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name)) continue;
                PropertyInfo? prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null && prop.CanRead)
                {
                    try { if (prop.GetValue(obj) is T val) return val; } catch { }
                }
                FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    try { if (field.GetValue(obj) is T val) return val; } catch { }
                }
            }
            return null;
        }
    }
}
