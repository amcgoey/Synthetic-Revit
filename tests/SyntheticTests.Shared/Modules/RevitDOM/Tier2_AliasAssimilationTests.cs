using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class Tier2_AliasAssimilationTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        private Material CreateMaterial(Document doc, string name)
        {
            ElementId matId = Material.Create(doc, name);
            return (Material)doc.GetElement(matId);
        }

        [Test]
        public void ToRevit_SuccessfulAssimilation()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Alias Assimilation Test"))
                {
                    tg.Start();

                    // 1. Setup the Test Environment (Create Alias Material and WallType)
                    Material aliasMaterial;
                    WallType wallType;
                    using (var t = new Transaction(doc, "Create Alias Material and WallType"))
                    {
                        t.Start();
                        aliasMaterial = CreateMaterial(doc, "AliasMaterial_Test");

                        wallType = new FilteredElementCollector(doc)
                            .OfClass(typeof(WallType))
                            .Cast<WallType>()
                            .First();

                        // Assign alias material to its compound structure
                        CompoundStructure cs = wallType.GetCompoundStructure();
                        if (cs != null)
                        {
                            var layers = cs.GetLayers();
                            if (layers.Count > 0)
                            {
                                layers[0].MaterialId = aliasMaterial.Id;
                                cs.SetLayers(layers);
                                wallType.SetCompoundStructure(cs);
                            }
                        }
                        t.Commit();
                    }

                    // Verify initial reference
                    Assert.AreEqual(aliasMaterial.Id, wallType.GetCompoundStructure().GetLayers()[0].MaterialId);

                    // 2. Construct the POCO MaterialModel representing "Primary Material"
                    var primaryModel = new MaterialModel
                    {
                        Name = "PrimaryMaterial_Test",
                        Class = "Autodesk.Revit.DB.Material",
                        Aliases = new List<string> { "AliasMaterial_Test" }
                    };

                    // 3. Execute StandardSerializationEngine.ToRevit()
                    var engine = new StandardSerializationEngine();
                    var results = engine.ToRevit(new List<ObjectModel> { primaryModel }, doc).ToList();

                    foreach (var res in results)
                    {
                        Console.WriteLine($"[TEST LOG] Model: {res.Model.GetType().Name}, Success: {res.Success}, Error: {res.ErrorMessage}, Action: {res.Action}, Message: {res.Message}");
                        if (res.Exception != null) Console.WriteLine($"[TEST LOG] Exception: {res.Exception}");
                    }

                    // 4. Assert Primary Material was created
                    var allMats = new FilteredElementCollector(doc)
                        .OfClass(typeof(Material))
                        .Cast<Material>()
                        .Select(m => m.Name)
                        .ToList();
                    Console.WriteLine("[TEST LOG] All materials: " + string.Join(", ", allMats));

                    var primaryMaterial = new FilteredElementCollector(doc)
                        .OfClass(typeof(Material))
                        .Cast<Material>()
                        .FirstOrDefault(m => m.Name == "PrimaryMaterial_Test");
                    Assert.IsNotNull(primaryMaterial, "Primary Material should be created.");

                    // Assert Alias Material no longer exists
                    var aliasCheck = new FilteredElementCollector(doc)
                        .OfClass(typeof(Material))
                        .Cast<Material>()
                        .FirstOrDefault(m => m.Name == "AliasMaterial_Test");
                    Assert.IsNull(aliasCheck, "Alias Material should be deleted.");

                    // Assert WallType's compound structure now references the newly created Primary Material
                    Assert.AreEqual(primaryMaterial.Id, wallType.GetCompoundStructure().GetLayers()[0].MaterialId);

                    // Assert serialization results
                    Assert.IsTrue(results.Any(r => r.Success && r.Model == primaryModel && r.Action == "Created"), "Should contain a primary creation result");
                    Assert.IsTrue(results.Any(r => r.Success && r.Model == primaryModel && r.Action == "Merged Alias"), "Should contain a merge alias success result");

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ToRevit_GracefulDegradation_OnAliasFailure()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Alias Failure Test"))
                {
                    tg.Start();

                    // Create the alias material that will be deleted early during resolution
                    Material aliasMaterial;
                    using (var t = new Transaction(doc, "Create Fail Alias Material"))
                    {
                        t.Start();
                        aliasMaterial = CreateMaterial(doc, "AliasMaterial_FailTest");
                        t.Commit();
                    }

                    // 1. Construct POCO MaterialModel with alias name "AliasMaterial_FailTest"
                    var primaryModel = new MaterialModel
                    {
                        Name = "PrimaryMaterial_FailTest",
                        Class = "Autodesk.Revit.DB.Material",
                        Aliases = new List<string> { "AliasMaterial_FailTest" }
                    };

                    // 2. Execute ToRevit using our FailIdentityService
                    var realIdentity = new RevitIdentityService();
                    var failIdentity = new FailIdentityService(realIdentity, doc, aliasMaterial.Id);
                    var engine = new StandardSerializationEngine(failIdentity);
                    var results = engine.ToRevit(new List<ObjectModel> { primaryModel }, doc).ToList();

                    foreach (var res in results)
                    {
                        Console.WriteLine($"[TEST LOG 2] Model: {res.Model.GetType().Name}, Success: {res.Success}, Error: {res.ErrorMessage}, Action: {res.Action}, Message: {res.Message}");
                        if (res.Exception != null) Console.WriteLine($"[TEST LOG 2] Exception: {res.Exception}");
                    }

                    // 3. Assert primary transaction succeeded and Primary Material exists
                    var allMats = new FilteredElementCollector(doc)
                        .OfClass(typeof(Material))
                        .Cast<Material>()
                        .Select(m => m.Name)
                        .ToList();
                    Console.WriteLine("[TEST LOG 2] All materials: " + string.Join(", ", allMats));

                    var primaryMaterial = new FilteredElementCollector(doc)
                        .OfClass(typeof(Material))
                        .Cast<Material>()
                        .FirstOrDefault(m => m.Name == "PrimaryMaterial_FailTest");
                    Assert.IsNotNull(primaryMaterial, "Primary Material should exist even if alias swap fails.");

                    // 4. Assert serialization results contain success and failure/warning entries
                    Assert.IsTrue(results.Any(r => r.Success && r.Model == primaryModel && r.Action == "Created"), "Should contain a primary creation success result");
                    Assert.IsTrue(results.Any(r => !r.Success && r.Model == primaryModel && r.Action == "Alias Swap Failed"), "Should contain an alias swap failure result");

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }
    }

    /// <summary>
    /// Custom mock IdentityService that deletes the targeted alias element immediately after resolving it.
    /// This causes subsequent Swap or Delete operations inside ToRevit to fail with a Revit exception.
    /// </summary>
    public class FailIdentityService : IIdentityService
    {
        private readonly IIdentityService _realService;
        private readonly Document _doc;
        private readonly ElementId _toDelete;

        public FailIdentityService(IIdentityService realService, Document doc, ElementId toDelete)
        {
            _realService = realService;
            _doc = doc;
            _toDelete = toDelete;
        }

        public ElementId ResolveElementId(ElementIdModel model, Document doc)
        {
            var id = _realService.ResolveElementId(model, doc);
            if (id == _toDelete)
            {
                using (var tx = new Transaction(_doc, "Delete Alias Element Early"))
                {
                    tx.Start();
                    _doc.Delete(_toDelete);
                    tx.Commit();
                }
            }
            return id;
        }

        public Element ResolveElement(ElementIdModel model, Document doc)
        {
            return _realService.ResolveElement(model, doc);
        }

        public IEnumerable<Element> GetElementsByElementIdModels(Document doc, IEnumerable<ElementIdModel> identifiers)
        {
            return _realService.GetElementsByElementIdModels(doc, identifiers);
        }

        public ElementIdModel ToModel(ElementId id, Document doc, bool isTemplate = false)
        {
            return _realService.ToModel(id, doc, isTemplate);
        }
    }
}
