using Autodesk.Revit.DB;
using Synthetic.Modules.ViewManagement.Commands;
using Synthetic.Modules.ViewManagement.Models;
using Synthetic.Modules.ViewManagement.Utilities;

using System;
using System.Collections.Generic;
using System.Text;

using revitDB = Autodesk.Revit.DB;
using revitView = Autodesk.Revit.DB.View;
using revitView3D = Autodesk.Revit.DB.View3D;
using RevitDoc = Autodesk.Revit.DB.Document;
using revitElem = Autodesk.Revit.DB.Element;
using revitElemId = Autodesk.Revit.DB.ElementId;
using revitViewOrientation = Autodesk.Revit.DB.ViewOrientation3D;
using revitXYZ = Autodesk.Revit.DB.XYZ;
using revitBBxyz = Autodesk.Revit.DB.BoundingBoxXYZ;
using revitBBuv = Autodesk.Revit.DB.BoundingBoxUV;
using revitParam = Autodesk.Revit.DB.Parameter;
using revitSheet = Autodesk.Revit.DB.ViewSheet;
using revitViewport = Autodesk.Revit.DB.Viewport;
using revitCollector = Autodesk.Revit.DB.FilteredElementCollector;
using revitElementFilter = Autodesk.Revit.DB.ElementFilter;
using revitFamilySymbol = Autodesk.Revit.DB.FamilySymbol;
using revitOutline = Autodesk.Revit.DB.Outline;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;

namespace Synthetic.Modules.ViewManagement.Utilities
{
    /// <summary>
    /// Utility methods for managing and auto-numbering Revit Views.
    /// </summary>
    public class ViewUtil
    {
        /// <summary>
        /// Renumbers the views on the Active Sheet
        /// </summary>
        /// <param name="doc">The active Revit document.</param>
        /// <param name="familyType">Revit Family Symbol that represents the origin element</param>
        /// <param name="xGridName">Name of the parameter that represents the X grid spacing</param>
        /// <param name="yGridName">Name of the parameter that represents the Y grid spacing</param>
        /// <returns name="Viewports">Revit viewport objects on the sheet.</returns>
        public static List<revitViewport>? AutoNumber(RevitDoc doc, revitFamilySymbol familyType, string xGridName, string yGridName)
        {
            revitSheet rSheet = (revitSheet)doc.ActiveView;

            return _renumberViewsOnSheet(familyType, xGridName, yGridName, rSheet, doc);
        }

        #region Utility Functions

        /// <summary>
        /// Sets a workset to visible in a specific view.
        /// </summary>
        /// <param name="view">The Revit View</param>
        /// <param name="workset">The Workset to show</param>
        /// <returns>True if successful, False if skipped due to template</returns>
        public static bool SetWorksetVisibilityInView(revitView view, revitDB.Workset workset)
        {
            if (view.ViewTemplateId != revitElemId.InvalidElementId)
            {
                return false;
            }

            view.SetWorksetVisibility(workset.Id, revitDB.WorksetVisibility.Visible);
            return true;
        }

        internal static List<revitViewport>? _renumberViewsOnSheet(revitFamilySymbol familyType, string xGridName, string yGridName, revitSheet rSheet, RevitDoc document)
        {
            string transactionName = "Renumber views on sheet";

            //  Initialize variables
            revitFamilySymbol rFamilySymbol = (revitFamilySymbol)familyType;

            //  Get all viewport ID's on the sheet.
            List<revitElemId> viewportIds = (List<revitElemId>)rSheet.GetAllViewports();
            List<revitViewport>? viewports = null;

            //  Get the family Instances in view
            revitElemId symbolId = familyType.Id;

            revitCollector collector = new revitCollector(document, rSheet.Id);
            revitElementFilter filterInstance = new revitDB.FamilyInstanceFilter(document, symbolId);

            collector.OfClass(typeof(revitDB.FamilyInstance)).WherePasses(filterInstance);

            revitDB.FamilyInstance originFamily = (revitDB.FamilyInstance)collector.FirstElement();

            //  If family instance is found in the view
            //  Then renumber views.
            if (originFamily != null)
            {
                revitDB.LocationPoint location = (revitDB.LocationPoint)originFamily.Location;
                revitXYZ originPoint = location.Point;

                double gridX = rFamilySymbol.LookupParameter(xGridName).AsDouble();
                double gridY = rFamilySymbol.LookupParameter(yGridName).AsDouble();

                using (Autodesk.Revit.DB.Transaction trans = new Autodesk.Revit.DB.Transaction(document))
                {
                    trans.Start(transactionName);
                    viewports = _tempRenumberViewports(viewportIds, document);
                    viewports = _renumberViewports(viewports, gridX, gridY, originPoint.X, originPoint.Y);
                    trans.Commit();
                }
            }

            return viewports;
        }

        /// <summary>
        /// Given a list of viewport element IDs, the function will get the viewport from the document and give each viewport a temporary sheet number.  Function will ignore legends.
        /// </summary>
        /// <param name="viewPortIds">Revit ElementId of the viewports.</param>
        /// <param name="doc">The Revit Document the viewports are in.</param>
        /// <returns name="viewports">Returns the Revit viewports.</returns>
        internal static List<revitViewport> _tempRenumberViewports(List<revitElemId> viewPortIds, RevitDoc doc)
        {
            List<revitViewport> viewPorts = new List<revitViewport>();
            int i = 1;

            foreach (revitElemId id in viewPortIds)
            {
                revitViewport vp = (revitViewport)doc.GetElement(id);
                revitView v = (revitView)doc.GetElement(vp.ViewId);

                if (
                    v.ViewType == revitDB.ViewType.FloorPlan
                    || v.ViewType == revitDB.ViewType.CeilingPlan
                    || v.ViewType == revitDB.ViewType.Elevation
                    || v.ViewType == revitDB.ViewType.ThreeD
                    || v.ViewType == revitDB.ViewType.DraftingView
                    || v.ViewType == revitDB.ViewType.AreaPlan
                    || v.ViewType == revitDB.ViewType.Section
                    || v.ViewType == revitDB.ViewType.Detail
                    || v.ViewType == revitDB.ViewType.Rendering
                    )
                {
                    viewPorts.Add(vp);

                    revitDB.Parameter param = vp.get_Parameter(revitDB.BuiltInParameter.VIEWPORT_DETAIL_NUMBER);
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
        internal static List<revitViewport> _renumberViewports(List<revitViewport> viewports, double gridX, double gridY, double originX, double originY)
        {
            //const double viewportOffset = 0.0114;

            //int i = 1;

            foreach (revitViewport vp in viewports)
            {
                revitOutline labelOutline = vp.GetLabelOutline();
                revitXYZ minPt = labelOutline.MinimumPoint;

                string viewNumber = _calculateViewNumber(minPt.X, minPt.Y, gridX, gridY, originX, originY);

                revitDB.Parameter param = vp.get_Parameter(revitDB.BuiltInParameter.VIEWPORT_DETAIL_NUMBER);
                if (param != null) param.Set(viewNumber);
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
