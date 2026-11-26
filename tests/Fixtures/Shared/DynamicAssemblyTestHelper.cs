using System;
using System.Reflection;
using System.Reflection.Emit;

namespace SyntheticTests.Helpers
{
    /// <summary>
    /// Creates dynamic in-memory assemblies with optional versions and <see cref="AssemblyMetadataAttribute"/> entries
    /// for multi-TFM unit tests and live Revit smoke tests.
    /// </summary>
    public static class DynamicAssemblyTestHelper
    {
        public static Assembly CreateDynamicAssembly(
            Version? version,
            params (string Key, string Value)[] metadataEntries)
        {
            AssemblyName name = new AssemblyName("DynamicAssemblyTest_" + Guid.NewGuid().ToString("N"));
            if (version != null)
            {
                name.Version = version;
            }

#if NETFRAMEWORK
            AssemblyBuilder builder = AppDomain.CurrentDomain.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
#else
            AssemblyBuilder builder = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
#endif
            if (metadataEntries != null && metadataEntries.Length > 0)
            {
                ConstructorInfo ctor = typeof(AssemblyMetadataAttribute).GetConstructor(new[] { typeof(string), typeof(string) })!;
                foreach ((string key, string value) in metadataEntries)
                {
                    builder.SetCustomAttribute(new CustomAttributeBuilder(ctor, new object[] { key, value }));
                }
            }

            return builder;
        }
    }
}
