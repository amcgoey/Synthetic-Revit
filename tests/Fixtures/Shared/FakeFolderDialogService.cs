using System.Collections.Generic;
using Synthetic.Platform.UI;

namespace SyntheticTests.Fixtures
{
    public class FakeFolderDialogService : IFolderDialogService
    {
        public string? NextResult { get; set; }
        public List<(string Title, string? InitialPath)> Calls { get; } = new List<(string, string?)>();

        public string? SelectFolder(string title, string? initialPath = null)
        {
            Calls.Add((title, initialPath));
            return NextResult;
        }
    }
}
