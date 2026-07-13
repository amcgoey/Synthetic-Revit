using System;
using System.Linq;
using Autodesk.Revit.DB;
using RevitCategory = Autodesk.Revit.DB.Category;

namespace Synthetic.Modules.RevitDOM
{
    internal class CategoryTranslator : IModelTranslator<RevitCategory, CategoryModel>
    {
        private readonly IIdentityService _identityService;

        public CategoryTranslator(IIdentityService identityService)
        {
            _identityService = identityService ?? throw new ArgumentNullException(nameof(identityService));
        }

        public void ExtractSpecifics(RevitCategory revitElement, CategoryModel model, Document doc)
        {
            if (revitElement == null) throw new ArgumentNullException(nameof(revitElement));
            if (model == null) throw new ArgumentNullException(nameof(model));

            model.Name = revitElement.Name;

            long catIdVal;
#if REVIT2022 || REVIT2023
            catIdVal = revitElement.Id.IntegerValue;
#else
            catIdVal = revitElement.Id.Value;
#endif

            model.ElementId = new ElementIdModel
            {
                Name = revitElement.Name,
                Class = "Autodesk.Revit.DB.Category",
                Category = revitElement.Name,
                Id = catIdVal,
                IsTemplate = model.IsTemplate
            };

            model.CategoryId.Id = catIdVal;
            model.CategoryId.Name = revitElement.Name;
            model.CategoryId.Category = revitElement;
            model.CategoryId.Document = doc;
            model.CategoryId.IsTemplate = model.IsTemplate;

            model.IsCuttable = revitElement.IsCuttable;

            if (revitElement.IsCuttable)
            {
                try
                {
                    model.LineWeightCut = revitElement.GetLineWeight(GraphicsStyleType.Cut);
                    ElementId linePatternCutId = revitElement.GetLinePatternId(GraphicsStyleType.Cut);
                    model.LinePatternCut = _identityService.ToModel(linePatternCutId, doc, model.IsTemplate);
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                {
                    model.LineWeightCut = null;
                    model.LinePatternCut = null;
                }
            }

            model.LineWeightProjection = revitElement.GetLineWeight(GraphicsStyleType.Projection);
            ElementId linePatternProjId = revitElement.GetLinePatternId(GraphicsStyleType.Projection);
            model.LinePatternProjection = _identityService.ToModel(linePatternProjId, doc, model.IsTemplate);

            if (revitElement.LineColor != null && revitElement.LineColor.IsValid)
            {
                model.LineColor = revitElement.LineColor.ToModel();
            }

            if (revitElement.Material != null)
            {
                model.Material = _identityService.ToModel(revitElement.Material.Id, doc, model.IsTemplate);
            }

            if (revitElement.Parent != null)
            {
                model.ParentCategoryName = revitElement.Parent.Name;
            }

            string parentName = revitElement.Parent != null ? revitElement.Parent.Name : "";
            model.CategoryGroupLevel2 = !string.IsNullOrEmpty(parentName) ? parentName : revitElement.Name;

            bool isLineStyle = string.Equals(revitElement.Name, "Lines", StringComparison.OrdinalIgnoreCase) || 
                               string.Equals(parentName, "Lines", StringComparison.OrdinalIgnoreCase);

            bool isImported = revitElement.Name.StartsWith("Imports in ", StringComparison.OrdinalIgnoreCase) || 
                              parentName.StartsWith("Imports in ", StringComparison.OrdinalIgnoreCase) ||
                              revitElement.Name.IndexOf(".dwg", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              parentName.IndexOf(".dwg", StringComparison.OrdinalIgnoreCase) >= 0;

            string lvl1 = "Model Categories";
            if (isLineStyle)
            {
                lvl1 = "Line Styles";
            }
            else if (isImported)
            {
                lvl1 = "Imported Categories";
            }
            else if (revitElement.CategoryType == CategoryType.Annotation)
            {
                lvl1 = "Annotation Categories";
            }
            else if (revitElement.CategoryType == CategoryType.AnalyticalModel)
            {
                lvl1 = "Analytical Categories";
            }
            else if (revitElement.CategoryType == CategoryType.Model)
            {
                lvl1 = "Model Categories";
            }

            model.CategoryGroupLevel1 = lvl1;
        }

        public RevitCategory? InjectSpecifics(CategoryModel model, RevitCategory? revitElement, Document doc)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            if (revitElement == null)
            {
                if (!string.IsNullOrEmpty(model.ParentCategoryName))
                {
                    RevitCategory? parentCategory = doc.Settings.Categories
                        .Cast<RevitCategory>()
                        .FirstOrDefault(c => c.Name.Equals(model.ParentCategoryName, StringComparison.OrdinalIgnoreCase));

                    if (parentCategory != null)
                    {
                        revitElement = parentCategory.SubCategories
                            .Cast<RevitCategory>()
                            .FirstOrDefault(c => c.Name.Equals(model.Name, StringComparison.OrdinalIgnoreCase));

                        if (revitElement == null)
                        {
                            try
                            {
                                revitElement = doc.Settings.Categories.NewSubcategory(parentCategory, model.Name);
                            }
                            catch (Exception ex)
                            {
                                SerializationResultModel.LogWarning($"Failed to create subcategory '{model.Name}': {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        SerializationResultModel.LogWarning($"Parent category '{model.ParentCategoryName}' not found; skipping creation of subcategory '{model.Name}'.");
                    }
                }
                else
                {
                    SerializationResultModel.LogWarning($"Cannot create top-level category '{model.Name}'; top-level category creation is not supported.");
                }
            }

            if (revitElement != null)
            {
                if (revitElement.IsCuttable)
                {
                    try
                    {
                        if (model.LineWeightCut.HasValue && model.LineWeightCut.Value >= 1 && model.LineWeightCut.Value <= 16)
                        {
                            revitElement.SetLineWeight(model.LineWeightCut.Value, GraphicsStyleType.Cut);
                        }

                        if (model.LinePatternCut != null)
                        {
                            ElementId lpId = _identityService.ResolveElementId(model.LinePatternCut, doc);
                            revitElement.SetLinePatternId(lpId, GraphicsStyleType.Cut);
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                    {
                        // Ignore uncuttable graphics style exceptions
                    }
                    catch (Exception ex)
                    {
                        SerializationResultModel.LogWarning($"Quirk encountered setting cut style for category '{model.Name}': {ex.Message}");
                    }
                }

                if (model.LineWeightProjection.HasValue && model.LineWeightProjection.Value >= 1 && model.LineWeightProjection.Value <= 16)
                {
                    revitElement.SetLineWeight(model.LineWeightProjection.Value, GraphicsStyleType.Projection);
                }

                if (model.LineColor != null)
                {
                    revitElement.LineColor = model.LineColor.ToColor();
                }

                if (model.Material != null)
                {
                    var matElem = _identityService.ResolveElement(model.Material, doc);
                    if (matElem is Material mat)
                    {
                        revitElement.Material = mat;
                    }
                    else if (model.Material.Name == "<By Category>" || string.IsNullOrEmpty(model.Material.Name))
                    {
                        revitElement.Material = null;
                    }
                }

                if (model.LinePatternProjection != null)
                {
                    ElementId lpId = _identityService.ResolveElementId(model.LinePatternProjection, doc);
                    revitElement.SetLinePatternId(lpId, GraphicsStyleType.Projection);
                }
            }

            return revitElement;
        }

        #region Explicit IModelTranslator implementations

        void IModelTranslator.ExtractSpecifics(object revitElement, ObjectModel model, Document doc)
        {
            ExtractSpecifics((RevitCategory)revitElement, (CategoryModel)model, doc);
        }

        object? IModelTranslator.InjectSpecifics(ObjectModel model, object? revitElement, Document doc)
        {
            return InjectSpecifics((CategoryModel)model, (RevitCategory?)revitElement, doc);
        }

        #endregion
    }

    public static class CategoryExtensions
    {
        public static CategoryIdModel ToCategoryIdModel(this RevitCategory category, Document document, bool isTemplate = false)
        {
            if (category == null) return null;
            var model = new CategoryIdModel
            {
                Category = category,
                Document = document,
                Name = category.Name,
                IsTemplate = isTemplate
            };

            long idVal;
#if REVIT2022 || REVIT2023
            idVal = category.Id.IntegerValue;
#else
            idVal = category.Id.Value;
#endif
            model.Id = idVal;

            return model;
        }

        public static RevitCategory GetCategory(this CategoryIdModel model, Document document)
        {
            if (document == null) return model.Category as RevitCategory;
            var cachedCat = model.Category as RevitCategory;
            if (cachedCat == null)
            {
                if (model.Id != 0)
                {
#if REVIT2022 || REVIT2023
                    cachedCat = RevitCategory.GetCategory(document, new ElementId((int)model.Id));
#else
                    cachedCat = RevitCategory.GetCategory(document, new ElementId(model.Id));
#endif
                }

                if (cachedCat == null && !string.IsNullOrEmpty(model.Name))
                {
                    cachedCat = document.Settings.Categories
                        .Cast<RevitCategory>()
                        .FirstOrDefault(c => c.Name.Equals(model.Name, StringComparison.OrdinalIgnoreCase));

                    if (cachedCat == null)
                    {
                        foreach (RevitCategory parent in document.Settings.Categories)
                        {
                            RevitCategory sub = parent.SubCategories
                                .Cast<RevitCategory>()
                                .FirstOrDefault(c => c.Name.Equals(model.Name, StringComparison.OrdinalIgnoreCase));
                            if (sub != null)
                            {
                                cachedCat = sub;
                                break;
                            }
                        }
                    }
                }
                model.Category = cachedCat;
            }
            return cachedCat;
        }
    }

    public static class CategoryModelExtensions
    {
        public static CategoryModel ToCategoryModel(this RevitCategory category, Document doc, bool isTemplate = false)
        {
            if (category == null) return null;
            var model = new CategoryModel();
            model.IsTemplate = isTemplate;
            model.CategoryId = category.ToCategoryIdModel(doc, isTemplate);

            long catIdVal;
#if REVIT2022 || REVIT2023
            catIdVal = category.Id.IntegerValue;
#else
            catIdVal = category.Id.Value;
#endif

            model.ElementId = new ElementIdModel
            {
                Name = category.Name,
                Class = "Autodesk.Revit.DB.Category",
                Category = category.Name,
                Id = catIdVal,
                IsTemplate = isTemplate
            };

            model.IsCuttable = category.IsCuttable;

            if (category.IsCuttable)
            {
                try
                {
                    model.LineWeightCut = category.GetLineWeight(GraphicsStyleType.Cut);
                    ElementId linePatternCutId = category.GetLinePatternId(GraphicsStyleType.Cut);
                    if (linePatternCutId == ElementId.InvalidElementId || linePatternCutId == LinePatternElement.GetSolidPatternId())
                    {
                        long solidIdVal;
#if REVIT2022 || REVIT2023
                        solidIdVal = LinePatternElement.GetSolidPatternId().IntegerValue;
#else
                        solidIdVal = LinePatternElement.GetSolidPatternId().Value;
#endif
                        model.LinePatternCut = new ElementIdModel { Id = solidIdVal, Name = "Solid", Class = "Autodesk.Revit.DB.LinePatternElement", Category = "" };
                    }
                    else
                    {
                        model.LinePatternCut = linePatternCutId.ToModel(doc, isTemplate);
                    }
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                {
                    model.LineWeightCut = null;
                    model.LinePatternCut = null;
                }
            }
            else
            {
                model.LineWeightCut = null;
                model.LinePatternCut = null;
            }

            model.LineWeightProjection = category.GetLineWeight(GraphicsStyleType.Projection);

            ElementId linePatternProjId = category.GetLinePatternId(GraphicsStyleType.Projection);
            if (linePatternProjId == ElementId.InvalidElementId || linePatternProjId == LinePatternElement.GetSolidPatternId())
            {
                long solidIdVal;
#if REVIT2022 || REVIT2023
                solidIdVal = LinePatternElement.GetSolidPatternId().IntegerValue;
#else
                solidIdVal = LinePatternElement.GetSolidPatternId().Value;
#endif
                model.LinePatternProjection = new ElementIdModel { Id = solidIdVal, Name = "Solid", Class = "Autodesk.Revit.DB.LinePatternElement", Category = "" };
            }
            else
            {
                model.LinePatternProjection = linePatternProjId.ToModel(doc, isTemplate);
            }

            if (category.LineColor != null && category.LineColor.IsValid)
            {
                model.LineColor = category.LineColor.ToModel();
            }
            if (category.Material != null)
            {
                model.Material = category.Material.Id.ToModel(doc, isTemplate);
            }

            if (category.Parent != null)
            {
                model.ParentCategoryName = category.Parent.Name;
            }

            string parentName = category.Parent != null ? category.Parent.Name : "";
            model.CategoryGroupLevel2 = !string.IsNullOrEmpty(parentName) ? parentName : category.Name;

            bool isLineStyle = string.Equals(category.Name, "Lines", StringComparison.OrdinalIgnoreCase) || 
                               string.Equals(parentName, "Lines", StringComparison.OrdinalIgnoreCase);

            bool isImported = category.Name.StartsWith("Imports in ", StringComparison.OrdinalIgnoreCase) || 
                              parentName.StartsWith("Imports in ", StringComparison.OrdinalIgnoreCase) ||
                              category.Name.IndexOf(".dwg", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              parentName.IndexOf(".dwg", StringComparison.OrdinalIgnoreCase) >= 0;

            string lvl1 = "Model Categories";
            if (isLineStyle)
            {
                lvl1 = "Line Styles";
            }
            else if (isImported)
            {
                lvl1 = "Imported Categories";
            }
            else if (category.CategoryType == CategoryType.Annotation)
            {
                lvl1 = "Annotation Categories";
            }
            else if (category.CategoryType == CategoryType.AnalyticalModel)
            {
                lvl1 = "Analytical Categories";
            }
            else if (category.CategoryType == CategoryType.Model)
            {
                lvl1 = "Model Categories";
            }

            model.CategoryGroupLevel1 = lvl1;
            return model;
        }

        public static RevitCategory GetCategory(this CategoryModel model, Document doc)
        {
            return model.CategoryId.GetCategory(doc);
        }
    }
}
