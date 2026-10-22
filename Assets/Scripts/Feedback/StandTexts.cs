using System.Collections.Generic;
using UnityEngine;

namespace VRSurgery.Feedback
{
    /// <summary>
    /// The stand's new lines of text, read from one data file
    /// (Assets/Resources/Textos/textos_estande.txt) so the advisor can review and change them
    /// without touching code. Tab-separated key and text; "\n" breaks a line; "#" comments.
    /// A key missing from the file falls back to the text given in code, so a typo in the file
    /// never leaves a blank pop-up.
    /// </summary>
    public static class StandTexts
    {
        public const string ResourcePath = "Textos/textos_estande";

        private static Dictionary<string, string> _lines;

        public static string Get(string key, string fallback)
        {
            if (_lines == null) { Load(); }
            return _lines.TryGetValue(key, out string text) && !string.IsNullOrEmpty(text) ? text : fallback;
        }

        /// <summary>Reads the file again, e.g. after it was edited in the Editor.</summary>
        public static void Load()
        {
            _lines = new Dictionary<string, string>();
            TextAsset file = Resources.Load<TextAsset>(ResourcePath);
            if (file == null)
            {
                Debug.LogWarning($"[Textos] {ResourcePath} ausente; usando os textos de reserva do código.");
                return;
            }

            foreach (string raw in file.text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line.StartsWith("#")) { continue; }

                int tab = line.IndexOf('\t');
                if (tab <= 0) { continue; }
                _lines[line.Substring(0, tab).Trim()] = line.Substring(tab + 1).Replace("\\n", "\n");
            }
        }
    }
}
