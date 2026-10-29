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
        private PhysicalBody _physical;

        /// <summary>The wrestler's physically simulated body.</summary>
        public PhysicalBody Physical => _physical;

        public BodyProportions Body => _rig.Body;

        /// <summary>The wrestler's standing height in metres.</summary>
        public float Height { get; private set; }

        public void Build(CharacterLibrary.CharacterBase characterBase, WrestlerLook look, Color singlet)
        {
            Release();
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;
            Height = look.HeightMetres;
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
            // The physical joints are built in a wrestling stance, so their zero sits in the middle of
            // the range the wrestler works in (bent elbows and knees), far from where a joint's angle
            // wraps round.
            _rig.Apply(StancePose(_rig), 0f, 0f);
            _physical = new PhysicalBody(_rig.Find, look.HeightMetres, look.MassKg, name);
        }

        /// <summary>A neutral standing stance at the model's place and facing: knees bent, arms ready in front.</summary>
        private static Pose StancePose(CharacterRig rig)
        {
            Vector3 forward3 = Vector3.ProjectOnPlane(rig.Bind.ChestNormal, Vector3.up).normalized;
            var forward = new Vector2(forward3.x, forward3.z);
            var left = new Vector2(-forward.y, forward.x);
            Vector3 pelvis = rig.Bind.Pelvis;
            var position = new Vector2(pelvis.x, pelvis.z);
            const float baseWidth = 0.5f;
            var frame = new WrestlerFrame
            {
                Position = position,
                Facing = forward,
                LeftFoot = position + (left * (baseWidth * 0.5f)),
                RightFoot = position - (left * (baseWidth * 0.5f)),
                Level = 0.7f,
                Posture = 0.6f,
                Balance = 1f,
                Ground = Wrestling.Core.Match.GroundState.Standing,
                RollSide = 1f,
            };
            Pose pose = PoseSolver.SolveBody(frame, rig.Body, PoseRole.Neutral);
            PoseSolver.SolveArms(ref pose, rig.Body, PoseRole.Neutral, frame, default, System.Array.Empty<Wrestling.Core.Match.Grip>());
            return pose;
        }

        /// <summary>Current world positions of the motion-take joints, read from the rig (after physics).</summary>
        public Vector3[] JointPositions()
        {
            Vector3 P(string bone) => _rig.Find(bone).position;
            Vector3 neck = P("Neck");
            Vector3 headBase = P("Head");
            return new[]
            {
                (P("LeftUpLeg") + P("RightUpLeg")) * 0.5f,
                neck,
                headBase + ((headBase - neck).normalized * ((headBase - neck).magnitude * 0.6f)),
                P("LeftArm"), P("RightArm"),
                P("LeftForeArm"), P("RightForeArm"),
                P("LeftHand"), P("RightHand"),
                P("LeftUpLeg"), P("RightUpLeg"),
                P("LeftLeg"), P("RightLeg"),
                P("LeftFoot"), P("RightFoot"),
                P("LeftToeBase"), P("RightToeBase"),
            };
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

            _physical?.Destroy();
            _physical = null;
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
