using Synthetic.Features.SheetIndex.Models;

namespace Synthetic.Features.SheetIndex.Adapters
{
    /// <summary>
    /// Single-query Revit port isolating all Sheet Index database harvesting behind an immutable project snapshot.
    /// </summary>
    public interface ISheetIndexAdapter
    {
        /// <summary>
        /// Queries sheets, revisions, Print Sets, and sheet schedules from the active project into an immutable snapshot.
        /// </summary>
        SheetIndexSnapshot LoadProjectSnapshot();
    }
}
