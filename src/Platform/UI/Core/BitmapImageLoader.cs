using System.IO;
using System.Windows.Media.Imaging;

namespace Synthetic.Platform.UI
{
    /// <summary>
    /// Loads frozen WPF <see cref="BitmapImage"/> instances without retaining file locks on disk.
    /// </summary>
    public static class BitmapImageLoader
    {
        /// <summary>
        /// Loads a <see cref="BitmapImage"/> from <paramref name="path"/> into memory and freezes it,
        /// closing the underlying file handle immediately.
        /// </summary>
        /// <param name="path">Path to the image file on disk.</param>
        /// <returns>A frozen <see cref="BitmapImage"/> safe for cross-thread and ribbon usage.</returns>
        public static BitmapImage Load(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
        }
    }
}
