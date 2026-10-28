using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Wrestling.Presentation;

namespace Wrestling.Editor
{
    /// <summary>
    /// Builds <see cref="CharacterLibrary"/> from the files tools/characters/build_wrestlers.py
    /// exported into Assets/Characters: the base models, one skin set per exported skin, the body
    /// material template and URP materials for eyes, brows, lashes, hair and shoes.
    /// </summary>
    public static class CharacterLibraryBuilder
    {
        public const string LibraryPath = CharacterImport.Root + "CharacterLibrary.asset";
        private const string MaterialFolder = CharacterImport.Root + "Materials";

        [MenuItem("Wrestling/Build Character Library")]
        public static CharacterLibrary Build()
        {
            AssetDatabase.Refresh();
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
            {
                AssetDatabase.CreateFolder(CharacterImport.Root.TrimEnd('/'), "Materials");
            }

            CharacterLibrary library = AssetDatabase.LoadAssetAtPath<CharacterLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<CharacterLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            library.Male = BuildBase("male");
            library.Female = BuildBase("female");
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            Debug.Log($"Character library written to {LibraryPath}.");
            return library;
        }

        public static void BuildFromCommandLine()
        {
            try
            {
                Build();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static CharacterLibrary.CharacterBase BuildBase(string name)
        {
            string metaPath = CharacterImport.Root + $"wrestler_{name}.json";
            if (!File.Exists(metaPath))
            {
                throw new FileNotFoundException($"Run tools/characters/build_wrestlers.py first: {metaPath} is missing.");
            }

            JObject meta = JObject.Parse(File.ReadAllText(metaPath));
            string textures = CharacterImport.Textures + name + "/";
            Texture2D Texture(string file) =>
                AssetDatabase.LoadAssetAtPath<Texture2D>(textures + file) ?? throw new FileNotFoundException($"Texture {textures + file} is missing.");

            var result = new CharacterLibrary.CharacterBase
            {
                Name = name,
                Model = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterImport.Root + (string)meta["model"])
                    ?? throw new FileNotFoundException($"Model {(string)meta["model"]} is missing."),
                Body = Material($"Body_{name}", Shader.Find("Wrestling/Wrestler Skin"), _ => { }),
            };

            var skins = new List<CharacterLibrary.SkinSet>();
            foreach (JProperty skin in ((JObject)meta["skins"]).Properties())
            {
                var files = (JObject)skin.Value;
                string albedo = (string)files["diffuseTexture"] ?? throw new InvalidDataException($"{skin.Name}: no diffuse texture.");
                string normal = (string)files["normalmapTexture"];
                skins.Add(new CharacterLibrary.SkinSet
                {
                    Name = skin.Name,
                    Albedo = Texture(albedo),
                    Normal = normal != null ? Texture(normal) : null,
                });
            }

            result.Skins = skins.ToArray();
            var partNames = new List<string>();
            var partMaterials = new List<Material>();
            foreach (JObject part in (JArray)meta["parts"])
            {
                string objectName = (string)part["object"];
                string kind = (string)part["kind"];
                var files = (JObject)part["textures"];
                string baseColor = (string)files["baseColor"];
                string normal = (string)files["normal"];
                bool cutout = kind == "Eyebrows" || kind == "Eyelashes" || kind == "Hair";
                Material material = Material($"{kind}_{name}", Shader.Find("Universal Render Pipeline/Lit"), m =>
                {
                    if (baseColor != null)
                    {
                        m.SetTexture("_BaseMap", Texture(baseColor));
                    }

                    if (normal != null)
                    {
                        m.SetTexture("_BumpMap", Texture(normal));
                        m.EnableKeyword("_NORMALMAP");
                    }

                    m.SetFloat("_Smoothness", kind == "Eyes" ? 0.85f : kind == "Hair" ? 0.3f : 0.25f);
                    if (kind == "Clothes")
                    {
                        // Sneaker and sock tinted one colour read as a high-top wrestling shoe.
                        m.SetColor("_BaseColor", new Color(0.13f, 0.13f, 0.15f));
                    }
                    if (cutout)
                    {
                        m.SetFloat("_AlphaClip", 1f);
                        m.SetFloat("_Cutoff", 0.4f);
                        m.EnableKeyword("_ALPHATEST_ON");
                        m.SetFloat("_Cull", (float)CullMode.Off);
                        m.renderQueue = (int)RenderQueue.AlphaTest;
                    }
                });
                partNames.Add(objectName);
                partMaterials.Add(material);
            }

            result.PartNames = partNames.ToArray();
            result.PartMaterials = partMaterials.ToArray();
            return result;
        }

        private static Material Material(string materialName, Shader shader, Action<Material> configure)
        {
            if (shader == null)
            {
                throw new InvalidOperationException($"Shader for {materialName} not found.");
            }

            string path = $"{MaterialFolder}/{materialName}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            configure(material);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
