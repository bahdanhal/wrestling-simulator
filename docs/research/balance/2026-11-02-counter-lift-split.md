# Counter-lift split into lift and arch phases (2026-11-02)

Change: `technique.all.counter_lift` goes from one phase (lift, 28–42 ticks) to two:

- **lift**, 14–22 ticks: same attributes and steepness (1.2) as before; outcomes clean, partial, blocked; no end states.
- **arch**, 16–24 ticks: upper-body strength 40%, lower-body strength 30%, counter wrestling 30%; steepness 0.2; outcomes clean or partial (partial lands in a scramble); end states danger direct, flank, scramble.

Stamina and burst are split between the phases (1.0 + 1.0, 12 + 10), the same totals as before. The lift is the contest: a blocked lift fails as before. The arch's low steepness keeps the share of scoring counter-lifts where it was. With steepness 1.0 and other attributes (first try below), Greco-Roman danger and flank landings fell from 50 to 31.

Baseline: the "After" sections of [2026-11-01-counter-lift-timing.md](2026-11-01-counter-lift-timing.md). The technique data was unchanged between that run and this one.

## Viewer-style bouts (level 60 ± 12, AI both corners), 1000 per style

First try (arch steepness 1.0, flexibility in place of lower-body strength), Greco-Roman:

    GrecoRoman: 1000 bouts, mean length 350,0 s, points/bout 9,06, victories Fall 5,2%, Points 70,4%, TechnicalSuperiority 24,4%
      counter_lift: 238 starts, points 0
        counter_lift Completed:DangerDirect 15
        counter_lift Completed:Flank 16
        counter_lift Completed:Scramble 133
        counter_lift Failed:Blocked 73

Final:

    GrecoRoman: 1000 bouts, mean length 350,0 s, points/bout 9,11, victories Fall 5,1%, Points 70,2%, TechnicalSuperiority 24,7%
      counter_lift: 227 starts, points 0
      front_suplex: 767 starts, points 0
        counter_lift Completed:DangerDirect 20
        counter_lift Completed:Flank 25
        counter_lift Completed:Scramble 109
        counter_lift Failed:Blocked 72
        front_suplex Completed:DangerDirect 5
        front_suplex Completed:Flank 5
        front_suplex Completed:Scramble 7
        front_suplex Failed:Blocked 108
        front_suplex Failed:Countered 561
        front_suplex Failed:FailedIntoScramble 7
        front_suplex Failed:Stalled 73
    Freestyle: 1000 bouts, mean length 349,1 s, points/bout 12,01, victories Fall 6,2%, Points 70,6%, TechnicalSuperiority 23,2%
      counter_lift: 25 starts, points 0
      front_suplex: 51 starts, points 0
        counter_lift Completed:DangerDirect 2
        counter_lift Completed:Flank 1
        counter_lift Completed:Scramble 16
        counter_lift Failed:Blocked 6
        front_suplex Completed:DangerDirect 2
        front_suplex Failed:Blocked 7
        front_suplex Failed:Countered 39
        front_suplex Failed:FailedIntoScramble 2
        front_suplex Failed:Stalled 1
    Womens: 1000 bouts, mean length 347,6 s, points/bout 12,08, victories Fall 5,7%, Points 69,3%, TechnicalSuperiority 25,0%
      counter_lift: 15 starts, points 0
      front_suplex: 62 starts, points 0
        counter_lift Completed:DangerDirect 2
        counter_lift Completed:Flank 1
        counter_lift Completed:Scramble 8
        counter_lift Failed:Blocked 4
        front_suplex Completed:DangerDirect 1
        front_suplex Completed:Flank 1
        front_suplex Completed:Scramble 1
        front_suplex Failed:Blocked 6
        front_suplex Failed:Countered 46
        front_suplex Failed:Stalled 7

