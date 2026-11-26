using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Synthetic.Features.DetailItemFactory.Settings;
using Synthetic.Platform.Settings;
using SyntheticTests.Helpers;

namespace SyntheticTests.Features.DetailItemFactory
{
    [TestFixture]
    [UnitTest]
    public class DetailItemFactorySettingsTests
    {
        [Test]
        public void ParameterlessConstructor_AndDefaults_InitializeExpectedBaselineValues()
        {
            var settings = new DetailItemFactorySettings();

            Assert.AreEqual("DetailItemFactorySettings", DetailItemFactorySettings.Name);
            Assert.AreEqual("DetailItemFactorySettings", settings.ModuleKey);
            Assert.AreEqual(string.Empty, settings.OutputPath);
            Assert.AreEqual("Detail Items", settings.TargetSubcategory);
            Assert.IsTrue(settings.IsValid());

            settings.OutputPath = @"C:\Custom\Out";
            settings.TargetSubcategory = "Heavy Line";

            DetailItemFactorySettings returned = settings.Defaults();
            Assert.AreSame(settings, returned);
            Assert.AreEqual(string.Empty, settings.OutputPath);
            Assert.AreEqual("Detail Items", settings.TargetSubcategory);
        }

        [Test]
        public void JsonRoundTrip_PreservesExactSchemaKeys_AndMatchesSettingsTemplate()
        {
            var original = new DetailItemFactorySettings
            {
                OutputPath = @"\\studio-server\standards\DetailItems",
                TargetSubcategory = "Medium Lines"
            };

            string json = JsonConvert.SerializeObject(original, Formatting.Indented);
            JObject obj = JObject.Parse(json);
            Assert.AreEqual(2, obj.Count, "JSON schema must remain strictly OutputPath and TargetSubcategory.");
            Assert.AreEqual(@"\\studio-server\standards\DetailItems", obj["OutputPath"]?.Value<string>());
            Assert.AreEqual("Medium Lines", obj["TargetSubcategory"]?.Value<string>());

            DetailItemFactorySettings? roundTripped = JsonConvert.DeserializeObject<DetailItemFactorySettings>(json);
            Assert.IsNotNull(roundTripped);
            Assert.AreEqual(original.OutputPath, roundTripped!.OutputPath);
            Assert.AreEqual(original.TargetSubcategory, roundTripped.TargetSubcategory);
            Assert.IsTrue(roundTripped.IsValid());

            string templatePath = Path.Combine(
                TestPathHelper.GetProjectRoot(),
                "src",
                "Platform",
                "Settings",
                "Addin",
                "Defaults",
                "SyntheticSettings.template.json");
            Config? templateConfig = Config.ReadFromFile(templatePath);
            Assert.IsNotNull(templateConfig);
            DetailItemFactorySettings templateSettings = templateConfig!.GetSettings<DetailItemFactorySettings>(DetailItemFactorySettings.Name);
            DetailItemFactorySettings defaults = new DetailItemFactorySettings().Defaults();
            Assert.AreEqual(defaults.OutputPath, templateSettings.OutputPath);
            Assert.AreEqual(defaults.TargetSubcategory, templateSettings.TargetSubcategory);
        }

        [Test]
        public void DeepClone_IsolatesFieldsFromSource()
        {
            var source = new DetailItemFactorySettings
            {
                OutputPath = @"C:\Original\Path",
                TargetSubcategory = "Thin Lines"
            };

            DetailItemFactorySettings clone = source.DeepClone();
            clone.OutputPath = @"C:\Mutated\Path";
            clone.TargetSubcategory = "Wide Lines";

            Assert.AreEqual(@"C:\Original\Path", source.OutputPath);
            Assert.AreEqual("Thin Lines", source.TargetSubcategory);
        }

        [TestCase("")]
        [TestCase(@"C:\")]
        [TestCase(@"C:\NonExistent\Offline\DetailItems")]
        [TestCase(@"D:/Studio/DetailItems/")]
        [TestCase(@"\\offline-server\share\DetailItems")]
        [TestCase(@"\\offline-server\share\")]
        public void IsValid_AcceptsEmptyAndValidSyntacticPaths_WithoutRequiringDirectoryOnDisk(string outputPath)
        {
            var settings = new DetailItemFactorySettings
            {
                OutputPath = outputPath,
                TargetSubcategory = "Detail Items"
            };

            Assert.IsTrue(settings.IsValid());
        }

        [TestCase("   ")]
        [TestCase(@"relative\folder")]
        [TestCase(@".\relative")]
        [TestCase(@"..\parent")]
        [TestCase(@"\drive-relative-root")]
        [TestCase(@"C:drive-relative")]
        [TestCase(@"C:")]
        [TestCase(@"\\server-only")]
        [TestCase(@"\\server\")]
        [TestCase(@"C:\Invalid*Folder")]
        [TestCase(@"C:\Invalid?Folder")]
        [TestCase(@"C:\Invalid<Folder")]
        [TestCase(@"C:\Invalid>Folder")]
        [TestCase(@"C:\Invalid|Folder")]
        [TestCase("C:\\Invalid\"Folder")]
        [TestCase(@"C:\Invalid:Stream")]
        [TestCase(@"C:\TrailingDot.\Sub")]
        [TestCase(@"C:\CON\Sub")]
        [TestCase(@"C:\Out\NUL")]
        [TestCase(@"1:\BadDrive")]
        public void IsValid_RejectsInvalidPathSyntax(string outputPath)
        {
            var settings = new DetailItemFactorySettings
            {
                OutputPath = outputPath,
                TargetSubcategory = "Detail Items"
            };

            Assert.IsFalse(settings.IsValid());
        }
    }
}
