using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.MergeDuplicates.ViewModels;
using Synthetic.RevitDOM.Operations.Merge;
using Synthetic.RevitDOM.Models;


namespace SyntheticTests
{
    [TestFixture]
    public class Tier2_MergeDuplicatesIntegrationTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
            string errPath = Path.Combine(Path.GetTempPath(), "synthetic_test_error.txt");
            if (File.Exists(errPath))
            {
                File.Delete(errPath);
            }
        }

        #region Helper Methods

        private static string GetProjectRoot()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string addinPath = Path.Combine(appData, "Autodesk", "Revit", "Addins", "2026", "Synthetic2026.addin");
            if (File.Exists(addinPath))
            {
                string content = File.ReadAllText(addinPath);
                var match = System.Text.RegularExpressions.Regex.Match(content, @"<Assembly>(.*?)\\output\\Synthetic\\Synthetic2026\.dll", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string root = match.Groups[1].Value;
                    if (Directory.Exists(root))
                    {
                        return root;
                    }
                }
            }

            string envPath = Environment.GetEnvironmentVariable("SYNTHETIC_PROJECT_ROOT");
            if (!string.IsNullOrEmpty(envPath) && Directory.Exists(envPath))
            {
                return envPath;
            }

            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "tests")))
            {
                dir = Path.GetDirectoryName(dir);
            }
            return dir ?? TestContext.CurrentContext.TestDirectory;
        }

        private Document OpenTestTemplate(Autodesk.Revit.ApplicationServices.Application app)
        {
            string projectRoot = GetProjectRoot();
            string testModelName = "TestTemplate" + app.VersionNumber + ".rvt";
            string modelPathStr = Path.Combine(projectRoot, "tests", "test_models", testModelName);
            if (!File.Exists(modelPathStr))
            {
                throw new FileNotFoundException("Test template model not found: " + modelPathStr);
            }

            ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(modelPathStr);
            OpenOptions openOptions = new OpenOptions
            {
                DetachFromCentralOption = DetachFromCentralOption.DetachAndDiscardWorksets
            };
            return app.OpenDocumentFile(modelPath, openOptions);
        }

        private Family DuplicateFamily(Document doc, Family sourceFamily, string suffix, out string tempPath)
        {
            Document famDoc = doc.EditFamily(sourceFamily);
            if (famDoc == null)
            {
                throw new InvalidOperationException("EditFamily returned null.");
            }

            try
            {
                string tempDir = Path.GetTempPath();
                string newName = sourceFamily.Name + suffix;
                tempPath = Path.Combine(tempDir, newName + ".rfa");

                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                famDoc.SaveAs(tempPath);
            }
            finally
            {
                famDoc.Close(false);
            }

            // Load duplicate family back in a transaction on doc
            Family duplicatedFamily;
            using (Transaction t = new Transaction(doc, "Load Duplicate Family"))
            {
                t.Start();
                bool loaded = doc.LoadFamily(tempPath, new ProcessMergeFamilyLoadOptions(), out duplicatedFamily);
                if (!loaded || duplicatedFamily == null)
                {
                    throw new InvalidOperationException("Failed to load duplicated family.");
                }
                t.Commit();
            }

            return duplicatedFamily;
        }

        private FamilyInstance PlaceInstance(Document doc, FamilySymbol symbol)
        {
            if (!symbol.IsActive)
            {
                symbol.Activate();
            }

            Category cat = symbol.Category;
            if (cat != null && cat.Id == new ElementId(BuiltInCategory.OST_TitleBlocks))
            {
                ViewSheet sheet = ViewSheet.Create(doc, ElementId.InvalidElementId);
                return doc.Create.NewFamilyInstance(XYZ.Zero, symbol, sheet);
            }

            // Check if it's a detail component or annotation
            if (symbol.Family.FamilyCategory.CategoryType == CategoryType.Annotation ||
                symbol.Family.FamilyCategory.Id == new ElementId(BuiltInCategory.OST_DetailComponents))
            {
                FilteredElementCollector viewCollector = new FilteredElementCollector(doc);
                ViewFamilyType? draftingViewType = viewCollector
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(vt => vt.ViewFamily == ViewFamily.Drafting);

                ViewDrafting draftingView;
                if (draftingViewType != null)
                {
                    draftingView = ViewDrafting.Create(doc, draftingViewType.Id);
                }
                else
                {
                    draftingView = ViewDrafting.Create(doc, ElementId.InvalidElementId);
                }
                return doc.Create.NewFamilyInstance(XYZ.Zero, symbol, draftingView);
            }

            // Fallback for 3D model elements
            Level level = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault();
            if (level == null)
            {
                level = Level.Create(doc, 0.0);
            }
            return doc.Create.NewFamilyInstance(XYZ.Zero, symbol, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
        }

        private static bool InjectParameterToFamilyDynamic(FamilyManager famManager, string paramName)
        {
            object? paramGroup = null;
            object? paramType = null;

            Type? groupTypeIdType = typeof(ElementId).Assembly.GetType("Autodesk.Revit.DB.GroupTypeId");
            if (groupTypeIdType != null)
            {
                paramGroup = groupTypeIdType.GetProperty("Data")?.GetValue(null);
                paramType = typeof(ElementId).Assembly.GetType("Autodesk.Revit.DB.SpecTypeId+String")?.GetProperty("Text")?.GetValue(null);
            }
            else
            {
                paramGroup = Enum.Parse(typeof(ElementId).Assembly.GetType("Autodesk.Revit.DB.BuiltInParameterGroup")!, "PG_DATA");
                paramType = Enum.Parse(typeof(ElementId).Assembly.GetType("Autodesk.Revit.DB.ParameterType")!, "Text");
            }

            if (famManager == null)
            {
                Console.WriteLine("[ERROR] FamilyManager is null.");
                return false;
            }
            if (paramGroup == null)
            {
                Console.WriteLine("[ERROR] paramGroup is null.");
                return false;
            }
            if (paramType == null)
            {
                Console.WriteLine("[ERROR] paramType is null.");
                return false;
            }

            System.Reflection.MethodInfo? addParamMethod = null;
            if (groupTypeIdType != null)
            {
                addParamMethod = famManager.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "AddParameter" && 
                                         m.GetParameters().Length == 4 && 
                                         m.GetParameters()[1].ParameterType.Name.Contains("ForgeTypeId") &&
                                         m.GetParameters()[2].ParameterType.Name.Contains("ForgeTypeId"));
            }
            else
            {
                addParamMethod = famManager.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "AddParameter" && 
                                         m.GetParameters().Length == 4 && 
                                         m.GetParameters()[1].ParameterType.Name.Contains("BuiltInParameterGroup") &&
                                         m.GetParameters()[2].ParameterType.Name.Contains("ParameterType"));
            }

            if (addParamMethod != null)
            {
                try
                {
                    addParamMethod.Invoke(famManager, new object[] { paramName, paramGroup, paramType, false });
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] AddParameter invoke failed: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine($"[INNER EXCEPTION] {ex.InnerException.Message}\n{ex.InnerException.StackTrace}");
                    }
                    return false;
                }
            }
            else
            {
                Console.WriteLine("[ERROR] AddParameter method with 4 arguments not found.");
            }
            return false;
        }

        private static void ExportPocoSnapshot(IEnumerable<Element> elements, string fileName)
        {
            string projectRoot = GetProjectRoot();
            string assetsDir = Path.Combine(projectRoot, "tests", "SyntheticTests.Shared", "Assets");
            if (!Directory.Exists(assetsDir))
            {
                Directory.CreateDirectory(assetsDir);
            }

            var models = elements
                .Where(el => el != null)
                .Select(el => el.ToModel(false))
                .ToList();

            string json = JsonConvert.SerializeObject(models, Formatting.Indented);
            string filePath = Path.Combine(assetsDir, fileName);
            File.WriteAllText(filePath, json);
        }

        #endregion

        #region Integration Tests

        [Test]
        public void Test_MergeDuplicates_SuccessfulMerge()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = OpenTestTemplate(app);

            string tempPath = string.Empty;

            try
            {
                // 1. Locate editable, loadable family
                Family? sourceFamily = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .FirstOrDefault(f => f.IsEditable && !f.IsInPlace);
                Assert.IsNotNull(sourceFamily, "Could not find a loadable, editable family in the document.");

                // 2. Duplicate the family (suffix "1" ensures duplicate base name match)
                Family duplicatedFamily = DuplicateFamily(doc, sourceFamily, "1", out tempPath);
                ElementId duplicateFamilyId = duplicatedFamily.Id;

                // 3. Place family instance of the duplicate family symbol
                FamilySymbol sourceSymbol = (FamilySymbol)doc.GetElement(sourceFamily.GetFamilySymbolIds().First());
                FamilySymbol duplicateSymbol = (FamilySymbol)doc.GetElement(duplicatedFamily.GetFamilySymbolIds().First());

                FamilyInstance instance;
                using (Transaction t = new Transaction(doc, "Place Duplicate Family Instance"))
                {
                    t.Start();
                    instance = PlaceInstance(doc, duplicateSymbol);
                    t.Commit();
                }

                Assert.IsNotNull(instance, "Failed to place family instance.");
                Assert.AreEqual(duplicateSymbol.Id, instance.GetTypeId(), "Placed instance should initially point to duplicate symbol.");

                using (var tg = new TransactionGroup(doc, "Merge Duplicates Integration Test"))
                {
                    tg.Start();

                    // Export the duplicate family symbols
                    var familySymbols = sourceFamily.GetFamilySymbolIds()
                        .Concat(duplicatedFamily.GetFamilySymbolIds())
                        .Select(id => doc.GetElement(id))
                        .ToList();
                    ExportPocoSnapshot(familySymbols, "test_duplicate_families.json");

                    // 4. Run Fast Scan
                    var token = CancellationToken.None;
                    var clusters = MergeAnalysisEngine.RunFastScan(doc, token);
                    foreach (var c in clusters)
                    {
                        app.WriteJournalComment($"[TEST_CLUSTER_DEBUG] Cluster: {c.ClusterName}", true);
                        foreach (var i in c.Items)
                        {
                            app.WriteJournalComment($"  [TEST_CLUSTER_DEBUG] Item: Name={i.ItemName}, ID={i.RevitElementId.ToElementId().ToString()}", true);
                        }
                    }
                    var targetCluster = clusters.FirstOrDefault(c => c.ClusterName.Contains(sourceFamily.Name));
                    Assert.IsNotNull(targetCluster, "MergeAnalysisEngine should detect the duplicate cluster.");

                    // 5. Run Deep Scan
                    MergeAnalysisEngine.RunDeepScan(doc, targetCluster, token);
                    Assert.IsFalse(targetCluster.HasSchemaMismatch, "Should not have schema mismatch.");
                    Assert.IsFalse(targetCluster.HasOriginMismatch, "Should not have origin mismatch.");

                    // 6. Generate Recommendations
                    MergeAnalysisEngine.GenerateRecommendations(targetCluster);

                    // Set primary item and ensure duplicate is included for merge
                    var primaryItem = targetCluster.Items.FirstOrDefault(i => i.RevitElementId.ToElementId() == sourceFamily.Id);
                    Assert.IsNotNull(primaryItem, "Primary item should exist in cluster.");
                    targetCluster.UpdatePrimaryItem(primaryItem);

                    var duplicateItem = targetCluster.Items.FirstOrDefault(i => i.RevitElementId.ToElementId() == duplicatedFamily.Id);
                    Assert.IsNotNull(duplicateItem, "Duplicate item should exist in cluster.");
                    duplicateItem.IsIncludedForMerge = true;

                    // 7. Execute Merge
                    var queueVM = new MergeQueueViewModel();
                    queueVM.QueuedClusters.Add(targetCluster);

                    var handler = new ProcessMergeEventHandler();
                    handler.QueueRequest(queueVM, null, null, token);
                    handler.Execute(_uiapp);

                    // 8. Assertions
                    // Verify duplicate family was deleted/purged
                    var deletedFamily = doc.GetElement(duplicateFamilyId);
                    if (deletedFamily != null)
                    {
                        Assert.Fail($"Duplicate family should be deleted. Deletion error: {(File.Exists(Path.Combine(Path.GetTempPath(), "synthetic_test_error.txt")) ? File.ReadAllText(Path.Combine(Path.GetTempPath(), "synthetic_test_error.txt")) : "No log file found.")}");
                    }

                    // Verify instance is redirected to the primary family symbol
                    Assert.AreEqual(sourceSymbol.Id, instance.GetTypeId(), "Family instance should be redirected to the primary family symbol.");

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);

                if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch { }
                }
            }
        }

        [Test]
        public void Test_MergeDuplicates_DetectsSchemaMismatch()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = OpenTestTemplate(app);

            string tempPath = string.Empty;

            try
            {
                // 1. Locate editable, loadable family
                Family? sourceFamily = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .FirstOrDefault(f => f.IsEditable && !f.IsInPlace);
                Assert.IsNotNull(sourceFamily, "Could not find a loadable, editable family in the document.");

                // 2. Duplicate the family (suffix "2" ensures duplicate base name match)
                Family duplicatedFamily = DuplicateFamily(doc, sourceFamily, "2", out tempPath);

                // 3. Edit duplicated family and inject custom parameter
                Document famDoc = doc.EditFamily(duplicatedFamily);
                Assert.IsNotNull(famDoc, "EditFamily returned null.");

                try
                {
                    using (Transaction t = new Transaction(famDoc, "Add Parameter to Duplicate Family"))
                    {
                        t.Start();
                        bool added = InjectParameterToFamilyDynamic(famDoc.FamilyManager, "Schema_Mismatch_Test_Param");
                        Assert.IsTrue(added, "Should successfully inject parameter into family.");
                        t.Commit();
                    }

                    SaveAsOptions saveOptions = new SaveAsOptions { OverwriteExistingFile = true };
                    famDoc.SaveAs(tempPath, saveOptions);
                }
                finally
                {
                    famDoc.Close(false);
                }

                // Load the updated family from disk back into doc
                using (Transaction t = new Transaction(doc, "Reload Modified Family"))
                {
                    t.Start();
                    doc.LoadFamily(tempPath, new ProcessMergeFamilyLoadOptions(), out duplicatedFamily);
                    t.Commit();
                }

                using (var tg = new TransactionGroup(doc, "Schema Mismatch Test"))
                {
                    tg.Start();

                    // 4. Run Scan & Analyze
                    var token = CancellationToken.None;
                    var clusters = MergeAnalysisEngine.RunFastScan(doc, token);
                    foreach (var c in clusters)
                    {
                        app.WriteJournalComment($"[TEST_CLUSTER_DEBUG] Cluster: {c.ClusterName}", true);
                        foreach (var i in c.Items)
                        {
                            app.WriteJournalComment($"  [TEST_CLUSTER_DEBUG] Item: Name={i.ItemName}, ID={i.RevitElementId.ToElementId().ToString()}", true);
                        }
                    }
                    var targetCluster = clusters.FirstOrDefault(c => c.ClusterName.Contains(sourceFamily.Name));
                    Assert.IsNotNull(targetCluster, "MergeAnalysisEngine should detect the duplicate cluster.");

                    MergeAnalysisEngine.RunDeepScan(doc, targetCluster, token);

                    // 5. Assert
                    Assert.IsTrue(targetCluster.HasSchemaMismatch, "Deep scan should detect schema mismatch because of injected parameter.");

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);

                if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch { }
                }
            }
        }

        [Test]
        public void Test_MergeDuplicates_ResolvesParameterConflicts()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = OpenTestTemplate(app);

            string tempPath = string.Empty;

            try
            {
                // 1. Locate editable, loadable family
                Family? sourceFamily = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .FirstOrDefault(f => f.IsEditable && !f.IsInPlace);
                Assert.IsNotNull(sourceFamily, "Could not find a loadable, editable family in the document.");

                // 2. Duplicate the family (suffix "3" ensures duplicate base name match)
                Family duplicatedFamily = DuplicateFamily(doc, sourceFamily, "3", out tempPath);
                ElementId duplicateFamilyId = duplicatedFamily.Id;

                FamilySymbol sourceSymbol = (FamilySymbol)doc.GetElement(sourceFamily.GetFamilySymbolIds().First());
                FamilySymbol duplicateSymbol = (FamilySymbol)doc.GetElement(duplicatedFamily.GetFamilySymbolIds().First());

                // Place duplicate instance
                FamilyInstance instance;
                using (Transaction t = new Transaction(doc, "Place Instance and Set Parameters"))
                {
                    t.Start();
                    instance = PlaceInstance(doc, duplicateSymbol);

                    // 3. Set conflicting parameter values on the Description parameter (Type parameter)
                    sourceSymbol.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION).Set("Primary Value");
                    duplicateSymbol.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION).Set("Duplicate Value");

                    t.Commit();
                }

                using (var tg = new TransactionGroup(doc, "Parameter Conflict Resolution Test"))
                {
                    tg.Start();

                    // 4. Run Scan
                    var token = CancellationToken.None;
                    var clusters = MergeAnalysisEngine.RunFastScan(doc, token);
                    foreach (var c in clusters)
                    {
                        app.WriteJournalComment($"[TEST_CLUSTER_DEBUG] Cluster: {c.ClusterName}", true);
                        foreach (var i in c.Items)
                        {
                            app.WriteJournalComment($"  [TEST_CLUSTER_DEBUG] Item: Name={i.ItemName}, ID={i.RevitElementId.ToElementId().ToString()}", true);
                        }
                    }
                    var targetCluster = clusters.FirstOrDefault(c => c.ClusterName.Contains(sourceFamily.Name));
                    Assert.IsNotNull(targetCluster, "MergeAnalysisEngine should detect the duplicate cluster.");

                    // 5. Deep Scan & Recommendations
                    MergeAnalysisEngine.RunDeepScan(doc, targetCluster, token);
                    MergeAnalysisEngine.GenerateRecommendations(targetCluster);

                    // 6. Locate conflict row & designate winner
                    var primaryItem = targetCluster.Items.FirstOrDefault(i => i.RevitElementId.ToElementId() == sourceFamily.Id);
                    Assert.IsNotNull(primaryItem);
                    targetCluster.UpdatePrimaryItem(primaryItem);

                    var duplicateItem = targetCluster.Items.FirstOrDefault(i => i.RevitElementId.ToElementId() == duplicatedFamily.Id);
                    Assert.IsNotNull(duplicateItem);
                    duplicateItem.IsIncludedForMerge = true;

                    var mapping = targetCluster.TypeMappings.First();
                    var descRow = mapping.ParameterResolutions.FirstOrDefault(r => r.ParameterName.Equals("Description", StringComparison.OrdinalIgnoreCase));
                    Assert.IsNotNull(descRow, "Should find Description parameter row.");
                    Assert.IsTrue(descRow.HasConflict, "Should detect parameter value conflict.");

                    // Designate duplicate symbol's parameter value as the winner
                    descRow.WinningValueElementId = duplicateSymbol.Id.ToModel(doc);

                    // 7. Execute Merge
                    var queueVM = new MergeQueueViewModel();
                    queueVM.QueuedClusters.Add(targetCluster);

                    var handler = new ProcessMergeEventHandler();
                    handler.QueueRequest(queueVM, null, null, token);
                    handler.Execute(_uiapp);

                    // 8. Assertions
                    // Verify duplicate family was deleted/purged
                    var deletedFamily = doc.GetElement(duplicateFamilyId);
                    if (deletedFamily != null)
                    {
                        Assert.Fail($"Duplicate family should be deleted. Deletion error: {(File.Exists(Path.Combine(Path.GetTempPath(), "synthetic_test_error.txt")) ? File.ReadAllText(Path.Combine(Path.GetTempPath(), "synthetic_test_error.txt")) : "No log file found.")}");
                    }

                    // Verify winning parameter value was copied to primary symbol
                    string finalValue = sourceSymbol.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION).AsString();
                    Assert.AreEqual("Duplicate Value", finalValue, "Primary family symbol's Description parameter should retain the designated winning value.");

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);

                if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch { }
                }
            }
        }

        [Test]
        public void Test_MergeGroupDuplicates_SuccessfulMerge()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = OpenTestTemplate(app);

            try
            {
                GroupType? sourceGroupType = null;
                GroupType? duplicateGroupType = null;
                Group? groupInstance1 = null;
                Group? groupInstance2 = null;

                using (Transaction t = new Transaction(doc, "Create Test Groups"))
                {
                    t.Start();

                    // Create Model Curve on a Level Plane to form a Model Group
                    Level? level = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level))
                        .Cast<Level>()
                        .FirstOrDefault();
                    Assert.IsNotNull(level, "Document must have at least one level.");

                    Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, level.Elevation));
                    SketchPlane sketchPlane = SketchPlane.Create(doc, plane);

                    Line line = Line.CreateBound(XYZ.Zero, new XYZ(5, 0, 0));
                    ModelCurve modelLine = doc.Create.NewModelCurve(line, sketchPlane);

                    // Create group
                    Group sourceGroupInstance = doc.Create.NewGroup(new List<ElementId> { modelLine.Id });
                    sourceGroupType = sourceGroupInstance.GroupType;
                    sourceGroupType.Name = "TestGroup";

                    // Duplicate group type
                    duplicateGroupType = (GroupType)sourceGroupType.Duplicate("TestGroup1");

                    // Place instances
                    groupInstance1 = doc.Create.PlaceGroup(new XYZ(0, 0, 0), sourceGroupType);
                    groupInstance2 = doc.Create.PlaceGroup(new XYZ(5, 0, 0), duplicateGroupType);

                    t.Commit();
                }

                Assert.IsNotNull(sourceGroupType);
                Assert.IsNotNull(duplicateGroupType);
                Assert.IsNotNull(groupInstance1);
                Assert.IsNotNull(groupInstance2);

                ElementId duplicateGroupTypeId = duplicateGroupType.Id;

                using (var tg = new TransactionGroup(doc, "Merge Group Duplicates Integration Test"))
                {
                    tg.Start();

                    // Export the duplicate group types
                    var groupTypes = new List<Element> { sourceGroupType, duplicateGroupType };
                    ExportPocoSnapshot(groupTypes, "test_duplicate_groups.json");

                    var token = CancellationToken.None;
                    var clusters = MergeAnalysisEngine.RunFastScan(doc, token);
                    var targetCluster = clusters.FirstOrDefault(c => c.ClusterName.Contains("TestGroup"));
                    Assert.IsNotNull(targetCluster, "MergeAnalysisEngine should detect the duplicate group cluster.");

                    MergeAnalysisEngine.RunDeepScan(doc, targetCluster, token);
                    Assert.IsFalse(targetCluster.HasSchemaMismatch, "Should not have schema mismatch.");
                    Assert.IsFalse(targetCluster.HasOriginMismatch, "Should not have origin mismatch.");

                    MergeAnalysisEngine.GenerateRecommendations(targetCluster);

                    var primaryItem = targetCluster.Items.FirstOrDefault(i => i.RevitElementId.ToElementId() == sourceGroupType.Id);
                    Assert.IsNotNull(primaryItem, "Primary item should exist in cluster.");
                    targetCluster.UpdatePrimaryItem(primaryItem);

                    var duplicateItem = targetCluster.Items.FirstOrDefault(i => i.RevitElementId.ToElementId() == duplicateGroupType.Id);
                    Assert.IsNotNull(duplicateItem, "Duplicate item should exist in cluster.");
                    duplicateItem.IsIncludedForMerge = true;

                    var queueVM = new MergeQueueViewModel();
                    queueVM.QueuedClusters.Add(targetCluster);

                    var handler = new ProcessMergeEventHandler();
                    handler.QueueRequest(queueVM, null, null, token);
                    handler.Execute(_uiapp);

                    // Verify duplicate group type was deleted/purged
                    var deletedGroupType = doc.GetElement(duplicateGroupTypeId);
                    Assert.IsNull(deletedGroupType, "Duplicate group type should be deleted.");

                    // Verify instance is redirected to the primary group type
                    Assert.AreEqual(sourceGroupType.Id, groupInstance2.GroupType.Id, "Group instance should be redirected to the primary group type.");

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void Test_MergeGroupDuplicates_ResolvesParameterConflicts()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = OpenTestTemplate(app);

            GroupType? sourceGroupType = null;
            GroupType? duplicateGroupType = null;
            Group? groupInstance1 = null;
            Group? groupInstance2 = null;

            // Create shared parameters file
            string tempFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".txt");
            File.WriteAllText(tempFilePath, ""); // Create empty file
            
            string originalSharedParamFile = app.SharedParametersFilename;
            app.SharedParametersFilename = tempFilePath;

            try
            {
                using (Transaction t = new Transaction(doc, "Create Test Groups and Bind Parameter"))
                {
                    t.Start();

                    // 1. Create a Type Parameter bound to Model Groups category
                    DefinitionFile defFile = app.OpenSharedParameterFile();
                    Assert.IsNotNull(defFile, "Failed to open temporary shared parameter file.");

                    DefinitionGroup group = defFile.Groups.Create("TestGroup");
                    Definition definition = group.Definitions.Create(new ExternalDefinitionCreationOptions("TestParam", SpecTypeId.String.Text));
                    Assert.IsNotNull(definition, "Failed to create shared parameter definition.");

                    CategorySet categorySet = app.Create.NewCategorySet();
                    Category groupCat1 = doc.Settings.Categories.get_Item(BuiltInCategory.OST_IOSModelGroups);
                    categorySet.Insert(groupCat1);

                    Binding binding = app.Create.NewTypeBinding(categorySet);
                    bool inserted = doc.ParameterBindings.Insert(definition, binding);
                    Assert.IsTrue(inserted, "Failed to insert parameter binding.");

                    // 2. Create Model Curve on a Level Plane to form a Model Group
                    Level? level = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level))
                        .Cast<Level>()
                        .FirstOrDefault();
                    Assert.IsNotNull(level, "Document must have at least one level.");

                    Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, level.Elevation));
                    SketchPlane sketchPlane = SketchPlane.Create(doc, plane);

                    Line line = Line.CreateBound(XYZ.Zero, new XYZ(5, 0, 0));
                    ModelCurve modelLine = doc.Create.NewModelCurve(line, sketchPlane);

                    // Create group
                    Group sourceGroupInstance = doc.Create.NewGroup(new List<ElementId> { modelLine.Id });
                    sourceGroupType = sourceGroupInstance.GroupType;
                    sourceGroupType.Name = "TestGroupParam";

                    // Duplicate group type
                    duplicateGroupType = (GroupType)sourceGroupType.Duplicate("TestGroupParam1");

                    // Place instances
                    groupInstance1 = doc.Create.PlaceGroup(new XYZ(0, 0, 0), sourceGroupType);
                    groupInstance2 = doc.Create.PlaceGroup(new XYZ(5, 0, 0), duplicateGroupType);

                    // Set conflicting parameter values on the newly created Type Parameter using LookupParameter
                    var srcDescParam = sourceGroupType.LookupParameter("TestParam");
                    var dupDescParam = duplicateGroupType.LookupParameter("TestParam");
                    
                    Assert.IsNotNull(srcDescParam, "Source group type should have TestParam parameter.");
                    Assert.IsNotNull(dupDescParam, "Duplicate group type should have TestParam parameter.");
                    
                    srcDescParam.Set("Primary Value");
                    dupDescParam.Set("Duplicate Value");

                    t.Commit();
                }

                Assert.IsNotNull(sourceGroupType);
                Assert.IsNotNull(duplicateGroupType);
                Assert.IsNotNull(groupInstance1);
                Assert.IsNotNull(groupInstance2);

                ElementId duplicateGroupTypeId = duplicateGroupType.Id;

                using (var tg = new TransactionGroup(doc, "Parameter Conflict Resolution Test for Groups"))
                {
                    tg.Start();

                    var token = CancellationToken.None;
                    var clusters = MergeAnalysisEngine.RunFastScan(doc, token);
                    var targetCluster = clusters.FirstOrDefault(c => c.ClusterName.Contains("TestGroupParam"));
                    Assert.IsNotNull(targetCluster, "MergeAnalysisEngine should detect the duplicate group cluster.");

                    MergeAnalysisEngine.RunDeepScan(doc, targetCluster, token);
                    MergeAnalysisEngine.GenerateRecommendations(targetCluster);

                    var primaryItem = targetCluster.Items.FirstOrDefault(i => i.RevitElementId.ToElementId() == sourceGroupType.Id);
                    Assert.IsNotNull(primaryItem);
                    targetCluster.UpdatePrimaryItem(primaryItem);

                    var duplicateItem = targetCluster.Items.FirstOrDefault(i => i.RevitElementId.ToElementId() == duplicateGroupType.Id);
                    Assert.IsNotNull(duplicateItem);
                    duplicateItem.IsIncludedForMerge = true;

                    var mapping = targetCluster.TypeMappings.First();
                    var descRow = mapping.ParameterResolutions.FirstOrDefault(r => r.ParameterName.Equals("TestParam", StringComparison.OrdinalIgnoreCase));
                    Assert.IsNotNull(descRow, "Should find TestParam parameter row.");
                    Assert.IsTrue(descRow.HasConflict, "Should detect parameter value conflict.");

                    // Designate duplicate type's parameter value as the winner
                    descRow.WinningValueElementId = duplicateGroupType.Id.ToModel(doc);

                    var queueVM = new MergeQueueViewModel();
                    queueVM.QueuedClusters.Add(targetCluster);

                    var handler = new ProcessMergeEventHandler();
                    handler.QueueRequest(queueVM, null, null, token);
                    handler.Execute(_uiapp);

                    // Verify duplicate group type was deleted/purged
                    var deletedGroupType = doc.GetElement(duplicateGroupTypeId);
                    Assert.IsNull(deletedGroupType, "Duplicate group type should be deleted.");

                    // Verify winning parameter value was copied to primary group type
                    string finalValue = sourceGroupType.LookupParameter("TestParam").AsString();
                    Assert.AreEqual("Duplicate Value", finalValue, "Primary group type's TestParam parameter should retain the designated winning value.");

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
                app.SharedParametersFilename = originalSharedParamFile;
                try
                {
                    if (File.Exists(tempFilePath))
                    {
                        File.Delete(tempFilePath);
                    }
                }
                catch {}
            }
        }

        #endregion
    }
}
