using Autodesk.Revit.DB;
using Synthetic.Modules.ViewManagement.Commands;
using Synthetic.Modules.ViewManagement.Models;
using Synthetic.Modules.ViewManagement.Utilities;

using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;

using View = Autodesk.Revit.DB.View;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;
using Synthetic.Shared.UI;
using Synthetic.Shared.RevitAPI;

namespace Synthetic.Modules.ViewManagement.Models
{
    /// <summary>
    /// Class used to automatically renumber views based on a grid.  Uses a family on the sheet to determine the origin point and the spacing of the grid.
    /// </summary>
    public class ViewAutoNumModel
    {
        #region Properties

        /// <summary>
        /// A Revit Document
        /// </summary>
        public Document Document { get; set; }

        /// <summary>
        /// A Revit Family used as the Origin Point
        /// </summary>
        public FamilySymbol? Family { get; set; }

        /// <summary>
        /// Name of the parameter in the Revit Family that specifies the X grid spacing
        /// </summary>
        public string XSpacingParameter { get; set; }

        /// <summary>
        /// Name of the parameter in the Revit Family that specifies the X grid spacing
        /// </summary>
        public string YSpacingParameter { get; set; }

        /// <summary>
        /// The X grid spacing used to number views on relative the origin point.
        /// </summary>
        public double XSpacing { get; set; }

        /// <summary>
        /// The Y grid spacing used to number views on relative the origin point.
        /// </summary>
        public double YSpacing { get; set; }
        
        #endregion


        #region Constructors
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="doc">A Revit Document</param>
        /// <param name="familyName">Name of the Family used as the origin point</param>
        /// <param name="symbolName">Name of the Family Type used as the origin</param>
        /// <param name="xSpacingParameter">Name of the Parameter in the Family that specifies the X grid spacing</param>
        /// <param name="ySpacingParameter">Name of the Parameter in the Family that specifies the Y grid spacing</param>
        public ViewAutoNumModel(Document doc, string familyName, string symbolName, string xSpacingParameter, string ySpacingParameter)
        {
            this.Document = doc;
            this.Family = FamilySymbolUtil.GetByName( this.Document, familyName, symbolName );
            this.XSpacingParameter = xSpacingParameter;
            this.YSpacingParameter = ySpacingParameter;

            if (this.Family != null)
            {
                //this.Origin = FamilySymbolUtil.GetOrigin(this.Family);
                this.XSpacing = this.Family.LookupParameter(this.XSpacingParameter).AsDouble();
                this.YSpacing = this.Family.LookupParameter(this.YSpacingParameter).AsDouble();
            }
            else 
            {
                //this.Origin = XYZ.Zero;
                this.XSpacing = 0.0833333333;
                this.YSpacing = 0.0833333333;
            }
        }
        #endregion

        #region Methods
        /// <summary>
        /// Renumbers the views on the given sheet based on the views location in a grid
        /// </summary>
        /// <param name="sheet">Sheet to renumber views on</param>
        /// <returns>Viewports on the sheet.</returns>
        public List<Viewport>? AutoNumberOnSheet (ViewSheet sheet)
        {
            if (this.Family == null) return null;
            List<Viewport>? viewports = null;
            FamilyInstance instance = (FamilyInstance)FamilySymbolUtil.GetInstancesInView(this.Document, this.Family, sheet).FirstElement();

            if (instance != null)
            {
                LocationPoint location = (LocationPoint)instance.Location;
                XYZ originPoint = location.Point;

                List<ElementId> viewportIds = (List<ElementId>)sheet.GetAllViewports();

                Synthetic.Shared.UI.ProgressCoordinator.Initialize("Auto-Numbering Views", "Renumbering views on sheet...", viewportIds.Count);

                try
                {
                    viewports = _tempRenumberViewports(viewportIds, this.Document);
                    if (!Synthetic.Shared.UI.ProgressCoordinator.IsCancelled())
                    {
                        viewports = _renumberViewports(viewports, this.XSpacing, this.YSpacing, originPoint.X, originPoint.Y);
                    }
                }
                finally
                {
                    Synthetic.Shared.UI.ProgressCoordinator.Close();
                }
            }
            return viewports;
        }
        #endregion

