"""Synthetic geometry checks independent of Harmony4D's stored annotations."""
import unittest
import tempfile
from pathlib import Path
import numpy as np
from harmony_camera import Camera, triangulate, quaternion_rotation, validate_affine, load_cameras, fit_plane


class CameraGeometryTests(unittest.TestCase):
    def setUp(self):
        params = [900, 910, 960, 540, 0.08, -0.02, 0.005, -0.001]
        first = np.eye(4)
        second = np.eye(4)
        second[0, 3] = -1.3
        self.cameras = {"cam01": Camera("cam01", params, first, 1), "cam02": Camera("cam02", params, second, 1)}

    def test_projection_ray_and_triangulation(self):
        point = np.array([0.3, -0.4, 4.5])
        observations = {name: camera.project(point).tolist() for name, camera in self.cameras.items()}
        for name, camera in self.cameras.items():
            origin, ray = camera.ray(observations[name])
            expected = point - origin
            expected /= np.linalg.norm(expected)
            np.testing.assert_allclose(ray, expected, atol=1e-10)
        result, errors = triangulate(self.cameras, observations)
        np.testing.assert_allclose(result, point, atol=1e-9)
        self.assertLess(max(view["residual_pixels"] for view in errors.values()), 1e-7)

    def test_rejects_parallel_rays_and_one_view(self):
        self.cameras["cam02"] = self.cameras["cam01"]
        with self.assertRaisesRegex(ValueError, "parallel"):
            triangulate(self.cameras, {"cam01": [960, 540], "cam02": [960, 540]})
        with self.assertRaisesRegex(ValueError, "two"):
            triangulate(self.cameras, {"cam01": [960, 540]})

    def test_rejects_nonfinite_and_degenerate_inputs(self):
        params = [900, 910, 960, 540, 0, 0, 0, 0]
        for value in (float("nan"), 0, -1):
            invalid = params.copy()
            invalid[0] = value
            with self.assertRaises(ValueError):
                Camera("cam01", invalid, np.eye(4), 1)
        for q in ([0, 0, 0, 0], [float("nan"), 0, 0, 0]):
            with self.assertRaises(ValueError):
                quaternion_rotation(q)
        for transform in (np.zeros((4, 4)), np.diag([1, 1, 1, 0])):
            with self.assertRaises(ValueError):
                validate_affine(transform, "test")
        with self.assertRaises(ValueError):
            self.cameras["cam01"].project([float("nan"), 1, 2])
        with self.assertRaises(ValueError):
            triangulate(self.cameras, {"cam01": [float("nan"), 3], "cam02": [1, 2]})

    def test_calibration_gap_and_range_require_explicit_override(self):
        with tempfile.TemporaryDirectory() as path:
            folder = Path(path)
            np.save(folder / "scale.npy", np.eye(4))
            (folder / "cameras.txt").write_text("1 OPENCV_FISHEYE 1920 1080 900 900 960 540 0 0 0 0\n")
            (folder / "images.txt").write_text(
                "1 1 0 0 0 0 0 0 1 cam01/00001.jpg\n\n"
                "2 1 0 0 0 0 0 0 1 cam01/00101.jpg\n\n")
            with self.assertRaisesRegex(ValueError, "gap"):
                load_cameras(folder, 50, ["cam01"])
            with self.assertRaisesRegex(ValueError, "outside"):
                load_cameras(folder, 102, ["cam01"])
            camera = load_cameras(folder, 102, ["cam01"], None)["cam01"]
            self.assertEqual(camera.calibration_frame_gap, 1)
            self.assertEqual(camera.calibration_range, (1, 101))

    def test_plane_orientation_and_degenerate_points(self):
        points = [[0, 0, 2], [1, 0, 2], [0, 1, 2], [1, 1, 2.01]]
        normal, offset, singular, residuals = fit_plane(points, [0, 0, 1])
        self.assertGreater(normal[2], 0.99)
        self.assertAlmostEqual(np.linalg.norm(normal), 1)
        self.assertLess(max(abs(residuals)), 0.01)
        self.assertEqual(len(singular), 3)
        self.assertAlmostEqual(normal @ np.mean(points, axis=0) + offset, 0)
        with self.assertRaisesRegex(ValueError, "collinear"):
            fit_plane([[0, 0, 0], [1, 0, 0], [2, 0, 0]], [0, 0, 1])
        with self.assertRaisesRegex(ValueError, "Up"):
            fit_plane(points, [0, 0, 0])


if __name__ == "__main__":
    unittest.main()
