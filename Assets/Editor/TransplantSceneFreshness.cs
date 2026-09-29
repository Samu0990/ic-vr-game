using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VRSurgery.EditorTools
{
    /// <summary>
    /// Asks, once per Editor session, to rebuild the transplant scene when the builder has changed
    /// since this machine last built it.
    ///
    /// The scene is generated, so pulling new builder code changes nothing on screen until
    /// someone runs the menu — and the committed scene file is whatever was last generated,
    /// possibly by someone else, possibly long ago. Opening the project and seeing the old room
    /// is the easiest mistake to make here. This turns it into a question instead.
    ///
    /// Asks, never acts: a rebuild replaces the scene file, and the answer may be "not now".
    /// Silent in batch mode, so test runs and command-line builds are never interrupted.
    /// </summary>
    [InitializeOnLoad]
    public static class TransplantSceneFreshness
    {
        private const string BuiltVersionKey = "VRSurgery.TransplantScene.BuiltVersion";
        private const string AskedThisSessionKey = "VRSurgery.TransplantScene.Asked";

        static TransplantSceneFreshness()
        {
            EditorApplication.delayCall += Check;
        }

        /// <summary>Records that this machine's scene matches the current builder.</summary>
        public static void MarkBuilt() => EditorPrefs.SetInt(BuiltVersionKey, TransplanteSceneBuilder.BuildVersion);

        private static void Check()
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) { return; }
            if (SessionState.GetBool(AskedThisSessionKey, false)) { return; }
            SessionState.SetBool(AskedThisSessionKey, true);

            int built = EditorPrefs.GetInt(BuiltVersionKey, 0);
            if (built >= TransplanteSceneBuilder.BuildVersion) { return; }

            bool rebuild = EditorUtility.DisplayDialog(
                "Cena do transplante desatualizada",
                "O construtor da cena TransplanteCardiaco mudou desde a última vez que ela foi gerada neste " +
                "computador (sala de cirurgia, bisturi com nota da incisão, serra, cautério, cânulas e clampe, desfibrilador, sutura, tecido, monitores).\n\n" +
                "Reconstruir agora no nível Médio? Leva alguns segundos e substitui o arquivo da cena.",
                "Reconstruir agora", "Depois");

            if (!rebuild) { return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { return; }

            TransplanteSceneBuilder.BuildMedium();
        }
    }
}
