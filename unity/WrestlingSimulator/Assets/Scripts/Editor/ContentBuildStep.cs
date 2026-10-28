using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Wrestling.Data;
using Wrestling.Presentation;

namespace Wrestling.Editor
{
    /// <summary>
    /// Copies the repository's <c>data/</c> folder into StreamingAssets for a player build, with a
    /// manifest so Android can extract the files, and removes the copy afterwards.
    /// </summary>
    public sealed class ContentBuildStep : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        private static string Target => Path.Combine(Application.streamingAssetsPath, ContentLocator.StreamingFolder);

        public void OnPreprocessBuild(BuildReport report)
        {
            string source = GameContent.FindDataDirectory(Application.dataPath)
                ?? throw new BuildFailedException("Could not find the repository's data/ folder.");
            GameContent.Load(source); // Fail the build on invalid content.
            if (Directory.Exists(Target))
            {
                Directory.Delete(Target, true);
            }

            string[] files = Directory.GetFiles(source, "*.json", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(source, path).Replace('\\', '/'))
                .OrderBy(path => path, System.StringComparer.Ordinal)
                .ToArray();
            foreach (string relative in files)
            {
                string destination = Path.Combine(Target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(Path.Combine(source, relative), destination);
            }

            File.WriteAllText(Path.Combine(Target, ContentLocator.ManifestFile), string.Join("\n", files) + "\n");
            AssetDatabase.Refresh();
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (Directory.Exists(Target))
            {
                Directory.Delete(Target, true);
                string meta = Target + ".meta";
                if (File.Exists(meta))
                {
                    File.Delete(meta);
                }

                AssetDatabase.Refresh();
            }
        }
    }
}
