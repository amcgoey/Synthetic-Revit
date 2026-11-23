using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Synthetic.Platform.Settings
{
    /// <summary>
    /// Pure comparison utility for detecting configuration drift between an external linked JSON payload and document settings.
    /// </summary>
    public static class SyncDriftComparer
    {
        /// <summary>
        /// Compares an external linked settings JSON payload against active document settings while tolerating legacy files missing newer module keys.
        /// </summary>
        /// <param name="externalJson">The raw JSON text read from the linked settings file.</param>
        /// <param name="documentSettings">The dictionary of active document settings keyed by module key.</param>
        /// <returns>True if external JSON is null/invalid or any present synced module differs structurally from the document settings; otherwise, false.</returns>
        public static bool HasSyncDrift(string externalJson, IReadOnlyDictionary<string, object> documentSettings)
        {
            if (string.IsNullOrWhiteSpace(externalJson))
            {
                return true;
            }

            Dictionary<string, JToken>? extDict;
            try
            {
                extDict = JsonConvert.DeserializeObject<Dictionary<string, JToken>>(externalJson);
            }
            catch (JsonException)
            {
                return true;
            }

            if (extDict == null)
            {
                return true;
            }

            extDict.Remove(SettingsModuleKeys.Sync);

            if (documentSettings == null)
            {
                return true;
            }

            string documentJson = JsonConvert.SerializeObject(documentSettings, Formatting.Indented);
            Dictionary<string, JToken>? docDict = JsonConvert.DeserializeObject<Dictionary<string, JToken>>(documentJson);
            if (docDict == null)
            {
                return true;
            }

            docDict.Remove(SettingsModuleKeys.Sync);

            foreach (KeyValuePair<string, JToken> kvp in extDict)
            {
                if (docDict.TryGetValue(kvp.Key, out JToken? docToken) && !JToken.DeepEquals(kvp.Value, docToken))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
