namespace Synthetic.Features.MultiModelPdfExport.Models
{
    /// <summary>
    /// Specifies whether exported views/sheets combine into one PDF per document or separate PDFs.
    /// </summary>
    public enum MultiModelPdfFileMode
    {
        CombinedPerDocument = 0,
        SeparatePerViewSheet = 1
    }
}
