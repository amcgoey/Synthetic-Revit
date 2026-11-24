using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using NUnit.Framework;
using Synthetic.Features.SheetIndex.Adapters;
using Synthetic.Features.SheetIndex.Models;
using Synthetic.Platform.Revit;
using SyntheticTests.Fixtures;

namespace SyntheticTests.Features.SheetIndex
{
    /// <summary>
    /// Live Revit adapter tests verifying <see cref="RevitSheetIndexAdapter"/> harvesting of
    /// <see cref="ViewSheet"/>, <see cref="Revision"/>, <see cref="ViewSheetSet"/>, and <see cref="ViewSchedule"/>
    /// sort/group definitions inside rolled-back transactions.
    /// </summary>
    [TestFixture]
    [RevitAdapterTest]
    public class RevitSheetIndexAdapterRevitTests : RevitTemplateRollbackFixture
    {
        [Test]
        public void LoadProjectSnapshot_HarvestsSheetsExcludingPlaceholders_AndRevisionsInSequenceOrder()
        {
            WithRolledBackDocument(doc =>
            {
                string uniqueSuffix = Guid.NewGuid().ToString("N").Substring(0, 6);
                string r1Desc = "Permit Issue " + uniqueSuffix;
                string r2Desc = "Construction Issue " + uniqueSuffix;

                Revision rev1 = null!;
                Revision rev2 = null!;
                ViewSheet sheetA10 = null!;
                ViewSheet sheetA2 = null!;
                ViewSheet sheetA100 = null!;
                ViewSheet placeholderSheet = null!;

                doc.ExecuteTransaction("Seed Sheets and Revisions", FailurePipelines.ObserveOnly(), () =>
                {
                    rev1 = Revision.Create(doc);
                    rev1.Description = r1Desc;
                    rev1.RevisionDate = "2026-01-15";

                    rev2 = Revision.Create(doc);
                    rev2.Description = r2Desc;
                    rev2.RevisionDate = "2026-02-20";

                    sheetA10 = ViewSheet.Create(doc, ElementId.InvalidElementId);
                    sheetA10.SheetNumber = "A10-" + uniqueSuffix;
                    sheetA10.Name = "Floor Plan 10";

                    sheetA2 = ViewSheet.Create(doc, ElementId.InvalidElementId);
                    sheetA2.SheetNumber = "A2-" + uniqueSuffix;
                    sheetA2.Name = "Floor Plan 2";
                    sheetA2.SetAdditionalRevisionIds(new List<ElementId> { rev1.Id });

                    sheetA100 = ViewSheet.Create(doc, ElementId.InvalidElementId);
                    sheetA100.SheetNumber = "A100-" + uniqueSuffix;
                    sheetA100.Name = "Cover Sheet";
                    sheetA100.SetAdditionalRevisionIds(new List<ElementId> { rev2.Id });

                    placeholderSheet = ViewSheet.CreatePlaceholder(doc);
                    placeholderSheet.SheetNumber = "P999-" + uniqueSuffix;
                    placeholderSheet.Name = "Placeholder Sheet";
                });

                var adapter = new RevitSheetIndexAdapter(doc);
                SheetIndexSnapshot snapshot = adapter.LoadProjectSnapshot();

                Assert.IsFalse(snapshot.Sheets.Any(s => s.UniqueId == placeholderSheet.UniqueId), "Placeholder sheets must be excluded.");

                List<string> harvestedNumbers = snapshot.Sheets
                    .Where(s => s.SheetNumber.EndsWith(uniqueSuffix, StringComparison.Ordinal))
                    .Select(s => s.SheetNumber)
                    .ToList();
                CollectionAssert.AreEqual(
                    new[] { "A2-" + uniqueSuffix, "A10-" + uniqueSuffix, "A100-" + uniqueSuffix },
                    harvestedNumbers);

                SheetIndexSheetModel harvestedA2 = snapshot.Sheets.First(s => s.UniqueId == sheetA2.UniqueId);
                Assert.IsTrue(harvestedA2.RevisionIds.Contains(rev1.UniqueId));
                Assert.IsFalse(harvestedA2.RevisionIds.Contains(rev2.UniqueId));

                SheetIndexSheetModel harvestedA100 = snapshot.Sheets.First(s => s.UniqueId == sheetA100.UniqueId);
                Assert.IsTrue(harvestedA100.RevisionIds.Contains(rev2.UniqueId));

                int idx1 = -1;
                int idx2 = -1;
                for (int i = 0; i < snapshot.Revisions.Count; i++)
                {
                    if (snapshot.Revisions[i].UniqueId == rev1.UniqueId) idx1 = i;
                    if (snapshot.Revisions[i].UniqueId == rev2.UniqueId) idx2 = i;
                }

                Assert.GreaterOrEqual(idx1, 0);
                Assert.Greater(idx2, idx1, "Revisions must be ordered by SequenceNumber.");
                Assert.AreEqual(r1Desc, snapshot.Revisions[idx1].Name);
                Assert.AreEqual("2026-01-15", snapshot.Revisions[idx1].Date);
                Assert.AreEqual(r2Desc, snapshot.Revisions[idx2].Name);
                Assert.AreEqual("2026-02-20", snapshot.Revisions[idx2].Date);
            });
        }

