#region Namespaces
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Synthetic.Modules.FamilyManagement.Commands;

using System;
using System.IO;
using System.Collections.Generic;
using System.Windows;
using Synthetic.Infrastructure.Serialization;
using Synthetic.Infrastructure.IO;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;
using Application = Autodesk.Revit.ApplicationServices.Application;
using View = Autodesk.Revit.DB.View;
using System.Linq;
using System.Runtime;
using Autodesk.Revit.DB.Events;

using Synthetic.Shared.RevitAPI;
using SFamilyUtil = Synthetic.Shared.RevitAPI.FamilyUtil;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;

#endregion

namespace Synthetic.Modules.FamilyManagement.Commands
{
    /// <summary>
    /// Sets the path and sheet to an Excel file with Worksets.  Stores the setting in an Extensible Storage.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class FamiliesForceReinsert : IExternalCommand
    {
        /// <summary>
        /// Execute a Revit Command
        /// </summary>
        /// <param name="commandData">commandData</param>
        /// <param name="message">message</param>
        /// <param name="elements">Currently selected elements</param>
        /// <returns>A Autodesk.Revit.UI.Result</returns>
        public Result Execute(
          ExternalCommandData commandData,
          ref string message,
          ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Application app = uiapp.Application;
            Document doc = uidoc.Document;

            SFamilyUtil.ForceReinsertAnnotation(doc);
            
            return Result.Succeeded;
        }
    }
}
