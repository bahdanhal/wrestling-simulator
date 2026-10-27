using UnityEditor;
using Wrestling.Diagnostics;

namespace Wrestling.Editor
{
    /// <summary>Runs the determinism probe inside the Unity editor, from the menu or from batch mode.</summary>
    public static class DeterminismCheck
    {
        [MenuItem("Wrestling/Diagnostics/Run Determinism Probe")]
        public static void RunFromMenu()
        {
            DeterminismProbeRunner.RunAndLog();
        }

        /// <summary>
        /// Batch-mode entry point. Exits with code 0 on a match, 2 on a mismatch:
        /// Unity -batchmode -projectPath unity/WrestlingSimulator -executeMethod Wrestling.Editor.DeterminismCheck.RunFromCommandLine
        /// </summary>
        public static void RunFromCommandLine()
        {
            bool matches = DeterminismProbeRunner.RunAndLog();
            EditorApplication.Exit(matches ? 0 : 2);
        }
    }
}
