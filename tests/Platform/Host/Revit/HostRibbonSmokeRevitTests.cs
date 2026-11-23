using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using NUnit.Framework;
using Synthetic.Platform.Host;
using SyntheticTests.Helpers;

namespace SyntheticTests.Platform.Host
{
    /// <summary>
    /// Live Revit smoke tests verifying that all 29 ribbon commands in <c>src/Assets/ribbon_config.json</c>
    /// plus <c>CmdSetTemplate</c> resolve cleanly as concrete <see cref="IExternalCommand"/> types
    /// in the host payload assembly.
    /// </summary>
    [TestFixture]
    [RevitSmokeTest]
    public class HostRibbonSmokeRevitTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
            TestWorkerJournalMarker.EnsureWritten(uiapp);
        }

        [Test]
        public void RibbonConfig_AllTwentyNineRibbonCommands_AndCmdSetTemplate_ResolveInPayloadAssembly()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");

            string projectRoot = TestPathHelper.GetProjectRoot();
            string configPath = Path.Combine(projectRoot, "src", "Assets", "ribbon_config.json");
            Assert.IsTrue(File.Exists(configPath), $"Expected ribbon_config.json at '{configPath}'.");

            RibbonConfig? config = JsonConvert.DeserializeObject<RibbonConfig>(File.ReadAllText(configPath));
            Assert.IsNotNull(config);
            Assert.IsNotNull(config!.Panels);

            var ribbonClasses = new List<string>();
            foreach (PanelConfig panel in config.Panels!)
            {
                CollectCommandClasses(panel.Items, ribbonClasses);
                CollectCommandClasses(panel.SlideoutItems, ribbonClasses);
            }

            Assert.AreEqual(29, ribbonClasses.Count, "Expected exactly 29 PushButton command entries in ribbon_config.json.");

            var allExpectedCommands = new List<string>(ribbonClasses)
            {
                "Synthetic.Features.AutoTagger.Commands.CmdSetTemplate"
            };

            var payloadAssembly = typeof(App).Assembly;
            var failures = new List<string>();

            foreach (string className in allExpectedCommands)
            {
                if (!className.StartsWith("Synthetic.Features.", StringComparison.Ordinal) ||
                    className.IndexOf(".Commands.", StringComparison.Ordinal) < 0)
                {
                    failures.Add($"{className}: expected Synthetic.Features.<Slice>.Commands.* namespace.");
                    continue;
                }

                Type? resolvedType = payloadAssembly.GetType(className, throwOnError: false);
                if (resolvedType == null)
                {
                    failures.Add($"{className}: type not found in {payloadAssembly.GetName().Name}.");
                    continue;
                }

                if (resolvedType.IsAbstract || !typeof(IExternalCommand).IsAssignableFrom(resolvedType))
                {
                    failures.Add($"{className}: resolved type is not a concrete IExternalCommand.");
                }
            }

            Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
        }

        private static void CollectCommandClasses(IEnumerable<RibbonItemConfig>? items, ICollection<string> classes)
        {
            if (items == null)
            {
                return;
            }

            foreach (RibbonItemConfig item in items)
            {
                if (!string.IsNullOrWhiteSpace(item.Class))
                {
                    classes.Add(item.Class);
                }

                CollectCommandClasses(item.SubItems, classes);
            }
        }
    }
}
