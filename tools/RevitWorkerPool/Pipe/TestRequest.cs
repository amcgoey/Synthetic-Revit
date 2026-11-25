using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RevitWorkerPool.Pipe;

/// <summary>
/// Named-pipe request payload matching <c>ricaun.RevitTest.Shared.TestRequest</c>.
/// </summary>
public sealed class TestRequest : INotifyPropertyChanged
{
    private string? _info;
    private string[]? _testFilters;
    private string? _testPathFile;

    public string? Info
    {
        get => _info;
        set
        {
            if (!string.Equals(_info, value, StringComparison.Ordinal))
            {
                _info = value;
                OnPropertyChanged();
            }
        }
    }

    public string[]? TestFilters
    {
        get => _testFilters;
        set
        {
            if (!ReferenceEquals(_testFilters, value))
            {
                _testFilters = value;
                OnPropertyChanged();
            }
        }
    }

    public string? TestPathFile
    {
        get => _testPathFile;
        set
        {
            if (!string.Equals(_testPathFile, value, StringComparison.Ordinal))
            {
                _testPathFile = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