        [Test]
        public void LoadProjectSnapshot_HarvestsPrintSets_FilteringNonSheetsAndPlaceholders()
        {
            WithRolledBackDocument(doc =>
            {
                string uniqueSuffix = Guid.NewGuid().ToString("N").Substring(0, 6);
                string printSetName = "Test Print Set " + uniqueSuffix;

                ViewSheet sheetA103 = null!;
                ViewSheet sheetA101 = null!;
                ViewSheet placeholder = null!;
                ViewSchedule schedule = null!;

                doc.ExecuteTransaction("Seed Print Set", FailurePipelines.ObserveOnly(), () =>
                {
                    sheetA103 = ViewSheet.Create(doc, ElementId.InvalidElementId);
                    sheetA103.SheetNumber = "A103-" + uniqueSuffix;
                    sheetA103.Name = "Roof Plan";

                    sheetA101 = ViewSheet.Create(doc, ElementId.InvalidElementId);
                    sheetA101.SheetNumber = "A101-" + uniqueSuffix;
                    sheetA101.Name = "First Floor Plan";

                    placeholder = ViewSheet.CreatePlaceholder(doc);
                    placeholder.SheetNumber = "P101-" + uniqueSuffix;
                    placeholder.Name = "Placeholder";

                    schedule = ViewSchedule.CreateSheetList(doc);
                    schedule.Name = "Mixed Set Schedule " + uniqueSuffix;

                    var viewSet = new ViewSet();
                    viewSet.Insert(sheetA103);
                    viewSet.Insert(sheetA101);
                    viewSet.Insert(placeholder);
                    viewSet.Insert(schedule);

                    PrintManager printManager = doc.PrintManager;
                    printManager.PrintRange = PrintRange.Select;
                    printManager.Apply();
                    ViewSheetSetting setting = printManager.ViewSheetSetting;
                    setting.CurrentViewSheetSet = setting.InSession;
                    setting.CurrentViewSheetSet.Views = viewSet;
                    Assert.IsTrue(setting.SaveAs(printSetName), "Failed to save test ViewSheetSet.");
                });

                var adapter = new RevitSheetIndexAdapter(doc);
                SheetIndexSnapshot snapshot = adapter.LoadProjectSnapshot();

                CollectionAssert.Contains(snapshot.AvailablePrintSets, printSetName);
                Assert.IsTrue(snapshot.PrintSetSheetMap.TryGetValue(printSetName, out IReadOnlyList<string>? ids));
                CollectionAssert.AreEqual(
                    new[] { sheetA101.UniqueId, sheetA103.UniqueId },
                    ids!.ToArray());
            });
        }

