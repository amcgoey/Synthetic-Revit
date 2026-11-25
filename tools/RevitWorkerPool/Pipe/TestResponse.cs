using System.ComponentModel;
using System.Runtime.CompilerServices;
using ricaun.NUnit.Models;

namespace RevitWorkerPool.Pipe;

/// <summary>
/// Named-pipe response payload matching <c>ricaun.RevitTest.Shared.TestResponse</c>.
/// </summary>
public sealed class TestResponse : INotifyPropertyChanged
{
    private bool _isBusy;
    private TestModel? _test;
    private TestAssemblyModel? _tests;
    private string? _info;

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy != value)
            {
                _isBusy = value;
                OnPropertyChanged();
            }
        }
    }

    public TestModel? Test
    {
        get => _test;
        set
        {
            if (!ReferenceEquals(_test, value))
            {
                _test = value;
                OnPropertyChanged();
            }
        }
    }

    public TestAssemblyModel? Tests
    {
        get => _tests;
        set
        {
            if (!ReferenceEquals(_tests, value))
            {
                _tests = value;
                OnPropertyChanged();
            }
        }
    }

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

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
