namespace Synthetic.Features.MultiModelPdfExport.Models
{
    /// <summary>
    /// Identifies the backing storage topology of a Revit project document.
    /// </summary>
    public enum MultiModelPdfStorageKind
    {
        Workshared = 0,
        CloudAcc = 1,
        Local = 2,
        Unsaved = 3
    }
}