        [Test]
        public void LoadProjectSnapshot_HarvestsSchedules_WithNonStringSortGroupFields_AndExcludesEmptySchedules()
        {
            WithRolledBackDocument(doc =>
            {
                string originalSharedParamsFile = App.SharedParametersFilename;
                string tempFilePath = Path.Combine(Path.GetTempPath(), "sheetindex_params_" + Guid.NewGuid().ToString("N") + ".txt");
                string uniqueSuffix = Guid.NewGuid().ToString("N").Substring(0, 6);
                string intParamName = "SheetSortGroup_" + uniqueSuffix;
                string doubleParamName = "SheetWeight_" + uniqueSuffix;
                string populatedScheduleName = "Populated Sheet Index " + uniqueSuffix;
                string emptyScheduleName = "Empty Sheet Index " + uniqueSuffix;

                try
                {
                    File.WriteAllText(tempFilePath, @"# This is a Revit shared parameter file.
*META	VERSION	MINVERSION
META	2	1
*GROUP	ID	NAME
*PARAM	GUID	NAME	DATATYPE	DATAGROUP	VISIBLE	DESCRIPTION	USERMODIFIABLE	COLUMNHEADER
");
                    App.SharedParametersFilename = tempFilePath;

                    DefinitionFile defFile = App.OpenSharedParameterFile();
                    Assert.IsNotNull(defFile, "Failed to open temporary shared parameter file.");
                    DefinitionGroup group = defFile.Groups.Create("SheetIndexTestGroup");
                    Definition intDef = group.Definitions.Create(new ExternalDefinitionCreationOptions(intParamName, SpecTypeId.Int.Integer));
                    Definition doubleDef = group.Definitions.Create(new ExternalDefinitionCreationOptions(doubleParamName, SpecTypeId.Number));

                    CategorySet categorySet = App.Create.NewCategorySet();
                    Category sheetsCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Sheets);
                    categorySet.Insert(sheetsCategory);
                    Binding instanceBinding = App.Create.NewInstanceBinding(categorySet);

                    doc.ExecuteTransaction("Bind Sheet Parameters", FailurePipelines.ObserveOnly(), () =>
                    {
                        Assert.IsTrue(doc.ParameterBindings.Insert(intDef, instanceBinding), "Failed to bind integer parameter to OST_Sheets.");
                        Assert.IsTrue(doc.ParameterBindings.Insert(doubleDef, instanceBinding), "Failed to bind number parameter to OST_Sheets.");
                    });

                    ViewSheet sheetA101 = null!;
                    ViewSheet sheetA102 = null!;
                    ViewSheet sheetA201 = null!;

                    doc.ExecuteTransaction("Seed Sheets and Schedules", FailurePipelines.ObserveOnly(), () =>
                    {
                        sheetA101 = ViewSheet.Create(doc, ElementId.InvalidElementId);
                        sheetA101.SheetNumber = "A101-" + uniqueSuffix;
                        sheetA101.Name = "Floor 1";
                        sheetA101.LookupParameter(intParamName)!.Set(10);
                        sheetA101.LookupParameter(doubleParamName)!.Set(2.5);

                        sheetA102 = ViewSheet.Create(doc, ElementId.InvalidElementId);
                        sheetA102.SheetNumber = "A102-" + uniqueSuffix;
                        sheetA102.Name = "Floor 2";
                        sheetA102.LookupParameter(intParamName)!.Set(2);
                        sheetA102.LookupParameter(doubleParamName)!.Set(10.5);

                        sheetA201 = ViewSheet.Create(doc, ElementId.InvalidElementId);
                        sheetA201.SheetNumber = "A201-" + uniqueSuffix;
                        sheetA201.Name = "Struct 1";
                        sheetA201.LookupParameter(intParamName)!.Set(2);
                        sheetA201.LookupParameter(doubleParamName)!.Set(1.5);

                        // Populated schedule sorted/grouped by non-string StorageType fields (Integer -> Double)
                        ViewSchedule populatedSchedule = ViewSchedule.CreateSheetList(doc);
                        populatedSchedule.Name = populatedScheduleName;

                        IList<SchedulableField> schedulableFields = populatedSchedule.Definition.GetSchedulableFields();
                        SchedulableField sheetNumberSchedField = schedulableFields.First(sf =>
                            sf.ParameterId == new ElementId(BuiltInParameter.SHEET_NUMBER));
                        SchedulableField intSchedField = schedulableFields.First(sf =>
                            string.Equals(sf.GetName(doc), intParamName, StringComparison.Ordinal));
                        SchedulableField doubleSchedField = schedulableFields.First(sf =>
                            string.Equals(sf.GetName(doc), doubleParamName, StringComparison.Ordinal));

                        ScheduleField sheetNumField = populatedSchedule.Definition.AddField(sheetNumberSchedField);
                        ScheduleField intField = populatedSchedule.Definition.AddField(intSchedField);
                        ScheduleField doubleField = populatedSchedule.Definition.AddField(doubleSchedField);

                        populatedSchedule.Definition.AddSortGroupField(
                            new ScheduleSortGroupField(intField.FieldId, ScheduleSortOrder.Ascending) { ShowHeader = true });
                        populatedSchedule.Definition.AddSortGroupField(
                            new ScheduleSortGroupField(doubleField.FieldId, ScheduleSortOrder.Ascending) { ShowHeader = false });

                        // Empty schedule filtered to match zero sheets
                        ViewSchedule emptySchedule = ViewSchedule.CreateSheetList(doc);
                        emptySchedule.Name = emptyScheduleName;
                        SchedulableField emptyNumSchedField = emptySchedule.Definition.GetSchedulableFields().First(sf =>
                            sf.ParameterId == new ElementId(BuiltInParameter.SHEET_NUMBER));
                        ScheduleField emptyNumField = emptySchedule.Definition.AddField(emptyNumSchedField);
                        emptySchedule.Definition.AddFilter(
                            new ScheduleFilter(emptyNumField.FieldId, ScheduleFilterType.Equal, "DOES_NOT_EXIST_" + uniqueSuffix));
                    });

                    var adapter = new RevitSheetIndexAdapter(doc);
                    SheetIndexSnapshot snapshot = adapter.LoadProjectSnapshot();

                    // Empty schedule must be excluded everywhere
                    Assert.IsFalse(snapshot.Schedules.Any(s => s.Name == emptyScheduleName), "Empty schedule must be excluded from snapshot.Schedules.");
                    Assert.IsFalse(snapshot.AvailableSchedules.Contains(emptyScheduleName), "Empty schedule must be excluded from AvailableSchedules.");
                    Assert.IsFalse(snapshot.ScheduleSheetMap.ContainsKey(emptyScheduleName), "Empty schedule must be excluded from ScheduleSheetMap.");

                    // Populated schedule must preserve Integer + Double StorageType sorting and SectionGroup values
                    SheetIndexScheduleModel? harvestedSchedule = snapshot.Schedules.FirstOrDefault(s => s.Name == populatedScheduleName);
                    Assert.IsNotNull(harvestedSchedule, "Expected populated schedule to be harvested.");
                    CollectionAssert.AreEqual(
                        new[] { sheetA201.UniqueId, sheetA102.UniqueId, sheetA101.UniqueId },
                        harvestedSchedule!.Sheets.Select(s => s.UniqueId).ToArray());

                    Assert.AreEqual("2", harvestedSchedule.Sheets[0].SectionGroup);
                    Assert.AreEqual("2", harvestedSchedule.Sheets[1].SectionGroup);
                    Assert.AreEqual("10", harvestedSchedule.Sheets[2].SectionGroup);
                }
                finally
                {
                    App.SharedParametersFilename = originalSharedParamsFile;
                    if (File.Exists(tempFilePath))
                    {
                        try
                        {
                            File.Delete(tempFilePath);
                        }
                        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                        {
                            TestContext.WriteLine($"Failed to delete temp shared parameter file '{tempFilePath}': {ex.Message}");
                        }
                    }
                }
            });
        }

