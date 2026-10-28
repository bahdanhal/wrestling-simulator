using UnityEngine;

namespace Wrestling.Presentation
{
    /// <summary>
    /// A wrestler's generated body on the <see cref="Skeleton"/> rig, posed from a <see cref="Pose"/>
    /// every frame. Display only.
    /// </summary>
    public sealed class SkinnedFigure : MonoBehaviour
    {
        private readonly Vector3[] _positions = new Vector3[Skeleton.BoneCount];
        private readonly Quaternion[] _rotations = new Quaternion[Skeleton.BoneCount];
        private Transform[] _bones;
        private SkinnedMeshRenderer _renderer;
        private Mesh _mesh;
        private Material[] _materials;

        public BodyProportions Body { get; private set; }

        /// <summary>Builds the rig and renderer for a body. <paramref name="materials"/> are owned by the figure from now on.</summary>
        public void Build(BodyProportions body, BodyMesh bodyMesh, Material[] materials)
        {
            Release();
            Body = body;
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _bones = new Transform[Skeleton.BoneCount];
            for (int i = 0; i < Skeleton.BoneCount; i++)
            {
                var bone = new GameObject(((Bone)i).ToString()).transform;
                Bone? parent = Skeleton.ParentOf((Bone)i);
                bone.SetParent(parent == null ? transform : _bones[(int)parent.Value], false);
                _bones[i] = bone;
            }

            var meshObject = new GameObject("Body");
            meshObject.transform.SetParent(transform, false);
            _renderer = meshObject.AddComponent<SkinnedMeshRenderer>();
            _mesh = bodyMesh.Mesh;
            _materials = materials;
            _renderer.sharedMesh = _mesh;
            _renderer.bones = _bones;
            _renderer.rootBone = _bones[(int)Bone.Hips];
            _renderer.sharedMaterials = materials;
            _renderer.updateWhenOffscreen = true;
            _renderer.quality = SkinQuality.Bone4;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            Apply(PoseSolver.BindPose(body));
        }

        public void Apply(in Pose pose)
        {
            Skeleton.Compute(in pose, _positions, _rotations);
            for (int i = 0; i < Skeleton.BoneCount; i++)
            {
                _bones[i].SetPositionAndRotation(_positions[i], _rotations[i]);
            }
        }

        private void OnDestroy() => Release();

        private void Release()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                DestroyNow(transform.GetChild(i).gameObject);
            }

            if (_mesh != null)
            {
                DestroyNow(_mesh);
                _mesh = null;
            }

            if (_materials != null)
            {
                foreach (Material material in _materials)
                {
                    DestroyNow(material);
                }

                _materials = null;
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
