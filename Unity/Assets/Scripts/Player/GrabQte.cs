using System;
using Blink.Combat;
using Blink.Interaction;
using Blink.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Blink.Player
{
    /// <summary>Whatever grabbed you. <see cref="GrabQte"/> pulls the camera onto its face and tells it when to bite, when it's shoved and when it's flung off.</summary>
    public interface IGrabber
    {
        /// <summary>World point the camera is pulled to look at: the zombie's face.</summary>
        Vector3 GrabFacePoint { get; }

        /// <summary>Lunge in and bite. On a steady beat from the moment it grabs you; <paramref name="hurts"/> when you missed a ring, and it should damage you.</summary>
        void GrabBite(bool hurts);

        /// <summary>A good press pushed it back. <paramref name="final"/>: that was the last one and you broke free.</summary>
        void GrabShoved(bool final);

        /// <summary>
        /// You broke free and shoved it off with both hands: fly back a few metres along <paramref name="away"/>
        /// (flat, from you to it) and lie stunned for a while before getting back up. It isn't hurt.
        /// </summary>
        void GrabThrown(Vector3 away);

        /// <summary>It's over and time is running normally again. Hand the body back to its AI (if it isn't already).</summary>
        void GrabResolved(bool escaped);
    }

    /// <summary>
    /// Quick-time event when a zombie grabs you because you blinked. The world freezes (timeScale 0) apart from the
    /// zombie that has you, which keeps lunging at your face on a beat while the camera is yanked round to stare at it.
    /// Whatever you were carrying is dropped and your weapon goes away (<see cref="PlayerWeapons"/> brings the
    /// hands out, and the weapon back once you're free): you fight it off bare-handed.
    /// After a short scare, a white ring appears somewhere on screen and closes onto a red target with a key in its
    /// middle: press that key as they meet (W/A/S/D, or the gamepad's Y/X/A/B face button in the same spot). A hit
    /// shoves the zombie back; the wrong key, bad timing or a ring left to pass means it bites, which costs health with
    /// a red flash, and the next ring follows with a new key. <see cref="Beats"/> hits and you break free: a two-handed
    /// shove flings it a few metres back, where it lies stunned (unhurt) and then gets up and comes for you again, while
    /// time eases through slow motion back to normal. Die from the bites and time eases back over your body instead.
    /// Timing lives in <see cref="GrabRing"/>.
    ///
    /// Drawn with IMGUI in the <see cref="BlinkTheme"/> style (a thin bone ring closing on a key cap inside a thin red
    /// circle, red dots for hits). Runs on unscaled time.
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

        // Escape, seconds after the last hit: the hands' shove reaches full stretch and flings it off while the world is
        // still frozen, then time eases into slow motion as it flies, and back up to full speed.
        const float LaunchAt = 0.08f;
        const float SlowMoScale = 0.35f;
        const float SlowMoUntil = 0.65f;
        const float EscapeEndsAt = 1.25f;
        // Death: hold on the last bite, then ease back.
        const float DeathHoldSeconds = 0.6f;
        const float DeathEndsAt = 1.2f;

        // Look: target radius in 1080p pixels (scaled by BlinkTheme); colours come from the theme.
        const float TargetRadius = 38f;

        enum Phase { Struggle, Escape, Death }

        static GrabQte _instance;
        static float _endedAt = float.NegativeInfinity;
        static readonly Func<float> Random01 = () => UnityEngine.Random.value;

        public static bool Active => _instance != null && _instance._running;

        /// <summary>A correct press that didn't finish the QTE (a shove that hasn't broken free yet).</summary>
        public static event Action Beat;

        /// <summary>The last press landed: the big two-handed shove that flings it off (the hands and rumble play it).</summary>
        public static event Action BrokeFree;

        /// <summary>
        /// True during a QTE and briefly after, so the presses and clicks used for it don't register as
        /// gunshots, and blinks made while mashing it don't skip time.
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
            BrokeFree = null;
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
        bool _running, _gamepad, _launched;
        int _bites;
        float _biteTimer, _endClock, _pulse, _flash, _hud = 1f, _shade = 1f;
        float _baseFixedDeltaTime;
        Vector2 _spot; // normalised screen position of the current target, y down
        PlayerController _player;
        Health _playerHealth;
        CameraFeel _cameraFeel;

        Texture2D _vignette;

        void Begin(IGrabber grabber)
        {
            _grabber = grabber;
            _running = true;
            _phase = Phase.Struggle;
            _launched = false;
            _ring = new GrabRing(Beats, MeetSeconds, WindowSeconds, WindowShrinkPerBeat, GapSeconds, IntroSeconds, Random01);
            _spot = GrabRing.PickSpot(Random01, null, Aspect);
            _bites = 0;
            _biteTimer = 0f; // first bite on the first frame: that's the jump-scare
            _flash = 0f;
            _hud = _shade = 1f;
            _gamepad = LastUsedGamepad();

            _baseFixedDeltaTime = Time.fixedDeltaTime;
            Time.timeScale = 0f;
            Carryable.DropHeld(); // both hands on it; the prop hangs where it was until time runs again
            _player = FindAnyObjectByType<PlayerController>();
            if (_player != null)
            {
                _player.InputEnabled = false;
                _playerHealth = _player.GetComponent<Health>();
                _cameraFeel = _player.GetComponentInChildren<CameraFeel>();
            }
            Began?.Invoke();
        }

        // ponytail: last-updated device wins; a drifting stick can claim it, compare real button presses if that bites.
        // A pad's lastUpdateTime moves with stick noise too, so ask the UI's device tracker, which only counts real input.
        static bool LastUsedGamepad() => Gamepad.current != null && BlinkInput.UsingGamepad;

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
                case Phase.Escape: UpdateEscape(dt); break;
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
                    _grabber.GrabShoved(false);
                    Jolt(1.5f, 0.03f);
                    Beat?.Invoke();
                    _spot = GrabRing.PickSpot(Random01, _spot, Aspect);
                    _biteTimer = Mathf.Max(_biteTimer, 0.5f); // let the shove land before it lunges again
                    break;
                case GrabRing.Result.Escaped:
                    _grabber.GrabShoved(true);
                    Jolt(2f, 0f);
                    BrokeFree?.Invoke();
                    _phase = Phase.Escape;
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

        void UpdateEscape(float dt)
        {
            _endClock += dt;
            bool gone = IsGone(_grabber);
            if (!_launched && _player != null && !gone) _player.TurnTowards(_grabber.GrabFacePoint, LookSharpness, dt);

            if (!_launched && _endClock >= LaunchAt)
            {
                _launched = true;
                if (!gone) _grabber.GrabThrown(Away());
            }

            // Frozen until it's flung, then slow motion while it flies, then up to full speed.
            SetTimeScale(_endClock < LaunchAt ? 0f
                : _endClock < SlowMoUntil ? SlowMoScale * Smooth01((_endClock - LaunchAt) / 0.08f)
                : Mathf.Lerp(SlowMoScale, 1f, Smooth01((_endClock - SlowMoUntil) / (EscapeEndsAt - SlowMoUntil))));

            _hud = 1f - Smooth01((_endClock - LaunchAt) / 0.2f);
            _shade = 1f - Smooth01((_endClock - LaunchAt) / (EscapeEndsAt - LaunchAt));
            if (_endClock >= EscapeEndsAt) Finish();
        }

        /// <summary>Flat direction from you to the zombie: the way the shove sends it.</summary>
        Vector3 Away()
        {
            Vector3 forward = _player != null ? _player.transform.forward : Vector3.forward;
            if (_player == null) return forward;
            Vector3 d = _grabber.GrabFacePoint - _player.transform.position;
            d.y = 0f;
            return d.sqrMagnitude > 1e-4f ? d.normalized : forward;
        }

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

        /// <summary>The face button for a grab key, by position (matches <see cref="ButtonFor"/>).</summary>
        public static PadDirection PadPosition(GrabKey k) => k switch
        {
            GrabKey.Up => PadDirection.Up, GrabKey.Left => PadDirection.Left, GrabKey.Down => PadDirection.Down, _ => PadDirection.Right,
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
            if (_flash > 0f) BlinkTheme.Fill(new Rect(0f, 0f, w, h), BlinkTheme.Fade(BlinkTheme.Accent, _flash));

            if (_hud <= 0f) return;
            if (_phase == Phase.Struggle && _ring.RingVisible) DrawRing(new Vector2(_spot.x * w, _spot.y * h));
            BlinkWidgets.QtePips(_ring.Hits, Beats, _hud);
            if (_phase == Phase.Death) BlinkWidgets.Banner("Bitten", _hud);
        }

        void DrawRing(Vector2 c)
        {
            float a = Mathf.Clamp01(_ring.RingSeconds / 0.1f) * _hud;
            // The label follows the device in use, live: the key on keyboard, the face button by position on a pad.
            _gamepad = BlinkInput.UsingGamepad;
            var glyph = _gamepad ? InputGlyph.Face(PadPosition(_ring.Key)) : InputGlyph.Key(Label(_ring.Key, false));
            BlinkWidgets.QteTarget(c, BlinkTheme.Px(TargetRadius), _ring.RingScale, glyph, a);
        }

        void EnsureAssets()
        {
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
    }
}
