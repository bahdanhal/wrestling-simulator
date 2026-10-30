using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Wrestling.Presentation;

namespace Wrestling.Editor
{
    /// <summary>Creates the bout viewer scene and its materials from code, so the scene is reproducible.</summary>
    public static class BoutViewerSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/BoutViewer.unity";
        private const string MaterialFolder = "Assets/Presentation/Materials";

        [MenuItem("Wrestling/Build Bout Viewer Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.58f, 0.62f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.36f, 0.38f, 0.44f);
            RenderSettings.ambientGroundColor = new Color(0.16f, 0.17f, 0.20f);
            RenderSettings.skybox = null;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.045f, 0.06f, 0.095f);
            RenderSettings.fogDensity = 0.011f;

            var cameraObject = new GameObject("Broadcast Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.045f, 0.06f, 0.095f);
            camera.fieldOfView = 34f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 90f;
            cameraObject.tag = "MainCamera";
            BroadcastCamera broadcast = cameraObject.AddComponent<BroadcastCamera>();

            var keyLight = new GameObject("Arena Light");
            Light key = keyLight.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.30f;
            key.color = new Color(1f, 0.96f, 0.90f);
            key.shadows = LightShadows.Soft;
            keyLight.transform.rotation = Quaternion.Euler(54f, -28f, 0f);

            var fillLight = new GameObject("Fill Light");
            Light fill = fillLight.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.45f;
            fill.color = new Color(0.72f, 0.82f, 1f);
            fill.shadows = LightShadows.None;
            fillLight.transform.rotation = Quaternion.Euler(32f, 145f, 0f);

            var rimLight = new GameObject("Rim Light");
            Light rim = rimLight.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.intensity = 0.52f;
            rim.color = new Color(0.84f, 0.90f, 1f);
            rim.shadows = LightShadows.None;
            rimLight.transform.rotation = Quaternion.Euler(26f, -158f, 0f);

            var matObject = new GameObject("Mat");
            MatView mat = matObject.AddComponent<MatView>();
            mat.Configure(
                Lit("Mat_CentralSurface", new Color(0.95f, 0.73f, 0.16f), 0.28f),
                Lit("Mat_PassivityZone", new Color(0.92f, 0.38f, 0.11f), 0.28f),
                Lit("Mat_ProtectionArea", new Color(0.09f, 0.25f, 0.56f), 0.26f),
                Lit("Mat_RedCorner", new Color(0.80f, 0.11f, 0.13f), 0.28f),
                Lit("Mat_BlueCorner", new Color(0.08f, 0.22f, 0.72f), 0.28f),
                Lit("Platform", new Color(0.14f, 0.15f, 0.18f), 0.22f));

            var canvasObject = new GameObject("Scoreboard", typeof(RectTransform));
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 0.08f;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            Scoreboard scoreboard = canvasObject.AddComponent<Scoreboard>();

            var viewerObject = new GameObject("Bout Viewer");
            BoutViewer viewer = viewerObject.AddComponent<BoutViewer>();
            viewer.Configure(
                mat,
                broadcast,
                scoreboard,
                Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),
                CharacterLibraryBuilder.Build());

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"Bout viewer scene written to {ScenePath}.");
        }

        public static void BuildFromCommandLine()
        {
            Build();
            EditorApplication.Exit(0);
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Presentation"))
            {
                AssetDatabase.CreateFolder("Assets", "Presentation");
            }

            if (!AssetDatabase.IsValidFolder(MaterialFolder))
            {
                AssetDatabase.CreateFolder("Assets/Presentation", "Materials");
            }
        }

        private static Material Lit(string materialName, Color colour, float smoothness)
        {
            EnsureFolders();
            string path = $"{MaterialFolder}/{materialName}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
