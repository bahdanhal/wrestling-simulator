"""Two physically simulated wrestlers as a MuJoCo model.

Each wrestler is a humanoid sized from a standing height: pelvis (free root), abdomen, chest,
head, upper arms, forearms, thighs, shins and feet, with ball joints as three hinges and position
actuators (PD muscles) whose force limits stand in for strength. Masses follow standard body
segment proportions of the wrestler's weight. The two wrestlers collide with each other and the mat,
not with themselves. Z is up.

The keypoint sites match the game's 17 motion-take joints (Wrestling.Data.Motion.MotionJoint).
"""
from __future__ import annotations

from dataclasses import dataclass

JOINTS = [
    "Pelvis", "Neck", "Head", "ShoulderL", "ShoulderR", "ElbowL", "ElbowR", "HandL", "HandR",
    "HipL", "HipR", "KneeL", "KneeR", "AnkleL", "AnkleR", "ToeL", "ToeR",
]

# Segment mass shares (Dempster/Winter style, rounded), per body.
MASS = {
    "pelvis": 0.142, "abdomen": 0.139, "chest": 0.216, "head": 0.081,
    "upper_arm": 0.028, "forearm": 0.022, "thigh": 0.10, "shin": 0.0465, "foot": 0.0145,
}

# Joint: (hinge axes, range in degrees per axis, muscle strength N·m per axis at 75 kg).
BALL = ("x", "y", "z")


@dataclass
class Proportions:
    height: float
    mass: float

    @property
    def leg(self) -> float:
        return self.height * 0.47

    @property
    def thigh(self) -> float:
        return self.leg * 0.52

    @property
    def shin(self) -> float:
        return self.leg * 0.48

    @property
    def torso(self) -> float:
        return self.height * 0.30

    @property
    def shoulder(self) -> float:
        return self.height * 0.115

    @property
    def hip(self) -> float:
        return self.height * 0.055

    @property
    def upper_arm(self) -> float:
        return self.height * 0.17

    @property
    def forearm(self) -> float:
        return self.height * 0.15

    @property
    def neck(self) -> float:
        return self.height * 0.04

    @property
    def head_radius(self) -> float:
        return self.height * 0.065


def _hinges(prefix: str, axes: str, ranges, strengths, scale: float):
    """Hinge joints and their actuators for one anatomical joint."""
    joints = []
    actuators = []
    vectors = {"x": "1 0 0", "y": "0 1 0", "z": "0 0 1"}
    for axis, (low, high), strength in zip(axes, ranges, strengths):
        name = f"{prefix}_{axis}"
        joints.append(f'<joint name="{name}" type="hinge" axis="{vectors[axis]}" range="{low} {high}" damping="{2 * scale:.2f}" armature="0.02"/>')
        force = strength * scale
        actuators.append(f'<position name="{name}" joint="{name}" kp="{force * 4:.1f}" kv="{force * 0.25:.2f}" forcerange="{-force:.1f} {force:.1f}" ctrlrange="{low * 0.01745:.3f} {high * 0.01745:.3f}"/>')
    return joints, actuators


