# Hit reactions

Getting hit visibly affects both living zombies and dead bodies. Both are new components that only listen to existing events, so no weapon, ragdoll or brain script was changed.

## Living zombies: `ZombieHitReaction` (Mobs, on the Zombie root)

- **Input:** every `Hitbox.Struck` under the zombie (where and which way), paired with the `Health.Damaged` that follows it (how much). Melee blows are flagged through `PlayerWeapons.MeleeHit` (one static subscription shared by every zombie) and land `meleeStrength` (1.5x) harder.
- **Size:** all hits in one frame are summed. The total damage becomes a strength with `HitReactionMath.Strength`: a pistol shot (34 damage) is 1.0, an axe blow (60 damage, then x1.5) is about 2.2. Every hit varies by ±20% in size and ±12° in direction, so repeated hits don't look the same.
- **Flinch:** after the Animator, in `LateUpdate`, additive rotations go on the humanoid bones. They are driven by critically damped springs (`FlinchSpring`), which peak about 0.06 s after the hit and settle in about 0.3 s:
  - **Torso:** spine, chest and neck bend back along the shot. A hit off the centre line also twists the hit side away. The head whips back a beat later.
  - **Arms:** an arm hit, or a chest hit near a shoulder, swings that upper arm back (the forearm flops after it) and pulls the clavicle round.
  - **Legs:** a leg hit buckles the knee, dips the hips up to 12 cm, folds the torso forward and sags it onto that leg.
  - **No drift:** the pose is rebuilt from the animated pose every frame. If the Animator didn't write a bone (for example when it's culled off screen), the old offset is undone first, so nothing builds up.
- **Stagger** (`StaggerState`):
  - The agent's speed drops at once and eases back (a pistol shot slows it by 70% for about 0.6 s, a leg hit by more and for longer).
  - The animation slows by half that, and the body slides back about 25 cm along the shot.
  - It runs at execution order 50, after `MobController`, and only scales the speed `MobController` set that frame. It puts the speed back only if nothing else changed it meanwhile, so the two never fight.
- **Death:** stops at once. `Health.Died`, `Health.IsDead` or `ZombieRagdoll.IsActive` all stop it, and after that it never writes a bone again, so the ragdoll starts from the pose on screen.
- **Time:** it runs on scaled time, so 3x fast-forward speeds it up. It is an `ISkippable`, and a blink skip moves the springs, the stagger and the slide on by the skipped time. The springs are solved exactly, so a 0.5 s skip ends the flinch.
- **Knobs** (Inspector):
  - Strength: `referenceDamage`, `strengthExponent`, `maxStrength`, `meleeStrength`, `variation`, `directionJitter`.
  - Angles: `bendDegrees`, `twistDegrees`, `headDegrees`, `armDegrees`, `shoulderDegrees`, `buckleDegrees`, `hipDip`, `legSagDegrees`, plus a `*PeakSeconds` and a `max*` cap for each.
  - Stagger: `staggerDepth`, `staggerSeconds`, `legStaggerBonus`, `animationSlowdown`, `knockbackSpeed`, `knockbackFade`.
- `React(hitbox, point, direction, damage)` and `MarkMelee()` are public for damage that doesn't go through a hitbox.

## Dead bodies: `CorpseShotReaction` (Mobs, one per scene, creates itself)

- **Gunshots:** the weapons ignore the `Corpse` layer, so for every `PlayerWeapons.RayFired` this re-casts the shot against `Corpse` only, up to where the shot stopped (or its full range on a miss). Without a hub it listens to every `Gun.Fired` instead.
- **The push:** the first corpse limb found gets `AddForceAtPosition(..., Impulse)` along the shot. Most of a downward shot's downward part is taken out, with a little lift added, so a body on the floor moves instead of being pressed into it. The bullet still carries on to its target.
- **Axe:** on `AxeViewModel.StrikeApex` (through `PlayerWeapons.Axe.ViewModel`) a sphere sweep against `Corpse`, out to the axe's reach, shoves the nearest corpse limb. It is processed in `LateUpdate` and stops short of whatever the blade connected with (`MeleeHit`).
- **No double push on the kill:** the killing shot or blow is skipped, because `ZombieRagdoll` already pushed that body. The first report of a body that died this frame counts as the kill.
- **Frozen bodies:** a frozen body is woken (every bone body dynamic again) and frozen again on its own `RagdollSettleClock` (at rest for 0.4 s, or 2.5 s at most). If a body that is still falling gets frozen by its own ragdoll mid-push, it is woken again with its velocities until this clock says it has settled.
- **Blink skips:** it steps physics for the woken bodies, but only when no ragdoll is still falling. Otherwise `RagdollSkipper` already steps the scene, and stepping it again would move everything twice.
- **Setup:** it creates itself after the first scene loads (`RuntimeInitializeOnLoadMethod`, `DontDestroyOnLoad`), so no shared prefab had to change. To tune it, place one in a scene (or on GameSystems) and the bootstrap does nothing.
- **Knobs:** `pistolImpulse` (35 N·s), `axeImpulse` (80), `otherImpulse` (35), `floorDeflect`, `lift`, `axeRadius`, `settleSeconds`, `restSpeed`, `restSeconds`, `maxSimulatedSeconds`. It raises `Pushed(body, point, impulse)` for effects.

Pure maths (`HitReactionMath`, `FlinchSpring`, `StaggerState`) is covered by `Tests/EditMode/HitReactionTests.cs`.
