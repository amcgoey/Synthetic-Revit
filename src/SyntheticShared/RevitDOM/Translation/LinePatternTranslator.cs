using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Translation
{
    internal class LinePatternTranslator : IModelTranslator<LinePatternElement, LinePatternElementModel>
    {
        public void ExtractSpecifics(LinePatternElement revitElement, LinePatternElementModel model, Document doc)
        {
            if (revitElement == null) throw new ArgumentNullException(nameof(revitElement));
            if (model == null) throw new ArgumentNullException(nameof(model));

            var lp = revitElement.GetLinePattern();
            if (lp != null)
            {
                var segments = lp.GetSegments();
                if (segments != null)
                {
                    model.Segments = segments.Select(s => new LinePatternSegmentModel
                    {
                        Type = s.Type.ToString(),
                        Length = s.Length
                    }).ToList();
                }
            }
        }

        public LinePatternElement? InjectSpecifics(LinePatternElementModel model, LinePatternElement? revitElement, Document doc)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            LinePattern lp = new LinePattern(model.Name);
            if (model.Segments != null && model.Segments.Count > 0)
            {
                List<LinePatternSegment> segments = model.Segments.Select(s => new LinePatternSegment(
                    (LinePatternSegmentType)Enum.Parse(typeof(LinePatternSegmentType), s.Type),
                    s.Length
                )).ToList();
                lp.SetSegments(segments);
            }

            if (revitElement == null)
            {
                revitElement = LinePatternElement.Create(doc, lp);
            }
            else
            {
                revitElement.SetLinePattern(lp);
            }

            return revitElement;
        }

        #region Explicit IModelTranslator implementations

        void IModelTranslator.ExtractSpecifics(object revitElement, ObjectModel model, Document doc)
        {
            ExtractSpecifics((LinePatternElement)revitElement, (LinePatternElementModel)model, doc);
        }

        object? IModelTranslator.InjectSpecifics(ObjectModel model, object? revitElement, Document doc)
        {
            return InjectSpecifics((LinePatternElementModel)model, (LinePatternElement?)revitElement, doc);
        }

        #endregion
    }
}
