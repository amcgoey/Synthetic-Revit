using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Operations;

namespace SyntheticTests.RevitDOM
{
    [TestFixture]
    [UnitTest]
    public class GoldenJsonContractTests
    {
        private static readonly string[] GoldenFileNames =
        {
            "HostObjType.json",
            "DimensionType.json",
            "FillPatternElement.json",
            "Material.json",
            "ParameterFilterElement.json",
            "View.json"
        };

        [SetUp]
        public void SetUp()
        {
            SerializationResultModel.ClearWarnings();
        }

        [TearDown]
        public void TearDown()
        {
            SerializationResultModel.ClearWarnings();
        }

        [TestCaseSource(nameof(GoldenFileNames))]
        public void GoldenFixture_IsCopiedToOutput_AndRoundTripsFromJsonToJsonWithZeroDrift(string fileName)
        {
            string outputAssetPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Assets", "golden", fileName);
            Assert.IsTrue(File.Exists(outputAssetPath),
                $"Expected nested golden JSON asset to be copied to test output directory: {outputAssetPath}");

            string goldenJson = File.ReadAllText(outputAssetPath);
            Assert.IsFalse(string.IsNullOrWhiteSpace(goldenJson), $"Golden JSON '{fileName}' must not be empty.");

            List<ElementModel> hydrated = PocoJsonSerializer.FromJson(goldenJson).ToList();
            Assert.IsEmpty(SerializationResultModel.CurrentThreadWarnings,
                $"PocoJsonSerializer.FromJson emitted warnings for '{fileName}': {string.Join("; ", SerializationResultModel.CurrentThreadWarnings)}");
            Assert.IsNotEmpty(hydrated, $"Golden JSON '{fileName}' must hydrate at least one ElementModel.");

            string roundTrippedJson = PocoJsonSerializer.ToJson(hydrated);
            Assert.IsEmpty(SerializationResultModel.CurrentThreadWarnings,
                $"PocoJsonSerializer.ToJson emitted warnings for '{fileName}': {string.Join("; ", SerializationResultModel.CurrentThreadWarnings)}");

            string normalizedExpected = NormalizeJsonFormatting(goldenJson);
            string normalizedActual = NormalizeJsonFormatting(roundTrippedJson);

            Assert.AreEqual(
                normalizedExpected,
                normalizedActual,
                $"Canonical formatted JSON drift detected on FromJson -> ToJson for '{fileName}'.");

            Assert.IsTrue(
                JToken.DeepEquals(JObject.Parse(normalizedExpected), JObject.Parse(normalizedActual)),
                $"Structural JSON mismatch on FromJson -> ToJson for '{fileName}'.");
        }

        [Test]
        public void HostObjTypeGolden_HydratesCompoundStructureAndParameters()
        {
            var models = LoadGoldenModels("HostObjType.json");
            Assert.AreEqual(1, models.Count);

            var hostObj = models[0] as HostObjTypeModel;
            Assert.IsNotNull(hostObj, "HostObjType.json must hydrate to HostObjTypeModel.");
            Assert.AreEqual("Autodesk.Revit.DB.WallType", hostObj!.Class);
            Assert.AreEqual("Basic Wall", hostObj.FamilyName);
            Assert.AreEqual("Golden_Partition_Wall", hostObj.Name);
            Assert.AreEqual("Interior", hostObj.Function?.Value);

            Assert.IsNotNull(hostObj.Structure);
            Assert.AreEqual(2, hostObj.Structure!.Layers.Count);
            Assert.AreEqual("Structure", hostObj.Structure.Layers[0].Function);
            Assert.AreEqual(0.5, hostObj.Structure.Layers[0].Width);
            Assert.IsTrue(hostObj.Structure.Layers[0].StructuralMaterial);
            Assert.AreEqual(2, hostObj.Structure.Layers[0].Priority);
            Assert.AreEqual("Golden_Structural_Steel", hostObj.Structure.Layers[0].MaterialId.Name);

            Assert.AreEqual("Finish1", hostObj.Structure.Layers[1].Function);
            Assert.AreEqual(0.125, hostObj.Structure.Layers[1].Width);
            Assert.IsFalse(hostObj.Structure.Layers[1].StructuralMaterial);
            Assert.AreEqual(5, hostObj.Structure.Layers[1].Priority);

            Assert.AreEqual(1, hostObj.Parameters.Count);
            Assert.AreEqual("Type Comments", hostObj.Parameters[0].Name);
        }

