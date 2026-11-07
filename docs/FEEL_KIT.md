# BLINK: Feel Kit

Sound, music, controller haptics and VFX for BLINK: what to use, where to get it, its licence, and the event in the code it hangs off. Researched 7 Nov 2026; links were opened and licences read on the source page. Prefer **CC0** (no credit). Anything **CC-BY** needs a line in [CREDITS.md](../CREDITS.md).

Hooks refer to the contracts in [ARCHITECTURE.md](ARCHITECTURE.md).

## Setup status

| Item | Status |
|---|---|
| Gamepad haptics (`GamepadHaptics` on Player) | Done (PR #9) |
| `JammedDoor` exposes `IsForcing`, `Progress01`, `Opened` | Done (PR #9) |
| Blood impacts | Done (PR #7) |
| Blink eyelids | Done (PR #8) |
| `Zombie` layer | Done |
| Post Processing on the Player prefab camera | **To do**: the camera has no URP camera data, so it defaults off and no Volume effect shows |
| Global Volume in `House.unity` | **To do** |
| Decal renderer feature on `PC_Renderer` (blood decals) | **To do** |
| Render Objects feature for the eyes-shut red silhouettes | **To do** with that effect |
| Ghost trails: cache zombie positions in `LateUpdate` | `Skipped` fires *after* zombies have moved, so the old position must be cached |
| AudioMixer (`Master › World`, `Master › Player`) | **To do** before wiring sounds |

## Grab these first

- [Kenney Impact Sounds](https://kenney.nl/assets/impact-sounds) + [RPG Audio](https://kenney.nl/assets/rpg-audio) (CC0): footsteps, impacts, doors, creaks
- [PaulMorek Zombie Moans pack](https://freesound.org/people/PaulMorek/packs/12519/) (CC0): idle groans
- [Catastic Zombie Sound Pack Free](https://assetstore.unity.com/packages/audio/sound-fx/zombie-sound-pack-free-version-124430) (Asset Store): 40+ zombie footsteps, bites, bodyfalls
- [Free Firearm Sound Library](https://opengameart.org/content/the-free-firearm-sound-library) (CC0): gun variety
- [Kenney Particle Pack](https://kenney.nl/assets/particle-pack) + [Smoke Particles](https://kenney.nl/assets/smoke-particles) (CC0): muzzle, sparks, smoke
- [Blood Overlay](https://opengameart.org/content/blood-overlay) + [Horror Texture Pack](https://opengameart.org/content/horror-texture-pack) (CC0): decals, screen splatter

Freesound downloads need a free login. Rows marked **LOUD** should sit high in the mix (see Mixing).

## Sound effects

### Gun

| Sound | Asset | Licence | Hook | Notes |
|---|---|---|---|---|
| **Pistol shot + indoor tail** | [Small pistol gunshot indoors](https://freesound.org/people/acidsnowflake/sounds/402789/) | CC0 | `Gun.Fired` | 3.0 s, small-room tail built in |
| **Pistol shot (dry alt)** | [Gunshot_002.wav](https://freesound.org/people/Brokenphono/sounds/344142/) | CC0 | `Gun.Fired` | 0.57 s, 9mm blank |
| **Dry fire** | [handgun-dryfire.wav](https://freesound.org/people/davdud101/sounds/119168/) | CC0 | `Gun.DryFired` | 1.0 s |
| **Reload** | [Handgun Reload Sound Effect](https://opengameart.org/content/handgun-reload-sound-effect) | CC0 | `ReloadStarted / Finished` | Mag drop, insert, rack in one file. Split in Audacity |
| **Bullet into flesh** | [Bullet Blood 4 (JustInvoke)](https://freesound.org/people/JustInvoke/sounds/138481/) | CC0 | `Fired, hit has Health` | 0.22 s, made for bullet-on-flesh. Randomise pitch 0.85–1.15 |
| **Bullet into flesh (alt)** | [SFX_Impact_Bullet_Hit_Body_1](https://freesound.org/people/PoundSoundUK/sounds/719198/) | CC-BY 4.0 | `Fired, hit has Health` | Sharper hit-marker style. Credit PoundSoundUK |
| **Gore squelch layer** | [gore_gory_blood_smash_flesh](https://freesound.org/people/EricsSoundschmiede/sounds/513315/) | CC0 | `Fired, hit has Health` | Layer quietly under the hit. 6:40 of takes to slice |
| **Impact: wood** | [Single rock hitting wood](https://freesound.org/people/worthahep88/sounds/319226/) | CC0 | `Fired, hit world` | Recorded as a bullet-into-wood stand-in |
| **Impact: wood (alt)** | [Kenney Impact Sounds: impactWood_light_000–004](https://kenney.nl/assets/impact-sounds) | CC0 | `Fired, hit world` | Inside the zip. Pitch up 1.3 so it reads as a bullet |
| **Impact: plaster / concrete** | [ricochet.mp3 (cedarstudios)](https://freesound.org/people/cedarstudios/sounds/148840/) | CC0 | `Fired, hit world` | Several ricochets to slice |
| **Impact: concrete (alt)** | [bullet ricochet.wav (aust_paul)](https://freesound.org/people/aust_paul/sounds/30932/) | CC0 | `Fired, hit world` | Hard-surface ricochet |
| **Impact: dust layer** | [Kenney Impact Sounds: impactGeneric_light, impactMining](https://kenney.nl/assets/impact-sounds) | CC0 | `Fired, hit world` | Inside the zip. Layer under the ricochet |

### Blood, player, scares

| Sound | Asset | Licence | Hook | Notes |
|---|---|---|---|---|
| **Blood drip loop** | [Water Drop (willstepp)](https://freesound.org/people/willstepp/sounds/188293/) | CC0 | `gore spots, low HP` | Rhythmic drip loop. Pitch down 0.8 so it sounds thicker |
| **Blood drips (one-shots)** | [Blood_Drips_Tomato.wav](https://freesound.org/people/melissapons/sounds/178156/) | CC0 | `gore spots` | 1:23, slice |
| **Hurt grunt** | [Voice_AdultMale_PainGrunts_09](https://freesound.org/people/MrFossy/sounds/547209/) | CC0 | `Health.Damaged` | Numbered series for variety |
| **Hit thud** | Kenney impactPunch_medium | CC0 | `Health.Damaged` | 5 variants |
| **Death sting** | [DSGNStngr_DeathWhistle01](https://freesound.org/people/InMotionAudio/sounds/652035/) | CC0 | `Health.Died` | 24 s, trim |
| **Death sting (alt)** | [HorrorSting1](https://freesound.org/people/shelbyshark/sounds/513332/) | CC0 | `Health.Died` | 27 s |
| **Grab jump-scare** | [Intense Jumpscare](https://freesound.org/people/Unaxete/sounds/366168/) | CC0 | `FastForwardEnded(true)` | 4.1 s |
| **Grab jump-scare (alt)** | [Horror Stinger/Impacts](https://freesound.org/people/waffle.guy/sounds/867757/) | CC0 | `FastForwardEnded(true)` | Several hits in 10 s |

### Blink, eyes shut, body

| Sound | Asset | Licence | Hook | Notes |
|---|---|---|---|---|
| **Blink skip hit** | [Fast Forward (plasterbrain)](https://freesound.org/people/plasterbrain/sounds/402451/) | CC0 | `TimeSkipController.Skipped` | Tape-stop, fast-forward and glitch noise. Trim the best 0.5 s |
| **Blink skip (alt)** | [Rewind Short (LuKaiX)](https://freesound.org/s/700527/) | CC0 | `Skipped` | 1.8 s tape rewind. Trim, or play it reversed |
| **Blink skip (alt 2)** | [Vinyl rewind (TasmanianPower)](https://freesound.org/people/TasmanianPower/sounds/162493/) | CC0 | `Skipped` | 2.7 s DJ rewind-and-stop |
| **Eyes-shut underwater bed** | [Underwater Beneath Waterfall (Loopable)](https://freesound.org/people/Osiruswaltz/sounds/530167/) | CC0 | `FastForwardStarted` | 16 s loop, route to Player group |
| **Eyes-open gasp** | [man holds breath - gasp](https://freesound.org/people/morganveilleux/sounds/389996/) | CC0 | `FastForwardEnded` | 2.9 s |
| **Heartbeat (retrigger)** | [heartbeat.mp3 (ZHRØ)](https://freesound.org/people/ZHR%C3%98/sounds/551437/) | CC0 | `PlayerAudio` (nearest zombie) | LOUD. Chop one lub-dub, retrigger at BPM |
| **Heartbeat slow** | [Heartbeat 97 BPM](https://freesound.org/people/Cloud-10/sounds/688735/) | CC0 | `eyes shut, far` | LOUD. 9.9 s |
| **Heartbeat fast** | [Racing Panic Heartbeat](https://freesound.org/people/brktkrgll/sounds/856171/) | CC0 | `eyes shut, near` | LOUD. 6.0 s |
| **Breathing calm** | [Breathing, nose, calm](https://freesound.org/people/giddster/sounds/336531/) | CC0 | `idle` | LOUD. 18 s, cut a loop |
| **Breathing panicked** | [Male_Breath_Scared_Frozen_Loop](https://freesound.org/people/Nox_Sound/sounds/554906/) | CC0 | `low HP / near zombies` | LOUD. 7 s loop |

### Doors

| Sound | Asset | Licence | Hook | Notes |
|---|---|---|---|---|
| **Wood straining** | [Slow wooden creaks x5](https://freesound.org/people/peridactyloptrix/sounds/218163/) | CC0 | `JammedDoor.IsForcing` | 5 creaks to slice |
| **Rattle / tugging** | [Trying to open a locked door](https://freesound.org/people/Kodack/sounds/256606/) | CC0 | `JammedDoor.IsForcing` | 6.2 s |
| **Shoulder thumps** | [door wood old heavy kick open](https://freesound.org/people/kyles/sounds/452609/) | CC0 | `JammedDoor.IsForcing` | Kenney impactWood_heavy as extra |
| **Door bursts open** | [Kicking/Forcing/Breaking Wooden Door](https://freesound.org/people/qubodup/sounds/160213/) | CC0 | `JammedDoor.Opened` | 1.0 s |
| **Splinter layer** | [SFX_wood_cracking.wav](https://freesound.org/people/EricsSoundschmiede/sounds/669457/) | CC0 | `JammedDoor.Opened` | 3.4 s |
| **Slow creaky swing** | [Heavy Door Creaking_05](https://freesound.org/people/rambler52/sounds/455324/) | CC0 | `skip scare beats` | Kenney creak1-3 too |
| **Door slam** | [Bedroom Door Slam Open](https://freesound.org/people/tlcolbe/sounds/406641/) | CC0 | `skip scare beats` | LOUD. Kenney doorClose_1-4 too |

### Footsteps and floor

| Sound | Asset | Licence | Hook | Notes |
|---|---|---|---|---|
| **Steps on wood** | [Kenney Impact: footstep_wood x5, RPG footstep00-09](https://kenney.nl/assets/impact-sounds) | CC0 | `PlayerController` | LOUD. Ready to use |
| **Steps on wood, creaky** | [footsteps boots wood planks loose](https://freesound.org/people/alec_mackay/sounds/463669/) | CC0 | `PlayerController` | LOUD. 1:40, slice |
| **Steps on concrete** | [Kenney Impact: footstep_concrete x5](https://kenney.nl/assets/impact-sounds) | CC0 | `PlayerController` | LOUD. Alt: [SecureSubset](https://freesound.org/people/SecureSubset/sounds/813622/) |
| **Random floorboard creak** | [Floor Creaking_High_Near_Mono](https://freesound.org/people/_stubb/sounds/406620/) | CC0 | `ambient timer` | LOUD. Several creaks |

### Water (FloodWater)

| Sound | Asset | Licence | Hook | Notes |
|---|---|---|---|---|
| **Wading + slosh** | [Wading in Shallow Water](https://freesound.org/people/ryansitz/sounds/342932/) | CC0 | `footsteps in water trigger` | 26 s, slice |
| **Wading (alt)** | [Splashing Footsteps Shallow Water](https://freesound.org/people/ChristopherJngs/sounds/861369/) | CC0 | `footsteps in water` | 8.9 s |
| **Drips with reverb** | [Water drops with reverb](https://freesound.org/people/Patrick_Corra/sounds/696438/) | CC0 | `room ambience` | 8.1 s |
| **Trickle loop** | [Tiny trickling forest creek (loopable)](https://freesound.org/people/Mjeno/sounds/405138/) | CC0 | `room ambience` | Low-pass so it sounds indoors |

### Boiler / generator

| Sound | Asset | Licence | Hook | Notes |
|---|---|---|---|---|
| **Startup** | [Honda Generator Start-Up](https://freesound.org/people/Skybonator/sounds/709272/) | CC0 | `generator start` | 44 s incl. pull-start |
| **Startup / power-down** | [Machine Start Up:Power Down](https://freesound.org/people/Taylor_Pro_Sounds/sounds/210333/) | CC0 | `generator start` | 18 s |
| **Charging hum loop** | [electric hum.wav (soundofsong)](https://freesound.org/people/soundofsong/sounds/641335/) | CC0 | `generator charging` | Hum with buzz. Ramp pitch 0.6 → 1.2 with progress |
| **Charging rumble layer** | [Low Rumble.wav (tec_studio)](https://freesound.org/people/tec_studio/sounds/386556/) | CC0 | `generator charging` | Machine rumble under the hum. Fade in with progress |
| **Hum (realistic)** | [hum electric transformer](https://freesound.org/people/kyles/sounds/453445/) | CC0 | `generator charging` | 28.7 s |
| **Powered clunk** | [Small Lever Switch Heavy Toggle](https://freesound.org/people/loganzsound/sounds/877302/) | CC0 | `generator done` | Kenney metalLatch layer |

### Debris, flashlight, ambience

| Sound | Asset | Licence | Hook | Notes |
|---|---|---|---|---|
| **Debris fall / kick** | [Foley_Stones_Falls_Debris](https://freesound.org/people/Nox_Sound/sounds/550342/) | CC0 | `Debris props` | 28 s, slice |
| **Glass crunch** | [Walking on Broken Glass](https://freesound.org/people/RileyB711/sounds/708199/) | CC0 | `glass trigger` | LOUD. Kenney impactGlass_light too |
| **Flashlight click** | [Small_Flashlight_Click_on_Fast](https://freesound.org/people/Rudmer_Rotteveel/sounds/502506/) | CC0 | `Flashlight toggle` | 0.125 s |
| **Flicker buzz** | [Light Buzz Short Loop](https://freesound.org/people/Nox_Sound/sounds/553075/) | CC0 | `flickering lamps` | 20 s loop |
| **Interior wind** | [AMBLM wind interior howling](https://freesound.org/people/LudwigMueller/sounds/202519/) | CC0 | `house bed` | 2:00 |
| **Rain on roof** | [Rain in the attic_1](https://freesound.org/people/o_ciz/sounds/475468/) | CC0 | `house bed` | 3:10 |
| **Distant thunder** | [Distant Thunder Clap_Gentle](https://freesound.org/people/mikala_oidua/sounds/347562/) | CC0 | `random timer` | 18.5 s |

## Zombie vocals

Hang these on `MobController.StateChanged` and `Attacked`, played from a 3D AudioSource on each zombie. Randomise the clip, pitch (0.9–1.1) and volume on every play.


### Vocals and foley

| Sound | Asset | Licence | Hook | Notes |
|---|---|---|---|---|
| **Idle groans (main)** | [Zombie Moans pack (PaulMorek)](https://freesound.org/people/PaulMorek/packs/12519/) | CC0 | `State.Idle` | Whole pack CC0 |
| **Idle groans** | [A Moaning Zombie.wav (kathol)](https://freesound.org/people/kathol/sounds/58626/) | CC0 | `State.Idle` |  |
| **Idle groans** | [Zombie moans (EmoPreben)](https://opengameart.org/content/zombie-moans-01-by-emopreben) | CC0 | `State.Idle` | 3 WAVs |
| **Idle / aggro** | [Zombie noises and moans (ianzazz)](https://opengameart.org/content/zombie-noises-and-moans) | CC0 | `Idle / Chase` | 4 OGGs incl. fast zombie |
| **Idle / aggro / death** | [Zombie pack (Under7dude)](https://freesound.org/people/Under7dude/packs/10129/) | CC0 | `all states` | 7 short clips |
| **Everything, one import** | [Catastic Zombie Sound Pack Free](https://assetstore.unity.com/packages/audio/sound-fx/zombie-sound-pack-free-version-124430) | Royalty-free | `all states` | 40+: voices, bites, bodyfalls, steps |
| **Aggro growl** | [Horrific Zombie Growl](https://freesound.org/people/MrPokephile/sounds/133974/) | CC0 | `Idle → Chase` | 3.9 s |
| **Aggro growls** | [Scary Monsters vol. 1 (giddster)](https://freesound.org/people/giddster/packs/22548/) | CC0 | `Idle → Chase` | Mixed pack: some files CC-BY, check each |
| **Shuffling steps** | [Shuffling 1/2/3 (Starvolt)](https://freesound.org/people/Starvolt/sounds/189231/) | CC0 | `Chase, 3D loop` | 6 takes, IDs 189225–189232 |
| **Bite** | [Zombie Flesh Bites](https://freesound.org/people/MrPokephile/sounds/155973/) | CC0 | `MobController.Attacked` | 14 s, chop into 5–8 |
| **Bite (short)** | [Zombie Bite (Lukeo135)](https://freesound.org/people/Lukeo135/sounds/518155/) | CC0 | `Attacked` | 0.8 s |
| **Death gurgle** | [ZombieDeath.wav (JollyGreenZ)](https://freesound.org/people/JollyGreenZ/sounds/534602/) | CC0 | `State.Dead` | 3.2 s |
| **Body fall on wood** | [Thud / Falling on wooden floor](https://freesound.org/people/Breviceps/sounds/447922/) | CC0 | `State.Dead` | 0.4 s, fits the house |
| **Distant groans** | [Ghost monster voice moaning](https://opengameart.org/content/ghost-monster-voice-moaning-growling) | CC0 | `ambience` | Low-pass for distance |

## Music

Two looping 2D AudioSources started together, Bed and Tension. Each frame, find the nearest zombie in Chase and set `t = Mathf.InverseLerp(maxDist, minDist, d)`, then move Tension's volume toward `t` with `Time.unscaledDeltaTime`. No FMOD or Wwise needed.


### Tracks

| Use | Track | Licence | Trigger | Notes |
|---|---|---|---|---|
| **Drone bed (main)** | [Abandoned passages](https://opengameart.org/content/abandoned-passages-horror-ambience-loop) | CC0 | `eyes open` | Seamless loop |
| **Drone bed (rooms)** | [Ambience Pack 1 Sci-Fi Horror](https://opengameart.org/content/ambience-pack-1-sci-fi-horror) | CC0 | `per room` | 5 loops of about 1 min |
| **Near-silence bed** | [Long Note Three](https://incompetech.com/music/royalty-free/mp3-royaltyfree/Long%20Note%20Three.mp3) | CC-BY 4.0 | `eyes open` | 3:12 dark long note |
| **Tension layer** | [Orchestral Suspense Loop 1](https://freesound.org/people/awrmacd/sounds/387223/) | CC0 | `nearest zombie distance` | 1:30 pulsing strings loop |
| **Tension (alt)** | [Bent and Broken](https://incompetech.com/music/royalty-free/mp3-royaltyfree/Bent%20and%20Broken.mp3) | CC-BY 4.0 | `nearest zombie distance` | 4:28, composer tags it zombie horror |
| **Tension, very close** | [Gathering Darkness](https://incompetech.com/music/royalty-free/mp3-royaltyfree/Gathering%20Darkness.mp3) | CC-BY 4.0 | `zombie very near` | Screeching |
| **Generator finale** | [Some Amount of Evil](https://incompetech.com/music/royalty-free/mp3-royaltyfree/Some%20Amount%20of%20Evil.mp3) | CC-BY 4.0 | `generator starts` | 2:03, intense from bar one |
| **Horde bed** | [Zombie Horde.wav](https://freesound.org/people/MindsetHaunt/sounds/168310/) | CC0 | `finale` | 1:28 crowd, loop under music |
| **Horde hit** | [Zombie Horde.mp3](https://freesound.org/people/thegreatperson/sounds/210826/) | CC0 | `generator starts` | 6 s burst |
| **Title loop** | [Oppressive Gloom](https://incompetech.com/music/royalty-free/mp3-royaltyfree/Oppressive%20Gloom.mp3) | CC-BY 4.0 | `title: Blink to begin` | Loops cleanly |
| **Caught / death sting** | [Horror Surprise](https://freesound.org/people/qubodup/sounds/814048/) | CC0 | `Health.Died` | 10 s |
| **Escape outro** | [Dark Walk](https://incompetech.com/music/royalty-free/mp3-royaltyfree/Dark%20Walk.mp3) | CC-BY 4.0 | `end screen` | 1:26 piano, trim |

## Controller haptics

Built: `Scripts/Effects/GamepadHaptics.cs` on the Player prefab, the **only** thing that calls `Gamepad.SetMotorSpeeds`, from one `Update` on unscaled time. Steady layers (eyes-shut drone and heartbeat, door strain) and one-shot pulses mix by **per-motor max**, so a short tick never cuts a gap in the drone and a big pulse still punches through. A pulse only replaces a running pulse of equal or lower priority. **Strength** (0–2, default 1.6) scales everything; motor output is capped at 1 and rounded to 0.05 steps (only sent on change).

| Event | Hook | Low motor | High motor | Shape | Priority |
|---|---|---|---|---|---|
| **Blink** | `BlinkDetector.Blinked` | 0.1 | 0.6 | 60 ms flat tick | 0 |
| **Eyes-shut drone** | `TimeSkipController.FastForwardStarted → Ended` | 0.08 → 0.16 (nearest zombie out of range → on top of you), swells in over 0.3 s | 0 | steady for the whole fast-forward | layer |
| **Heartbeat (eyes shut)** | every `PlayerAudio.Heartbeat(intensity, bpm)` during fast-forward | lub 0.45 → 1.0, dub 0.6× | lub 0 → 0.3 | lub 80 ms, dub 70 ms starting 180 ms after; squeezed if the period gets too short | layer |
| **Grab interrupts eyes-shut** | `FastForwardEnded(true)` | 1.0 | 1.0 | 250 ms flat, then nothing (even though the eyes are still shut) | layer |
| **Gunshot** | `Gun.Fired` | 0.45 | 0.9 | 90 ms fade | 1 |
| **Dry fire** | `Gun.DryFired` | 0 | 0.3 | 30 ms flat | 0 |
| **Reload done** | `Gun.ReloadFinished` | 0 | 0.45 | 2 × 40 ms, 70 ms gap | 0 |
| **Took damage** | `Health.Damaged` | 0.5–1.0 | 0.3–0.8 | 150–400 ms fade, scaled by damage | 2 |
| **Grabbed (QTE starts)** | `GrabQte.Active` turns on | 1.0 | 1.0 | 450 ms fade | 3 |
| **Forcing door** | `JammedDoor.IsForcing` | 0.1 → 0.5 | 0 → 0.25 | rises with progress | background |
| **Door bursts** | `JammedDoor.Opened` | 0.8 | 0.5 | 250 ms fade | 1 |
| **Generator charging** | `(not built yet)` | 0.1 → 0.5 | 0 → 0.25 | same as door | background |


- **DualSense or DS4:** rumble works over **USB only**; Bluetooth gets nothing (Input System limitation). Xbox pads work over USB or Bluetooth.
- **Adaptive triggers** need a plugin, so skipped.
- **Eyes shut: feel them coming.** `EyesShutRumble` (pure logic, tuned in `GamepadHaptics` > Eyes Shut) runs a low drone for as long as fast-forward runs, plus a lub-dub on each beat of the **same heartbeat you hear**: `PlayerAudio` owns the one heart (`HeartbeatMath` + `HeartbeatClock`: 65 → 170 bpm, nearest living zombie from 15 m to 2 m) and raises `Heartbeat` on every beat, so felt and heard beats land together and nothing is computed twice. No zombie in range = no heartbeat, drone only (silence = safe). Opening your eyes stops it at once. Everything is on real time, so 3× fast-forward doesn't speed the pulse up. Without `PlayerAudio` in the scene you get the drone only.
- **Motors never stop on their own.** The script resets them on disable, application pause, focus loss (Update may not run then), quit, Editor pause and when the active pad changes. The eyes-shut layer also switches itself off if fast-forward ends without telling it.
- **A connected but unused pad still buzzes** during mouse-and-keyboard play.

## Visual effects

VFX Graph isn't installed, so use Shuriken particles plus URP Volumes. Size: S is under an hour, M is a few hours.

### Combat

| Effect | How | Asset | Hook | Size |
|---|---|---|---|---|
| **Blood burst** | **Done** (PR #7): `BloodImpactSpawner` with Vefects blood | see CREDITS.md | `Gun.Fired` | — |
| **Blood decal** | URP Decal Projector, raycast ~2 m past the zombie to the wall or floor. Cap at 40, recycle oldest | [Blood Overlay](https://opengameart.org/content/blood-overlay) CC0 | `Gun.Fired` | S |
| **Death pool + drips** | Decal under the hips growing 0.2 → 1.2 m over 3 s, plus small dripping particles. Unparented so it outlives the corpse | CC0 blood textures | `Health.Died` | S |
| **Muzzle flash** | 1–2 Kenney muzzle sprites for 0.05 s, pooled point light for 0.04 s with no shadows, 3-particle smoke puff. Unscaled | [Kenney Smoke](https://kenney.nl/assets/smoke-particles) CC0 | `Gun.Fired` | S |
| **Wall sparks + dust** | Stretched sparks in bright HDR orange so Bloom catches them, plus grey dust | CC0 Kenney spark, dirt | `Gun.Fired, no Health hit` | S |
| **Shell casings** | Mesh particle with collision and bounce. Skip if short of time | none | `Gun.Fired` | S |

### Time effects

| Effect | How | Asset | Hook | Size |
|---|---|---|---|---|
| **Blink flash** | Second Volume: Chromatic Aberration 1, Lens Distortion −0.3. Set weight to 1, then fade to 0 over 0.35 s unscaled | built-in URP | `Skipped / FastForwardEnded` | S |
| **Ghost trails** | TrailRenderer per zombie: on skip, Clear, then AddPosition(old) and AddPosition(now). Hero version: BakeMesh a fading ghost at the old position | URP Samples: Trail effect | `Skipped + LateUpdate cache` | S / M |
| **Eyes-shut view** | Volume: saturation −100, exposure −2.5, heavy vignette. Render Objects on Zombie layer: red unlit, depth test Always, so zombies show through walls | built-in URP | `FastForwardStarted / Ended` | M |
| **Stare cost** | t from SecondsSinceLastBlink 8 → 14 s drives a Volume (Gaussian DoF, red vignette), a vein overlay and Perlin shake on a child camera pivot | Veins: generate_image | `BlinkDetector` | S / M |

### Player and world

| Effect | How | Asset | Hook | Size |
|---|---|---|---|---|
| **Damage overlay** | Red vignette pulse plus a blood splatter on a screen Image fading over 1.5 s. Base vignette grows as HP drops | [Blood Overlay](https://opengameart.org/content/blood-overlay) CC0 | `Health.Damaged (player)` | S |
| **Door burst dust** | 30 large smoke particles pushed along the door normal, plus wood chips | CC0 Kenney smoke | `JammedDoor.Opened` | S |
| **Water splashes** | Droplets plus a ring sprite at feet or hit.point. Use the Water layer (index 4) | CC0 Kenney | `FloodWater trigger` | S |
| **Generator power-up** | Spark burst plus crackle loop, a flickering light and HDR lamps ramping 0 → 3 | CC0 Kenney sparks | `generator done` | S |

The Unity Particle Pack's exact contents and a free red-vein texture couldn't be confirmed. Generate the veins with the Unity MCP `generate_image` tool.

## Mixing and time

- **LOUD rows** (heartbeat, breathing, door slam, footsteps, floor creaks, glass): normalise to −1 dBFS in Audacity, AudioSource volume 1, Player group +6 dB. Heartbeat and breathing should be the loudest thing in the mix while eyes are shut.
- **Audio ignores `Time.timeScale`.** Leave pitch at 1; at 3× pitch it sounds like a chipmunk. To hint at speed, raise the World group's Pitch to 1.15–1.3 while fast-forwarding.
- **Eyes-shut muffle:** Lowpass Simple on World. Snapshot `Open`: 22 kHz, 0 dB. Snapshot `Shut`: 500–800 Hz, World −10 dB, Player +6 dB. `shut.TransitionTo(0.15f)` on `FastForwardStarted`, `open.TransitionTo(0.25f)` on `FastForwardEnded`.
- **Set the AudioMixer's Update Mode to Unscaled Time**, or snapshot fades run 3× faster with eyes shut.
- **Scaled vs unscaled:** world effects (blood, sparks, splashes, dust) use scaled time. Muzzle flash, Volume fades, camera shake, UI overlays and music fades use unscaled time.
- **Heartbeat audio:** drive it from `EyesShutBegan/Ended`, not fast-forward, because a grab ends fast-forward while the eyes are still shut. Retrigger one lub-dub at a rate set by the nearest zombie's distance.
- **Licences in the repo:** Pixabay, Zapsplat and Sonniss forbid redistributing raw files. If the repo is public, commit only CC0 and CC-BY audio.

## Credits to add if used

```
Music by Kevin MacLeod (incompetech.com), licensed under Creative Commons: By Attribution 4.0
Bullet impact by PoundSoundUK (CC-BY 4.0)            [only if the alt flesh hit is used]
"Dark Ambient Drone" by Tsorthan Grove (CC-BY 4.0)   [only if used]
Horror sounds by Dylan Seibert                       [only if used]
```
