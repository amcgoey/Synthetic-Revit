using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.Modules.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class ParameterElementTranslatorTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void ParameterElementTranslator_Extract_Success()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            string originalSharedParamsFile = app.SharedParametersFilename;
            string tempFilePath = Path.Combine(Path.GetTempPath(), "temp_shared_params_" + Guid.NewGuid().ToString() + ".txt");

            try
            {
                // Create a valid temporary shared parameters file
                File.WriteAllText(tempFilePath, @"# This is a Revit shared parameter file.
*META	VERSION	MINVERSION
META	2	1
*GROUP	ID	NAME
*PARAM	GUID	NAME	DATATYPE	DATAGROUP	VISIBLE	DESCRIPTION	USERMODIFIABLE	COLUMNHEADER
");
                app.SharedParametersFilename = tempFilePath;

                using (var tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    // Open the shared parameters file and create a definition
                    DefinitionFile defFile = app.OpenSharedParameterFile();
                    Assert.IsNotNull(defFile, "Failed to open temporary shared parameter file.");

                    DefinitionGroup group = defFile.Groups.Create("TestGroup");
                    Definition definition = group.Definitions.Create(new ExternalDefinitionCreationOptions("TestParam", SpecTypeId.String.Text));
                    Assert.IsNotNull(definition);

                    // Create CategorySet containing Walls and Doors
                    CategorySet categorySet = app.Create.NewCategorySet();
                    Category wallsCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Walls);
                    Category doorsCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Doors);
                    categorySet.Insert(wallsCat);
                    categorySet.Insert(doorsCat);

                    // Create instance binding
                    Binding binding = app.Create.NewInstanceBinding(categorySet);

                    using (var t = new Transaction(doc, "Bind Parameter"))
                    {
                        t.Start();
                        bool inserted = doc.ParameterBindings.Insert(definition, binding);
                        Assert.IsTrue(inserted, "Failed to insert parameter binding.");
                        t.Commit();
                    }

                    // Retrieve the bound element
                    var extDef = (ExternalDefinition)definition;
                    SharedParameterElement? sharedParamElem = SharedParameterElement.Lookup(doc, extDef.GUID);
                    Assert.IsNotNull(sharedParamElem);

                    // Set "vary across groups"
                    using (var t = new Transaction(doc, "Set Vary Across Groups"))
                    {
                        t.Start();
                        sharedParamElem!.GetDefinition().SetAllowVaryBetweenGroups(doc, true);
                        t.Commit();
                    }

                    var translator = new ParameterElementTranslator();
                    var model = new ParameterElementModel { IsTemplate = false };
                    model.Populate(sharedParamElem!, false);

                    // Act
                    translator.ExtractSpecifics(sharedParamElem!, model, doc);

                    // Assert
                    Assert.AreEqual("TestParam", model.Name);
                    Assert.IsTrue(model.IsInstanceBinding);
                    Assert.IsTrue(model.IsVaryByGroup);
                    Assert.AreEqual(2, model.Categories.Count);

                    var categoryNames = model.Categories.Select(c => c.Name).ToList();
                    CollectionAssert.Contains(categoryNames, wallsCat.Name);
                    CollectionAssert.Contains(categoryNames, doorsCat.Name);

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
                app.SharedParametersFilename = originalSharedParamsFile;
                if (File.Exists(tempFilePath))
                {
                    try { File.Delete(tempFilePath); } catch { }
                }
            }
        }

        [Test]
        public void ParameterElementTranslator_Inject_Success()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            string originalSharedParamsFile = app.SharedParametersFilename;
            string tempFilePath = Path.Combine(Path.GetTempPath(), "temp_shared_params_" + Guid.NewGuid().ToString() + ".txt");

            try
            {
                // Create a valid temporary shared parameters file
                File.WriteAllText(tempFilePath, @"# This is a Revit shared parameter file.
*META	VERSION	MINVERSION
META	2	1
*GROUP	ID	NAME
*PARAM	GUID	NAME	DATATYPE	DATAGROUP	VISIBLE	DESCRIPTION	USERMODIFIABLE	COLUMNHEADER
");
                app.SharedParametersFilename = tempFilePath;

                using (var tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    // Pre-create the definition in the file so the translator finds it
                    DefinitionFile defFile = app.OpenSharedParameterFile();
                    Assert.IsNotNull(defFile);
                    DefinitionGroup group = defFile.Groups.Create("TemporaryGroup");
                    Definition definition = group.Definitions.Create(new ExternalDefinitionCreationOptions("InjectParam", SpecTypeId.String.Text));
                    Assert.IsNotNull(definition);

                    var wallsCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Walls);
                    var catModel = wallsCat.ToCategoryIdModel(doc, false);

                    var model = new ParameterElementModel
                    {
                        Name = "InjectParam",
                        IsInstanceBinding = true,
                        IsVaryByGroup = true,
                        Categories = new List<CategoryIdModel> { catModel },
                        IsTemplate = false
                    };

                    var translator = new ParameterElementTranslator();

                    // Act
                    ParameterElement? injectedElem = null;
                    using (var t = new Transaction(doc, "Inject Parameter"))
                    {
                        t.Start();
                        injectedElem = translator.InjectSpecifics(model, null, doc);
                        t.Commit();
                    }

                    // Assert
                    Assert.IsNotNull(injectedElem);
                    Assert.AreEqual("InjectParam", injectedElem!.Name);

                    // Verify in doc bindings
                    ElementBinding? elementBinding = null;
                    DefinitionBindingMapIterator iter = doc.ParameterBindings.ForwardIterator();
                    while (iter.MoveNext())
                    {
                        if (iter.Key.Name == "InjectParam")
                        {
                            elementBinding = iter.Current as ElementBinding;
                            break;
                        }
                    }

                    Assert.IsNotNull(elementBinding);
                    Assert.IsTrue(elementBinding is InstanceBinding);
                    Assert.AreEqual(1, elementBinding!.Categories.Size);
                    Assert.IsTrue(elementBinding.Categories.Contains(wallsCat));

                    var internalDef = injectedElem.GetDefinition() as InternalDefinition;
                    Assert.IsNotNull(internalDef);
                    Assert.IsTrue(internalDef!.VariesAcrossGroups);

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
                app.SharedParametersFilename = originalSharedParamsFile;
                if (File.Exists(tempFilePath))
                {
                    try { File.Delete(tempFilePath); } catch { }
                }
            }
        }
    }
}
