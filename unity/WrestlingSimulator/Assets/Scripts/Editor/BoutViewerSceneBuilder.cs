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
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.58f, 0.66f);
            RenderSettings.ambientEquatorColor = new Color(0.32f, 0.33f, 0.36f);
            RenderSettings.ambientGroundColor = new Color(0.12f, 0.12f, 0.13f);
            RenderSettings.skybox = null;

            var cameraObject = new GameObject("Broadcast Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.045f, 0.06f);
            camera.fieldOfView = 34f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 80f;
            cameraObject.tag = "MainCamera";
            BroadcastCamera broadcast = cameraObject.AddComponent<BroadcastCamera>();

            var keyLight = new GameObject("Arena Light");
            Light key = keyLight.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.25f;
            key.color = new Color(1f, 0.97f, 0.92f);
            key.shadows = LightShadows.Soft;
            keyLight.transform.rotation = Quaternion.Euler(58f, -25f, 0f);

            var fillLight = new GameObject("Fill Light");
            Light fill = fillLight.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.35f;
            fill.color = new Color(0.75f, 0.82f, 1f);
            fill.shadows = LightShadows.None;
            fillLight.transform.rotation = Quaternion.Euler(35f, 150f, 0f);

            var matObject = new GameObject("Mat");
            MatView mat = matObject.AddComponent<MatView>();
            mat.Configure(
                Lit("Mat_CentralSurface", new Color(0.95f, 0.76f, 0.18f), 0.25f),
                Lit("Mat_PassivityZone", new Color(0.93f, 0.42f, 0.12f), 0.25f),
                Lit("Mat_ProtectionArea", new Color(0.10f, 0.27f, 0.62f), 0.25f),
                Lit("Mat_RedCorner", new Color(0.78f, 0.10f, 0.12f), 0.25f),
                Lit("Mat_BlueCorner", new Color(0.08f, 0.20f, 0.70f), 0.25f),
                Lit("Platform", new Color(0.16f, 0.17f, 0.19f), 0.15f));

            var canvasObject = new GameObject("Scoreboard", typeof(RectTransform));
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
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
                Lit("Singlet_Red", new Color(0.80f, 0.08f, 0.10f), 0.45f),
                Lit("Singlet_Blue", new Color(0.07f, 0.22f, 0.75f), 0.45f),
                Lit("Skin_A", new Color(0.86f, 0.66f, 0.52f), 0.35f),
                Lit("Skin_B", new Color(0.70f, 0.50f, 0.37f), 0.35f),
                Lit("Shoe", new Color(0.08f, 0.08f, 0.09f), 0.3f));

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

        private static Material Lit(string materialName, Color colour, float smoothness)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Presentation"))
            {
                AssetDatabase.CreateFolder("Assets", "Presentation");
            }

            if (!AssetDatabase.IsValidFolder(MaterialFolder))
            {
                AssetDatabase.CreateFolder("Assets/Presentation", "Materials");
            }

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