        [Test]
        public void DimensionTypeGolden_HydratesDistinctStylesWithCompositeKeys()
        {
            var models = LoadGoldenModels("DimensionType.json");
            Assert.AreEqual(2, models.Count);

            var dims = models.OfType<DimensionTypeModel>().ToList();
            Assert.AreEqual(2, dims.Count);
            Assert.IsTrue(dims.Any(d => d.DimensionStyle == "Angular" && d.Name == "Golden_Angular_Dim"));
            Assert.IsTrue(dims.Any(d => d.DimensionStyle == "Linear" && d.Name == "Golden_Linear_Dim"));
        }

        [Test]
        public void FillPatternElementGolden_HydratesPatternAndFillGrids()
        {
            var models = LoadGoldenModels("FillPatternElement.json");
            Assert.AreEqual(1, models.Count);

            var fp = models[0] as FillPatternElementModel;
            Assert.IsNotNull(fp, "FillPatternElement.json must hydrate to FillPatternElementModel.");
            Assert.AreEqual("Golden_Diagonal_Crosshatch", fp!.Name);
            Assert.IsNotNull(fp.Pattern);
            Assert.AreEqual("Drafting", fp.Pattern!.Target);
            Assert.AreEqual("ToHost", fp.Pattern.HostOrientation);
            Assert.AreEqual(2, fp.Pattern.FillGrids.Count);
            Assert.AreEqual(0.125, fp.Pattern.FillGrids[0].Offset);
            Assert.NotNull(fp.Pattern.FillGrids[0].Origin);
        }

        [Test]
        public void MaterialGolden_HydratesNestedAppearanceStructuralAndThermalAssets()
        {
            var models = LoadGoldenModels("Material.json");
            Assert.AreEqual(1, models.Count);

            var mat = models[0] as MaterialModel;
            Assert.IsNotNull(mat, "Material.json must hydrate to MaterialModel.");
            Assert.AreEqual("Golden_Structural_Steel", mat!.Name);

            Assert.IsNotNull(mat.AppearanceAsset);
            Assert.AreEqual("Golden_Steel_Appearance", mat.AppearanceAsset!.Name);
            Assert.AreEqual(0.75, mat.AppearanceAsset.Smoothness);
            Assert.AreEqual(150, mat.AppearanceAsset.Color?.Red);

            Assert.IsNotNull(mat.StructuralAsset);
            Assert.AreEqual("Golden_Steel_Structural", mat.StructuralAsset!.Name);
            Assert.AreEqual("Metal", mat.StructuralAsset.StructuralAssetClass);
            Assert.AreEqual(490.0, mat.StructuralAsset.Density);
            Assert.AreEqual(29000000.0, mat.StructuralAsset.YoungModulus);
            Assert.AreEqual(0.3, mat.StructuralAsset.PoissonRatio);

            Assert.IsNotNull(mat.ThermalAsset);
            Assert.AreEqual("Golden_Steel_Thermal", mat.ThermalAsset!.Name);
            Assert.AreEqual("Solid", mat.ThermalAsset.ThermalMaterialType);
            Assert.AreEqual(45.0, mat.ThermalAsset.ThermalConductivity);
            Assert.AreEqual(0.46, mat.ThermalAsset.SpecificHeat);
            Assert.AreEqual(0.8, mat.ThermalAsset.Emissivity);

            Assert.AreEqual("Golden_Diagonal_Crosshatch", mat.SurfaceForegroundPatternId?.Name);
            Assert.AreEqual("Golden_Diagonal_Crosshatch", mat.CutForegroundPatternId?.Name);
        }

        [Test]
        public void ParameterFilterElementGolden_HydratesCategoriesAndRecursiveRootRule()
        {
            var models = LoadGoldenModels("ParameterFilterElement.json");
            Assert.AreEqual(1, models.Count);

            var filter = models[0] as ParameterFilterElementModel;
            Assert.IsNotNull(filter, "ParameterFilterElement.json must hydrate to ParameterFilterElementModel.");
            Assert.AreEqual("Golden_Wall_Comment_Filter", filter!.Name);
            Assert.AreEqual(1, filter.Categories.Count);
            Assert.AreEqual("Walls", filter.Categories[0].Name);
            Assert.AreEqual(-2000011, filter.Categories[0].Id);

            Assert.IsNotNull(filter.RootRule);
            Assert.AreEqual("LogicalAnd", filter.RootRule!.RuleType);
            Assert.AreEqual(2, filter.RootRule.InnerRules.Count);
            Assert.AreEqual("BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS", filter.RootRule.InnerRules[0].ParameterId?.Name);
            Assert.AreEqual("Golden", filter.RootRule.InnerRules[0].RuleValue);
            Assert.AreEqual("BuiltInParameter.ALL_MODEL_MARK", filter.RootRule.InnerRules[1].ParameterId?.Name);
            Assert.AreEqual("G1", filter.RootRule.InnerRules[1].RuleValue);
        }

