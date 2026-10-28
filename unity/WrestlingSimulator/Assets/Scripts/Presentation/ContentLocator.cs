using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using Wrestling.Data;

namespace Wrestling.Presentation
{
    /// <summary>
    /// Finds the game's <c>data/</c> folder. In the editor it is the repository's own folder; in a
    /// player build it is copied into StreamingAssets at build time. Android packs StreamingAssets
    /// inside the APK, so there the files listed in the manifest are first extracted to persistent storage.
    /// </summary>
    public static class ContentLocator
    {
        public const string StreamingFolder = "data";
        public const string ManifestFile = "manifest.txt";

        /// <summary>Loads content synchronously where the files are directly readable (editor, desktop, iOS).</summary>
        public static GameContent LoadDirect()
        {
            return GameContent.Load(DirectDataDirectory());
        }

        public static string DirectDataDirectory()
        {
#if UNITY_EDITOR
            string repository = GameContent.FindDataDirectory(Application.dataPath);
            if (repository != null)
            {
                return repository;
            }
#endif
            return Path.Combine(Application.streamingAssetsPath, StreamingFolder);
        }

        /// <summary>True when the data files must be extracted before they can be read.</summary>
        public static bool NeedsExtraction => Application.platform == RuntimePlatform.Android;

        /// <summary>Extracts the packaged data files to persistent storage, then calls <paramref name="onLoaded"/>.</summary>
        public static IEnumerator LoadExtracted(Action<GameContent> onLoaded, Action<string> onError)
        {
            string source = Application.streamingAssetsPath + "/" + StreamingFolder + "/";
            string target = Path.Combine(Application.persistentDataPath, StreamingFolder, Application.version);
            using (UnityWebRequest manifestRequest = UnityWebRequest.Get(source + ManifestFile))
            {
                yield return manifestRequest.SendWebRequest();
                if (manifestRequest.result != UnityWebRequest.Result.Success)
                {
                    onError($"Could not read the content manifest: {manifestRequest.error}");
                    yield break;
                }

                foreach (string line in manifestRequest.downloadHandler.text.Split('\n'))
                {
                    string relative = line.Trim();
                    if (relative.Length == 0)
                    {
                        continue;
                    }

                    using (UnityWebRequest fileRequest = UnityWebRequest.Get(source + relative))
                    {
                        yield return fileRequest.SendWebRequest();
                        if (fileRequest.result != UnityWebRequest.Result.Success)
                        {
                            onError($"Could not extract '{relative}': {fileRequest.error}");
                            yield break;
                        }

                        string path = Path.Combine(target, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        File.WriteAllBytes(path, fileRequest.downloadHandler.data);
                    }
                }
            }

            GameContent content;
            try
            {
                content = GameContent.Load(target);
            }
            catch (Exception exception)
            {
                onError(exception.Message);
                yield break;
            }

            onLoaded(content);
        }
    }
}
