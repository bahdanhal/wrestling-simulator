# Realism review artifacts

Reviewed for the movement/proportion changes, 1 November 2026.

- `recorded-snap-camera-comparison.png` and `recorded-snap-native.mp4`: original Harmony4D cam01
  images beside aligned joint annotations and the character rig with ankle and wrist trajectory
  mapping. All 29 samples from take frames
  176–204 play at their native 20 fps. Camera/model viewpoints differ. Ankle-trajectory IK is
  enabled alongside wrist IK; unreachable targets and actual rig positions are retained in
  `recorded-snap-provenance.json`.
- `morphology-male.png` and `morphology-female.png`: source body and two same-height builds in bind
  and wrestling poses. Captions show leg ratio and wingspan. These inspect skeleton/skin fitting,
  not the quality of a wrestling interaction.
- `keyed-penetration-review.mp4` and `keyed-lock-review.mp4`: live production-content gameplay
  after the phase timing changes, with physical knees and pelvis-target diagnostic markers.
  Freestyle level 60; seeds 10 and 101, warm-up 1110 and 388 ticks respectively; 150 captured
  ticks at 30 fps, following continuous warm-up. The AI counters these entries, so they do not
  demonstrate an uninterrupted full technique. These still use keyed animation; they are
  review evidence, not accepted recordings of realistic technique or correctly preserved grips.

The camera source is [Harmony4D](https://huggingface.co/datasets/Jyun-Ting/Harmony4D), Rawal
Khirodkar, Jyun-Ting Song, Jinkun Cao, Zhengyi Luo and Kris Kitani (Carnegie Mellon University),
NeurIPS 2024, MIT. Original archive members and attribution are in the provenance file. Camera
images are resized and padded, without cropping or reconstructing their contents. Characters use
the project's MakeHuman CC0 bases.

The source floor is estimated from ankles; toes are synthesized and wrists are not palm-contact
anchors. These artifacts expose those limitations and do not establish full contact preservation
or expert acceptance of the technique. The [review workflow](../../research/motion-review.md)
explains how to reproduce them.