        [Test]
        public void ViewGolden_HydratesPolymorphicViewPlanAndViewTemplate()
        {
            var models = LoadGoldenModels("View.json");
            Assert.AreEqual(2, models.Count);

            var planTemplate = models.OfType<ViewPlanModel>().FirstOrDefault(v => v.Name == "Golden_Plan_Template");
            Assert.IsNotNull(planTemplate, "ViewTemplates entry must hydrate polymorphically to ViewPlanModel.");
            Assert.IsTrue(planTemplate!.IsTemplate);
            Assert.IsTrue(((ElementModel)planTemplate).IsTemplate);
            Assert.AreEqual(1, planTemplate.NonControlledParameterIds.Count);

            var floorPlan = models.OfType<ViewPlanModel>().FirstOrDefault(v => v.Name == "Golden_Floor_Plan");
            Assert.IsNotNull(floorPlan, "Views entry must hydrate polymorphically to ViewPlanModel.");
            Assert.IsFalse(floorPlan!.IsTemplate);
            Assert.AreEqual("ShadingWithEdges", floorPlan.DisplayStyle?.Value);
            Assert.AreEqual(100, floorPlan.Scale);
            Assert.IsNotNull(floorPlan.ViewRange);
            Assert.AreEqual(7.5, floorPlan.ViewRange!.TopOffset);
            Assert.IsNotNull(floorPlan.CropBox);
            Assert.IsNotNull(floorPlan.ViewFilterOverrides);
            Assert.AreEqual(1, floorPlan.ViewFilterOverrides!.Count);
            Assert.AreEqual("Golden_Wall_Comment_Filter", floorPlan.ViewFilterOverrides[0].FilterId?.Name);
            Assert.IsNotNull(floorPlan.CategoryGraphicOverrides);
            Assert.AreEqual(1, floorPlan.CategoryGraphicOverrides!.Count);
        }

        [TestCaseSource(nameof(GoldenFileNames))]
        public void GoldenFixture_WithTemplateOmittedVolatileIds_NormalizesWithZeroDrift(string fileName)
        {
            string outputAssetPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Assets", "golden", fileName);
            string goldenJson = File.ReadAllText(outputAssetPath);

            JObject templateRoot = JObject.Parse(goldenJson);
            StripDefaultVolatileIds(templateRoot);
            string templateJson = templateRoot.ToString(Formatting.Indented);

            List<ElementModel> hydrated = PocoJsonSerializer.FromJson(templateJson).ToList();
            Assert.IsEmpty(SerializationResultModel.CurrentThreadWarnings);
            Assert.IsNotEmpty(hydrated);

            string roundTrippedJson = PocoJsonSerializer.ToJson(hydrated);
            Assert.IsEmpty(SerializationResultModel.CurrentThreadWarnings);

            JObject roundTrippedNormalized = JObject.Parse(roundTrippedJson);
            StripDefaultVolatileIds(roundTrippedNormalized);

            Assert.AreEqual(
                NormalizeJsonFormatting(templateJson),
                NormalizeJsonFormatting(roundTrippedNormalized.ToString(Formatting.Indented)),
                $"Template-normalized FromJson -> ToJson drift detected for '{fileName}'.");
        }

        private static List<ElementModel> LoadGoldenModels(string fileName)
        {
            string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Assets", "golden", fileName);
            string json = File.ReadAllText(path);
            return PocoJsonSerializer.FromJson(json).ToList();
        }

        private static void StripDefaultVolatileIds(JToken token)
        {
            if (token is JObject obj)
            {
                JProperty? idProp = obj.Property("Id");
                if (idProp != null && idProp.Value.Type == JTokenType.Integer && idProp.Value.Value<long>() == 0L)
                {
                    idProp.Remove();
                }

                JProperty? uniqueIdProp = obj.Property("UniqueId");
                if (uniqueIdProp != null && uniqueIdProp.Value.Type == JTokenType.Null)
                {
                    uniqueIdProp.Remove();
                }

                foreach (JProperty prop in obj.Properties())
                {
                    StripDefaultVolatileIds(prop.Value);
                }
            }
            else if (token is JArray array)
            {
                foreach (JToken item in array)
                {
                    StripDefaultVolatileIds(item);
                }
            }
        }
        }

        private static string NormalizeJsonFormatting(string json)
        {
            return json.Replace("\r\n", "\n").Trim();
        }
    }
}