## wrestling-sim balance, 2000 bouts per style (gr, fs, ww), final

      Points                  74,2%
      TechnicalSuperiority    21,6%
      Fall                     4,2%
      points per bout          8,8
      attempts per bout       20,0
      attack success           7,7%
      points by reason       Takedown 24%, StandingHoldToDanger 19%, Passivity 16%, Exposure 11%, Throw 10%, GrandAmplitude 8%, GrandAmplitudeToDirectDanger 6%, Reversal 3%, StepOut 2%, LiftToDanger 1%
      Points                  69,4%
      TechnicalSuperiority    23,7%
      Fall                     6,9%
      points per bout         11,6
      attempts per bout       26,3
      attack success           9,4%
      points by reason       Takedown 28%, StandingHoldToDanger 25%, Throw 20%, Exposure 9%, GrandAmplitude 5%, Reversal 3%, LiftToDanger 2%, LiftToChestOrArms 2%, GrandAmplitudeToDirectDanger 2%, StepOut 2%, ActivityClock 1%
      Points                  72,0%
      TechnicalSuperiority    21,4%
      Fall                     6,6%
      points per bout         11,6
      attempts per bout       26,8
      attack success           8,7%
      points by reason       Takedown 28%, StandingHoldToDanger 26%, Throw 20%, Exposure 8%, GrandAmplitude 5%, Reversal 4%, GrandAmplitudeToDirectDanger 2%, LiftToDanger 2%, LiftToChestOrArms 2%, StepOut 2%, ActivityClock 1%

Every headline figure is within 0.3 points of the baseline (Greco-Roman: points 73.8 → 74.2%, technical superiority 21.6 → 21.6%, fall 4.6 → 4.2%, points per bout 8.8 → 8.8). Counter-lift outcomes in Greco-Roman viewer bouts: scoring (danger or flank) 50 → 45, blocked 83 → 72, scramble 106 → 109, within seed-to-seed noise.

## Follow-up: longer arches (counter-lift and front suplex arch 26–36 ticks)

Phase length only (presentation timing; the contest is unchanged).

wrestling-sim balance, 2000 bouts per style (gr, fs, ww):

      Points                  74,0%
      TechnicalSuperiority    21,8%
      Fall                     4,2%
      points per bout          8,8
      attempts per bout       20,0
      attack success           7,7%

      Points                  69,5%
      TechnicalSuperiority    23,6%
      Fall                     6,9%
      points per bout         11,6
      attempts per bout       26,3
      attack success           9,4%

      Points                  72,0%
      TechnicalSuperiority    21,4%
      Fall                     6,6%
      points per bout         11,6
      attempts per bout       26,8
      attack success           8,7%

Viewer-style bouts, 1000 per style:

    GrecoRoman: 1000 bouts, mean length 350,7 s, points/bout 9,07, victories Fall 5,0%, Points 70,8%, TechnicalSuperiority 24,2%
      counter_lift: 236 starts, points 0
      front_suplex: 782 starts, points 0
        counter_lift Completed:DangerDirect 19
        counter_lift Completed:Flank 29
        counter_lift Completed:Scramble 111
        counter_lift Failed:Blocked 76
        front_suplex Completed:DangerDirect 5
        front_suplex Completed:Flank 2
        front_suplex Completed:Scramble 3
        front_suplex Failed:Blocked 114
        front_suplex Failed:Countered 578
        front_suplex Failed:FailedIntoScramble 5
        front_suplex Failed:Stalled 74
    Freestyle: 1000 bouts, mean length 348,8 s, points/bout 12,02, victories Fall 6,1%, Points 70,4%, TechnicalSuperiority 23,5%
      counter_lift: 29 starts, points 0
      front_suplex: 55 starts, points 0
        counter_lift Completed:DangerDirect 2
        counter_lift Completed:Flank 1
        counter_lift Completed:Scramble 19
        counter_lift Failed:Blocked 7
        front_suplex Completed:DangerDirect 2
        front_suplex Completed:Flank 1
        front_suplex Completed:Scramble 1
        front_suplex Failed:Blocked 5
        front_suplex Failed:Countered 41
        front_suplex Failed:FailedIntoScramble 3
        front_suplex Failed:Stalled 2
    Womens: 1000 bouts, mean length 347,3 s, points/bout 12,07, victories Fall 5,6%, Points 69,3%, TechnicalSuperiority 25,1%
      counter_lift: 16 starts, points 0
      front_suplex: 66 starts, points 0
        counter_lift Completed:DangerDirect 1
        counter_lift Completed:Flank 1
        counter_lift Completed:Scramble 7
        counter_lift Failed:Blocked 7
        front_suplex Completed:DangerDirect 2
        front_suplex Completed:Flank 1
        front_suplex Completed:Scramble 1
        front_suplex Failed:Blocked 8
        front_suplex Failed:Countered 46
        front_suplex Failed:Stalled 8
