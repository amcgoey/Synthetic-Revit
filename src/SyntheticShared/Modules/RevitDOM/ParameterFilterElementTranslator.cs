using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Synthetic.Modules.RevitDOM
{
    internal class ParameterFilterElementTranslator : IModelTranslator<ParameterFilterElement, ParameterFilterElementModel>
    {
        private readonly IIdentityService _identityService;

        public ParameterFilterElementTranslator(IIdentityService identityService)
        {
            _identityService = identityService ?? throw new ArgumentNullException(nameof(identityService));
        }

        public void ExtractSpecifics(ParameterFilterElement revitElement, ParameterFilterElementModel model, Document doc)
        {
            if (revitElement == null) throw new ArgumentNullException(nameof(revitElement));
            if (model == null) throw new ArgumentNullException(nameof(model));

            model.Categories = new List<CategoryIdModel>();
            foreach (ElementId catId in revitElement.GetCategories())
            {
                Category cat = Category.GetCategory(doc, catId);
                if (cat != null)
                {
                    model.Categories.Add(cat.ToCategoryIdModel(doc, model.IsTemplate));
                }
            }

            ElementFilter elemFilter = revitElement.GetElementFilter();
            if (elemFilter != null)
            {
                model.RootRule = ExtractFilter(elemFilter, doc, model.IsTemplate, _identityService);
            }
        }

        public ParameterFilterElement? InjectSpecifics(ParameterFilterElementModel model, ParameterFilterElement? revitElement, Document doc)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            ICollection<ElementId> categoryIds = model.Categories
                .Select(c => c.GetCategory(doc)?.Id)
                .Where(id => id != null)
                .Select(id => id!)
                .ToList();

            if (revitElement == null)
            {
                revitElement = ParameterFilterElement.Create(doc, model.Name, categoryIds);
            }
            else
            {
                revitElement.Name = model.Name;
                revitElement.SetCategories(categoryIds);
            }

            if (model.RootRule != null)
            {
                ElementFilter? nativeFilter = BuildNativeFilter(model.RootRule, doc, _identityService);
                if (nativeFilter != null)
                {
                    revitElement.SetElementFilter(nativeFilter);
                }
            }

            return revitElement;
        }

        #region Explicit IModelTranslator implementations

        void IModelTranslator.ExtractSpecifics(object revitElement, ObjectModel model, Document doc)
        {
            ExtractSpecifics((ParameterFilterElement)revitElement, (ParameterFilterElementModel)model, doc);
        }

        object? IModelTranslator.InjectSpecifics(ObjectModel model, object? revitElement, Document doc)
        {
            return InjectSpecifics((ParameterFilterElementModel)model, (ParameterFilterElement?)revitElement, doc);
        }

        #endregion

        #region Extraction Helpers

        private static FilterRuleModel? ExtractFilter(ElementFilter filter, Document doc, bool isTemplate, IIdentityService identityService)
        {
            if (filter is LogicalAndFilter andFilter)
            {
                return new FilterRuleModel
                {
                    RuleType = "LogicalAnd",
                    InnerRules = andFilter.GetFilters().Select(f => ExtractFilter(f, doc, isTemplate, identityService)).Where(r => r != null).Select(r => r!).ToList()
                };
            }
            if (filter is LogicalOrFilter orFilter)
            {
                return new FilterRuleModel
                {
                    RuleType = "LogicalOr",
                    InnerRules = orFilter.GetFilters().Select(f => ExtractFilter(f, doc, isTemplate, identityService)).Where(r => r != null).Select(r => r!).ToList()
                };
            }
            if (filter is ElementParameterFilter paramFilter)
            {
                IList<FilterRule> rules = paramFilter.GetRules();
                if (rules != null && rules.Count > 0)
                {
                    if (rules.Count == 1)
                    {
                        return ExtractRule(rules[0], doc, isTemplate, identityService);
                    }
                    else
                    {
                        return new FilterRuleModel
                        {
                            RuleType = "LogicalAnd",
                            InnerRules = rules.Select(r => ExtractRule(r, doc, isTemplate, identityService)).ToList()
                        };
                    }
                }
            }
            return null;
        }

        private static FilterRuleModel ExtractRule(FilterRule rule, Document doc, bool isTemplate, IIdentityService identityService)
        {
            var model = new FilterRuleModel { RuleType = "ParameterFilter" };
            model.ParameterId = ExtractParameterId(rule.GetRuleParameter(), doc, isTemplate, identityService);

            string evaluator = "FilterStringRule:FilterStringContains";

            if (rule is FilterStringRule stringRule)
            {
                model.RuleValue = stringRule.RuleString;
                var eval = stringRule.GetEvaluator();
                evaluator = "FilterStringRule:" + eval.GetType().Name;
            }
            else if (rule is FilterIntegerRule intRule)
            {
                model.RuleValue = intRule.RuleValue.ToString();
                var eval = intRule.GetEvaluator();
                evaluator = "FilterIntegerRule:" + eval.GetType().Name;
            }
            else if (rule is FilterDoubleRule doubleRule)
            {
                model.RuleValue = doubleRule.RuleValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var eval = doubleRule.GetEvaluator();
                evaluator = "FilterDoubleRule:" + eval.GetType().Name;
            }
            else if (rule is FilterElementIdRule idRule)
            {
                long idVal;
#if REVIT2022 || REVIT2023
                idVal = idRule.RuleValue.IntegerValue;
#else
                idVal = idRule.RuleValue.Value;
#endif
                model.RuleValue = idVal.ToString();
                var eval = idRule.GetEvaluator();
                evaluator = "FilterElementIdRule:" + eval.GetType().Name;
            }

            model.Evaluator = evaluator;
            return model;
        }

        private static ElementIdModel ExtractParameterId(ElementId paramId, Document doc, bool isTemplate, IIdentityService identityService)
        {
            if (paramId == null) return new ElementIdModel(isTemplate);

            long idVal;
#if REVIT2022 || REVIT2023
            idVal = paramId.IntegerValue;
#else
            idVal = paramId.Value;
#endif

            if (idVal < 0)
            {
                var bip = (BuiltInParameter)idVal;
                return new ElementIdModel
                {
                    Id = idVal,
                    Name = "BuiltInParameter." + bip.ToString(),
                    Class = "Autodesk.Revit.DB.ParameterElement",
                    IsTemplate = isTemplate
                };
            }
            else
            {
                Element paramElem = doc.GetElement(paramId);
                if (paramElem != null)
                {
                    if (paramElem is SharedParameterElement spe)
                    {
                        return new ElementIdModel
                        {
                            Id = idVal,
                            Name = spe.Name,
                            Class = "Autodesk.Revit.DB.SharedParameterElement",
                            UniqueId = spe.GuidValue.ToString(),
                            IsTemplate = isTemplate
                        };
                    }
                    else
                    {
                        return new ElementIdModel
                        {
                            Id = idVal,
                            Name = paramElem.Name,
                            Class = "Autodesk.Revit.DB.ParameterElement",
                            UniqueId = paramElem.UniqueId,
                            IsTemplate = isTemplate
                        };
                    }
                }
                return identityService.ToModel(paramId, doc, isTemplate);
            }
        }

        #endregion

        #region Injection Helpers

        public static ElementFilter? BuildNativeFilter(FilterRuleModel model, Document doc, IIdentityService identityService)
        {
            if (model.RuleType == "LogicalAnd")
            {
                List<ElementFilter> innerFilters = new List<ElementFilter>();
                foreach (var inner in model.InnerRules)
                {
                    var nativeInner = BuildNativeFilter(inner, doc, identityService);
                    if (nativeInner != null)
                    {
                        innerFilters.Add(nativeInner);
                    }
                }
                if (innerFilters.Count > 0)
                {
                    return new LogicalAndFilter(innerFilters);
                }
            }
            else if (model.RuleType == "LogicalOr")
            {
                List<ElementFilter> innerFilters = new List<ElementFilter>();
                foreach (var inner in model.InnerRules)
                {
                    var nativeInner = BuildNativeFilter(inner, doc, identityService);
                    if (nativeInner != null)
                    {
                        innerFilters.Add(nativeInner);
                    }
                }
                if (innerFilters.Count > 0)
                {
                    return new LogicalOrFilter(innerFilters);
                }
            }
            else if (model.RuleType == "ParameterFilter")
            {
                try
                {
                    if (model.ParameterId == null || string.IsNullOrEmpty(model.Evaluator) || model.RuleValue == null)
                    {
                        return null;
                    }

                    string ruleClass = "";
                    string evaluatorClass = model.Evaluator ?? "";

                    if (evaluatorClass.Contains(":"))
                    {
                        var parts = evaluatorClass.Split(':');
                        ruleClass = parts[0];
                        evaluatorClass = parts[1];
                    }
                    else
                    {
                        if (evaluatorClass.StartsWith("FilterString"))
                        {
                            ruleClass = "FilterStringRule";
                        }
                        else
                        {
                            ruleClass = "FilterDoubleRule";
                        }
                    }

                    string ruleValue = model.RuleValue;

                    // Resolve the parameter ElementId using the injected IIdentityService
                    ElementId parameterId = identityService.ResolveElementId(model.ParameterId, doc);

#if REVIT2022 || REVIT2023
                    FilterRule? rule = null;

                    if (ruleClass == "FilterStringRule")
                    {
                        if (evaluatorClass == "FilterStringEquals")
                            rule = ParameterFilterRuleFactory.CreateEqualsRule(parameterId, ruleValue, true);
                        else if (evaluatorClass == "FilterStringBeginsWith")
                            rule = ParameterFilterRuleFactory.CreateBeginsWithRule(parameterId, ruleValue, true);
                        else if (evaluatorClass == "FilterStringEndsWith")
                            rule = ParameterFilterRuleFactory.CreateEndsWithRule(parameterId, ruleValue, true);
                        else if (evaluatorClass == "FilterStringContains")
                            rule = ParameterFilterRuleFactory.CreateContainsRule(parameterId, ruleValue, true);
                        else if (evaluatorClass == "FilterStringGreater")
                            rule = ParameterFilterRuleFactory.CreateGreaterRule(parameterId, ruleValue, true);
                        else if (evaluatorClass == "FilterStringGreaterOrEqual")
                            rule = ParameterFilterRuleFactory.CreateGreaterOrEqualRule(parameterId, ruleValue, true);
                        else if (evaluatorClass == "FilterStringLess")
                            rule = ParameterFilterRuleFactory.CreateLessRule(parameterId, ruleValue, true);
                        else if (evaluatorClass == "FilterStringLessOrEqual")
                            rule = ParameterFilterRuleFactory.CreateLessOrEqualRule(parameterId, ruleValue, true);
                    }
                    else if (ruleClass == "FilterIntegerRule")
                    {
                        int val = int.Parse(ruleValue);
                        if (evaluatorClass == "FilterNumericEquals")
                            rule = ParameterFilterRuleFactory.CreateEqualsRule(parameterId, val);
                        else if (evaluatorClass == "FilterNumericGreater")
                            rule = ParameterFilterRuleFactory.CreateGreaterRule(parameterId, val);
                        else if (evaluatorClass == "FilterNumericGreaterOrEqual")
                            rule = ParameterFilterRuleFactory.CreateGreaterOrEqualRule(parameterId, val);
                        else if (evaluatorClass == "FilterNumericLess")
                            rule = ParameterFilterRuleFactory.CreateLessRule(parameterId, val);
                        else if (evaluatorClass == "FilterNumericLessOrEqual")
                            rule = ParameterFilterRuleFactory.CreateLessOrEqualRule(parameterId, val);
                    }
                    else if (ruleClass == "FilterDoubleRule")
                    {
                        double val = double.Parse(ruleValue, System.Globalization.CultureInfo.InvariantCulture);
                        double precision = 1e-6;
                        if (evaluatorClass == "FilterNumericEquals")
                            rule = ParameterFilterRuleFactory.CreateEqualsRule(parameterId, val, precision);
                        else if (evaluatorClass == "FilterNumericGreater")
                            rule = ParameterFilterRuleFactory.CreateGreaterRule(parameterId, val, precision);
                        else if (evaluatorClass == "FilterNumericGreaterOrEqual")
                            rule = ParameterFilterRuleFactory.CreateGreaterOrEqualRule(parameterId, val, precision);
                        else if (evaluatorClass == "FilterNumericLess")
                            rule = ParameterFilterRuleFactory.CreateLessRule(parameterId, val, precision);
                        else if (evaluatorClass == "FilterNumericLessOrEqual")
                            rule = ParameterFilterRuleFactory.CreateLessOrEqualRule(parameterId, val, precision);
                    }
                    else if (ruleClass == "FilterElementIdRule")
                    {
                        ElementId val = new ElementId(int.Parse(ruleValue));
                        if (evaluatorClass == "FilterNumericEquals")
                            rule = ParameterFilterRuleFactory.CreateEqualsRule(parameterId, val);
                        else if (evaluatorClass == "FilterNumericGreater")
                            rule = ParameterFilterRuleFactory.CreateGreaterRule(parameterId, val);
                        else if (evaluatorClass == "FilterNumericGreaterOrEqual")
                            rule = ParameterFilterRuleFactory.CreateGreaterOrEqualRule(parameterId, val);
                        else if (evaluatorClass == "FilterNumericLess")
                            rule = ParameterFilterRuleFactory.CreateLessRule(parameterId, val);
                        else if (evaluatorClass == "FilterNumericLessOrEqual")
                            rule = ParameterFilterRuleFactory.CreateLessOrEqualRule(parameterId, val);
                    }

                    if (rule != null)
                    {
                        return new ElementParameterFilter(rule);
                    }
#else
                    ParameterValueProvider provider = new ParameterValueProvider(parameterId);
                    FilterRule? rule = null;

                    if (ruleClass == "FilterStringRule")
                    {
                        FilterStringRuleEvaluator? eval = null;
                        if (evaluatorClass == "FilterStringEquals") eval = new FilterStringEquals();
                        else if (evaluatorClass == "FilterStringBeginsWith") eval = new FilterStringBeginsWith();
                        else if (evaluatorClass == "FilterStringEndsWith") eval = new FilterStringEndsWith();
                        else if (evaluatorClass == "FilterStringContains") eval = new FilterStringContains();
                        else if (evaluatorClass == "FilterStringGreater") eval = new FilterStringGreater();
                        else if (evaluatorClass == "FilterStringGreaterOrEqual") eval = new FilterStringGreaterOrEqual();
                        else if (evaluatorClass == "FilterStringLess") eval = new FilterStringLess();
                        else if (evaluatorClass == "FilterStringLessOrEqual") eval = new FilterStringLessOrEqual();

                        if (eval != null)
                        {
                            rule = new FilterStringRule(provider, eval, ruleValue);
                        }
                    }
                    else if (ruleClass == "FilterIntegerRule")
                    {
                        FilterNumericRuleEvaluator? eval = null;
                        if (evaluatorClass == "FilterNumericEquals") eval = new FilterNumericEquals();
                        else if (evaluatorClass == "FilterNumericGreater") eval = new FilterNumericGreater();
                        else if (evaluatorClass == "FilterNumericGreaterOrEqual") eval = new FilterNumericGreaterOrEqual();
                        else if (evaluatorClass == "FilterNumericLess") eval = new FilterNumericLess();
                        else if (evaluatorClass == "FilterNumericLessOrEqual") eval = new FilterNumericLessOrEqual();

                        if (eval != null)
                        {
                            int val = int.Parse(ruleValue);
                            rule = new FilterIntegerRule(provider, eval, val);
                        }
                    }
                    else if (ruleClass == "FilterDoubleRule")
                    {
                        FilterNumericRuleEvaluator? eval = null;
                        if (evaluatorClass == "FilterNumericEquals") eval = new FilterNumericEquals();
                        else if (evaluatorClass == "FilterNumericGreater") eval = new FilterNumericGreater();
                        else if (evaluatorClass == "FilterNumericGreaterOrEqual") eval = new FilterNumericGreaterOrEqual();
                        else if (evaluatorClass == "FilterNumericLess") eval = new FilterNumericLess();
                        else if (evaluatorClass == "FilterNumericLessOrEqual") eval = new FilterNumericLessOrEqual();

                        if (eval != null)
                        {
                            double val = double.Parse(ruleValue, System.Globalization.CultureInfo.InvariantCulture);
                            double precision = 1e-6;
                            rule = new FilterDoubleRule(provider, eval, val, precision);
                        }
                    }
                    else if (ruleClass == "FilterElementIdRule")
                    {
                        FilterNumericRuleEvaluator? eval = null;
                        if (evaluatorClass == "FilterNumericEquals") eval = new FilterNumericEquals();
                        else if (evaluatorClass == "FilterNumericGreater") eval = new FilterNumericGreater();
                        else if (evaluatorClass == "FilterNumericGreaterOrEqual") eval = new FilterNumericGreaterOrEqual();
                        else if (evaluatorClass == "FilterNumericLess") eval = new FilterNumericLess();
                        else if (evaluatorClass == "FilterNumericLessOrEqual") eval = new FilterNumericLessOrEqual();

                        if (eval != null)
                        {
                            ElementId val = new ElementId(long.Parse(ruleValue));
                            rule = new FilterElementIdRule(provider, eval, val);
                        }
                    }

                    if (rule != null)
                    {
                        return new ElementParameterFilter(rule);
                    }
#endif
                }
                catch (Exception ex)
                {
                    doc.Application.WriteJournalComment($"[AG2_ERROR] Filter rule reconstruction failed: {ex.Message}", true);
                    return null;
                }
            }

            return null;
        }

        #endregion
    }
}
