using System;
using System.Windows;

namespace Synthetic.Platform.UI
{
    /// <summary>
    /// Runtime-safe resource dictionary merging <c>SyntheticTheme.xaml</c> from the
    /// owning add-in assembly without relying on host <c>Application.Current.Resources</c>
    /// or leading-slash pack URI resolution.
    /// </summary>
    public sealed class SyntheticThemeDictionary : ResourceDictionary
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SyntheticThemeDictionary"/> class.
        /// </summary>
        public SyntheticThemeDictionary()
        {
            string asmName = typeof(SyntheticThemeDictionary).Assembly.GetName().Name!;
            Source = new Uri($"/{asmName};component/Platform/UI/Addin/SyntheticTheme.xaml", UriKind.Relative);
        }
    }
}
