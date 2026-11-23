using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using SyntheticTests.Fixtures;

namespace SyntheticTests.RevitDOM
{
    [TestFixture]
    [RevitIsolatedDocTest]
    public class AliasSwapEngineIsolatedDocRevitTests : RevitIsolatedDocumentFixture
    {
        [Test]
        public void SwapElementReferences_RemapsFamilyInstanceSymbols()
        {
            Document doc = OpenIsolatedTestTemplateCopy();

            using (var tg = new TransactionGroup(doc, "Swap FamilyInstance Symbol Test"))
            {
                tg.Start();

                FamilySymbol symbol1, symbol2;
                FamilyInstance instance;

                using (var t = new Transaction(doc, "Create Family Symbols and Instance"))
                {
                    t.Start();
                    symbol1 = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .Cast<FamilySymbol>()
                        .FirstOrDefault(s => s.Family != null && s.Family.IsEditable && !s.Family.IsInPlace);

                    if (symbol1 == null)
                    {
                        symbol1 = new FilteredElementCollector(doc)
                            .OfClass(typeof(FamilySymbol))
                            .Cast<FamilySymbol>()
                            .FirstOrDefault();
                    }

                    Assert.IsNotNull(symbol1, "A FamilySymbol should exist in TestTemplate document.");

                    symbol2 = (FamilySymbol)symbol1.Duplicate("DuplicatedSymbol_" + Guid.NewGuid().ToString().Substring(0, 8));

                    if (!symbol1.IsActive)
                    {
                        symbol1.Activate();
                    }

                    instance = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilyInstance))
                        .Cast<FamilyInstance>()
                        .FirstOrDefault(fi => fi.GetTypeId() == symbol1.Id);

                    if (instance == null)
                    {
                        Category? cat = symbol1.Category;
                        if (cat != null && cat.Id == new ElementId(BuiltInCategory.OST_TitleBlocks))
                        {
                            ViewSheet sheet = ViewSheet.Create(doc, ElementId.InvalidElementId);
                            instance = doc.Create.NewFamilyInstance(XYZ.Zero, symbol1, sheet);
                        }
                        else if (symbol1.Family != null && symbol1.Family.FamilyCategory != null &&
                            (symbol1.Family.FamilyCategory.CategoryType == CategoryType.Annotation ||
                            symbol1.Family.FamilyCategory.Id == new ElementId(BuiltInCategory.OST_DetailComponents)))
                        {
                            ViewDrafting draftingView = ViewDrafting.Create(doc, ElementId.InvalidElementId);
                            instance = doc.Create.NewFamilyInstance(XYZ.Zero, symbol1, draftingView);
                        }
                        else
                        {
                            Level level = new FilteredElementCollector(doc)
                                .OfClass(typeof(Level))
                                .Cast<Level>()
                                .FirstOrDefault() ?? Level.Create(doc, 0.0);
                            instance = doc.Create.NewFamilyInstance(XYZ.Zero, symbol1, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                        }
                    }

                    t.Commit();
                }

                Assert.IsNotNull(instance, "Placed family instance should not be null.");
                Assert.AreEqual(symbol1.Id, instance.GetTypeId());

                // Act - Swap symbol1.Id with symbol2.Id using AliasSwapEngine
                RedirectionResultModel result = AliasSwapEngine.SwapElementReferences(doc, symbol1.Id, symbol2.Id);

                // Assert
                Assert.IsNotNull(result, "RedirectionResultModel should not be null.");
                Assert.IsEmpty(result.Errors, "Errors: " + string.Join("; ", result.Errors));
                Assert.IsEmpty(result.Warnings, "Warnings: " + string.Join("; ", result.Warnings));
                Assert.AreEqual(symbol2.Id, instance.GetTypeId());
                Assert.IsNotNull(instance.Symbol, "FamilyInstance.Symbol should not be null.");
                Assert.AreEqual(symbol2.Id, instance.Symbol.Id);
                Assert.IsTrue(result.InstancesCount > 0, "InstancesCount should be greater than 0.");

                tg.RollBack();
            }
        }
    }
}
