# Physics imitation training

Trains a controller that drives two physically simulated wrestlers' muscles to reproduce a recorded
two-person take (motion capture or motion extracted from video). The simulation is plain physics
(gravity, contact with the mat and each other, muscle force limits), so whatever the controller learns
is physically possible; the recording supplies the natural style. This is the DeepMimic approach
applied to two bodies in contact.

## Pipeline

1. **Reference** (`reference.py`): a take's 17 joint positions per actor are resampled to 30 Hz and
   fitted to joint angles by physics-based inverse kinematics (weightless bodies pulled onto the
   recorded positions).
2. **Model** (`wrestlers.py`): two humanoids sized to the recorded people, with PD muscles whose force
   limits stand in for strength; the wrestlers collide with each other and the mat, not themselves.
3. **Environment** (`env.py`): one controller sets both wrestlers' muscle targets (reference angles +
   learned offsets); reward for matching pose, velocity, keypoints and root; episodes start at random
   reference frames and end when a body drifts away.
4. **Training** (`train.py`): PPO.
5. **Export** (`train.py --export`): rolls the trained controller out and writes the simulated motion
   as a new take, which the game plays through its recorded-motion pipeline.

## Setup

```bash
python3.12 -m venv tools/imitation/.venv
tools/imitation/.venv/bin/pip install -r tools/imitation/requirements.txt
```

## Use

```bash
cd tools/imitation
.venv/bin/python train.py --take ../../data/motion/takes/<take>.json --iterations 2000 --out runs/<name>
.venv/bin/python train.py --take ../../data/motion/takes/<take>.json --export runs/<name>/policy.pt --out-take ../../data/motion/takes
```

A laptop CPU is enough to check that everything runs. A real throw needs hours to days of training on
a GPU machine (`--device cuda`, more `--envs`).

Takes made by the game's motion recorder (`Wrestling.Editor.MotionRecorder`) are fine for testing this
pipeline but are not real motion; only takes from real footage or capture should be trained for the game.
