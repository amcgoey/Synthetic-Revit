using Synthetic.Modules.Worksets.Commands;
using Synthetic.Modules.Worksets.Models;
using Synthetic.Modules.Worksets.Utilities;

using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Application = Autodesk.Revit.ApplicationServices.Application;

using Synthetic.Modules.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.RevitAPI;
using Synthetic.Infrastructure.IO;
using Newtonsoft.Json;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.Modules.Worksets.Commands
{
    /// <summary>
    /// Revit external command to reload recorded elements from a JSON file and move them back to their recorded workset.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ElementsOnWorksetReload : IExternalCommand
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
            Document document = uidoc.Document;

            Result commandResult = Result.Succeeded;

            string? fullPath = CommandUtil.OpenJSON();
            if (fullPath != null && fullPath != String.Empty && File.Exists(fullPath))
            {
                string json = File.ReadAllText(fullPath);
                ElementsOnWorkset? elementsOnWorkset = JsonConvert.DeserializeObject<ElementsOnWorkset>(json);
                if (elementsOnWorkset != null)
                {
                    elementsOnWorkset.MoveToWorkset(document);
                    if (elementsOnWorkset.IfLog())
                    {
                        string logFileName = Path.GetFileNameWithoutExtension(fullPath);
                        string logExtension = Path.GetExtension(fullPath);
                        string? logPath = Path.GetDirectoryName(fullPath);

                        if (logPath != null)
                        {
                            string logFullPath = Path.Combine(logPath, logFileName + " - " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + " Log Reload" + logExtension);
                            File.WriteAllText(logFullPath, elementsOnWorkset.GetLog());
                        }
                    }
                }
            }
            else { commandResult = Result.Failed; }

            return commandResult;
        }
    }
}