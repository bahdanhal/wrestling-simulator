# Harmony4D motion tools

Real two-person grappling motion from [Harmony4D](https://huggingface.co/datasets/Jyun-Ting/Harmony4D) (Khirodkar, Song, Cao, Luo, Kitani; Carnegie Mellon; NeurIPS 2024; MIT). About 20 synchronised cameras, 3D keypoints of both people at 20 fps. Licence and content notes: `docs/research/footage-sources.md`. What the data shows against our keyed clips: `docs/research/real-motion-comparison.md`.

Use a separate virtual environment (`pip install -r requirements.txt`). Put downloads and outputs outside the repository.

## 1. Fetch the 3D poses only

The archives are 2–44 GB of images each, but only the pose files are fetched (byte ranges, one request per sequence, about 31 MB for all grappling and MMA):

```bash
python harmony_fetch.py OUT/train train/02_grappling.zip train/03_grappling2.zip train/12_mma.zip train/13_mma2.zip train/14_mma3.zip train/15_mma4.zip
```

```bash
python harmony_fetch.py OUT/test test/03_grappling2.zip test/15_mma4.zip test/16_mma5.zip
```

Each pose `.npy` is a pickled Python dict. It is never unpickled: `harmony_fetch.py` reads the arrays out of the pickle stream's bytes, refusing any reference beyond numpy's array types, and honours each array's memory-order flag (the archives differ).

## 2. Find situations

```bash
python harmony_events.py "OUT/*/*/*.npz" OUT/events.json
```

Detects stance, collar ties, body locks, level changes, shots (the shooter's own hips travel in), snap-downs and takedowns from the joints. Every floor is fitted per sequence (`harmony_world.py`): each sequence has its own reconstruction axes.

## 3. Check them by eye

```bash
python harmony_render.py OUT/events.json shot OUT/shots.png 9
```

Stick figures side-on to the line between the bodies; actor 0 red, actor 1 blue. Detections are candidates only: a wrestler driven down by the other also drops and travels, so confirm each segment against the camera images (`exo/camNN/images/NNNNN.jpg` in the same archive, frame number from the `.npz`'s `frames`) before using it. Checked segments are listed in `segments.json`.

## 4. Make a take

```bash
python harmony_take.py OUT/train/02_grappling/001_grappling.npz OUT/001.json
```

Prints both people's heights (from their own bone lengths) and the frame count. Then import it with `motion-import --keypoints` and the heights, with the Harmony4D attribution and `--source-license MIT` (see the takes in `data/motion/takes/` for the exact source fields), and assign a checked segment with `motion-assign`.

## 5. Compare with the keyed clips

```bash
python harmony_compare.py REPO OUT OUT/events.json
```

For each segment in `segments.json`: hip heights (as shares of stature), trunk lean and the distance between the bodies, real against keyed, and how much faster than real the engine's phase plays it; then depth, timing and speed over all real shots and level changes.
