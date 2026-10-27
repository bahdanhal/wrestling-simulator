using UnityEngine;
using Wrestling.Core.Diagnostics;

namespace Wrestling.Diagnostics
{
    /// <summary>
    /// Runs the core determinism probe in a player build (iOS, Android, desktop) and logs
    /// whether this device reproduces the reference hash. Used in device determinism checks.
    /// </summary>
    public sealed class DeterminismProbeRunner : MonoBehaviour
    {
        public const string LogPrefix = "[DeterminismProbe]";

        public static bool RunAndLog()
        {
            ulong hash = DeterminismProbe.Run();
            bool matches = hash == DeterminismProbe.ReferenceHash;
            string message = $"{LogPrefix} platform={Application.platform} hash=0x{hash:X16} reference=0x{DeterminismProbe.ReferenceHash:X16} result={(matches ? "PASS" : "FAIL")}";
            if (matches)
            {
                Debug.Log(message);
            }
            else
            {
                Debug.LogError(message);
            }

            return matches;
        }

        private void Start()
        {
            RunAndLog();
        }
    }
}
