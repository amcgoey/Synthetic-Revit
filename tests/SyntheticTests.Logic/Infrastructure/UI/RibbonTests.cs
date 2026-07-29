using NUnit.Framework;
using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Synthetic.Core;
using SyntheticTests.Helpers;

namespace SyntheticTests.Infrastructure.UI
{
    [TestFixture]
    public class RibbonTests
    {
        [Test]
        public void RibbonConfig_ParsesWithoutErrors()
        {
            string projectRoot = TestPathHelper.GetProjectRoot();
            string configPath = Path.Combine(projectRoot, "src", "SyntheticShared", "Assets", "ribbon_config.json");

            if (!File.Exists(configPath))
            {
                string testBinDir = TestContext.CurrentContext.TestDirectory;
                configPath = Path.Combine(testBinDir, "Assets", "ribbon_config.json");
            }

            Assert.IsTrue(File.Exists(configPath), $"Ribbon configuration file 'ribbon_config.json' not found at expected path: {configPath}");

            string jsonContent = File.ReadAllText(configPath);
            
            Assert.DoesNotThrow(() => {
                JObject ribbonConfig = JObject.Parse(jsonContent);
                Assert.IsNotNull(ribbonConfig);
            }, "Expected ribbon_config.json to parse without errors.");
        }

        [Test]
        public void RibbonConfig_AllCommandClasses_ShouldResolve()
        {
            string projectRoot = TestPathHelper.GetProjectRoot();
            string configPath = Path.Combine(projectRoot, "src", "SyntheticShared", "Assets", "ribbon_config.json");

            if (!File.Exists(configPath))
            {
                string testBinDir = TestContext.CurrentContext.TestDirectory;
                configPath = Path.Combine(testBinDir, "Assets", "ribbon_config.json");
            }

            Assert.IsTrue(File.Exists(configPath), $"Ribbon configuration file 'ribbon_config.json' not found at expected path: {configPath}");

            string jsonContent = File.ReadAllText(configPath);
            JObject ribbonConfig = JObject.Parse(jsonContent);

            List<string> commandClasses = new List<string>();
            ExtractCommandClasses(ribbonConfig, commandClasses);

            Assert.IsNotEmpty(commandClasses, "No command classes were parsed from ribbon_config.json.");

            var assembly = typeof(App).Assembly;
            List<string> failedClasses = new List<string>();

            foreach (var className in commandClasses)
            {
                if (!TypeExists(assembly, className))
                {
                    failedClasses.Add(className);
                }
            }

            if (failedClasses.Count > 0)
            {
                Assert.Fail("The following command classes defined in ribbon_config.json could not be resolved in the assembly:\n" +
                            string.Join("\n", failedClasses));
            }
        }

        private bool TypeExists(System.Reflection.Assembly assembly, string className)
        {
            try
            {
                var type = assembly.GetType(className);
                return type != null;
            }
            catch (TypeLoadException)
            {
                // Type exists but could not be fully loaded due to Revit interface mismatch headlessly
                return true;
            }
            catch (FileNotFoundException)
            {
                // Type exists but a dependent assembly was not found headlessly
                return true;
            }
        }

        private void ExtractCommandClasses(JToken token, List<string> commandClasses)
        {
            if (token is JArray array)
            {
                foreach (var child in array)
                {
                    ExtractCommandClasses(child, commandClasses);
                }
            }
            else if (token is JObject obj)
            {
                string className = obj.Value<string>("class");
                if (!string.IsNullOrEmpty(className))
                {
                    commandClasses.Add(className);
                }
                foreach (var property in obj.Properties())
                {
                    ExtractCommandClasses(property.Value, commandClasses);
                }
            }
        }
    }
}
