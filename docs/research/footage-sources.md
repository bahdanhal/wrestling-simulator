# Footage sources for motion extraction

Research on 29 October 2026 (updated 31 October) for real wrestling footage we may use to extract motion for the game's
technique clips. Folk and other grappling styles count: the mechanics of throws, lifts and
takedowns (hip loading, leg drive, contact) are the same.

Not legal advice. Every file below needs its licence checked on its own page before use, and
attribution recorded.

## Best source: Harmony4D (multi-view 3D, both people)

Added 31 October 2026. [Harmony4D](https://huggingface.co/datasets/Jyun-Ting/Harmony4D) (Khirodkar, Song, Cao, Luo, Kitani; Carnegie Mellon; NeurIPS 2024) records unscripted close-contact activities with about 20 synchronised 4K GoPros at 60 fps, and releases per-frame 3D keypoints for **both** people at 20 fps, triangulated from all cameras, so neither body is lost when they clinch.

- **Licence:** the Hugging Face dataset is tagged MIT, the [code repository](https://github.com/jyuntins/harmony4d) is MIT ("Copyright (c) 2024 Jyun-Ting Song"), and the paper is CC BY 4.0. Participants gave informed consent under IRB rules and were paid. Not gated. A third-party card ([Voxel51](https://docs.voxel51.com/dataset_zoo/datasets_hf/harmony4d.html)) says "not specified"; the MIT licence is confirmed.
- **Content:** standing wrestling in shorts and rash guards on a wrestling mat (stance, hand fighting, collar ties, body locks, level changes, scrambles to the mat), recreational-level athletes rather than elite. Grappling: 67 sequences, 14,107 frames (11.8 minutes). MMA (with clinches and takedowns): 60 sequences, 16,025 frames (13.4 minutes). Also hugging, dancing, karate and fencing.
- **Format:** each sequence's `processed_data/poses3d/NNNNN.npy` holds a dict of the two people (`aria01`, `aria02`), each a 17 × 4 float array (COCO-17 joints in metres, plus confidence), stored Fortran-ordered. The `.npy` files are pickled objects: read them by decoding the byte arrays from the pickle stream (`pickletools`), never with `allow_pickle=True`. SMPL fits are also included, but the SMPL body model is non-commercial, so use the keypoints.
- **Size:** the zips are mostly images (17–44 GB each), but zip archives can be read file by file over HTTP range requests, so only the pose files need downloading (31 MB for all grappling and MMA).
- **Tools and results:** `tools/motion/harmony4d/` fetches, checks, converts and compares; `real-motion-comparison.md` has what the data shows against our clips. Three takes are imported; they are not assigned to phases yet because our phases are 3–8 times shorter than the real movements.

## Pilot: single-camera Commons footage (31 October 2026)

Tried on [Sergey Semenov - Adam Coon](https://commons.wikimedia.org/wiki/File:Sergey_Semenov_-_Adam_Coon.ogv) (130 kg Greco-Roman, 1920 × 1080, 50 fps), around the overhead throw at 2:16–2:17.

- MediaPipe pose (Apache 2.0) found each wrestler in under half the frames and confused the blue wrestler with the referee.
- RTMPose-x with a YOLOX-x detector ([rtmlib](https://github.com/Tau-J/rtmlib), Apache 2.0) tracked the thrower in 152 of 175 frames, but the thrown wrestler, hidden behind him in the clinch and under him on landing, in only about 12 frames near the action.
- Conclusion: one camera from the stands cannot give both bodies through a throw. Nothing was imported. Still useful as timing reference: the throw's flight (lift to landing) takes about 0.75 s, where the keyed throw flights take 0.27–0.43 s.

## Other options checked

- **Commercial paired animation packs:** [Grab & Throw Takedown Animation Pack](https://www.gameassetdeals.com/asset/403288/grab-throw-takedown-animation-pack) (46+ clips, each takedown with a matching victim clip), [Paired Suplex Wrestling Animations](https://www.fab.com/listings/16dc7f2d-eac7-4c6e-9045-4ac0cdf19d70) (12 sequences), [ActorCore Hand to Hand Combat](https://actorcore.reallusion.com/3d-motion/pack/studio-mocap-hand-to-hand-combat?info=1) (mocap, FBX/BVH). Paid, game-style (pro-wrestling and action takedowns), not Olympic technique; check each licence for use in a shipped game and for redistributing derived data.
- **CMU Motion Capture Database:** free for research and commercial projects with credit, with about 55 two-person sequences, but everyday interactions (talking, playing, throwing a ball), not grappling.
- **InterHuman, Hi4D, CHI3D, Inter-X:** two-person interaction datasets, research-only or with no stated licence; not usable commercially without the authors' permission.

## Usable: free licences

### Wikimedia Commons

Commons only accepts free licences (CC BY, CC BY-SA, CC0, public domain); non-commercial licences
are not allowed there, so everything on it can be used commercially with attribution. CC BY-SA
files carry share-alike: motion we derive from them may have to be published under CC BY-SA too
(the derived clips, not the game).

**2018 World Wrestling Championships, Budapest** — filmed from the stands by Elekes Andor,
1920 × 1080, CC BY-SA 4.0 (checked on
[Sergey Semenov - Adam Coon](https://commons.wikimedia.org/wiki/File:Sergey_Semenov_-_Adam_Coon.ogv)).
15 senior bouts, mostly Greco-Roman, one freestyle:

- Sergey Semenov - Adam Coon
- Artem Surkov - Davor Stefanek
- Sergey Emelin - Victor Ciobanu; Victor Ciobanu - Sergey Jemelin
- Zhan Beleniuk - Metehan Basar
- Sailike Walihan - Aidos Sultangali; Kristian Fris - Szailike Waiha; Aidosz Sultangali - Ri Seung
- Viktor Nemes - Bjurberg Kessidis; Balázs Kiss - Laokratisz Keszidisz
- Joseph Colon - Mohammadbagher Eszmaeil Jakhkeshi
- Kim Hjonu - Bilan Nalgiev
- Tamás Lőrincz - Aleksandr Chekhirkin
- Zaurbek Szidakov - Avtandil Kentcsadze (freestyle)
- Heiki Nabi - Oscar Pino Hinds

His category ([Films by Elekes Andor](https://commons.wikimedia.org/wiki/Category:Films_by_Elekes_Andor),
548 files, mostly swimming) may hold more.

**Folk wrestling** (licence to check per file):

- Ekpeye wrestling, Nigeria — about 10 clips ("ETA Ekpeye 13–22", "Eta (Ekpeye wrestling match)")
- Ijaw wrestlers, Bayelsa state, Nigeria
- Buganda traditional wrestling (Ekigwo), Uganda — 3 clips
- Traditional African wrestling
- Naga wrestling, Nagaland, India — 2 clips
- Rakhine Kyin wrestling, Mrauk-U, Myanmar (2017)
- Mysore Dasara kushti (2018)
- Glima, Iceland — 2 clips ("Islanda lukto")
- Sambo — "Sambo-2014", a 2015 combat sambo championship
- Sumo — stable practice (Tokyo 2014), butsukari-geiko (collision training, 2022), Grand Sumo
  January 2022 day 11 (4 clips), Paris 2026 tournament

Searches for oil wrestling, schwingen, bökh and kurash found no videos on Commons.

### US government (public domain)

[DVIDS](https://www.dvidshub.net) hosts US military media, generally public domain. Army World Class
Athlete Program Greco-Roman wrestlers appear, but what we found is interviews and features
(for example [Day in the Life: Soldier Athlete](https://dvidshub.net/video/937700/day-life-soldier-athlete),
[Minuteman Report](https://dvidshub.net/video/414468/minuteman-report-tn-guardsmen-represents-us-wrestling)),
with little continuous bout or drilling footage. Worth a deeper search of the Armed Forces Wrestling
Championships.

## Needs permission

- **Internet Archive** — [Coach's Syllabus for Greco-Roman Wrestling](https://archive.org/details/3-basic-par-terre):
  three instructional videos (15 basic skills, throws, basic par terre); no licence stated, uploader
  ajax_sf. This is exactly the drilling footage we want, but we must find and ask the rights holder.
- **Club and coaching videos** (YouTube, CoachTube and similar): standard copyright unless marked
  Creative Commons; ask the club. A short written permission covers it, and they can send the original
  files.
- High school meets on the Internet Archive (OHSAA, Urbana): minors — **do not use**.

## Not usable

- Olympic and World Championship TV broadcasts (for example the London 2012 and Tokyo 2021 bouts on
  the Internet Archive, the 1984 ABC tapes under CC BY-NC-ND): broadcaster/IOC copyright or
  non-commercial.
- Free stock sites (Mixkit, Coverr): commercial use allowed, but the wrestling clips are pro wrestling,
  MMA and boxing, not grappling throws.
- Pro wrestling: staged, the mechanics differ.

## Quality notes for extraction

- Best: drilling at training, side-on fixed camera, both bodies in frame (the Greco-Roman syllabus).
- The 2018 Worlds bouts are real elite throws, but filmed from the stands with one moving camera; the
  bodies hide each other in clinches.
- Sumo butsukari-geiko and stable practice: clear, repeated pushing and contact.

## Sources

- [Wikimedia Commons media search: wrestling](https://commons.wikimedia.org/w/index.php?search=wrestling+filetype%3Avideo&title=Special:MediaSearch&type=video)
- [File:Sergey Semenov - Adam Coon.ogv](https://commons.wikimedia.org/wiki/File:Sergey_Semenov_-_Adam_Coon.ogv)
- [Category: Films by Elekes Andor](https://commons.wikimedia.org/wiki/Category:Films_by_Elekes_Andor)
- [Internet Archive: Coach's Syllabus for Greco-Roman Wrestling](https://archive.org/details/3-basic-par-terre)
- [DVIDS: Day in the Life: Soldier Athlete](https://dvidshub.net/video/937700/day-life-soldier-athlete)
- [DVIDS: Minuteman Report](https://dvidshub.net/video/414468/minuteman-report-tn-guardsmen-represents-us-wrestling)
- [Mixkit wrestling stock](https://mixkit.co/free-stock-video/wrestling-ring-with-fighters-4289/)
- [Coverr wrestling stock](https://coverr.co/stock-video-footage/wrestling)
