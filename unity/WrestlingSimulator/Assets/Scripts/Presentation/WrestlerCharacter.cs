using UnityEngine;

namespace Wrestling.Presentation
{
    /// <summary>
    /// One wrestler on the mat: an instance of the base character model, scaled to their height,
    /// shaped by blend shapes for build and face, dressed in their corner's singlet, and posed
    /// every frame from the pose solver. After posing, its twist bones and corrective shapes
    /// follow the final bones (<see cref="UpdateDeformation"/>). Display only.
    /// </summary>
    public sealed class WrestlerCharacter : MonoBehaviour
    {
        private static readonly int ExertionId = Shader.PropertyToID("_Exertion");
        private static readonly int FoldDriveId = Shader.PropertyToID("_FoldDrive");

        private GameObject _model;
        private Material _body;
        private SkinnedMeshRenderer _bodyRenderer;
        private FacialEffort _face;
        private CharacterRig _rig;
        private PhysicalBody _physical;
        private TwistBones _twist;
        private CorrectiveShapes _correctives;
        private float _exertion = -1f;
        private Vector4 _foldDrive = new Vector4(-1f, -1f, -1f, -1f);

        /// <summary>The wrestler's physically simulated body.</summary>
        public PhysicalBody Physical => _physical;

        public BodyProportions Body => _rig.Body;

        /// <summary>The rig the pose drives (for the post-physics pass on the shown bones).</summary>
        public CharacterRig Rig => _rig;

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
            if (skin.Normal != null)
            {
                _body.SetTexture("_BumpMap", skin.Normal);
                _body.SetFloat("_BumpScale", 0.6f);
            }
            else
            {
                _body.SetFloat("_BumpScale", 0f);
            }

            _body.SetColor("_SingletColor", singlet);
            _body.SetColor("_HairColor", look.ScalpColor);
            var propertyBlock = new MaterialPropertyBlock();
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
                    _bodyRenderer = renderer;
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
                    Material partMaterial = characterBase.PartMaterials[part];
                    renderer.sharedMaterial = partMaterial;
                    string lower = renderer.name.ToLowerInvariant();
                    if (lower.Contains("short") || lower.Contains("ponytail") || lower.Contains("hair") || lower.Contains("eyebrow"))
                    {
                        propertyBlock.Clear();
                        propertyBlock.SetColor("_BaseColor", look.HairTint);
                        renderer.SetPropertyBlock(propertyBlock);
                    }
                    else if (lower.Contains("shoes"))
                    {
                        propertyBlock.Clear();
                        propertyBlock.SetColor("_BaseColor", Color.Lerp(new Color(0.11f, 0.12f, 0.14f), singlet, 0.16f));
                        renderer.SetPropertyBlock(propertyBlock);
                    }
                }
            }

            _rig = new CharacterRig(transform);
            var bones = new System.Collections.Generic.Dictionary<string, Transform>(System.StringComparer.Ordinal);
            foreach (Transform t in _model.GetComponentsInChildren<Transform>(true))
            {
                bones[t.name.Contains(":") ? t.name.Substring(t.name.IndexOf(':') + 1) : t.name] = t;
            }

            Transform Find(string boneName) => bones.TryGetValue(boneName, out Transform bone) ? bone : null;
            _twist = new TwistBones(Find);
            _correctives = new CorrectiveShapes(Find, bodyRenderer);
            _face = new FacialEffort(bodyRenderer);
            bodyRenderer.quality = SkinQuality.Bone4;
            SetExertion(0f);

            // The physical joints are built in a wrestling stance, so their zero sits in the middle of
            // the range the wrestler works in (bent elbows and knees), far from where a joint's angle
            // wraps round.
            _rig.Apply(StancePose(_rig), 0f, 0f);
            UpdateDeformation();
            _physical = new PhysicalBody(_rig.Find, look.HeightMetres, look.MassKg, name);
        }

        /// <summary>
        /// Sets the twist bones and corrective shapes (and the singlet's fold strength) from the
        /// current final bones. Call after all posing and physics for the frame.
        /// </summary>
        public void UpdateDeformation()
        {
            if (_twist == null)
            {
                return;
            }

            _twist.Update();
            _correctives.Update();
            Vector4 drive = _correctives.FoldDrive;
            if ((drive - _foldDrive).sqrMagnitude > 1e-4f)
            {
                _foldDrive = drive;
                _body.SetVector(FoldDriveId, drive);
            }
        }

        /// <summary>
        /// How worked the wrestler is, 0 (fresh) to 1 (spent): builds sweat on the skin, dampens the
        /// singlet, and sets how hard they breathe between exertions (the breath advancing by
        /// <paramref name="deltaTime"/>).
        /// </summary>
        public void SetExertion(float exertion01, float deltaTime = 0f)
        {
            float exertion = Mathf.Clamp01(exertion01);
            _face?.Breathe(deltaTime, exertion);
            if (_body != null && Mathf.Abs(exertion - _exertion) > 1e-3f)
            {
                _exertion = exertion;
                _body.SetFloat(ExertionId, exertion);
            }
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

        public void Apply(in Pose pose, float leftGrip, float rightGrip)
        {
            _rig.Apply(in pose, leftGrip, rightGrip);
            ApplyExpression(in pose);
            UpdateDeformation();
        }

        public void ApplyExpression(in Pose pose)
        {
            float fatigue = Mathf.Max(0f, _exertion);
            float breath = _face?.BreathPhase ?? 0f;
            _rig.ApplyFace(in pose, fatigue, breath);
            _face?.Apply(pose.Focus, pose.Effort, fatigue);
        }

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
            _twist = null;
            _correctives = null;
            _face = null;
            _exertion = -1f;
            _foldDrive = new Vector4(-1f, -1f, -1f, -1f);
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