        #region Internal Functions

        /// <summary>
        /// Given a list of viewport element IDs, the function will get the viewport from the document and give each viewport a temporary sheet number.  Function will ignore legends.
        /// </summary>
        /// <param name="viewPortIds">Revit ElementId of the viewports.</param>
        /// <param name="doc">The Revit Document the viewports are in.</param>
        /// <returns name="viewports">Returns the Revit viewports.</returns>
        internal static List<Viewport> _tempRenumberViewports(List<ElementId> viewPortIds, Document doc)
        {
            List<Viewport> viewPorts = new List<Viewport>();
            int i = 1;

            foreach (ElementId id in viewPortIds)
            {
                if (Synthetic.Shared.UI.ProgressCoordinator.IsCancelled())
                {
                    break;
                }

                Viewport vp = (Viewport)doc.GetElement(id);
                View v = (View)doc.GetElement(vp.ViewId);

                if (
                    v.ViewType == ViewType.FloorPlan
                    || v.ViewType == ViewType.CeilingPlan
                    || v.ViewType == ViewType.Elevation
                    || v.ViewType == ViewType.ThreeD
                    || v.ViewType == ViewType.DraftingView
                    || v.ViewType == ViewType.AreaPlan
                    || v.ViewType == ViewType.Section
                    || v.ViewType == ViewType.Detail
                    || v.ViewType == ViewType.Rendering
                    )
                {
                    viewPorts.Add(vp);

                    Parameter param = vp.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER);
                    if (param != null) param.Set("!!" + i);
                    i++;
                }
            }
            return viewPorts;
        }

        /// <summary>
        /// Given a list of viewports, grid spacing and an origin point, function will renumber the viewports based on grid location.
        /// </summary>
        /// <param name="viewports">Revit ViewPorts</param>
        /// <param name="gridX">Grid spacing in the X direction</param>
        /// <param name="gridY">Grid spacing in the Y direction</param>
        /// <param name="originX">X coordinate of the grid origin</param>
        /// <param name="originY">Y coordinate of the grid origin</param>
        /// <returns name="viewports">The renumbered Revit ViewPorts</returns>
        internal static List<Viewport> _renumberViewports(List<Viewport> viewports, double gridX, double gridY, double originX, double originY)
        {
            foreach (Viewport vp in viewports)
            {
                if (Synthetic.Shared.UI.ProgressCoordinator.IsCancelled())
                {
                    break;
                }

                Outline labelOutline = vp.GetLabelOutline();
                XYZ minPt = labelOutline.MinimumPoint;

                string viewNumber = _calculateViewNumber(minPt.X, minPt.Y, gridX, gridY, originX, originY);

                Parameter param = vp.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER);
                if (param != null) param.Set(viewNumber);

                View v = (View)vp.Document.GetElement(vp.ViewId);
                Synthetic.Shared.UI.ProgressCoordinator.UpdateProgress(v.Name ?? $"Detail: {viewNumber}");
            }

            return viewports;
        }

        internal static string _calculateViewNumber(double viewX, double viewY, double gridX, double gridY, double originX, double originY)
        {
            // const double viewportOffset = 0.0114;
            const double viewportOffset = 0.0;

            double x = Math.Floor(((viewX - originX) + viewportOffset) / gridX + 1);
            double y = Math.Floor(((viewY - originY) + viewportOffset) / gridY + 1);

            string stringX = x.ToString();
            string stringY = _IntToLetters((int)Math.Abs(y));

            if (y < 0)
            {
                stringY = "-" + stringY;
            }
            else if (y == 0)
            {
                stringY = "!" + stringY;
            }

            return stringY + stringX;
        }

        /// <summary>
        /// Given an Int, returns an equivalent letter from the alphabet.
        /// </summary>
        /// <param name="value">An integer</param>
        /// <returns>A letter from the alphabet</returns>
        internal static string _IntToLetters(int value)
        {
            string result = string.Empty;
            while (--value >= 0)
            {
                result = (char)('A' + value % 26) + result;
                value /= 26;
            }
            return result;
        }

        #endregion
    }
}
