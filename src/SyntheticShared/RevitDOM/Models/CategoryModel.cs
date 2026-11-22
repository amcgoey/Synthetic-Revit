using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Newtonsoft.Json;

using Synthetic.Infrastructure.Serialization;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Models
{
    /// <summary>
    /// Model representation of a Revit Category, providing properties and methods to serialize and modify category settings.
    /// </summary>
    public class CategoryModel : ElementModel
    {
        /// <summary>
        /// Gets the class grouping for categories, returning parent name or a default.
        /// </summary>
        public override string Class
        {
            get
            {
                if (!string.IsNullOrEmpty(ParentCategoryName))
                {
                    return ParentCategoryName!;
                }
                return "Built-In Categories";
            }
            set { }
        }

        /// <summary>
        /// Gets the category type identifier.
        /// </summary>
        public override string Category
        {
            get => "Autodesk.Revit.DB.Category";
            set { }
        }

        /// <summary>
        /// Gets or sets the name of the parent category, if applicable.
        /// </summary>
        public string? ParentCategoryName { get; set; }

        private string? _categoryGroupLevel1;
        /// <summary>
        /// Gets or sets the Level 1 category grouping (e.g. Line Styles, Model Categories).
        /// </summary>
        public string CategoryGroupLevel1
        {
            get
            {
                if (string.IsNullOrEmpty(_categoryGroupLevel1))
                {
                    bool isLineStyle = string.Equals(Name, "Lines", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(ParentCategoryName, "Lines", StringComparison.OrdinalIgnoreCase);

                    bool isImported = (Name != null && (Name.StartsWith("Imports in ", StringComparison.OrdinalIgnoreCase) ||
                                      Name.IndexOf(".dwg", StringComparison.OrdinalIgnoreCase) >= 0)) ||
                                      (ParentCategoryName != null && (ParentCategoryName.StartsWith("Imports in ", StringComparison.OrdinalIgnoreCase) ||
                                      ParentCategoryName.IndexOf(".dwg", StringComparison.OrdinalIgnoreCase) >= 0));

                    if (isLineStyle) return "Line Styles";
                    if (isImported) return "Imported Categories";

                    return "Model Categories";
                }
                return _categoryGroupLevel1!;
            }
            set => _categoryGroupLevel1 = value;
        }

        private string? _categoryGroupLevel2;
        /// <summary>
        /// Gets or sets the Level 2 parent category grouping.
        /// </summary>
        public string CategoryGroupLevel2
        {
            get
            {
                if (string.IsNullOrEmpty(_categoryGroupLevel2))
                {
                    return !string.IsNullOrEmpty(ParentCategoryName) ? ParentCategoryName! : (Name ?? string.Empty);
                }
                return _categoryGroupLevel2!;
            }
            set => _categoryGroupLevel2 = value;
        }

        private bool? _isCuttable;
        /// <summary>
        /// Gets or sets a value indicating whether the category is cuttable in views.
        /// </summary>
        public bool IsCuttable
        {
            get => _isCuttable ?? true;
            set => _isCuttable = value;
        }

        /// <summary>
        /// Gets or sets the line color of the category.
        /// </summary>
        public ColorModel? LineColor { get; set; }

        /// <summary>
        /// Gets or sets the material model of the category.
        /// </summary>
        public ElementIdModel? Material { get; set; }

        /// <summary>
        /// Gets or sets the projection line weight.
        /// </summary>
        public int? LineWeightProjection { get; set; }

        /// <summary>
        /// Gets or sets the cut line weight.
        /// </summary>
        public int? LineWeightCut { get; set; }

        /// <summary>
        /// Gets or sets the projection line pattern.
        /// </summary>
        public ElementIdModel? LinePatternProjection { get; set; }

        /// <summary>
        /// Gets or sets the cut line pattern.
        /// </summary>
        public ElementIdModel? LinePatternCut { get; set; }

        /// <summary>
        /// Gets or sets the CategoryIdModel wrapper. Ignored in JSON.
        /// </summary>
        [JsonIgnoreAttribute]
        public CategoryIdModel CategoryId { get; set; }

        /// <summary>
        /// Gets or sets the underlying Revit Category object. Ignored in JSON.
        /// </summary>
        [JsonIgnoreAttribute]
        public object? RevitCategory
        {
            get => this.CategoryId.Category;
            set => this.CategoryId.Category = value;
        }

        /// <summary>
        /// Initializes a new instance of the CategoryModel class.
        /// </summary>
        public CategoryModel() : base()
        {
            this.CategoryId = new CategoryIdModel();
            this.IsTemplate = true;
            this.ElementId = new ElementIdModel { Class = "Autodesk.Revit.DB.Category" };
        }

        /// <summary>
        /// Populates the model parameters list.
        /// </summary>
        /// <param name="doc">The Revit document.</param>
        public void PopulateParametersList(object? doc = null)
        {
            this.Parameters = new List<ParameterModel>();

            this.Parameters.Add(new ParameterModel(
                "LineWeightProjection",
                this.LineWeightProjection?.ToString() ?? "",
                null,
                "Integer",
                0,
                "",
                false,
                false
            ));

            this.Parameters.Add(new ParameterModel(
                "LineWeightCut",
                this.LineWeightCut?.ToString() ?? "",
                null,
                "Integer",
                0,
                "",
                false,
                false
            ));

            string colorStr = "";
            if (this.LineColor != null)
            {
                colorStr = $"{this.LineColor.Red}, {this.LineColor.Green}, {this.LineColor.Blue}";
            }
            this.Parameters.Add(new ParameterModel(
                "LineColor",
                colorStr,
                null,
                "String",
                0,
                "",
                false,
                false
            ));

            this.Parameters.Add(new ParameterModel(
                "Material",
                this.Material?.Name ?? "",
                this.Material,
                "ElementId",
                0,
                "",
                false,
                false
            ));

            this.Parameters.Add(new ParameterModel(
                "LinePatternProjection",
                this.LinePatternProjection?.Name ?? "",
                this.LinePatternProjection,
                "ElementId",
                0,
                "",
                false,
                false
            ));

            this.Parameters.Add(new ParameterModel(
                "LinePatternCut",
                this.LinePatternCut?.Name ?? "",
                this.LinePatternCut,
                "ElementId",
                0,
                "",
                false,
                false
            ));
        }

        /// <summary>
        /// Synchronizes the model's property values from the internal parameters list.
        /// </summary>
        public void SyncFromParameters()
        {
            if (this.Parameters == null) return;

            var pLineWeightProjection = this.Parameters.FirstOrDefault(p => p.Name == "LineWeightProjection");
            if (pLineWeightProjection != null && int.TryParse(pLineWeightProjection.Value, out int lwp))
            {
                this.LineWeightProjection = lwp;
            }
            else
            {
                this.LineWeightProjection = null;
            }

            var pLineWeightCut = this.Parameters.FirstOrDefault(p => p.Name == "LineWeightCut");
            if (pLineWeightCut != null && int.TryParse(pLineWeightCut.Value, out int lwc))
            {
                this.LineWeightCut = lwc;
            }
            else
            {
                this.LineWeightCut = null;
            }

            var pLineColor = this.Parameters.FirstOrDefault(p => p.Name == "LineColor");
            if (pLineColor != null && !string.IsNullOrWhiteSpace(pLineColor.Value))
            {
                var parts = pLineColor.Value!.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 3 &&
                    byte.TryParse(parts[0].Trim(), out byte r) &&
                    byte.TryParse(parts[1].Trim(), out byte g) &&
                    byte.TryParse(parts[2].Trim(), out byte b))
                {
                    this.LineColor = new ColorModel(r, g, b);
                }
            }

            var pMaterial = this.Parameters.FirstOrDefault(p => p.Name == "Material");
            if (pMaterial != null)
            {
                if (pMaterial.ValueElemId != null)
                {
                    this.Material = pMaterial.ValueElemId;
                }
                else if (!string.IsNullOrWhiteSpace(pMaterial.Value))
                {
                    this.Material = new ElementIdModel { Name = pMaterial.Value!, Class = "Autodesk.Revit.DB.Material" };
                }
                else
                {
                    this.Material = null;
                }
            }

            var pLinePatternProjection = this.Parameters.FirstOrDefault(p => p.Name == "LinePatternProjection");
            if (pLinePatternProjection != null)
            {
                if (pLinePatternProjection.ValueElemId != null)
                {
                    this.LinePatternProjection = pLinePatternProjection.ValueElemId;
                }
                else if (!string.IsNullOrWhiteSpace(pLinePatternProjection.Value))
                {
                    this.LinePatternProjection = new ElementIdModel { Name = pLinePatternProjection.Value!, Class = "Autodesk.Revit.DB.LinePatternElement" };
                }
                else
                {
                    this.LinePatternProjection = null;
                }
            }

            var pLinePatternCut = this.Parameters.FirstOrDefault(p => p.Name == "LinePatternCut");
            if (pLinePatternCut != null)
            {
                if (pLinePatternCut.ValueElemId != null)
                {
                    this.LinePatternCut = pLinePatternCut.ValueElemId;
                }
                else if (!string.IsNullOrWhiteSpace(pLinePatternCut.Value))
                {
                    this.LinePatternCut = new ElementIdModel { Name = pLinePatternCut.Value!, Class = "Autodesk.Revit.DB.LinePatternElement" };
                }
                else
                {
                    this.LinePatternCut = null;
                }
            }
        }
    }

}

