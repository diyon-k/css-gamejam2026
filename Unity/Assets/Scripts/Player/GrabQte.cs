using System;
using Blink.Combat;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Blink.Player
{
    /// <summary>Whatever grabbed you. <see cref="GrabQte"/> pulls the camera onto its face and tells it when to bite, when it's shoved and when it dies.</summary>
    public interface IGrabber
    {
        /// <summary>World point the camera is pulled to look at: the zombie's face.</summary>
        Vector3 GrabFacePoint { get; }

        /// <summary>Lunge in and bite. On a steady beat from the moment it grabs you; <paramref name="hurts"/> when you missed a ring, and it should damage you.</summary>
        void GrabBite(bool hurts);

        /// <summary>
        /// A good press pushed it back. <paramref name="final"/>: that was the last one and you broke free.
        /// <paramref name="pushedBack"/> false: stay in your face for the killing blow (a headbutt) instead of being shoved off.
        /// </summary>
        void GrabShoved(bool final, bool pushedBack = true);

        /// <summary>You broke free and landed the killing blow (shot, axe or headbutt). Die now if it didn't already do it; <paramref name="blowDirection"/> is the way it travelled.</summary>
        void GrabKill(Vector3 blowDirection);

        /// <summary>It's over and time is running normally again. Hand the body back to its AI (if it's still alive).</summary>
        void GrabResolved(bool escaped);
    }

    /// <summary>
    /// Quick-time event when a zombie grabs you because you blinked. The world freezes (timeScale 0) apart from the
    /// zombie that has you, which keeps lunging at your face on a beat while the camera is yanked round to stare at it.
    /// After a short scare, a white ring appears somewhere on screen and closes onto a red target with a key in its
    /// middle: press that key as they meet (W/A/S/D, or the gamepad's Y/X/A/B face button in the same spot). A hit
    /// shoves the zombie back; the wrong key, bad timing or a ring left to pass means it bites, which costs health with
    /// a red flash, and the next ring follows with a new key. <see cref="Beats"/> hits and you break free with whatever
    /// is in your hand: the pistol shoots it point-blank, the axe chops it, bare hands headbutt it (<see cref="Headbutt"/>).
    /// The blow lands as time eases into slow motion, it drops in a burst of blood, and time eases back to normal. Die
    /// from the bites and time eases back over your body instead. Timing lives in <see cref="GrabRing"/>.
    ///
    /// Drawn with IMGUI in the HUD's style (thin white, one blood red, small bold caps). Runs on unscaled time.
    /// Creates itself on first use; no scene setup needed.
    /// </summary>
    public sealed class GrabQte : MonoBehaviour
    {
        // Struggle timing, real seconds.
        const int Beats = 3;
        const float MeetSeconds = 1.2f;            // ring closes onto the target this long after appearing (1.5 s in all)
        const float WindowSeconds = 0.15f;         // ± hit window on the first ring...
        const float WindowShrinkPerBeat = 0.015f;  // ...narrowing a little with each hit
        const float GapSeconds = 0.3f;             // after each ring, before the next one shows
        const float IntroSeconds = 0.6f;           // the scare lands before the first ring shows
        const float BiteSeconds = 0.9f;
        const float BlinkGraceSeconds = 0.5f;
        const float LookSharpness = 14f;           // how hard the camera is yanked onto the face

        // Escape: shove, killing blow in slow motion, ease back to full speed. Seconds after the last hit; a blow that
        // lands later (a slow axe) pushes the rest back with it.
        const float KillShotAt = 0.22f;
        const float SlowMoScale = 0.3f;
        const float SlowMoUntil = 0.95f;
        const float KillEndsAt = 1.5f;
        const float KillDamage = 1000f;
        // Death: hold on the last bite, then ease back.
        const float DeathHoldSeconds = 0.6f;
        const float DeathEndsAt = 1.2f;

        // Look, in pixels like GunHud.
        const float TargetRadius = 28f, TargetThickness = 6f, RingThickness = 2.5f;
        static readonly Color Blood = new(0.85f, 0.15f, 0.12f, 0.95f);
        static readonly Color Shadow = new(0f, 0f, 0f, 0.5f);

        enum Phase { Struggle, Kill, Death }
        enum KillMove { Shot, Chop, Headbutt }

        static GrabQte _instance;
        static float _endedAt = float.NegativeInfinity;
        static readonly Func<float> Random01 = () => UnityEngine.Random.value;

        public static bool Active => _instance != null && _instance._running;

        /// <summary>A correct press that didn't finish the QTE (a shove that hasn't broken free yet).</summary>
        public static event Action Beat;

        /// <summary>
        /// True during a QTE and briefly after, so the Space presses and clicks used for it don't register as
        /// blinks or gunshots (Space is also the keyboard blink key).
        /// </summary>
        public static bool BlocksInput => Active || Time.unscaledTime < _endedAt + BlinkGraceSeconds;

        /// <summary>Raised on each bite lunge; the first one is the jump-scare. Arg: it hurt (you missed). For rumble and sound.</summary>
        public static event Action<bool> Bit;

        /// <summary>A grab QTE started (the world is now frozen). For audio and effects.</summary>
        public static event Action Began;

        /// <summary>A grab QTE ended. Argument: true if the player broke free.</summary>
        public static event Action<bool> Resolved;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _instance = null;
            _endedAt = float.NegativeInfinity;
            Bit = null;
            Began = null;
            Resolved = null;
            Beat = null;
        }

        /// <summary>Starts a QTE for <paramref name="grabber"/>. Returns false if one is already running.</summary>
        public static bool TryBegin(IGrabber grabber)
        {
            if (Active || IsGone(grabber)) return false;
            if (_instance == null) _instance = new GameObject("GrabQte").AddComponent<GrabQte>();
            _instance.Begin(grabber);
            return true;
        }

        IGrabber _grabber;
        GrabRing _ring;
        Phase _phase;
        bool _running, _gamepad, _blowLanded, _swingStarted, _apexSeen;
        int _bites;
        float _biteTimer, _endClock, _pulse, _flash, _hud = 1f, _shade = 1f;
        KillMove _move;
        float _killAt; // when the killing blow lands, seconds into the Kill phase
        float _baseFixedDeltaTime;
        Vector2 _spot; // normalised screen position of the current target, y down
        PlayerController _player;
        Health _playerHealth;
        CameraFeel _cameraFeel;
        PlayerWeapons _weapons;
        Gun _gun;

        Texture2D _vignette;
        GUIStyle _big, _key;

        void Begin(IGrabber grabber)
        {
            _grabber = grabber;
            _running = true;
            _phase = Phase.Struggle;
            _blowLanded = _swingStarted = _apexSeen = false;
            _ring = new GrabRing(Beats, MeetSeconds, WindowSeconds, WindowShrinkPerBeat, GapSeconds, IntroSeconds, Random01);
            _spot = GrabRing.PickSpot(Random01, null, Aspect);
            _bites = 0;
            _biteTimer = 0f; // first bite on the first frame: that's the jump-scare
            _flash = 0f;
            _hud = _shade = 1f;
            _gamepad = LastUsedGamepad();

            _baseFixedDeltaTime = Time.fixedDeltaTime;
            Time.timeScale = 0f;
            _player = FindAnyObjectByType<PlayerController>();
            if (_player != null)
            {
                _player.InputEnabled = false;
                _playerHealth = _player.GetComponent<Health>();
                _cameraFeel = _player.GetComponentInChildren<CameraFeel>();
                _weapons = _player.GetComponent<PlayerWeapons>();
                if (_weapons == null) _weapons = PlayerWeapons.Instance;
                _gun = _weapons != null ? _weapons.Pistol : _player.GetComponentInChildren<Gun>();
            }
            Began?.Invoke();
        }

        /// <summary>How you finish it, from what's in your hand when you break free.</summary>
        KillMove PickKillMove()
        {
            if (_weapons == null) return _gun != null ? KillMove.Shot : KillMove.Headbutt; // no weapon hub (test scenes)
            switch (_weapons.ActiveKind)
            {
                case WeaponKind.Pistol when _gun != null: return KillMove.Shot;
                case WeaponKind.Axe when _weapons.Axe != null && _weapons.Axe.ViewModel != null: return KillMove.Chop;
                default: return KillMove.Headbutt;
            }
        }

        // ponytail: last-updated device wins; a drifting stick can claim it, compare real button presses if that bites.
        static bool LastUsedGamepad()
        {
            var pad = Gamepad.current;
            if (pad == null) return false;
            double kbm = Math.Max(Keyboard.current?.lastUpdateTime ?? 0, Mouse.current?.lastUpdateTime ?? 0);
            return pad.lastUpdateTime > kbm;
        }

        static bool IsGone(IGrabber g) => g == null || (g is UnityEngine.Object o && o == null);

        static float Aspect => Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;

        bool PlayerDead => _playerHealth != null && _playerHealth.IsDead;

        void Update()
        {
            if (!_running) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _pulse = Mathf.MoveTowards(_pulse, 0f, dt / 0.4f);
            _flash = Mathf.MoveTowards(_flash, 0f, dt / 0.4f);

            switch (_phase)
            {
                case Phase.Struggle: UpdateStruggle(dt); break;
                case Phase.Kill: UpdateKill(dt); break;
                case Phase.Death: UpdateDeath(dt); break;
            }
        }

        void UpdateStruggle(float dt)
        {
            if (IsGone(_grabber)) { Finish(); return; } // removed mid-grab: let go
            if (_player != null) _player.TurnTowards(_grabber.GrabFacePoint, LookSharpness, dt);

            if ((_biteTimer -= dt) <= 0f)
            {
                _biteTimer += BiteSeconds;
                Bite(hurts: false, _bites == 0 ? 6f : 2.5f); // the opening lunge hits hardest
            }

            if (_ring.Tick(dt) == GrabRing.Result.TimedOut) { Missed(); return; }
            if (!ReadKey(out var key)) return;

            switch (_ring.Press(key))
            {
                case GrabRing.Result.Hit:
                    _grabber.GrabShoved(false, pushedBack: true);
                    Jolt(1.5f, 0.03f);
                    Beat?.Invoke();
                    _spot = GrabRing.PickSpot(Random01, _spot, Aspect);
                    _biteTimer = Mathf.Max(_biteTimer, 0.5f); // let the shove land before it lunges again
                    break;
                case GrabRing.Result.Escaped:
                    _move = PickKillMove();
                    // The headbutt needs it close; the shot and the swing want room.
                    _grabber.GrabShoved(true, pushedBack: _move != KillMove.Headbutt);
                    if (_move != KillMove.Headbutt) Jolt(2f, 0.05f); // the headbutt draws the head back itself
                    _killAt = _move switch
                    {
                        KillMove.Chop => Mathf.Max(KillShotAt, _weapons.Axe.ViewModel.SecondsToApex),
                        KillMove.Headbutt => Headbutt.ContactSeconds,
                        _ => KillShotAt,
                    };
                    _phase = Phase.Kill;
                    _endClock = 0f;
                    break;
                case GrabRing.Result.Miss:
                    Missed();
                    break;
            }
        }

        /// <summary>A ring was missed: it bites, you lose health and see red. Dead → the death outro.</summary>
        void Missed()
        {
            Bite(hurts: true, 6f);
            _flash = 0.5f;
            _biteTimer = BiteSeconds; // the miss was the bite; don't stack another on top
            _spot = GrabRing.PickSpot(Random01, _spot, Aspect);

            // No Health to take from: the old rule, one miss and you're done.
            if (!PlayerDead && _playerHealth != null) return;
            _ring.Stop();
            _phase = Phase.Death;
            _endClock = 0f;
        }

        void UpdateKill(float dt)
        {
            _endClock += dt;
            bool gone = IsGone(_grabber);
            if (!_blowLanded && _player != null && !gone) _player.TurnTowards(_grabber.GrabFacePoint, LookSharpness, dt);

            if (_move == KillMove.Chop) DriveSwing(dt);
            else if (_move == KillMove.Headbutt && _cameraFeel != null)
            {
                Headbutt.Sample(_endClock, out var offset, out var tilt);
                _cameraFeel.SetScriptedOffset(offset, tilt);
            }

            // Time eases in so the blow's effects and the falling body play in slow motion, then up to full speed.
            float late = _killAt - KillShotAt, slowUntil = SlowMoUntil + late, endsAt = KillEndsAt + late;
            SetTimeScale(_endClock < _killAt ? 0f
                : _endClock < slowUntil ? SlowMoScale * Smooth01((_endClock - _killAt) / 0.08f)
                : Mathf.Lerp(SlowMoScale, 1f, Smooth01((_endClock - slowUntil) / (endsAt - slowUntil))));

            if (!_blowLanded && BlowDue())
            {
                _blowLanded = true;
                Vector3 eye = Eye;
                Vector3 direction = gone ? (_player != null ? _player.transform.forward : Vector3.forward)
                    : (_grabber.GrabFacePoint - eye).normalized;
                switch (_move)
                {
                    case KillMove.Shot: _gun.FireScripted(KillDamage); break; // muzzle flash, recoil, blood; usually the kill
                    case KillMove.Headbutt: if (!gone) HeadbuttHit(eye, direction); break;
                    // Chop: the axe's own strike already hit at the apex (damage, blood, sound, camera).
                }
                if (!gone) _grabber.GrabKill(direction); // makes sure of it
            }

            float exit = Smooth01((_endClock - _killAt) / (endsAt - _killAt));
            _hud = 1f - Smooth01((_endClock - _killAt) / 0.2f);
            _shade = 1f - exit;
            if (_endClock >= endsAt) Finish();
        }

        Vector3 Eye => _cameraFeel != null ? _cameraFeel.transform.position
            : _player != null ? _player.transform.position + Vector3.up * 1.6f : Vector3.zero;

        bool BlowDue() => _move switch
        {
            KillMove.Chop => _apexSeen || _endClock >= _killAt + 0.25f, // fallback if the swing couldn't play
            KillMove.Headbutt => _endClock >= _killAt,                  // forehead meets face
            _ => _endClock >= _killAt + 0.02f,                          // a frame into slow motion, so the effects can move
        };

        /// <summary>
        /// Plays the axe swing so its strike apex lands at <see cref="_killAt"/>. The world is frozen until then, so the
        /// swing is pushed along in real time up to the apex; after it the follow-through plays in the slow motion.
        /// </summary>
        void DriveSwing(float dt)
        {
            var axe = _weapons.Axe;
            var view = axe.ViewModel;
            if (!_swingStarted)
            {
                if (_endClock < _killAt - view.SecondsToApex) return;
                view.CancelSwing(); // a swing frozen mid-way when it grabbed you
                _swingStarted = true;
                // Aim the blade at its head: the sweep alone would take the arms reaching for you.
                var head = IsGone(_grabber) ? null : FaceCollider(out _);
                bool swung = head != null ? axe.TrySwingAt(head) : axe.TrySwing();
                if (!swung) return; // BlowDue's fallback kills it anyway
            }
            if (_apexSeen) return;
            view.Skip(Mathf.Max(0f, dt - Time.deltaTime)); // whatever world time didn't already move it
            if (view.ApexReached) _apexSeen = true;
        }

        /// <summary>
        /// The headbutt connects: finds the zombie's head (any part of it, failing that) right at its face, kills it and
        /// reports a melee hit so the blood, hit marker, camera, rumble and sound all react like an axe hit.
        /// </summary>
        void HeadbuttHit(Vector3 eye, Vector3 direction)
        {
            Vector3 face = _grabber.GrabFacePoint;
            var best = FaceCollider(out var target);
            if (best == null) return; // nothing to bleed; GrabKill still drops it

            Vector3 point = best.ClosestPoint(eye);
            if ((point - eye).sqrMagnitude < 1e-6f) point = face;
            Vector3 normal = (eye - point).sqrMagnitude > 1e-6f ? (eye - point).normalized : -direction;
            target.TakeDamage(KillDamage, point, direction);
            if (_weapons != null)
                _weapons.ReportMeleeHit(new MeleeHitInfo(WeaponKind.Hands, point, normal, direction, best, target, target.IsDead));
        }

        /// <summary>The grabber's head collider (or, failing that, whatever part of it is nearest its face).</summary>
        Collider FaceCollider(out IDamageable target)
        {
            Vector3 face = _grabber.GrabFacePoint;
            int count = Physics.OverlapSphereNonAlloc(face, 0.25f, _overlap, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            Collider best = null;
            target = null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var c = _overlap[i];
                var d = c.GetComponentInParent<IDamageable>();
                if (d == null || ReferenceEquals(d, _playerHealth)) continue;
                float score = (c.ClosestPoint(face) - face).sqrMagnitude - (Hitbox.IsHeadCollider(c) ? 1f : 0f); // head first
                if (score >= bestScore) continue;
                bestScore = score;
                best = c;
                target = d;
            }
            return best;
        }

        readonly Collider[] _overlap = new Collider[16];

        void UpdateDeath(float dt)
        {
            _endClock += dt;
            // No more turning: the camera belongs to the knock-out slump now (WakeUpSequence, on Health.Died).
            float exit = Smooth01((_endClock - DeathHoldSeconds) / (DeathEndsAt - DeathHoldSeconds));
            SetTimeScale(exit);
            _hud = 1f - Smooth01(_endClock / 0.25f);
            _shade = 1f - 0.6f * exit; // leave some dark over the body; the HUD takes over from here
            if (_endClock >= DeathEndsAt) Finish();
        }

        /// <summary>
        /// The grab key pressed this frame, if any. Several at once counts as the wrong one when there is one, so
        /// mashing every key never hits. Also notes which device you're on, for the label.
        /// </summary>
        bool ReadKey(out GrabKey key)
        {
            key = default;
            bool any = false;
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            for (int i = 0; i < 4; i++)
            {
                var k = (GrabKey)i;
                bool typed = kb != null && KeyFor(kb, k).wasPressedThisFrame;
                bool pushed = pad != null && ButtonFor(pad, k).wasPressedThisFrame;
                if (!typed && !pushed) continue;
                _gamepad = pushed && !typed;
                if (!any || k != _ring.Key) key = k;
                any = true;
            }
            return any;
        }

        static KeyControl KeyFor(Keyboard kb, GrabKey k) => k switch
        {
            GrabKey.Up => kb.wKey, GrabKey.Left => kb.aKey, GrabKey.Down => kb.sKey, _ => kb.dKey,
        };

        static ButtonControl ButtonFor(Gamepad pad, GrabKey k) => k switch
        {
            GrabKey.Up => pad.buttonNorth, GrabKey.Left => pad.buttonWest, GrabKey.Down => pad.buttonSouth, _ => pad.buttonEast,
        };

        static string Label(GrabKey k, bool gamepad) => k switch
        {
            GrabKey.Up => gamepad ? "Y" : "W",
            GrabKey.Left => gamepad ? "X" : "A",
            GrabKey.Down => gamepad ? "A" : "S",
            _ => gamepad ? "B" : "D",
        };

        void Bite(bool hurts, float joltDegrees)
        {
            _bites++;
            _pulse = 1f;
            _grabber.GrabBite(hurts);
            Jolt(joltDegrees, 0f);
            Bit?.Invoke(hurts);
        }

        void Jolt(float degrees, float pushBack)
        {
            if (_cameraFeel != null) _cameraFeel.AddJolt(degrees, pushBack);
        }

        /// <summary>Sets the world's speed, keeping physics steps the same length in real time so slow motion stays smooth.</summary>
        void SetTimeScale(float scale)
        {
            Time.timeScale = Mathf.Clamp01(scale);
            Time.fixedDeltaTime = _baseFixedDeltaTime * Mathf.Clamp(scale, 0.05f, 1f);
        }

        void Finish()
        {
            _running = false;
            _endedAt = Time.unscaledTime;
            Time.timeScale = 1f;
            Time.fixedDeltaTime = _baseFixedDeltaTime;
            if (_cameraFeel != null) _cameraFeel.ClearScriptedOffset();
            if (_player != null && !PlayerDead) _player.InputEnabled = true;
            var grabber = _grabber;
            _grabber = null;
            Resolved?.Invoke(_ring.Escaped);
            if (!IsGone(grabber)) grabber.GrabResolved(_ring.Escaped);
        }

        void OnDestroy()
        {
            if (_running)
            {
                Time.timeScale = 1f;
                Time.fixedDeltaTime = _baseFixedDeltaTime;
                if (_cameraFeel != null) _cameraFeel.ClearScriptedOffset();
            }
            if (_instance == this) _instance = null;
            if (_vignette != null) Destroy(_vignette);
        }

        static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        void OnGUI()
        {
            if (!_running || Event.current.type != EventType.Repaint) return;
            EnsureAssets();

            float w = Screen.width, h = Screen.height;
            // Dark vignette that closes in on every bite and leaves the face clear. Red only ever comes from a miss.
            if (_shade > 0f)
            {
                float grow = 0.08f * (1f - _pulse);
                var rect = new Rect(-w * grow, -h * grow, w * (1f + 2f * grow), h * (1f + 2f * grow));
                var old = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.9f * _shade);
                GUI.DrawTexture(rect, _vignette, ScaleMode.StretchToFill, true);
                GUI.color = old;
            }
            if (_flash > 0f) Fill(new Rect(0f, 0f, w, h), new Color(0.6f, 0f, 0f, _flash));

            if (_hud <= 0f) return;
            if (_phase == Phase.Struggle && _ring.RingVisible) DrawRing(new Vector2(_spot.x * w, _spot.y * h));
            DrawPips(w, h);
            if (_phase == Phase.Death)
            {
                _big.normal.textColor = Blood * new Color(1f, 1f, 1f, _hud);
                GUI.Label(new Rect(0f, h * 0.22f, w, 50f), "BITTEN", _big);
            }
        }

        void DrawRing(Vector2 c)
        {
            float a = Mathf.Clamp01(_ring.RingSeconds / 0.1f) * _hud;
            var fade = new Color(1f, 1f, 1f, a);
            Circle(c, TargetRadius, TargetThickness + 3f, Shadow * fade);
            Circle(c, TargetRadius, TargetThickness, Blood * fade);

            float r = TargetRadius * _ring.RingScale;
            Circle(c, r, RingThickness + 2f, Shadow * fade);
            Circle(c, r, RingThickness, fade);

            // The key to press, in the middle of the target, with a drop shadow so it reads over the zombie's face.
            var box = new Rect(c.x - TargetRadius, c.y - TargetRadius, TargetRadius * 2f, TargetRadius * 2f);
            var content = new GUIContent(Label(_ring.Key, _gamepad));
            _key.normal.textColor = Shadow * fade;
            GUI.Label(new Rect(box.x + 1.5f, box.y + 1.5f, box.width, box.height), content, _key);
            _key.normal.textColor = Color.white * fade;
            GUI.Label(box, content, _key);
        }

        void DrawPips(float w, float h)
        {
            const float size = 8f, gap = 6f;
            var fade = new Color(1f, 1f, 1f, _hud);
            float x = w * 0.5f - (Beats * size + (Beats - 1) * gap) * 0.5f, y = h - 56f;
            for (int i = 0; i < Beats; i++)
            {
                var r = new Rect(x + i * (size + gap), y, size, size);
                bool on = i < _ring.Hits;
                Fill(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, r.height + 2f), (on ? Blood : new Color(1f, 1f, 1f, 0.5f)) * fade);
                Fill(r, (on ? Blood : new Color(0f, 0f, 0f, 0.6f)) * fade);
            }
        }

        /// <summary>An anti-aliased circle outline centred on <paramref name="radius"/>.</summary>
        static void Circle(Vector2 centre, float radius, float thickness, Color color)
        {
            float outer = radius + thickness * 0.5f;
            var rect = new Rect(centre.x - outer, centre.y - outer, outer * 2f, outer * 2f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, thickness, outer);
        }

        void EnsureAssets()
        {
            if (_big == null)
            {
                _big = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.white },
                };
                _key = new GUIStyle(_big) { fontSize = 24, padding = new RectOffset(), margin = new RectOffset() };
            }
            if (_vignette != null) return;

            // Clear in the middle, dark at the edges (alpha only; tinted when drawn).
            const int size = 128;
            _vignette = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1.25f, Mathf.Sqrt(dx * dx + dy * dy)));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            _vignette.SetPixels32(pixels);
            _vignette.Apply();
        }

        static void Fill(Rect rect, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
