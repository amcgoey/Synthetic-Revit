using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Synthetic.Modules.RevitDOM
{
    internal class FillPatternTranslator : IModelTranslator<FillPatternElement, FillPatternElementModel>
    {
        public void ExtractSpecifics(FillPatternElement revitElement, FillPatternElementModel model, Document doc)
        {
            if (revitElement == null) throw new ArgumentNullException(nameof(revitElement));
            if (model == null) throw new ArgumentNullException(nameof(model));

            var pat = revitElement.GetFillPattern();
            if (pat != null)
            {
                model.Pattern = ToModel(pat);
            }
        }

        public FillPatternElement? InjectSpecifics(FillPatternElementModel model, FillPatternElement? revitElement, Document doc)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            if (model.Pattern == null) return revitElement;

            FillPattern fp = ToFillPattern(model.Pattern);

            if (revitElement == null)
            {
                revitElement = FillPatternElement.Create(doc, fp);
            }
            else
            {
                try
                {
                    revitElement.SetFillPattern(fp);
                }
                catch (Exception)
                {
                    // Ignore if system pattern doesn't support changing its pattern definition
                }
            }

            return revitElement;
        }

        private static FillPattern ToFillPattern(FillPatternModel pattern)
        {
            FillPatternTarget target = (FillPatternTarget)Enum.Parse(typeof(FillPatternTarget), pattern.Target);
            FillPatternHostOrientation orientation = (FillPatternHostOrientation)Enum.Parse(typeof(FillPatternHostOrientation), pattern.HostOrientation);
            FillPattern fillPattern = new FillPattern(pattern.Name, target, orientation);
            IList<FillGrid> fillgrids = new List<FillGrid>();
            if (pattern.FillGrids != null)
            {
                foreach (FillGridModel grid in pattern.FillGrids)
                {
                    fillgrids.Add(ToFillGrid(grid));
                }
            }
            if (fillgrids.Count > 0)
            {
                fillPattern.SetFillGrids(fillgrids);
            }
            return fillPattern;
        }

        private static FillPatternModel ToModel(FillPattern pattern)
        {
            if (pattern == null || !pattern.IsValidObject) return null;
            var model = new FillPatternModel();
            model.Name = pattern.Name;
            model.Target = pattern.Target.ToString();
            model.HostOrientation = pattern.HostOrientation.ToString();
            model.FillGrids = pattern.GetFillGrids().Select(g => ToModel(g)).ToList();
            return model;
        }

        private static FillGridModel ToModel(FillGrid fillGrid)
        {
            if (fillGrid == null || !fillGrid.IsValidObject) return null;
            return new FillGridModel(fillGrid.Angle, fillGrid.Offset, fillGrid.Origin.ToModel(), fillGrid.Shift);
        }

        private static FillGrid ToFillGrid(FillGridModel model)
        {
            if (model == null) return null;
            var fillGrid = new FillGrid(model.Angle, model.Offset);
            if (model.Origin != null)
            {
                fillGrid.Origin = model.Origin.ToUV();
            }
            fillGrid.Shift = model.Shift;
            return fillGrid;
        }

        #region Explicit IModelTranslator implementations

        void IModelTranslator.ExtractSpecifics(object revitElement, ObjectModel model, Document doc)
        {
            ExtractSpecifics((FillPatternElement)revitElement, (FillPatternElementModel)model, doc);
        }

        object? IModelTranslator.InjectSpecifics(ObjectModel model, object? revitElement, Document doc)
        {
            return InjectSpecifics((FillPatternElementModel)model, (FillPatternElement?)revitElement, doc);
        }

        #endregion
    }
}
