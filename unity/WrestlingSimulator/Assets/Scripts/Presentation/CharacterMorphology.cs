using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wrestling.Presentation
{
    /// <summary>
    /// Fits the instantiated bind skeleton to the athlete before the rig and articulation are
    /// measured. Bone translations deform the skin through its original bind matrices; segment
    /// scales stay unchanged and the resulting limb lengths are fixed throughout the bout.
    /// </summary>
    public static class CharacterMorphology
    {
        /// <returns>Measured extended fingertip-to-fingertip reach of the fitted bind skeleton and skin.</returns>
        public static float Apply(Transform root, WrestlerLook look)
        {
            var bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform bone in root.GetComponentsInChildren<Transform>(true))
            {
                string name = bone.name;
                int colon = name.IndexOf(':');
                bones[colon < 0 ? name : name.Substring(colon + 1)] = bone;
            }

            Transform B(string name) => bones.TryGetValue(name, out Transform bone)
                ? bone : throw new InvalidOperationException($"Character morphology requires bone '{name}'.");
            Transform hips = B("Hips");
            Transform neck = B("Neck");
            Transform leftFoot = B("LeftFoot");
            Transform rightFoot = B("RightFoot");
            Vector3 ankleMid = (leftFoot.position + rightFoot.position) * 0.5f;
            Vector3 neckPosition = neck.position;
            float legLength = 0.5f * (Length(B("LeftUpLeg"), B("LeftLeg")) + Length(B("LeftLeg"), leftFoot)
                + Length(B("RightUpLeg"), B("RightLeg")) + Length(B("RightLeg"), rightFoot));
            float legScale = look.HeightMetres * look.LegRatio / legLength;
            ScaleSegment(B("LeftUpLeg"), B("LeftLeg"), legScale);
            ScaleSegment(B("LeftLeg"), leftFoot, legScale);
            ScaleSegment(B("RightUpLeg"), B("RightLeg"), legScale);
            ScaleSegment(B("RightLeg"), rightFoot, legScale);
            // Keep both shoe soles at their original floor, moving the hips with the new legs.
            hips.position += ankleMid - ((leftFoot.position + rightFoot.position) * 0.5f);

            // Keep the upper chest, clavicles and neck as one girdle. Changing the neck offset
            // would raise/lower the shoulders relative to the head, creating a permanent shrug.
            Transform[] spine = { B("Spine"), B("Spine1"), B("Spine2") };
            float spineLength = 0f;
            foreach (Transform bone in spine)
            {
                spineLength += Length(bone.parent, bone);
            }

            Vector3 compensation = neckPosition - neck.position;
            // Redistribute torso length along the spine while leaving the head/neck at the same
            // standing height; a longer-legged athlete consequently has the shorter torso.
            float[] shares = new float[spine.Length];
            for (int i = 0; i < spine.Length; i++)
            {
                shares[i] = Length(spine[i].parent, spine[i]) / spineLength;
            }

            for (int i = 0; i < spine.Length; i++)
            {
                spine[i].position += compensation * shares[i];
            }

            Transform armL = B("LeftArm");
            Transform armR = B("RightArm");
            Transform elbowL = B("LeftForeArm");
            Transform elbowR = B("RightForeArm");
            Transform handL = B("LeftHand");
            Transform handR = B("RightHand");
            SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            float reach = 0f;
            // Wrist skin may share weights with the forearm: changing its length can move the
            // fingertip reach slightly too. Re-measure the deformed skin while fitting rather than
            // assuming that the source hand's extent remains unchanged.
            for (int fit = 0; fit < 6; fit++)
            {
                float hands = HandLength(renderers, handL, B("LeftHandMiddle3"))
                    + HandLength(renderers, handR, B("RightHandMiddle3"));
                float arms = Length(armL, elbowL) + Length(elbowL, handL) + Length(armR, elbowR) + Length(elbowR, handR);
                reach = Length(armL, armR) + arms + hands;
                if (Mathf.Abs(reach - look.WingspanMetres) < 0.0005f)
                {
                    break;
                }

                float armScale = (look.WingspanMetres - Length(armL, armR) - hands) / arms;
                if (armScale <= 0f || float.IsNaN(armScale) || float.IsInfinity(armScale))
                {
                    throw new InvalidOperationException("Wrestler wingspan is shorter than the character's shoulders and hands.");
                }

                ScaleSegment(armL, elbowL, armScale);
                ScaleSegment(elbowL, handL, armScale);
                ScaleSegment(armR, elbowR, armScale);
                ScaleSegment(elbowR, handR, armScale);
            }

            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                var baked = new Mesh();
                renderer.BakeMesh(baked);
                renderer.localBounds = baked.bounds;
                Release(baked);
            }

            // Measure again after deformation rather than reporting the requested profile value.
            float fittedSpan = Length(armL, armR) + Length(armL, elbowL) + Length(elbowL, handL)
                + Length(armR, elbowR) + Length(elbowR, handR)
                + HandLength(renderers, handL, B("LeftHandMiddle3"))
                + HandLength(renderers, handR, B("RightHandMiddle3"));
            if (Mathf.Abs(fittedSpan - look.WingspanMetres) >= 0.0005f)
            {
                throw new InvalidOperationException($"Character morphology could not fit wingspan {look.WingspanMetres:F4} m (measured {fittedSpan:F4} m).");
            }

            return fittedSpan;
        }

        private static float Length(Transform a, Transform b) => Vector3.Distance(a.position, b.position);

        private static void ScaleSegment(Transform proximal, Transform distal, float scale)
        {
            distal.position = proximal.position + ((distal.position - proximal.position) * scale);
            // Imported twist bones sit halfway down the segment and must keep that location.
            foreach (Transform child in proximal)
            {
                if (child.name.EndsWith(TwistBones.Suffix, StringComparison.Ordinal))
                {
                    child.localPosition *= scale;
                }
            }
        }

        /// <summary>Wrist-to-fingertip reach measured on the skinned hand, including its final phalanx.</summary>
        private static float HandLength(SkinnedMeshRenderer[] renderers, Transform hand, Transform middle)
        {
            Vector3 axis = (middle.position - hand.position).normalized;
            float length = 0f;
            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                Transform[] meshBones = renderer.bones;
                var onHand = new bool[meshBones.Length];
                bool any = false;
                for (int i = 0; i < meshBones.Length; i++)
                {
                    onHand[i] = meshBones[i] != null && (meshBones[i] == hand || meshBones[i].IsChildOf(hand));
                    any |= onHand[i];
                }

                if (!any)
                {
                    continue;
                }

                var baked = new Mesh();
                renderer.BakeMesh(baked);
                Vector3[] vertices = baked.vertices;
                BoneWeight[] weights = renderer.sharedMesh.boneWeights;
                for (int v = 0; v < vertices.Length; v++)
                {
                    BoneWeight w = weights[v];
                    float share = (onHand[w.boneIndex0] ? w.weight0 : 0f) + (onHand[w.boneIndex1] ? w.weight1 : 0f)
                        + (onHand[w.boneIndex2] ? w.weight2 : 0f) + (onHand[w.boneIndex3] ? w.weight3 : 0f);
                    if (share > 0.5f)
                    {
                        length = Mathf.Max(length, Vector3.Dot(renderer.transform.TransformPoint(vertices[v]) - hand.position, axis));
                    }
                }

                Release(baked);
            }

            if (length <= 0f)
            {
                throw new InvalidOperationException("Character morphology could not measure the skinned hand's fingertip reach.");
            }

            return length;
        }

        private static void Release(Mesh mesh)
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(mesh);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
    }
}
