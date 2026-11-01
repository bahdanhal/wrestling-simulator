"""Tests for image masking and robust diagnostic plane fitting."""
import unittest
import numpy as np
from harmony_scene_floor import in_polygon, robust_fit


class SceneFloorTests(unittest.TestCase):
    def test_concave_mask_excludes_notch(self):
        polygon = [[0, 0], [4, 0], [4, 4], [3, 4], [3, 2], [1, 2], [1, 4], [0, 4]]
        self.assertTrue(in_polygon([0.5, 3], polygon))
        self.assertFalse(in_polygon([2, 3], polygon))
        self.assertFalse(in_polygon([8, 1], polygon))

    def test_robust_plane_excludes_outlier(self):
        points = np.array([[0, 0, 1], [1, 0, 1], [0, 1, 1], [1, 1, 1],
                           [0.3, 0.4, 1], [0.4, 0.7, 4]])
        normal, offset, singular, included = robust_fit(points, [0, 0, 1], 0.01, 200)
        np.testing.assert_allclose(normal, [0, 0, 1], atol=1e-10)
        self.assertAlmostEqual(offset, -1)
        self.assertEqual(included.tolist(), [True, True, True, True, True, False])
        self.assertGreater(singular[1], 0)


if __name__ == "__main__":
    unittest.main()
