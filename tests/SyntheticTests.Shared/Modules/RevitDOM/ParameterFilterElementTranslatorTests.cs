using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.Modules.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class ParameterFilterElementTranslatorTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void ParameterFilterElementTranslator_ExtractSpecifics_PopulatesRootRule()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    var category = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Walls);
                    var categoryIds = new List<ElementId> { category.Id };

                    ParameterFilterElement? filterElem = null;
                    using (var t = new Transaction(doc, "Create Filter"))
                    {
                        t.Start();
                        filterElem = ParameterFilterElement.Create(doc, "Extraction Test Filter", categoryIds);

                        // Build native filter rule using preprocessor guards
                        ElementId parameterId = new ElementId((int)BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
#if REVIT2022 || REVIT2023
                        FilterRule rule1 = ParameterFilterRuleFactory.CreateContainsRule(parameterId, "abc", true);
                        FilterRule rule2 = ParameterFilterRuleFactory.CreateContainsRule(parameterId, "def", true);
                        var rulesList = new List<FilterRule> { rule1, rule2 };
                        var andFilter = new ElementParameterFilter(rulesList);
#else
                        var provider = new ParameterValueProvider(parameterId);
                        var eval1 = new FilterStringContains();
                        var eval2 = new FilterStringContains();
                        var rule1 = new FilterStringRule(provider, eval1, "abc");
                        var rule2 = new FilterStringRule(provider, eval2, "def");
                        var filter1 = new ElementParameterFilter(rule1);
                        var filter2 = new ElementParameterFilter(rule2);
                        var andFilter = new LogicalAndFilter(new List<ElementFilter> { filter1, filter2 });
#endif
                        filterElem.SetElementFilter(andFilter);
                        t.Commit();
                    }

                    Assert.IsNotNull(filterElem);

                    var translator = new ParameterFilterElementTranslator(new RevitIdentityService());
                    var model = new ParameterFilterElementModel { IsTemplate = false };
                    model.Populate(filterElem!, false);

                    // Act
                    translator.ExtractSpecifics(filterElem!, model, doc);

                    // Assert
                    Assert.AreEqual("Extraction Test Filter", model.Name);
                    Assert.AreEqual(1, model.Categories.Count);
                    Assert.AreEqual(category.Name, model.Categories[0].Name);

                    Assert.IsNotNull(model.RootRule);
                    Assert.AreEqual("LogicalAnd", model.RootRule!.RuleType);
                    Assert.AreEqual(2, model.RootRule.InnerRules.Count);

                    var child1 = model.RootRule.InnerRules[0];
                    Assert.AreEqual("ParameterFilter", child1.RuleType);
                    Assert.AreEqual("BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS", child1.ParameterId?.Name);
                    Assert.AreEqual("abc", child1.RuleValue);

                    var child2 = model.RootRule.InnerRules[1];
                    Assert.AreEqual("ParameterFilter", child2.RuleType);
                    Assert.AreEqual("BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS", child2.ParameterId?.Name);
                    Assert.AreEqual("def", child2.RuleValue);

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ParameterFilterElementTranslator_InjectSpecifics_CreatesViewFilterAndRules()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    var category = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Walls);
                    long catIdVal;
#if REVIT2022 || REVIT2023
                    catIdVal = category.Id.IntegerValue;
#else
                    catIdVal = category.Id.Value;
#endif

                    // Build a nested logical rules model
                    var rule1 = new FilterRuleModel
                    {
                        RuleType = "ParameterFilter",
                        ParameterId = new ElementIdModel { Name = "BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS" },
                        Evaluator = "FilterStringRule:FilterStringContains",
                        RuleValue = "xyz"
                    };
                    var rule2 = new FilterRuleModel
                    {
                        RuleType = "ParameterFilter",
                        ParameterId = new ElementIdModel { Name = "BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS" },
                        Evaluator = "FilterStringRule:FilterStringContains",
                        RuleValue = "123"
                    };
                    var andRule = new FilterRuleModel
                    {
                        RuleType = "LogicalAnd",
                        InnerRules = new List<FilterRuleModel> { rule1, rule2 }
                    };

                    var filterModel = new ParameterFilterElementModel
                    {
                        Name = "Injection Test Filter",
                        Class = "Autodesk.Revit.DB.ParameterFilterElement",
                        Categories = new List<CategoryIdModel>
                        {
                            new CategoryIdModel { Id = catIdVal, Name = category.Name }
                        },
                        RootRule = andRule
                    };

                    var translator = new ParameterFilterElementTranslator(new RevitIdentityService());
                    ParameterFilterElement? filterElem = null;

                    // Act
                    using (var t = new Transaction(doc, "Inject Filter"))
                    {
                        t.Start();
                        filterElem = translator.InjectSpecifics(filterModel, null, doc);
                        t.Commit();
                    }

                    // Assert
                    Assert.IsNotNull(filterElem);
                    Assert.AreEqual("Injection Test Filter", filterElem!.Name);
                    
                    var nativeFilter = filterElem.GetElementFilter();
                    Assert.IsNotNull(nativeFilter);
                    Assert.IsInstanceOf<LogicalAndFilter>(nativeFilter);

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}
