using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wrestling.Presentation;

namespace Wrestling.Editor
{
    /// <summary>Checks the actual constructed knee anchors and their passive stops under torque.</summary>
    public static class KneeJointChecks
    {
        public static void RunFromCommandLine()
        {
            try
            {
                EditorSceneManager.OpenScene(BoutViewerSceneBuilder.ScenePath, OpenSceneMode.Single);
                BoutViewer viewer = UnityEngine.Object.FindAnyObjectByType<BoutViewer>();
                viewer.Initialise(ContentLocator.LoadDirect());
                viewer.StartBout(1);
                PhysicalBody figure = viewer.RedFigure.Physical;
                ArticulationBody root = figure.Link("Hips");
                root.immovable = true;
                foreach (ArticulationBody part in root.GetComponentsInChildren<ArticulationBody>())
                {
                    part.useGravity = false;
                    foreach (Collider collider in part.GetComponentsInChildren<Collider>()) collider.enabled = false;
                    if (!part.isRoot)
                    {
                        foreach (ArticulationDriveAxis axis in new[] { ArticulationDriveAxis.X, ArticulationDriveAxis.Y, ArticulationDriveAxis.Z })
                        {
                            if (part.jointType == ArticulationJointType.RevoluteJoint && axis != ArticulationDriveAxis.X) continue;
                            part.SetDriveStiffness(axis, 0f);
                            part.SetDriveDamping(axis, 0f);
                        }
                    }
                }

                foreach (string side in new[] { "Left", "Right" })
                {
                    ArticulationBody thigh = figure.Link(side + "UpLeg");
                    ArticulationBody knee = figure.Link(side + "Leg");
                    ArticulationBody ankle = figure.Link(side + "Foot");
                    Vector3 axis = knee.transform.rotation * knee.anchorRotation * Vector3.right;
                    Vector3 parentAxis = thigh.transform.rotation * knee.parentAnchorRotation * Vector3.right;
                    Vector3 upper = knee.transform.position - thigh.transform.position;
                    Vector3 lower = ankle.transform.position - knee.transform.position;
                    if (knee.jointType != ArticulationJointType.RevoluteJoint || knee.twistLock != ArticulationDofLock.LimitedMotion || Vector3.Dot(axis, parentAxis) < 0.999f || Mathf.Abs(Vector3.Dot(axis, upper.normalized)) > 0.02f || Mathf.Abs(Vector3.Dot(axis, lower.normalized)) > 0.02f)
                        throw new InvalidOperationException(side + " knee anchor does not align with the limb's hinge plane.");
                    float stance = Vector3.Angle(upper, lower) - (knee.jointPosition[0] * Mathf.Rad2Deg);
                    if (Mathf.Abs(knee.xDrive.lowerLimit + stance + 5f) > 1f || Mathf.Abs(knee.xDrive.upperLimit + stance - 150f) > 1f)
                        throw new InvalidOperationException(side + " knee stops do not use the measured construction bend.");
                    foreach (float sign in new[] { 1f, -1f })
                    {
                        for (int step = 0; step < 500; step++)
                        {
                            knee.AddTorque((thigh.transform.rotation * knee.parentAnchorRotation * Vector3.right) * (30f * sign));
                            Physics.Simulate(0.005f);
                        }
                        float angle = knee.jointPosition[0] * Mathf.Rad2Deg;
                        float stop = sign > 0 ? knee.xDrive.upperLimit : knee.xDrive.lowerLimit;
                        if (Mathf.Abs(angle - stop) > 4f)
                            throw new InvalidOperationException($"{side} knee under torque: {angle:F1}°, expected stop {stop:F1}°.");
                    }
                }
                Debug.Log("[knee-checks] Both knee anchors align with the constructed limb planes; passive flexion and extension stops held under torque.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }
    }
}
