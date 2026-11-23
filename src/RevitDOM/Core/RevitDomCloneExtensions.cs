using Newtonsoft.Json;
using Synthetic.Platform.IO;
using Synthetic.RevitDOM.Models;

namespace Synthetic.RevitDOM.Translation
{
    /// <summary>
    /// Pure POCO deep-cloning extensions for <see cref="ObjectModel"/> hierarchies.
    /// </summary>
    public static class RevitDomCloneExtensions
    {
        public static T DeepClone<T>(this T source) where T : ObjectModel
        {
            if (source == null) return default!;
            string json = Json.EncodeMinimal(source);
            return (T)JsonConvert.DeserializeObject(json, source.GetType())!;
        }
    }
}