def humanoid(name: str, p: Proportions, x: float, y: float, yaw_deg: float, contype: int, conaffinity: int):
    """MJCF body tree and actuators for one wrestler standing at (x, y) facing yaw (deg, 0 = +x)."""
    s = p.mass / 75.0
    g = f'contype="{contype}" conaffinity="{conaffinity}" condim="3" friction="0.9 0.02 0.01"'
    acts = []

    def m(part):
        return f'mass="{MASS[part] * p.mass:.3f}"'

    def joint(prefix, axes, ranges, strengths):
        j, a = _hinges(f"{name}_{prefix}", axes, ranges, strengths, s)
        acts.extend(a)
        return "\n".join(j)

    hip_h = 0.07 + p.leg
    t = p.torso
    sh = p.shoulder
    leg_sites = lambda side, sx: f'''
          <body name="{name}_thigh_{side}" pos="0 {sx * p.hip:.3f} 0">
            {joint(f"hip_{side}", BALL, [(-120, 40), (-45, 45), (-40, 40)], [260, 200, 120])}
            <site name="{name}_Hip{side}" pos="0 0 0" size="0.01"/>
            <geom type="capsule" fromto="0 0 0 0 0 {-p.thigh:.3f}" size="{p.height * 0.045:.3f}" {m("thigh")} {g}/>
            <body name="{name}_shin_{side}" pos="0 0 {-p.thigh:.3f}">
              {joint(f"knee_{side}", "y", [(0, 150)], [220])}
              <site name="{name}_Knee{side}" pos="0 0 0" size="0.01"/>
              <geom type="capsule" fromto="0 0 0 0 0 {-p.shin:.3f}" size="{p.height * 0.032:.3f}" {m("shin")} {g}/>
              <body name="{name}_foot_{side}" pos="0 0 {-p.shin:.3f}">
                {joint(f"ankle_{side}", "yx", [(-45, 45), (-25, 25)], [120, 60])}
                <site name="{name}_Ankle{side}" pos="0 0 0" size="0.01"/>
                <site name="{name}_Toe{side}" pos="{p.height * 0.11:.3f} 0 -0.05" size="0.01"/>
                <geom type="box" pos="{p.height * 0.04:.3f} 0 -0.045" size="{p.height * 0.07:.3f} {p.height * 0.03:.3f} 0.025" {m("foot")} {g}/>
              </body>
            </body>
          </body>'''
    arm = lambda side, sy: f'''
            <body name="{name}_upper_arm_{side}" pos="0 {sy * sh:.3f} {t * 0.45 - 0.03:.3f}">
              {joint(f"shoulder_{side}", BALL, [(-180, 60), (-100, 100), (-90, 90)], [110, 110, 70])}
              <site name="{name}_Shoulder{side}" pos="0 0 0" size="0.01"/>
              <geom type="capsule" fromto="0 0 0 0 0 {-p.upper_arm:.3f}" size="{p.height * 0.028:.3f}" {m("upper_arm")} {g}/>
              <body name="{name}_forearm_{side}" pos="0 0 {-p.upper_arm:.3f}">
                {joint(f"elbow_{side}", "y", [(-150, 0)], [80])}
                <site name="{name}_Elbow{side}" pos="0 0 0" size="0.01"/>
                <site name="{name}_Hand{side}" pos="0 0 {-p.forearm:.3f}" size="0.01"/>
                <geom type="capsule" fromto="0 0 0 0 0 {-p.forearm:.3f}" size="{p.height * 0.023:.3f}" {m("forearm")} {g}/>
                <geom type="sphere" pos="0 0 {-p.forearm - 0.03:.3f}" size="{p.height * 0.025:.3f}" mass="{0.006 * p.mass:.3f}" {g}/>
              </body>
            </body>'''
    body = f'''
    <body name="{name}_pelvis" pos="{x:.3f} {y:.3f} {hip_h:.3f}" euler="0 0 {yaw_deg:.1f}">
      <freejoint name="{name}_root"/>
      <site name="{name}_Pelvis" pos="0 0 0" size="0.01"/>
      <geom type="capsule" fromto="0 {-p.hip:.3f} 0.02 0 {p.hip:.3f} 0.02" size="{p.height * 0.06:.3f}" {m("pelvis")} {g}/>
      {leg_sites("L", 1)}
      {leg_sites("R", -1)}
      <body name="{name}_abdomen" pos="0 0 {t * 0.12:.3f}">
        {joint("spine", BALL, [(-30, 30), (-30, 80), (-40, 40)], [300, 300, 200])}
        <geom type="capsule" fromto="0 0 0 0 0 {t * 0.4:.3f}" size="{p.height * 0.058:.3f}" {m("abdomen")} {g}/>
        <body name="{name}_chest" pos="0 0 {t * 0.4:.3f}">
          {joint("chest", BALL, [(-25, 25), (-20, 45), (-30, 30)], [300, 300, 200])}
          <geom type="capsule" fromto="0 {-sh * 0.55:.3f} {t * 0.25:.3f} 0 {sh * 0.55:.3f} {t * 0.25:.3f}" size="{p.height * 0.063:.3f}" {m("chest")} {g}/>
          <site name="{name}_Neck" pos="0 0 {t * 0.48:.3f}" size="0.01"/>
          <body name="{name}_head" pos="0 0 {t * 0.48:.3f}">
            {joint("neck", BALL, [(-40, 40), (-45, 60), (-70, 70)], [90, 90, 60])}
            <site name="{name}_Head" pos="0 0 {p.neck + p.head_radius:.3f}" size="0.01"/>
            <geom type="sphere" pos="0 0 {p.neck + p.head_radius:.3f}" size="{p.head_radius:.3f}" {m("head")} {g}/>
          </body>
          {arm("L", 1)}
          {arm("R", -1)}
        </body>
      </body>
    </body>'''
    return body, acts


def model_xml(heights, masses, ik: bool = False) -> str:
    """Two wrestlers facing each other. With ik=True: no gravity, no actuators, and every keypoint
    site connected to a mocap target body (used to fit joint angles to recorded positions)."""
    bodies = []
    actuators = []
    for i, (name, x, yaw, contype, conaffinity) in enumerate((("a", -0.45, 0.0, 2, 5), ("b", 0.45, 180.0, 4, 3))):
        b, a = humanoid(name, Proportions(heights[i], masses[i]), x, 0.0, yaw, contype, conaffinity)
        bodies.append(b)
        actuators.extend(a)
    mocap = ""
    equality = ""
    if ik:
        targets = []
        connects = []
        for name in ("a", "b"):
            for joint in JOINTS:
                targets.append(f'<body name="target_{name}_{joint}" mocap="true" pos="0 0 1"><site name="target_{name}_{joint}" size="0.01"/></body>')
                connects.append(f'<weld site1="{name}_{joint}" site2="target_{name}_{joint}" torquescale="0" solref="0.02 1"/>')
        mocap = "\n".join(targets)
        equality = "<equality>" + "\n".join(connects) + "</equality>"
    gravity = "0 0 0" if ik else "0 0 -9.81"
    xml = f'''<mujoco model="wrestlers">
  <compiler angle="degree" autolimits="true"/>
  <option timestep="0.002" gravity="{gravity}" integrator="implicitfast"/>
  <default><geom solref="0.004 1" solimp="0.95 0.99 0.001"/></default>
  <worldbody>
    <geom name="mat" type="plane" size="6 6 0.1" contype="1" conaffinity="6" friction="0.9 0.02 0.01"/>
    {"".join(bodies)}
    {mocap}
  </worldbody>
  {equality}
  {"" if ik else "<actuator>" + chr(10).join(actuators) + "</actuator>"}
</mujoco>'''
    return xml
