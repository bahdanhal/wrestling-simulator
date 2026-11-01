# Olympic Wrestling Simulator: Game Design Document

| | |
|---|---|
| Version | 1.0 (2026-10-27) |
| Rules reference | UWW International Wrestling Rules, January 2026 edition (Greco-Roman, Freestyle, Women's) |
| Engine | Unity (C#) + engine-independent `Wrestling.Core` |
| Platforms | iOS, Android (primary); macOS, Windows |

This document describes the full product. There is no MVP: every system described here ships as a complete, data-driven, production-quality implementation (see `CLAUDE.md`).

---

## Contents

1. Vision and pillars
2. Game modes and player roles
3. Rules model
4. Wrestler model
5. Match engine
6. Techniques and counters
7. Match controls (Play, Coach, Sim)
8. Career mode
9. World simulation
10. Tournaments and competition structure
11. Presentation (3D, camera, animation, commentary)
12. UI and screens
13. Data formats
14. Technical architecture
15. Balancing and validation
16. Open items to verify

---

## 1. Vision and pillars

**Vision:** the most authentic wrestling game ever made, playable on a phone. You build a wrestler from nothing, manage every week of their life, and wrestle the bouts yourself with gestures, coach them from the corner, or simulate them.

**Pillars**

1. **Authentic.** Real UWW rules, real scoring logic, real competition formats, real training and weight-cutting trade-offs. A wrestling coach should recognise what happens on the mat.
2. **Playable.** Realism never makes the game tedious or opaque. Every outcome can be explained (why a shot failed, why passivity was called).
3. **One engine, three views.** Play, Coach and Sim run the same simulation. A result never depends on which view you chose, only on decisions and input quality.
4. **Emergent bouts.** Counters, re-counters and scrambles come from the state of the bodies, not from scripted sequences.
5. **A life in the sport.** The career runs from youth wrestler to Olympic hopeful to coach to federation leader.

---

## 2. Game modes and player roles

### 2.1 Modes

| Mode | Description |
|---|---|
| **Career** | Main mode. Create a wrestler, live their career week by week, then continue as coach and federation manager. |
| **Coach Career** | Start directly as a club or national coach, managing a roster. |
| **Federation** | Run a national federation: budget, programs, selection, Olympic quotas. |
| **Quick Bout** | Any two wrestlers (created or from the world database), any style, weight class and ruleset. Play, Coach or Sim. |
| **Tournament** | Run a full single tournament (Worlds, Olympics, Ranking Series event) with any participants. |
| **Training Room** | Practise techniques and gestures against a configurable partner, with feedback on input quality. |
| **Replay Theatre** | Watch saved bouts with free camera, slow motion and an event timeline. |

### 2.2 Roles across one career arc

1. **Wrestler** (age 14 to retirement, typically 30–36). Training, weight, competition, selection.
2. **Coach** (after retirement, or from the start in Coach Career). Club roster, training plans for several wrestlers, corner coaching, recruiting.
3. **Federation manager** (late career, or from the start). National-level strategy.

Each role reuses the systems of the previous one. A coach writes training plans with the same planner the wrestler used; a federation manager selects teams from the same rankings.

---

## 3. Rules model

All rules are data (`rulesets/*.json`, section 13). The shipped default is **`uww-2026-01`**, built from the January 2026 UWW rulebook. Values below are the content of that ruleset. Older or custom rulesets can be added without code changes.

### 3.1 Styles

| Style | Key legality rules |
|---|---|
| **Greco-Roman (GR)** | No grasping below the belt line, no tripping, no active use of the legs (pushing, pressing, lifting, squeezing with legs). No treading on the opponent's feet. Bottom wrestler in par terre may not bend or raise legs to block a hold. |
| **Freestyle (FS)** | Legs may be attacked; trips and active use of legs allowed. Scissor lock with crossed feet on head, neck or body forbidden. |
| **Women's (WW)** | Freestyle rules, plus **all double nelsons forbidden** (standing and par terre). |

Illegal holds in all styles (Art. 50) are part of the ruleset: throat hold, arm twist over 90°, forearm arm lock, two hands on head or neck, chancery (front headlock pressure) hold, head-only holds, reverse waist hold thrown top-to-bottom, slamming a bridged opponent, breaking the bridge toward the head, double nelson unless from the side without legs. U15 and U17 also forbid all double nelsons.

### 3.2 Age categories and bout duration

| Category | Age | Bout |
|---|---|---|
| U15 | 14–15 | 2 × 2 min, 30 s break |
| U17 | 16–17 | 2 × 2 min, 30 s break |
| U20 | 18–20 | 2 × 3 min, 30 s break |
| U23 | 19–23 | 2 × 3 min, 30 s break |
| Senior | 20+ | 2 × 3 min, 30 s break |
| Veterans | 35+ | 2 × 2 min, 30 s break |

Score is cumulative across both periods. Minimum 20 minutes rest between a wrestler's bouts. A U20 wrestler may enter senior events unless aged 17 that year.

### 3.3 Weight categories (kg)

**Senior, U23, U20**

| | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
|---|---|---|---|---|---|---|---|---|---|---|
| FS | 57 | 61 | 65 | 70 | 74 | 79 | 86 | 92 | 97 | 125 |
| GR | 55 | 60 | 63 | 67 | 72 | 77 | 82 | 87 | 97 | 130 |
| WW | 50 | 53 | 55 | 57 | 59 | 62 | 65 | 68 | 72 | 76 |

**Olympic Games (and Olympic qualifiers)**

| FS | 57, 65, 74, 86, 97, 125 |
|---|---|
| GR | 60, 67, 77, 87, 97, 130 |
| WW | 50, 53, 57, 62, 68, 76 |

**U17:** FS/GR 41–45, 48, 51, 55, 60, 65, 71, 80, 92, 110. WW 36–40, 43, 46, 49, 53, 57, 61, 65, 69, 73.
**U15:** FS/GR 34–38, 41, 44, 48, 52, 57, 62, 68, 75, 85. WW 29–33, 36, 39, 42, 46, 50, 54, 58, 62, 66.

Senior and U23 wrestlers may enter the next category up. Heavyweight requires over 97 kg (FS/GR) or over 72 kg (WW).

### 3.4 The mat

- Wrestling area: 9 m diameter circle. Central wrestling surface: 7 m diameter. Passivity (orange) zone: 1 m band inside the 9 m circle. Protection area: 1.5 m border. Central circle: 1 m diameter.
- Red corner on the left, blue on the right; both coaches on the same side.

The engine uses real metric geometry (section 5.2).

### 3.5 Technical points (Art. 38)

The value of a hold is determined **only by the defending wrestler's position**. If the defender has at least one knee down, it is a par terre hold. The same hold is always worth more from standing than from par terre.

| Points | Situations |
|---|---|
| **1** | Opponent steps into the protection area with one entire foot (standing) without executing a hold, including stepping out while on the knees. Opponent stops the bout for injury without bleeding or visible injury. Opponent's lost challenge. FS: passive wrestler fails to score during the 30 s activity period. Reversal (bottom wrestler counters and passes behind). FS: opponent flees hold or mat, illegal action, brutality, illegal hold during an engaged hold, refuses correct par terre position after warning. Opponent's offensive foul after first warning. GR: opponent's 1st or 2nd passivity. |
| **2** | Takedown: getting behind and controlling, with the opponent on three of seven contact points (hands, knees, head, elbows). Throw that lands the opponent on the stomach or flank, with the defender losing control. Exposure: opponent's back at less than 90° to the mat, including on outstretched arms. Opponent rolls on shoulders or turns back past 90°. Blocking an attacker in danger position. GR: opponent's illegal action, brutality, illegal hold during an engaged hold, refusal of par terre position after warning. Opponent flees the mat in danger position. Opponent fouls in danger position. |
| **4** | Standing hold that brings the opponent into danger position in a continuous, dynamic action. Short-amplitude lift into danger position (attacker's knees may be down). Grand-amplitude hold that does not land in direct, immediate danger. Complete lift from standing or par terre landing on chest or outstretched arms with rotation. Instantaneous fall from standing with control. Defender keeps one hand on the mat but is immediately placed in danger position. |
| **5** | Grand-amplitude throw from standing into direct, immediate danger position. Complete lift from par terre with grand-amplitude throw into direct, immediate danger position. |

**Grand amplitude** (Art. 37): opponent loses all contact with the ground, is controlled, describes a broad sweeping curve in the air, and is brought to the ground.

**Danger position** (Art. 35): the line of the back or shoulders forms less than 90° with the mat while the wrestler resists with the upper body. Exactly 90° is neutral, not danger. Danger ends when the chest and stomach turn past 90° toward the mat.

**Scoring principles the engine enforces** (Art. 34):
- **Risk rule:** an attacker whose hold fails and who ends underneath without the opponent's own action gives up no points. Restart standing. Points go to the defender only for a counter in which the defender, by their own action, brought the attacker to the ground, acted continuously, or controlled the attacker blocked in a bridge.
- An attacker who goes to their own bridge and completes the hold is not penalised. If the attacker is blocked or countered in the bridge, points go to the opponent.
- Each new action scores separately until the wrestlers return to a defensible position (consecutive gut wrenches each score). Rolling shoulder to shoulder on the elbows in the bridge counts as one action.
- A hold scores only if completed before time expires. For a fall at the end of time, only the clock counts.
- Step-outs: if the attacker steps out first during a hold, a continuous completed hold earns its value; a failed one gives the opponent 1 point; lifting and controlling but not completing gives no points. Deliberately pushing the opponent out with no meaningful action earns no point (verbal warning only).
- An attempt that ends underneath in the protection area in neutral position without opponent action costs 1 point (stepped out first). If it ends with the attacker on their back under control, the opponent gets 2.

### 3.6 Zones and edge play (Art. 45, 49)

- A hold begun standing in the central area is valid wherever it ends. If it ends in the protection area, the bout stops and restarts standing in the centre, and the points are awarded.
- A fall in the protection area is invalid. A fall at the edge counts only with both shoulders inside the orange zone and the head not touching the protection area.
- Wrestlers may enter the orange zone with up to three of four feet during a hold begun in the centre. A hold may be started in the zone only with no more than two feet in it.
- If the defender steps into the zone, the referee calls "ZONE" and both must work back toward the centre. The first zone entry brings a colour warning. Three feet in the zone after the warning stops the bout.
- Par terre actions started in the zone are valid even if they end in the protection area. The attacker may continue if the opponent's head and shoulders stay in the zone.
- No counter performed in the protection area is scored.

### 3.7 Fleeing, negative wrestling and cautions

| Offence | Penalty |
|---|---|
| Fleeing a hold (standing or par terre) | Caution (O) + 1 point to opponent, restart in the same position |
| Fleeing the mat in par terre | Caution + 1, restart par terre |
| Fleeing the mat in danger position | Caution + 2, restart par terre |
| Fleeing the mat during an attack that ends in a takedown in the protection area | 2 (takedown) + caution + 1, restart par terre |
| Fleeing the mat during an attack without takedown | 1 (step-out) + caution + 1, restart standing |
| Negative wrestling (interlocking fingers, blocking, head down on the chest, avoiding contact) | Caution + 1 (FS/WW) or caution + 2 (GR), declared immediately, no passivity procedure |
| Defender's illegal action that prevents a hold | Caution + 1 (FS/WW) or + 2 (GR), plus the hold's value if the attacker completes it anyway |
| Attacker's illegal hold | Action void, "attention" first; repeat: caution + 1 |
| GR leg foul by attacker | 1st: warning; 2nd: caution + 1 to opponent |
| GR leg foul by defender | Caution + 2 to opponent |
| Bottom wrestler refuses correct par terre start | 1st: verbal warning; 2nd: caution + 1 (FS/WW) or + 2 (GR), restart par terre |
| Top wrestler refuses correct par terre start | 1st: verbal warning; 2nd: loses the position, restart standing |
| Wrestler interrupts the bout to change position | 1st: warning; 2nd: caution + 1 |
| Stalling (wiping, nose blowing, fake injury) | Caution + 1 |

**Three cautions in a bout = loss** (VCA). The third caution must be unanimous.

Faking injury in danger position does not stop the bout: the referee asks for the fall and the mat chairman confirms it.

### 3.8 Passivity

**Freestyle and Women's (activity clock)**
1. First offence (blocking, interlocking fingers, avoiding wrestling): brief stop, verbal warning (V).
2. Second offence: the passive wrestler is shown on the scoreboard and a **30-second activity period** starts. If the passive wrestler does not score in 30 s, the opponent gets 1 point, even if the opponent also scored in that period.
3. **0–0 after 2:00 of the first period:** the referee must designate one wrestler inactive and start the activity period. An action started just before 2:00 is allowed to finish. If it scores, there is no activity period. If not, the clock starts from the stoppage.
4. **Final 30 s of either period:** if all three officials agree a wrestler is evading or blocking, opponent gets 1 point and the evader a caution. This can be challenged.
5. The referee never interrupts a viable action to start or end an activity period.
6. U15, U17, Veterans: no verbal warning, activity time starts directly; the 0–0 mandatory designation happens at 1:00.

**Greco-Roman**
- Active wrestling = seeking contact, hooking, setting up attacks.
- **1st passivity of the bout** (whichever wrestler): stop, active wrestler gets 1 point and chooses standing or par terre.
- **2nd passivity:** same, 1 point and choice.
- **3rd and further:** choice of standing or par terre, no point.
- Calls are triggered in three situations: score 0–0; score tied with one wrestler clearly more active; one wrestler leads and the other is too defensive.
- A wrestler who scored from standing should not be called passive, unless they later go inactive while the opponent is clearly more active.
- Passivity criteria (each modelled as a detectable behaviour in the engine): evading attacks without countering; grabbing wrists without attacking; attacking without direct contact; returning to initial position right after starting an attack; fake attacks; evading into and staying in the zone; avoiding the centre; pinning the opponent in the zone; not hooking despite good position; defensive wrestling.

Passivity and negative wrestling are distinct and never substitute for each other. All passivity calls need the mat chairman's confirmation.

### 3.9 Par terre (Art. 44)

- Ordered start: bottom wrestler flat on the stomach in the centre, arms forward, legs back, uncrossed, hands and feet touching the mat. Top wrestler beside them, both knees down, both hands on the opponent's back.
- After the whistle the bottom wrestler may defend, stand up or counter.
- GR bottom wrestler: at most one hand on the opponent's upper body; no closed arms, no blocking with elbows and knees until the hold is locked; no active use of legs; no bending or raising legs to block.
- FS bottom wrestler: may not defend with two closed arms.
- The top wrestler cannot ask to restart standing. If the top wrestler cannot progress against effective defence, the referee restarts standing after a reasonable time.
- Momentary loss of knee contact once par terre is established is not standing.
- If the defender illegally blocks an attacker who has lifted them: caution + 1 (FS) or + 2 (GR), bout continues in par terre, plus the hold's value if completed.

### 3.10 Restart positions (Art. 28)

Restart **standing in the centre** when: one foot fully touches the protection area without action; wrestlers in a hold go into the zone with three or four feet and stay without executing; the bottom wrestler's head fully touches the protection area; the attacker commits a fault in par terre; any illegal action in standing.

Restart **par terre** when: a technical superiority or risk stop during par terre is overturned on review; the defender commits a fault or injury in par terre.

### 3.11 End of bout and victory types

**Technical superiority:** 8 points lead (GR) or 10 points lead (FS/WW). The referee waits for the attack or counter to finish. It is final only when the wrestlers return to neutral.

**Fall:** both shoulders held on the mat long enough for the referee to confirm complete control, confirmed by the mat chairman. An instantaneous touch of both shoulders is not a fall.

**Tie-breaking criteria** (in order):
1. The highest-value hold.
2. The most holds of the highest value.
3. The fewest cautions.
4. The last technical point scored, with these 1–1 special cases:
   - Both points non-technical (passivity, lost challenge): the **first** point wins.
   - One non-technical, one technical (hold or push-out): the **technical** point wins.
   - Both technical: the last scorer wins.
   - A caution held by the first scorer reverses the result (criterion 3 applies first).

Implementation notes (`Wrestling.Core.Bouts.TieBreak`):
- Criteria 1 and 2 compare every point award by its value, passivity and infringement points included. The rulebook settles "passivity point vs technical point" at criterion 4, which is only reachable if both count as 1-point actions earlier.
- Technical points are holds and push-outs. Passivity, activity-clock, lost-challenge, infringement and injury-stoppage points are non-technical.
- The 1–1 rules are applied to every level score: both wrestlers with technical points → last technical scorer; only one → that wrestler; none → first scorer.
- "Loser scored" for classification (VPO1, VSU1) means the loser has any points on the sheet.

**Classification points**

| Code | Result | Winner : Loser |
|---|---|---|
| VFA | Fall | 5 : 0 |
| VIN | Injury | 5 : 0 |
| VCA | 3 cautions | 5 : 0 |
| VFO | Forfeit | 5 : 0 |
| DSQ | Disqualification | 5 : 0 |
| VSU | Technical superiority, loser scored nothing | 4 : 0 |
| VSU1 | Technical superiority, loser scored | 4 : 1 |
| VPO | Points, loser scored nothing | 3 : 0 |
| VPO1 | Points, loser scored | 3 : 1 |
| 2DSQ / 2VIN / 2VFO | Both disqualified / injured / forfeited | 0 : 0 |

### 3.12 Challenge (Art. 31)

- One challenge per wrestler per bout. A successful challenge (decision changed) is kept; a lost challenge is gone, and the opponent gets **1 point**.
- Requested by the coach, confirmed or rejected by the wrestler. The bout stops when the action returns to neutral.
- Must be requested within **10 seconds** after the points appear on the scoreboard or after the wrestlers return to neutral.
- No challenge for passivity penalties or for a fall, except an illegal action that led to the fall or an action in the final seconds before it. Cautions can always be challenged.
- No challenge after the winner is declared. No counter-challenge.
- Without working video (technical failure), the original call stands and the coach keeps the challenge.

### 3.13 Bleeding and injury (Art. 26, 55)

- Up to **4 minutes total** of bleeding treatment per wrestler per bout. If bleeding is still visible after that, the wrestler loses by injury. If treatment finished in time, the wrestler continues but loses on any further bleeding.
- Stoppage for injury with no blood or visible injury: 1 point to the opponent.
- A deliberately injured wrestler wins; the offender is disqualified.
- The doctor's decision to stop a bout is final.
- An injured wrestler who cannot continue loses by injury, skips the second weigh-in, and is ranked by points accumulated.

### 3.14 Officials

Each bout has a **referee, judge and mat chairman**, never two of the same nationality and never from a wrestler's country. Decisions are unanimous or 2 of 3, except that passivity, cautions, falls and technical superiority need the mat chairman's confirmation. The mat chairman never gives the first opinion. Officials are simulated (section 5.9).

---

## 4. Wrestler model

### 4.1 Identity and body

| Field | Notes |
|---|---|
| Name, nationality, birth date, gender | Gender determines eligible styles (men: GR, FS; women: WW). |
| Height, wingspan, leg-to-torso ratio, frame | Fixed after age 20; growth curve before. Affect leverage per technique family (long legs help leg attacks and defending them; a low centre of gravity helps lifts and par terre defence). |
| **Natural weight** | Healthy body weight from frame and training; drifts with age, strength training and diet. |
| **Walk-around weight** | Current daily weight; changes week to week (section 8.5). |
| Dominant side | Affects technique side preference (left/right single leg, throw direction). |

### 4.2 Attributes

All attributes are 1–100. Each also has a hidden **potential ceiling**.

**Physical**

| Attribute | Effect |
|---|---|
| Upper-body strength | Lifts, throws, body locks, finishing, par terre turns |
| Lower-body strength | Driving through shots, base, lifting from the legs |
| Grip strength | Grip quality and grip decay rate |
| Explosiveness | Speed of entry, burst to a shot, lift initiation |
| Speed | Hand speed, footwork, reaction time |
| Balance | Recovering from off-balance states, base under pressure |
| Agility | Changing level and direction, scrambling, rolling |
| Flexibility | Bridging, escaping danger position, injury resistance in awkward positions |
| Aerobic endurance | Size of the base stamina pool and its recovery during the bout |
| Anaerobic capacity | Size of the burst pool used by explosive actions |
| Recovery | Between-period and between-bout recovery; weekly training recovery |
| Durability | Injury resistance |

**Technical (attack)** — rating per technique family; each known technique also has its own proficiency (section 4.3).

| Family | Styles |
|---|---|
| Leg attacks | FS, WW |
| Trips and sweeps | FS, WW |
| Upper-body throws (hip toss, headlock, arm throw) | All |
| Body lock and lifts (suplex, lateral drop, salto) | All (core of GR) |
| Go-behinds and drags (arm drag, duck under, slide-by) | All |
| Snap-downs and front headlock | All |
| Par terre top: turns (gut wrench, leg lace) | All (leg lace FS/WW only) |
| Par terre top: lifts (reverse lift, front lift) | All (core of GR) |

**Technical (defence and position)**

| Attribute | Effect |
|---|---|
| Hand-fighting | Winning and breaking grips, denying ties |
| Leg defence | Sprawl, hip block, whizzer against leg attacks |
| Upper-body defence | Hip block, re-arching, stepping around throws |
| Counter-wrestling | Recognising and executing counters in any phase |
| Par terre bottom defence | Flattening, base, hand placement within the rules, standing up |
| Bridging and danger defence | Escaping exposure, preventing falls |
| Mat awareness | Edge control, avoiding step-outs, using the zone |

**Mental**

| Attribute | Effect |
|---|---|
| Composure | Execution quality under pressure (score, clock, big events) |
| Aggression | AI tendency to attack; resistance to passivity calls |
| Discipline | Avoiding cautions, negative wrestling and illegal holds |
| Work rate | Pace sustained over the bout; training effort |
| Wrestling IQ | Reading the opponent, recognising openings, tactical decisions in AI |
| Determination | Performance when behind; comeback probability |
| Clutch | Bonus in the final 30 seconds and in medal bouts |

**Hidden**

| Attribute | Effect |
|---|---|
| Potential (per attribute) | Ceiling for development |
| Injury proneness | Base injury risk multiplier |
| Weight-cut tolerance | How much performance a cut costs |
| Professionalism | Training consistency, diet discipline, lifestyle events |
| Adaptability | Speed of learning new techniques and adjusting to new coaches or rules |
| Peak age | Age of peak physical attributes (normally 25–29) |

### 4.3 Technique repertoire

Each wrestler has a **repertoire**: the techniques they know, each with:

- **Proficiency** (1–100): grows with drilling and live use, decays slowly without practice.
- **Side** (left / right / both).
- **Setup preferences**: which ties or situations the wrestler usually enters from.
- **Signature flag**: up to three signature techniques per wrestler get bonus proficiency growth and commentary recognition.

A technique's effective rating in a bout combines family rating, technique proficiency, body leverage for that technique, and the physical attributes listed in the technique's definition (section 6.2).

### 4.4 Condition (changes over time)

| State | Scope | Effect |
|---|---|---|
| Stamina (aerobic pool) | Within bout | Below thresholds: slower, weaker, lower defence |
| Burst (anaerobic pool) | Within bout | Spent by explosive actions; regenerates when the pace drops |
| Grip fatigue | Within bout | Grip quality decays, grips break more easily |
| Accumulated fatigue | Tournament day | Carries between bouts, partly recovered in the 20+ minute rests |
| Training load | Weekly | Acute and chronic load (section 8.3) |
| Hydration and fuel | Weigh-in to bout | Set by the weight cut (section 8.5) |
| Injuries | Days to months | Per body part, with severity and attribute penalties |
| Form | Weeks | Recent results and training quality, -10 to +10 effective bonus |
| Morale | Weeks | Affected by results, selection, coach relationship, life events |
| Confidence vs. opponent | Per matchup | History of results against a specific opponent |

### 4.5 Ageing

Physical attributes grow until the wrestler's peak age, plateau, then decline (explosiveness and speed first, strength last). Technical and mental attributes can keep growing into the 30s. Recovery declines from the late 20s. Retirement is the player's choice, forced only by severe injury or age (offered from 30, unavoidable at 42).

### 4.6 Wrestler creation

The player builds the wrestler from scratch:

1. **Gender and style:** men choose GR or FS; women wrestle WW. A wrestler can switch between GR and FS later in the career, with a temporary adaptation penalty.
2. **Nationality:** determines the national federation, domestic competition, training culture, selection difficulty and calendar.
3. **Body:** height and frame within realistic ranges; this sets the natural weight and the realistic weight classes.
4. **Starting age:** 14 (U15 start, slowest but highest long-term ceiling) to 20 (senior start, fewer years).
5. **Background:** one of several backgrounds, each giving attribute and technique starting points, for example: wrestling family (technique and IQ), farm or physical labour (strength), judo convert (throws and grips), football or athletics (speed and endurance), late starter (fewer points, higher adaptability).
6. **Point allocation:** a budget of attribute points with increasing cost per point at higher values. The talent level chosen at the start (difficulty) sets the budget and the hidden potential ranges.
7. **Starting repertoire:** choose a fixed number of techniques from the families your attributes support.

An archetype label ("leg-attack specialist", "Greco thrower", "par terre grinder", "pressure wrestler", "counter wrestler") is **derived** from the build and shown to the player. It is never chosen and has no mechanical effect of its own.

---

## 5. Match engine

### 5.1 Principles

- Deterministic: the same seed, ruleset, wrestlers and input log always produce the same bout, on every platform. All simulation math is fixed-point (Q32.32 in a 64-bit integer). The random number generator is a seeded PCG32, one stream per bout.
- Fixed tick rate: **60 ticks per second** of bout time. A senior bout is 21,600 ticks plus stoppages.
- Pure simulation: no rendering, no Unity types. The renderer reads snapshots and events.
- The same engine runs Play, Coach and Sim. Sim runs without rendering and without per-tick snapshots.

### 5.2 Mat and spatial model

The mat is a 2D plane in metres, centred on the central circle. Zones are defined by radius from the ruleset (central surface radius 3.5 m, orange zone 3.5–4.5 m, protection area 4.5–6.0 m). Each wrestler has:

- Position and facing (2D) of the centre of mass
- Foot positions (derived from stance width and orientation), used for step-out and zone detection
- Knee, hand, head, elbow and shoulder ground contacts (for par terre, takedown and fall rules)

### 5.3 Body state per wrestler

| Variable | Range | Meaning |
|---|---|---|
| Stance | square / staggered (lead side) | Affects which attacks and defences are available |
| Level | 0 (flat) to 1 (upright) | Height of hips and head; a low level is needed for leg attacks |
| Posture | 0 (bent over) to 1 (upright) | Bent posture opens snap-downs and front headlock |
| Balance | 0 to 1 | Centre of mass over base; below thresholds the wrestler can be taken down or thrown with less force |
| Momentum | 2D vector | Carried motion; overcommitment opens counters |
| Base width | metres | Wide base resists pushing but slows movement |
| Ground state | standing / one knee / both knees / seated / prone / supine / bridge | Determines standing vs par terre valuation |
| Back angle | degrees to the mat | For danger position (< 90°) and fall detection |
| Shoulder contacts | left / right, contact time | For fall detection |

### 5.4 Contact model: grips and ties

Contact between wrestlers is a set of **grips**. Each grip has an owner, a type, a side, a **quality** (0–1) and a **strength** that decays with grip fatigue.

Grip types: collar tie, wrist control, two-on-one, underhook, overhook, double underhooks (body lock), front headlock, head-and-arm, waist from behind, gut wrench lock, single leg, double leg, ankle, crotch, leg lace (FS/WW). Grip types that are illegal for the style (below the belt in GR) are never available to the AI; in Play mode the gesture system cannot produce them.

The **hand-fight** is a continuous contest. Each tick both wrestlers' hand-fighting, speed, strength, fatigue and intent decide which grips are gained, improved, broken or denied. The combination of grips defines the **tie state** (for example "red underhook right, blue overhook", "red two-on-one left") that technique availability depends on.

### 5.5 Macro positions

| Position | Description |
|---|---|
| Neutral, apart | No grips |
| Neutral, tied | One or more grips, hand-fight in progress |
| Technique in progress | One wrestler is executing a technique phase (section 6) |
| Scramble | Contested position after a failed or countered technique; both wrestlers act continuously |
| Par terre | One wrestler on top; ordered start or reached during wrestling |
| Danger position | A wrestler's back below 90° to the mat while resisting |
| Stopped | Whistle: score review, zone restart, passivity, caution, injury, challenge, end of period |

### 5.6 The tick loop

Every tick, in a fixed order:

1. **Intents.** Each wrestler's controller (AI, player input or coach-modified AI) outputs an intent: push vector, hand-fight goal, technique to start or continue, defensive reaction.
2. **Hand-fight.** Resolve grip contests (section 5.4).
3. **Movement and pressure.** Apply push vectors against each other's resistance (strength, base, balance, fatigue). Update positions, momentum and foot positions.
4. **Techniques.** Advance active technique phases, evaluate phase transitions and counters (section 6).
5. **Balance and ground contact.** Update balance, level, ground state, back angle and contacts.
6. **Officiating.** The simulated officials observe the state: zone calls, step-outs, end-state scoring, danger position, falls, passivity and negative wrestling, technical superiority, fouls.
7. **Fatigue.** Spend and regenerate stamina, burst and grip strength.
8. **Events.** Emit events (grip gained, shot started, sprawl, points, caution, zone call) for the renderer, commentary, statistics and replays.

### 5.7 Resolution model

Every contested moment (a phase transition, a grip contest, a push) is resolved with a sigmoid success function:

```
x = k * (A - D + C) / resolutionScale
p(success) = 0.5 + 0.5 * x / (1 + |x|)
```

The algebraic sigmoid replaces the exponential logistic curve: it has the same S-shape but is exact in fixed-point arithmetic, so results are identical on every platform. All scales and rates are in `data/engine/tuning.json`.

- **A** (attacker score): technique rating (section 4.3) × fatigue multiplier × grip quality × **execution quality** + opening bonus
- **D** (defender score): the relevant defensive attribute × fatigue multiplier × reaction quality × anticipation (Wrestling IQ, history of this opponent using this technique in the bout)
- **C** (context): position on the mat, momentum, balance difference, body leverage for this technique, score and clock pressure × composure
- **k**: steepness, set per technique phase in data

Outcomes are not binary. Each phase defines graded outcomes (clean success, partial success, stalled, failed into scramble, countered), each with its own probability band.

**Execution quality** (0–1) is where input enters:
- AI: sampled from technique proficiency, composure and fatigue.
- Play mode: computed from gesture quality (section 7.1), then **capped by the wrestler's ability** (a novice cannot execute at elite quality, however good the swipe) and **floored by it** (an elite wrestler's poor swipe still produces a competent attempt).

### 5.8 Openings

The engine continuously detects **openings**: short windows when a specific technique family has a large opening bonus. Openings come from state, not chance:

- Opponent overcommitted (momentum toward you after a failed attack or a heavy push)
- Opponent's posture high or bent over, level too high, weight on one leg
- Opponent's grip broken or arm extended
- Opponent tired (burst pool empty)
- Opponent stepping back toward the edge

Openings last a few hundred milliseconds (data-defined per type). The AI sees them through Wrestling IQ; the player sees them through animation tells and an optional visual cue (section 7.1).

### 5.9 Officials model

Each official is a generated person with attributes: **accuracy** (probability of seeing the true state correctly), **passivity strictness**, **caution strictness**, **consistency**, and nationality.

- Officials observe the true simulation state with noise proportional to their accuracy and to how ambiguous the moment is (fast scrambles, edge situations, borderline danger angles). Points are given by the referee and judge; disagreements go to the mat chairman as in Art. 39–40.
- This produces realistic, occasional wrong calls, which makes the **challenge** meaningful: the coach (AI or player) can review the replay and challenge. The review compares the call against the true state with near-perfect accuracy.
- Passivity calls use the behaviour criteria of section 3.8, accumulated over a rolling window and filtered by strictness.

### 5.10 Scoring evaluator

Points are decided from the **end state** of each action, applying section 3.5:

1. When an action ends (both wrestlers back in a defensible position, a stoppage, or a new action starts), the evaluator collects the action record: initiator, defender's position at the start (standing or par terre), whether the defender left the ground completely, amplitude of the trajectory, landing state (stomach, flank, chest, arms, back angle), control established, ground-contact points, zone and protection area contacts.
2. It assigns values per Art. 38, including counters (points to the defender only if the defender's own action brought the attacker down, was continuous, or blocked the attacker in a bridge) and the risk rule.
3. Officials then observe this result with noise (section 5.9).

Who started the move never decides the points on its own.

### 5.11 Fall detection

A fall is valid when both shoulders are in contact with the mat and the top wrestler has control for at least the **fall confirmation time** (ruleset parameter, default 1.0 s of continuous contact, with official variance). Both shoulders must be inside the 9 m circle (orange zone included) with the head not in the protection area. Instantaneous contact never counts.

### 5.12 Fatigue model

- **Stamina** drains with pace (movement, pressure, hand-fighting) and recovers slowly during lower-pace moments and stoppages. 30-second break: partial recovery scaled by Recovery and Aerobic endurance.
- **Burst** drains with explosive actions (shots, lifts, sprawls, scrambles) and recovers quickly when the pace drops.
- **Grip** drains with grip time and contests, recovers when not gripping.
- Below thresholds, all physical attributes and execution quality drop on a curve. Hydration from the weight cut lowers the pool sizes.

### 5.13 AI wrestler

A **utility AI** chooses intents every decision interval (about 100–250 ms, faster with higher Wrestling IQ):

- Inputs: own and opponent state, grips, openings, score, clock, passivity status, fatigue, the bout plan from the coach, scouting knowledge of the opponent, and personality (Aggression, Discipline).
- Considerations: expected points per available technique, counter risk, stamina cost, passivity risk, mat position, protecting a lead or chasing.
- **Tactics** (set by the coach or the player in Coach mode) change the weights: pace (low / medium / high), focus (legs / upper body / par terre / counters), pressure (forward / balanced / defensive), edge strategy, risk tolerance.
- AI "mistakes" come from attributes (low IQ misses openings, low composure lowers execution quality, low discipline commits fouls), never from random handicaps.

### 5.14 Bout output

- Result, victory type and classification points
- Full event log (for commentary, replays and statistics)
- Statistics: attempts and success per technique family, points by type, step-outs, passivity calls, cautions, challenges, time in each position, stamina curves, grip dominance
- Condition changes: fatigue, injuries, morale and form effects

---

## 6. Techniques and counters

### 6.1 Concept

Every technique is a **sequence of phases**. The simulation owns the outcome; animation shows what the simulation decides. Counters can happen in any phase. A counter is itself a technique that starts from the current state, so counter-to-counter chains arise naturally, each level harder than the last because the wrestlers are more tired and off balance.

Standard phase vocabulary (a technique uses the phases it needs):

| Phase | Meaning | Typical counter windows |
|---|---|---|
| Setup | Creating the situation (fake, level change, pull, push) | Read and deny, punish the setup |
| Grip | Securing the required grip | Hand-fight, strip the grip |
| Entry | Moving the body in (penetration step, hip entry, lift start) | Sprawl, hip block, whizzer, step around, front headlock |
| Commitment | The point of no return | Re-position, counter-lift, block |
| Execution / flight | Lift, throw arc, drive, trip | Re-arch, roll through, switch the landing, post a hand |
| Landing | Contact with the mat | Turn out, land on top, bridge |
| Control | Establishing control for points or a fall | Bridge out, belly down, reversal, stand up |

### 6.2 Technique definition (data)

Each technique file defines:

- ID, name key (localised), family, styles where legal, age restrictions (for example double nelson illegal for U15/U17 and in WW)
- **Start requirements:** macro position, required and optional grips, relative position (front, side, behind), level and posture requirements
- **Phases:** for each phase, duration range in ticks, attributes used and their weights, possible outcomes with probability modifiers, stamina and burst cost
- **Counter windows:** per phase, which counter techniques are valid and the timing window
- **End states:** possible landing and control states that the scoring evaluator receives (section 5.10)
- **Gesture:** the gesture family and shape that triggers it in Play mode (section 7.1)
- **Animation segments:** references to the phase animation segments (section 11.3)
- **Foul risks:** illegal outcomes the technique can produce (for example a reverse waist hold thrown top-to-bottom), with probability tied to Discipline and fatigue

### 6.3 Shipped technique catalogue

The full catalogue ships at launch. Every entry is a complete data definition with animations.

**Standing, all styles**
Arm drag, duck under, slide-by, snap-down to go-behind, front headlock go-behind, headlock throw, hip toss (over the shoulder and under the arm variants), arm throw (single and double arm), body lock throw, lateral drop, suplex (front and rear), salto (bridging throw), underhook throw, two-on-one throw, push-out (pressure to step-out).

**Standing, FS and WW only**
Double leg (blast, high and low finishes), single leg (high, low, inside and outside finishes), high crotch, ankle pick, fireman's carry, sweep single, outside trip, inside trip, foot sweep, knee pick, leg lift to throw.

**Par terre top, all styles**
Gut wrench (standard and reverse), front lift, reverse body lift (GR signature), arm bar turn, front headlock roll, half nelson turn, double nelson from the side (FS and GR men only, legal only from the side without legs).

**Par terre top, FS and WW only**
Leg lace (ankle lace), cradle (near and far side), leg turk, step-over turn, leg ride control.

**Defence and counters, all styles**
Hand-fight strip, hip block, re-arch (counter-suplex), roll-through, step around, sprawl to front headlock (upper-body version), whizzer / overhook counter, bridge out, belly down, turn out, reversal (par terre), stand-up escape, counter-lift, counter-throw on landing.

**Defence and counters, FS and WW only**
Sprawl, crossface, leg pass back, Granby roll, switch, sit-out, counter ankle pick, counter-trip.

Techniques can be added at any time through data and animation content, with no engine changes.

### 6.4 Learning techniques

Wrestlers learn techniques through training (section 8.3): drilling sessions targeting a technique, coach specialities, training partners who use it, camps. Learning speed depends on Adaptability, family rating and coach quality. A new technique starts at low proficiency and becomes usable in live bouts above a threshold.

---

## 7. Match controls

### 7.1 Play mode: gestures

**Pressure (continuous):** touch-and-hold anywhere in the lower half of the screen and drag. The drag vector sets the push direction and intensity relative to the camera. Releasing relaxes pressure. Pressure drives mat position, step-outs, activity (passivity calls) and the opponent's fatigue, and it creates openings (an opponent pushing back hard can be snapped or thrown).

**Hand-fight (continuous):** short taps on the left or right side of the screen fight for grips on that side. Holding a tap keeps a grip. The current tie state is shown by small grip icons near the wrestlers.

**Techniques (moments):** gestures with a second finger, or a stroke after releasing pressure. Each technique family has a gesture shape:

| Gesture | Family |
|---|---|
| Fast straight swipe down-forward | Leg attacks (FS/WW) |
| Arc / half circle | Upper-body throws (hip toss, headlock, arm throw) |
| Upward scoop curving back over | Lifts and grand-amplitude throws (suplex, salto) |
| Sideways flick | Trips and sweeps (FS/WW), lateral drop |
| Swipe past the opponent and around | Go-behinds and drags |
| Sharp pull down | Snap-down |
| Circle (on top in par terre) | Gut wrench |
| Upward pull (on top in par terre) | Lifts from par terre |
| Swipe away from the attack | Defensive reaction (sprawl, hip block, step around) |
| Swipe against the throw direction during flight | Re-arch, roll-through, counter |

Which technique in a family is chosen depends on the current grips, position and repertoire (for example, an arc with an underhook gives an underhook throw; with head-and-arm it gives a headlock throw). The family and side come from the gesture; the engine picks the best matching technique from the repertoire.

**Gesture quality** (0–1) combines:
- Shape accuracy (template match using a point-cloud recogniser)
- Speed within the family's ideal range
- **Timing** relative to openings and phase windows
- Commitment (stroke length)

Quality becomes execution quality through the wrestler's skill cap and floor (section 5.7).

**Feedback:** subtle animation tells show openings; an optional assist highlights them. After each action, a small indicator rates the input (timing, shape). Haptic feedback on grips, impacts and points.

**Assist levels** (settings): Full assist (the engine picks techniques, you control timing only), Standard, Expert (no opening highlights, tighter timing windows). Assist affects only input interpretation, never the resolution model.

**Desktop and gamepad:**
- Mouse: left-drag for pressure, right-drag gestures for techniques, side buttons or keys for hand-fight.
- Gamepad: left stick pressure, right stick gestures (shape traced with the stick), shoulder buttons for hand-fight.
- Keyboard alternative bindings for accessibility.

**Pause and coach access:** in Play mode the player can pause during stoppages and the break to change tactics, and request challenges.

### 7.2 Coach mode

- The AI wrestles; the player sees the bout in 3D (speed 1×, 2× or 4×).
- **Live instructions** (shouted from the corner): pace, pressure direction, attack focus, "watch the edge", "defend the lead", "go now". The wrestler's responsiveness depends on their Discipline and the coach relationship.
- **Break talk** (30 s): choose up to two adjustments and a motivational line; effectiveness depends on coach skill and the wrestler's morale.
- **Challenge:** after any call, a replay of the action plays automatically and the player has the real 10-second window to press the challenge button. The wrestler can reject the challenge (based on their own read of the situation and personality).
- **Live data:** stamina bars, grip dominance, attempt counts, passivity status.

### 7.3 Sim mode

The bout runs headlessly in milliseconds. The result screen shows the score, victory type, a textual summary built from the event log, and key statistics. The player can open the full replay later, because the replay is just the seed and inputs.

**Win probability (scouting):** before a bout, the game runs the matchup many times headlessly (default 500 runs, in the background) and shows win probability and the likely outcome types. The same feature powers balancing tools (section 15).

---

## 8. Career mode

### 8.1 Time structure

- The career advances **one week at a time**. Each week has a plan (training, recovery, weight) and may contain events (tournaments, camps, trials, medical checks, media, life events).
- Tournament weeks switch to a **day-by-day** view: travel, weigh-in, medical control, competition days, recovery.
- The player can auto-advance multiple weeks with the current plan, stopping automatically at events that need a decision.

### 8.2 Season calendar

The calendar is generated each year from calendar templates (data), with realistic event types:

| Level | Events |
|---|---|
| Club / regional | Club tournaments, regional championships |
| National | National championships per age group, national team trials |
| International open | International tournaments (UWW calendar type), with 2 kg weight tolerance |
| UWW Ranking Series | Ranking events with ranking points and seeding effects, 2 kg tolerance |
| Continental | Continental championships (U17, U20, U23 upon request, senior) |
| World | World Championships (U17, U20, U23, senior; senior not held in Olympic years), World Cup (team, not in Olympic years) |
| Olympic | Olympic qualifiers and the Olympic Games every four years |
| Veterans | Veterans World Championships (35+) |

### 8.3 Training

Each week has **training slots** (morning and evening for 6 days, plus one rest day by default). The player (or the coach) assigns sessions:

| Session type | Develops | Load |
|---|---|---|
| Technique drilling (choose family or technique) | Technique proficiency, family rating | Low–medium |
| Situational wrestling (choose situation: edge, par terre top/bottom, from a tie) | Specific attributes and counters | Medium |
| Live wrestling / sparring | IQ, hand-fighting, all-round, conditioning | High |
| Strength (upper / lower / grip) | Strength attributes, natural weight up | Medium–high |
| Power / plyometrics | Explosiveness, speed | Medium–high |
| Conditioning (aerobic / interval) | Aerobic endurance, anaerobic capacity, weight down | Medium–high |
| Mobility | Flexibility, durability | Low |
| Film study | Wrestling IQ, scouting of next opponents | None |
| Recovery (massage, sauna, sleep) | Recovery, lowers fatigue | Negative |
| Rest | Full recovery | None |

**Development model:**
- Gain per session = base rate × intensity × coach quality × partner quality (for live and situational sessions) × age factor × distance to potential × Professionalism.
- Diminishing returns near potential; detraining slowly reduces attributes that are not trained.
- **Load model:** each session adds load; acute load (last 7 days) vs. chronic load (last 28 days) drives fatigue and **injury risk**. Overtraining reduces form; undertraining loses fitness.
- **Training partners:** the club roster provides partners with their own attributes; a training camp or a move to a stronger club provides better partners.
- **Periodisation:** the planner offers templates (base, build, peak, taper, recovery) that the player can adjust; peaking for an event increases form at that event.

### 8.4 Club, coach and environment

- The wrestler belongs to a club with a head coach, facilities and a roster of training partners.
- **Coach attributes:** technical knowledge per family, physical preparation, tactics, motivation, specialities (techniques they can teach), relationship with the wrestler.
- The player can request transfers to other clubs, attend national team camps when selected, and attend international camps (cost, travel fatigue, better partners).
- National team coaches also influence selection.

### 8.5 Weight management

- **Walk-around weight** changes week to week from diet, training and the natural weight.
- **Diet plan** per week: maintain, slow loss, fast loss, gain. Fast loss costs strength and recovery.
- **Weight cut** before the event: a water and food cut in the final days. Because UWW weigh-ins are on the **morning of competition** (and again on day 2 for finalists and repechage, with no tolerance), large water cuts have little time to recover. The model:
  - Dehydration percentage at weigh-in → reduces stamina pools, strength and composure, scaled by Weight-cut tolerance
  - Partial rehydration between weigh-in and the first bout
  - Day 2: the wrestler must make weight again, which limits how much they can recover overnight
- **Missing weight** = eliminated and ranked last without rank. Medical risk of an extreme cut (illness, injury, health events).
- **Changing weight class** is a long-term decision: moving up means facing bigger opponents with a smaller frame; moving down means harder cuts.

### 8.6 Injuries and health

- Injuries by body part (neck, shoulder, elbow, hand/fingers, ribs, back, knee, ankle, ear) with severity from minor to season-ending.
- Causes: bout incidents (from engine events like heavy landings, awkward counters), training load, cuts.
- Effects: unavailable, or competing hurt with attribute penalties and re-injury risk.
- Medical decisions: rest, treatment, surgery (long recovery, permanent small attribute changes possible), painkillers (competing hurt is the player's choice, with consequences).
- Illness during weight cuts or travel.

### 8.7 Selection and national team

- Each nation has a **selection policy** (data): national championship winner, trials, coach's choice, ranking-based, or a mix.
- Only one wrestler per country per weight class goes to Worlds and the Olympics. Rival wrestlers in your country and weight are a central part of the career.
- **Olympic quotas belong to the nation**, not the wrestler who earned them. Depending on the federation's policy, the quota earner may still face trials. This is an authentic and dramatic part of the career.
- National team status brings camps, funding and better partners.

### 8.8 Rankings and seeding

- The world ranking is computed from ranking points earned at Ranking Series events, continental and world championships (point tables in data).
- Seeds at major events come from the ranking (4 or 8 seeds depending on the event), placed per section 10.

### 8.9 Finances

- Income: federation stipend (depends on national team status and results), club salary, prize money, sponsorships (driven by results and fame).
- Costs: camps, travel to non-federation events, coaching and medical extras, nutrition support.
- Money enables better preparation but never buys attributes directly.

### 8.10 Morale, relationships and life events

- Morale responds to results, selection, coach relationship, rivalries, injuries and finances.
- Relationships: coach, rivals, training partners, federation officials.
- Life events (data-driven, weighted by Professionalism and situation): media interviews (answers affect fame, morale and relationships), offers to change nationality (allowed once in a career, per UWW rules), job or study pressure in lean years, family events.
- Rivalries form automatically from repeated close bouts and selection battles.

### 8.11 Fame and legacy

Results, style of wrestling (falls and big throws raise fame faster) and medals build fame. A Hall of Fame tracks careers across the world. Retirement leads into the coach role, where the player's former reputation affects job offers.

### 8.12 Coach role

- Jobs: club coach, national youth coach, national senior coach (offers depend on reputation and results).
- Manage a roster: training plans for each wrestler (bulk templates and individual edits), weight plans, event entries, corner coaching in Coach mode for every bout, recruiting young talent.
- Staff: assistant coaches, strength coach, physio (improve training and recovery).
- Goals set by the club or federation; success or failure affects the contract.

### 8.13 Federation role

- Budget allocation: youth programs, coaches' salaries, camps, competition entries, facilities.
- Selection policy and team selection per event.
- Olympic quota strategy: which events to target for quotas, which wrestlers to send.
- National championships organisation and the domestic competition structure.
- Goals from the national Olympic committee; success affects budget.

---

## 9. World simulation

### 9.1 World database

- Generated at career start from data: countries (with wrestling strength per style, coaching quality, population and talent rate, selection policy, naming data), clubs, coaches, officials and wrestlers of all ages.
- All people are **fictional**, with names generated from per-country name data. User-made rosters can be imported (section 13.7).
- Each country has a style culture profile (for example strong GR tradition, leg-attack freestyle culture) that biases generated attributes and techniques.

### 9.2 Yearly cycle

- Youth intake each year per country (count from population and wrestling popularity; quality from talent rate and coaching).
- AI wrestlers train with AI-made plans, change weight classes, get injured, switch clubs, retire.
- AI coaches and federations make selection decisions using the same rules as the player.

### 9.3 Background competition

- Every event in the world calendar runs, including those the player does not attend. Bouts not watched run in **Sim mode on the same engine** (no simplified model), so the world stays consistent with what the player sees.
- Performance budget: a full world week must simulate in under 2 seconds on a mid-range phone (section 14.4). Background bouts use the same simulation with event logging reduced to statistics.

---

## 10. Tournaments and competition structure

All formats come from Art. 8–14 and are implemented in full.

### 10.1 Individual elimination with repechage (8+ wrestlers)

- Direct elimination with ideal bracket sizes (8, 16, 32, 64). Otherwise qualification rounds bring the field to the ideal number, placed top-bottom / bottom-top for balance.
- **Draw** the day before the category starts. Non-seeded wrestlers fill positions top to bottom by draw number.
- **Seeds** (4 or 8) placed per the UWW seeded bracket layouts, as far apart as possible. If a seed is missing, the next ranked wrestler takes the place.
- **Repechage:** every wrestler who lost to either finalist enters repechage, in two separate groups (losers to the top-half finalist, losers to the bottom-half finalist). Repechage starts with first-round losers (including qualification round losers) up to the semi-final losers. The two repechage winners each receive bronze.
- Medals: gold, silver, two bronze. Two 5th places.

### 10.2 Two-day format

- **Day 1 morning:** medical control and weigh-in (30 minutes). Day 1: qualification and elimination rounds.
- **Day 2 morning:** weigh-in only for finalists and repechage wrestlers (15 minutes), **no tolerance**. Day 2: repechage and finals.
- 2 kg tolerance on the first weigh-in at World Cup, Ranking Series and international tournaments.
- Categories with fewer than 16 wrestlers may run in one day.
- Failing or missing a weigh-in: eliminated, ranked last without rank. If a day-2 wrestler fails, the opponent advances in that part of the bracket.

### 10.3 Small categories

- **Fewer than 6 wrestlers: Nordic system** (round robin). Ranking: most wins; head-to-head between two tied; otherwise classification points, falls, superiority wins, technical points scored, fewest conceded, lowest seed, lowest draw number. Only one bronze.
- **6 or 7 wrestlers:** two pools (Nordic within pools), semi-finals A1–B2 and A2–B1, final, one bronze match between semi-final losers. Placement into pools: lowest draw number to A, next to B, and so on; seeds placed per Art. 12.
- At Continental and World Championships a category with only 1 wrestler is cancelled.

### 10.4 Ranking from 7th place

By classification points, then: most falls, most superiority wins, most technical points scored, fewest technical points conceded, lowest seed, lowest draw number.

### 10.5 Team ranking at individual events

Points to a country's best-placed wrestler per category: 1st 25, 2nd 20, 3rd 15, 5th 10, 7th 8, 8th 6, 9th 4, 10th 2 (Nordic: 4th 12, 6th 9). Ties: most golds, then silvers, then bronzes.

### 10.6 Team competitions (World Cup style)

Dual meets: team win = 1 point. Ties in a dual meet decided by total classification points, falls, superiority wins, technical points scored, fewest conceded, last bout result. Group standings tie-break by head-to-head, then classification points, falls, superiority wins, points scored, fewest conceded, draw number.

### 10.7 Scheduling

- Sessions of up to three hours, a fixed number of mats (Olympics 3 mats over 7 days, senior Worlds 4 mats over 9 days), finals on one mat (all medal bouts on one mat at televised events).
- 20 minutes minimum rest between a wrestler's bouts; the scheduler respects it.
- Three calls at 30-second intervals before a forfeit.
- Disqualification, forfeit, double injury and doping cases are handled per Art. 14 (including bracket repair and medal reassignment).

### 10.8 Tournament day for the player

Between bouts: recovery actions (rehydrate, eat, rest, warm up), scouting the next opponent (film study during the day, win probability), and tactical plan for the next bout. Fatigue and injuries carry over.

---

## 11. Presentation

### 11.1 Visual direction

- Realistic, clean, broadcast-inspired 3D: competition halls, a mat built to UWW dimensions, red and blue singlets, scoreboards and officials.
- Characters built from a parametric body system (height, frame, muscle, weight class) driving one shared skeleton, so every generated wrestler looks distinct and correctly proportioned.
- Venues: club gym, national championship hall, Ranking Series arena, continental and world arenas, Olympic arena. Crowd density and atmosphere scale with event level.

### 11.2 Camera

- **Broadcast camera** (default): elevated side view that tracks the pair and keeps both wrestlers framed; switches to closer angles during par terre and throws.
- **Player camera** (Play mode): slightly lower and closer, oriented so gesture directions stay intuitive; the push direction is always relative to this camera.
- **Replay cameras:** multiple angles, slow motion, free orbit in Replay Theatre.
- Automatic instant replay after big scores and for challenges.

### 11.3 Animation

As decided in `CLAUDE.md`:

- **Short paired segments per technique phase** (0.3–1 s) for both wrestlers, stitched with **motion matching** so any phase can flow into any valid next phase from the current pose.
- **IK contact constraints** keep hands on the actual grip points from the contact model (section 5.4) and feet planted.
- **Active ragdoll** (physics-driven muscles following animation targets) for balance loss, impacts, landings and scrambles.
- The animation system is driven by simulation snapshots and events. It may smooth and blend but never changes the outcome. If the animation cannot reach a pose exactly, the simulation state wins and the animation corrects over the next frames.
- **Motion capture** is recorded per phase, including counters and transitions, with real wrestlers (markerless capture). Each technique ships with its full set of phase segments.

### 11.4 Audio

- Mat sounds (slaps, impacts, breathing, shoe squeaks), referee whistle and calls in the official English vocabulary ("Action blue", "Zone", "Attention"), coaches shouting, crowd reacting to the score and the clock.
- Music for menus and ceremonies.

### 11.5 Commentary

- Event-driven text commentary built from the event log with templates per event type and context (score, clock, rivalry, signature moves, records).
- Fully localised through string keys; templates have variants to avoid repetition.
- Shown as a ticker during bouts and as a written summary in Sim results.

### 11.6 Ceremonies

Medal ceremonies for the top four (gold, silver, two bronze), World Championship belt for senior world champions, Olympic podium.

---

## 12. UI and screens

### 12.1 Layout principles

- Mobile first: management screens work in portrait and landscape; bouts are landscape.
- Thumb-reachable primary actions, large touch targets, no hover-only interactions.
- Desktop: the same screens with wider layouts, mouse and keyboard shortcuts.
- Readable at a glance: every important number has an explanation on tap ("why did this go down?").

### 12.2 Main screens

| Screen | Content |
|---|---|
| Title / profile | Career saves, quick modes, settings |
| Wrestler creator | Steps from section 4.6 with live preview of the 3D model and derived archetype |
| Career hub | Current week, next event, alerts (injury, weight, selection), quick actions, news feed |
| Calendar | Year view with events, entries, deadlines, periodisation phases |
| Training planner | Weekly slot grid, session picker, load chart (acute vs chronic), predicted development, templates |
| Weight planner | Weight trend chart, target class, diet plan, cut plan, risk indicator |
| Wrestler profile | Attributes, repertoire with proficiency, condition, injuries, history, statistics, signature moves |
| Club and coach | Coach, partners, facilities, transfer options, camps |
| National team | Selection policy, rivals in the weight class, quota status, team camps |
| Rankings | World and national rankings per style and weight |
| Tournament | Entry list, bracket (with repechage view), schedule by mat and session, live results |
| Pre-bout | Opponent scouting, win probability, tactics, mode choice (Play / Coach / Sim) |
| Bout HUD | See 12.3 |
| Post-bout | Result, classification points, statistics, key moments, replay entry |
| Medical | Injuries, treatment options, return timeline |
| Finances | Income and costs |
| Inbox / news | Messages, offers, media requests, world news |
| Coach dashboard (coach role) | Roster overview, plans in bulk, event entries |
| Federation dashboard (federation role) | Budget, programs, selection, quota map |
| Replay Theatre | Bout list, timeline with events, camera controls |
| Settings | Controls and assist level, gesture sensitivity, handedness (mirror controls), camera, graphics quality, audio, language, accessibility |

### 12.3 Bout HUD

- Scoreboard: names, flags, points, cautions (O), passivity (P) markers, activity clock (FS), period and time, challenge availability.
- Stamina and burst bars (Coach mode always; Play mode optional).
- Grip icons and tie state.
- Opening cue (if assist enabled).
- Challenge button (with 10-second countdown after calls).
- Tactics button (during stoppages and the break).

### 12.4 Accessibility

- Assist levels for gestures (section 7.1), adjustable timing windows in Training Room.
- Left-handed mode (mirrors the control zones).
- Colour-blind safe red/blue indicators (shape and position cues in addition to colour).
- Text size scaling, subtitles for referee and coach calls, reduced motion for camera shakes.

### 12.5 Localisation

All text through string keys from day one. Launch languages are decided by the target markets (section 16); the systems support right-to-left text and non-Latin name data.

---

## 13. Data formats

### 13.1 Principles

- Plain **JSON** content files, each validated by a **JSON Schema** at build time and at load time.
- Stable string IDs (`technique.fs.double_leg_blast`), never array indexes.
- All user-facing text referenced by localisation keys.
- Every file carries `schemaVersion`. Loaders migrate older versions.
- Content is engine-independent: the same files load in Unity, the headless runner and tests.

### 13.2 Content layout

```
data/
  rulesets/            uww-2026-01.json, ...
  styles/              greco-roman.json, freestyle.json, womens.json
  techniques/          one file per technique
  grips/               grip type definitions
  openings/            opening type definitions
  attributes/          attribute definitions and growth curves
  backgrounds/         creation backgrounds
  traits/              hidden trait distributions
  training/            session types, periodisation templates
  injuries/            injury types by body part
  countries/           one file per country (strength, culture, selection policy)
  names/               name data per country
  calendars/           yearly calendar templates, event definitions
  rankings/            ranking point tables
  events-life/         life event definitions
  commentary/          commentary templates
  localization/        <language>.json string tables
```

### 13.3 Ruleset

The shipped ruleset is [`data/rulesets/uww-2026-01.json`](../data/rulesets/uww-2026-01.json), loaded by `Wrestling.Data.Rulesets.RulesetLoader` into `Wrestling.Core.Rules.Ruleset`. Excerpt:

```json
{
  "schemaVersion": 1,
  "id": "uww-2026-01",
  "technicalSuperiority": { "grecoRoman": 8, "freestyle": 10, "womens": 10 },
  "technicalPoints": { "stepOut": 1, "takedown": 2, "exposure": 2, "standingHoldToDanger": 4, "grandAmplitudeToDirectDanger": 5 },
  "passivity": {
    "grecoRoman": { "pointsByCall": [1, 1, 0], "activeWrestlerChoosesPosition": true },
    "activityClock": { "activitySeconds": 30, "pointIfNoScore": 1 }
  },
  "classificationPoints": { "fall": { "winner": 5, "loser": 0 }, "technicalSuperiorityLoserScored": { "winner": 4, "loser": 1 } }
}
```

Rule **values** live in the ruleset; deciding **which** rule applies to a situation (for example whether a throw is grand amplitude into direct danger) is code in `Wrestling.Core`, tested against the rulebook's own examples.

Loading is strict: every property is required (nullable values must be written as `null`), unknown properties and enum values are errors, numbers are read as exact decimals, and all validation errors are reported together.

### 13.4 Technique

One file per technique under `data/techniques/`, named after its id (`technique.all.hip_toss_overhook.json`), loaded by `Wrestling.Data.Techniques.TechniqueLoader` into `Wrestling.Core.Techniques.TechniqueCatalog`. Excerpt:

```json
{
  "schemaVersion": 1,
  "id": "technique.all.hip_toss_overhook",
  "nameKey": "tech.hip_toss_overhook.name",
  "role": "attack",
  "family": "upperBodyThrows",
  "styles": ["grecoRoman", "freestyle", "womens"],
  "forbiddenForAges": [],
  "start": {
    "positions": ["neutralTied"],
    "requiredGrips": [{ "type": "overhook", "side": "same" }],
    "optionalGrips": [{ "type": "headAndArm", "bonus": 0.1 }],
    "relativePositions": ["front"],
    "minimumOwnLevel": 0,
    "maximumOwnLevel": 1
  },
  "amplitude": "grandAmplitude",
  "phases": [
    {
      "id": "entry",
      "minimumTicks": 12,
      "maximumTicks": 20,
      "attributes": [
        { "attribute": "explosiveness", "percent": 40 },
        { "attribute": "upperBodyThrows", "percent": 40 },
        { "attribute": "balance", "percent": 20 }
      ],
      "steepness": 1,
      "staminaCost": 0.6,
      "burstCost": 3,
      "outcomes": ["clean", "stalled", "blocked"],
      "counterWindows": [{ "technique": "technique.all.hip_block", "fromTick": 0, "toTick": 10, "animation": null }],
      "endStates": [],
      "animation": "anim.hip_toss.entry"
    }
  ],
  "gesture": { "family": "arc" },
  "fouls": []
}
```

Catalogue validation: ids are unique and match file names; attacks have a family legal in every listed style; required grips are legal in every style; attribute percentages sum to exactly 100 per phase; only the final phase has end states; every counter window points at an existing counter or defence available in the same styles. A counter window's `animation` (null for the counter's own clip) names the clip the counter's first phase plays when started from that window, for attacks that leave the bodies arranged differently (a hip block behind a turned-in hip throw versus chest to chest against a lift). It changes presentation only, never the contest, and the clip must be in the counter's own animation file and feasible at its first phase's minimum length.

### 13.5 Wrestler (excerpt, as stored in a save)

```json
{
  "id": "wr-000184233",
  "name": { "given": "Arin", "family": "Kalvet" },
  "country": "country.est",
  "gender": "male",
  "style": "greco-roman",
  "birthDate": "2008-03-14",
  "body": { "heightCm": 176, "wingspanCm": 181, "legRatio": 0.47, "frame": "medium", "naturalWeightKg": 79.5, "dominantSide": "right" },
  "attributes": { "upper-body-strength": 62, "explosiveness": 58, "family:body-lock-lift": 55, "composure": 49 },
  "potential": { "upper-body-strength": 88, "explosiveness": 74 },
  "hidden": { "injuryProneness": 0.9, "weightCutTolerance": 1.1, "professionalism": 72, "adaptability": 64, "peakAge": 27 },
  "repertoire": [
    { "technique": "technique.all.body_lock_throw", "proficiency": 61, "side": "right", "signature": true }
  ],
  "condition": { "walkAroundWeightKg": 81.2, "form": 2, "morale": 64, "injuries": [] }
}
```

### 13.6 Saves

- World state (thousands of wrestlers, results, rankings) is stored in **SQLite**, one database per career, with versioned schema migrations.
- Saves are portable between platforms (same format on phone and desktop), which enables cloud save sync.
- Autosave each week and after each bout.

### 13.7 Replays and rosters

- **Replay file:** ruleset ID, content hash, engine version, both wrestlers' bout snapshots, seed, and the input log (AI decisions are re-derived; player inputs and coach instructions are stored). Replays are a few kilobytes.
- **Roster import/export:** JSON roster packs (wrestlers, countries, clubs) validated by schema, for community-made rosters.

---

## 14. Technical architecture

### 14.1 Solution layout

```
src/
  Wrestling.Core/            Pure C# (netstandard2.1), no Unity references
    Numerics/                Fixed-point types, deterministic RNG
    Diagnostics/             Determinism probe
    Rules/                   Ruleset model, scoring evaluator, officiating rules
    Bodies/                  Body state, contact and grip model
    Match/                   Tick loop, techniques runtime, officials, fatigue, events
    AI/                      Utility AI, tactics, coach AI
    Techniques/              Technique runtime model
    Career/                  Weekly loop, training, weight, injuries, finances, events
    World/                   Generation, yearly cycle, AI federations
    Competition/             Brackets, repechage, Nordic, pools, team events, scheduling, rankings
  Wrestling.Data/            Content loading, JSON Schema validation, migrations, SQLite persistence
  Wrestling.Sim/             Headless command-line runner (mass simulation, balancing reports)
tests/
  Wrestling.Core.Tests/      Unit and determinism tests
  Wrestling.Data.Tests/      Schema and content validation tests
unity/
  WrestlingSimulator/        Unity project: presentation, input, UI, audio, animation
data/                        Content (section 13.2)
docs/                        Design documents
```

Unity consumes `Wrestling.Core` and `Wrestling.Data` as compiled assemblies (or source packages). Unity code only renders, collects input and shows UI.

### 14.2 Simulation and rendering

- The simulation runs at a fixed 60 Hz; the renderer interpolates between snapshots at the display frame rate.
- In Play mode, input is sampled per frame, converted to intents with timestamps, and applied on the matching simulation tick.
- Background simulation (world weeks, scouting runs) runs on worker threads.

### 14.3 Determinism

- Fixed-point math for all simulation values; no floating point in `Wrestling.Core` simulation paths.
- Seeded PCG32 streams per bout, per week, per generation step.
- Cross-platform determinism tests: reference bouts are simulated on every target platform in CI and compared hash by hash.

### 14.4 Performance budgets

| Target | Budget |
|---|---|
| Bout rendering on a mid-range phone (2023 class) | 60 fps, 30 fps fallback mode |
| Headless bout simulation | under 5 ms per senior bout on a mid-range phone |
| World week simulation | under 2 s on a mid-range phone |
| Scouting (500 runs) | under 3 s in the background |
| Install size | defined per platform store limits; animation and venue content delivered with Addressables |

### 14.5 Other systems

- Cloud save sync (platform services) using the portable SQLite saves.
- Crash reporting and anonymous gameplay telemetry (opt-in) to support balancing.
- Online play (future): deterministic lockstep is possible because the engine is deterministic; not part of this document's scope.

---

## 15. Balancing and validation

### 15.1 Realism targets

The game is tuned so that simulated bouts match real statistics per style and level:

- Distribution of victory types (fall, technical superiority, points) per style
- Average total points per bout and per period
- Share of points by type (step-outs, takedowns, exposures, big throws, passivity points)
- Frequency of passivity calls and cautions
- Upset rates by ranking difference

Reference values are compiled from public UWW results of senior World Championships and Olympic Games (section 16).

### 15.2 Tools

- `Wrestling.Sim` runs thousands of bouts between generated or specified wrestlers and produces reports against the realism targets.
- Attribute sensitivity reports: how much each attribute changes win probability, to catch dominant or useless attributes.
- Technique reports: attempt and success rates per technique, counters per technique, points per attempt.
- Career reports: simulate full careers for the whole world to check medal distribution per country, ageing curves and injury rates.

### 15.3 Tests

- Rules tests: every rule in section 3 has unit tests (scoring cases from Art. 38, tie cases from the Art. 29 examples, passivity sequences, bracket and repechage cases from Art. 8–14).
- Determinism tests (section 14.3).
- Content tests: every technique and data file validates against its schema and references existing IDs and animations.

---

## 16. Open items to verify

These are facts or decisions to settle with research or a product decision. Systems are built to read them from data, so settling them changes content, not code.

1. **Olympic qualification system for LA 2028** (which events award quotas and how many). Source: the official UWW/IOC qualification system document for LA 2028.
2. **UWW ranking points tables** for Ranking Series, continental and world events, and the seeding rules for each event type.
3. **Real-world statistics** for the realism targets (section 15.1), compiled from public results of recent senior Worlds and Olympics.
4. **Fall confirmation time:** the rulebook says "long enough for the referee to confirm complete control"; the default of 1.0 s should be validated with referees or video analysis.
5. **Launch languages and target markets.**
6. **Business model** (premium, premium with expansions, or other). This affects content delivery and store setup, not gameplay systems.
7. **Mocap partners and sessions:** wrestlers and venue for capturing the technique catalogue (section 6.3).
8. **Veterans weight categories:** the rulebook defers them to separate veterans regulations, so the ruleset has none yet.
9. **Official UWW bracket charts** (no seeds, 4 seeds, 8 seeds; published at uww.org): bracket placement uses a balanced algorithm that follows the rulebook's principles (seeds far apart, qualification top-bottom/bottom-top). Compare it with the official charts and switch to chart data if they differ.
10. **Activity period across the end of a period** (Freestyle/Women's): the rulebook does not say what happens when the 30-second activity period is still running at the end of a period. The engine lets it lapse without a point; confirm with referees or video of recent events.