        [Test]
        public void LoadProjectSnapshot_HarvestsSchedules_WithDescendingStringSortAndSectionGroup()
        {
            WithRolledBackDocument(doc =>
            {
                string uniqueSuffix = Guid.NewGuid().ToString("N").Substring(0, 6);
                string scheduleName = "String Sort Schedule " + uniqueSuffix;

                ViewSheet sheetA1 = null!;
                ViewSheet sheetA2 = null!;
                ViewSheet sheetS1 = null!;

                doc.ExecuteTransaction("Seed String Sort Schedule", FailurePipelines.ObserveOnly(), () =>
                {
                    sheetA1 = ViewSheet.Create(doc, ElementId.InvalidElementId);
                    sheetA1.SheetNumber = "A1-" + uniqueSuffix;
                    sheetA1.Name = "Arch 1";
                    sheetA1.get_Parameter(BuiltInParameter.SHEET_DRAWN_BY)!.Set("ARCH");

                    sheetA2 = ViewSheet.Create(doc, ElementId.InvalidElementId);
                    sheetA2.SheetNumber = "A2-" + uniqueSuffix;
                    sheetA2.Name = "Arch 2";
                    sheetA2.get_Parameter(BuiltInParameter.SHEET_DRAWN_BY)!.Set("ARCH");

                    sheetS1 = ViewSheet.Create(doc, ElementId.InvalidElementId);
                    sheetS1.SheetNumber = "S1-" + uniqueSuffix;
                    sheetS1.Name = "Struct 1";
                    sheetS1.get_Parameter(BuiltInParameter.SHEET_DRAWN_BY)!.Set("STRUCT");

                    ViewSchedule schedule = ViewSchedule.CreateSheetList(doc);
                    schedule.Name = scheduleName;

                    IList<SchedulableField> schedulableFields = schedule.Definition.GetSchedulableFields();
                    SchedulableField drawnBySchedField = schedulableFields.First(sf =>
                        sf.ParameterId == new ElementId(BuiltInParameter.SHEET_DRAWN_BY));
                    SchedulableField numSchedField = schedulableFields.First(sf =>
                        sf.ParameterId == new ElementId(BuiltInParameter.SHEET_NUMBER));

                    ScheduleField drawnByField = schedule.Definition.AddField(drawnBySchedField);
                    ScheduleField numField = schedule.Definition.AddField(numSchedField);

                    schedule.Definition.AddSortGroupField(
                        new ScheduleSortGroupField(drawnByField.FieldId, ScheduleSortOrder.Ascending) { ShowHeader = true });
                    schedule.Definition.AddSortGroupField(
                        new ScheduleSortGroupField(numField.FieldId, ScheduleSortOrder.Descending) { ShowHeader = false });
                });

                var adapter = new RevitSheetIndexAdapter(doc);
                SheetIndexSnapshot snapshot = adapter.LoadProjectSnapshot();

                SheetIndexScheduleModel? harvested = snapshot.Schedules.FirstOrDefault(s => s.Name == scheduleName);
                Assert.IsNotNull(harvested);
                CollectionAssert.AreEqual(
                    new[] { sheetA2.UniqueId, sheetA1.UniqueId, sheetS1.UniqueId },
                    harvested!.Sheets.Select(s => s.UniqueId).ToArray());
                Assert.AreEqual("ARCH", harvested.Sheets[0].SectionGroup);
                Assert.AreEqual("ARCH", harvested.Sheets[1].SectionGroup);
                Assert.AreEqual("STRUCT", harvested.Sheets[2].SectionGroup);
            });
        }
    }
}
