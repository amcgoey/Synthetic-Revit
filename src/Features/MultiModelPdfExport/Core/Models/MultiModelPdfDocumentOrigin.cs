namespace Synthetic.Features.MultiModelPdfExport.Models
{
    /// <summary>
    /// Identifies how a Revit project document entered the Multi-Model PDF Export session.
    /// </summary>
    public enum MultiModelPdfDocumentOrigin
    {
        Active = 0,
        Linked = 1,
        OpenStandalone = 2
    }
}
