using Autodesk.Revit.DB;

namespace Synthetic.Modules.FamilyManagement.Utilities
{
    /// <summary>
    /// Safe family load options that prevent overwriting existing parameter values in the project.
    /// </summary>
    public class SafeFamilyLoadOptions : IFamilyLoadOptions
    {
        /// <summary>
        /// Called when a family is found in the project.
        /// </summary>
        public bool OnFamilyFound(
          bool familyInUse,
          out bool overwriteParameterValues)
        {
            overwriteParameterValues = false;
            return true;
        }

        /// <summary>
        /// Called when a shared nested family is found in the project.
        /// </summary>
        public bool OnSharedFamilyFound(
          Family sharedFamily,
          bool familyInUse,
          out FamilySource source,
          out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = false;
            return true;
        }
    }
}
