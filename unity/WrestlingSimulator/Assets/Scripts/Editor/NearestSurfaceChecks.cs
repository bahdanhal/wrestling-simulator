using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wrestling.Presentation;

namespace Wrestling.Editor
{
    public static class NearestSurfaceChecks
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
                var head = figure.Link("Head").GetComponentInChildren<SphereCollider>();
                Vector3 centre = head.transform.TransformPoint(head.center);
                var surface = figure.NearestSurface(centre, new[] { "Head" });
                Require(surface.Body == figure.Link("Head") && Mathf.Abs(surface.Distance - head.radius) < 1e-4f,
                    "Head centre did not project onto its spherical boundary.");
                var volume = figure.Nearest(centre, new[] { "Head" });
                Require(volume.Distance < 1e-6f && Vector3.Distance(volume.Point, centre) < 1e-6f,
                    "The existing volume-distance query changed.");
                Vector3 outside = centre + (head.transform.right * (head.radius * 2f));
                surface = figure.NearestSurface(outside, new[] { "Head" });
                Require(Mathf.Abs(surface.Distance - head.radius) < 1e-4f, "Exterior head distance is incorrect.");

                var foot = figure.Link("LeftFoot").GetComponentInChildren<BoxCollider>();
                foot.transform.rotation = Quaternion.Euler(23f, 37f, 12f);
                Vector3 footCentre = foot.transform.TransformPoint(foot.center);
                surface = figure.NearestSurface(footCentre, new[] { "LeftFoot" });
                float expected = .5f * Mathf.Min(foot.size.x, Mathf.Min(foot.size.y, foot.size.z));
                Require(surface.Body == figure.Link("LeftFoot") && Mathf.Abs(surface.Distance - expected) < 1e-4f,
                    "Rotated box centre did not project to its nearest face.");
                Physics.SyncTransforms();
                volume = figure.Nearest(footCentre, new[] { "LeftFoot" });
                Require(volume.Distance < 1e-5f, "Box volume-distance query changed.");
                Vector3 footOutside = foot.transform.TransformPoint(foot.center + new Vector3(foot.size.x, 0f, 0f));
                surface = figure.NearestSurface(footOutside, new[] { "LeftFoot" });
                Require(Mathf.Abs(surface.Distance - (foot.size.x * .5f)) < 1e-4f, "Exterior box distance is incorrect.");

                var calf = figure.Link("LeftLeg").GetComponentInChildren<CapsuleCollider>();
                Vector3 capsuleCentre = calf.transform.TransformPoint(calf.center);
                surface = figure.NearestSurface(capsuleCentre, new[] { "LeftLeg" });
                Require(surface.Body == figure.Link("LeftLeg") && Mathf.Abs(surface.Distance - calf.radius) < 1e-4f,
                    "Capsule axis did not project to a boundary.");
                surface = figure.NearestSurface(centre, new[] { "LeftFoot" });
                Require(surface.Body == figure.Link("LeftFoot"), "Surface query ignored the allowed segment list.");
                Debug.Log("[nearest-surface] PASS interior sphere, capsule and rotated box; exterior, allowed parts and unchanged volume query.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
