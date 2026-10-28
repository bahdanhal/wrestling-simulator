using UnityEngine;

namespace Wrestling.Presentation
{
    /// <summary>
    /// One wrestler on the mat: an instance of the base character model, scaled to their height,
    /// shaped by blend shapes for build and face, dressed in their corner's singlet, and posed
    /// every frame from the pose solver. Display only.
    /// </summary>
    public sealed class WrestlerCharacter : MonoBehaviour
    {
        private GameObject _model;
        private Material _body;
        private CharacterRig _rig;

        public BodyProportions Body => _rig.Body;

        public void Build(CharacterLibrary.CharacterBase characterBase, WrestlerLook look, Color singlet)
        {
            Release();
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;
            _model = Instantiate(characterBase.Model, transform, false);
            _model.name = characterBase.Name;

            SkinnedMeshRenderer bodyRenderer = null;
            float top = float.MinValue;
            float bottom = float.MaxValue;
            foreach (SkinnedMeshRenderer renderer in _model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                renderer.updateWhenOffscreen = true;
                top = Mathf.Max(top, renderer.bounds.max.y);
                bottom = Mathf.Min(bottom, renderer.bounds.min.y);
                Mesh mesh = renderer.sharedMesh;
                if (mesh.blendShapeCount > 0 && (bodyRenderer == null || mesh.vertexCount > bodyRenderer.sharedMesh.vertexCount))
                {
                    bodyRenderer = renderer;
                }
            }

            float modelHeight = top - bottom;
            transform.localScale = Vector3.one * (look.HeightMetres / modelHeight);

            CharacterLibrary.SkinSet skin = characterBase.Skins[Mathf.Clamp(look.Skin, 0, characterBase.Skins.Length - 1)];
            _body = new Material(characterBase.Body) { name = name + " body" };
            _body.SetTexture("_BaseMap", skin.Albedo);
            _body.SetTexture("_BumpMap", skin.Normal);
            _body.SetColor("_SingletColor", singlet);
            foreach (SkinnedMeshRenderer renderer in _model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer == bodyRenderer)
                {
                    var materials = new Material[renderer.sharedMesh.subMeshCount];
                    for (int m = 0; m < materials.Length; m++)
                    {
                        materials[m] = _body;
                    }

                    renderer.sharedMaterials = materials;
                    Mesh mesh = renderer.sharedMesh;
                    for (int i = 0; i < mesh.blendShapeCount; i++)
                    {
                        renderer.SetBlendShapeWeight(i, look.ShapeWeights.TryGetValue(mesh.GetBlendShapeName(i), out float weight) ? weight * 100f : 0f);
                    }

                    continue;
                }

                int part = System.Array.FindIndex(characterBase.PartNames, n => renderer.name.Contains(n));
                if (part >= 0)
                {
                    renderer.sharedMaterial = characterBase.PartMaterials[part];
                }
            }

            _rig = new CharacterRig(transform);
        }

        public void Apply(in Pose pose, float leftGrip, float rightGrip) => _rig.Apply(in pose, leftGrip, rightGrip);

        private void OnDestroy() => Release();

        private void Release()
        {
            if (_model != null)
            {
                DestroyNow(_model);
                _model = null;
            }

            if (_body != null)
            {
                DestroyNow(_body);
                _body = null;
            }
        }

        private static void DestroyNow(Object target)
        {
            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
