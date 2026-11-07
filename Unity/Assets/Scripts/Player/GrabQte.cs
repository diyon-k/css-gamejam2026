using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blink.Player
{
    /// <summary>
    /// Quick-time event when a zombie grabs you because you blinked. The world freezes (timeScale 0)
    /// until it resolves. Keyboard/mouse: a marker sweeps a bar, press Space or LMB while it's inside
    /// the zone, <see cref="Beats"/> times. Gamepad: press a random A/B/X/Y combo before time runs out.
    /// Any miss, wrong button or timeout fails. Creates itself on first use; no scene setup needed.
    /// </summary>
    public sealed class GrabQte : MonoBehaviour
    {
        const int Beats = 3;
        const float SweepSeconds = 1.1f;
        const int ComboLength = 4;
        const float ComboSeconds = 3f;
        const float BlinkGraceSeconds = 0.5f;

        static GrabQte _instance;
        static float _endedAt = float.NegativeInfinity;

        public static bool Active => _instance != null && _instance._running;

        /// <summary>
        /// True during a QTE and briefly after, so the Space presses and clicks used for it don't register as
        /// blinks or gunshots (Space is also the keyboard blink key).
        /// </summary>
        public static bool BlocksInput => Active || Time.unscaledTime < _endedAt + BlinkGraceSeconds;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _instance = null;
            _endedAt = float.NegativeInfinity;
        }

        /// <summary>Starts a QTE. Returns false if one is already running. Callback arg: passed.</summary>
        public static bool TryBegin(Action<bool> onDone)
        {
            if (Active) return false;
            if (_instance == null) _instance = new GameObject("GrabQte").AddComponent<GrabQte>();
            _instance.Begin(onDone);
            return true;
        }

        static readonly string[] ButtonNames = { "A", "B", "X", "Y" };

        Action<bool> _onDone;
        bool _running, _gamepad;
        float _startedAt;
        PlayerController _player;

        // Keyboard
        int _beat;
        float _zoneStart, _zoneWidth;

        // Gamepad
        readonly int[] _combo = new int[ComboLength];
        int _comboIndex;

        GUIStyle _big, _small;

        void Begin(Action<bool> onDone)
        {
            _onDone = onDone;
            _running = true;
            _startedAt = Time.unscaledTime;
            _gamepad = LastUsedGamepad();
            _beat = 0;
            _comboIndex = 0;
            for (int i = 0; i < ComboLength; i++) _combo[i] = UnityEngine.Random.Range(0, 4);
            NextZone();

            Time.timeScale = 0f;
            _player = FindAnyObjectByType<PlayerController>();
            if (_player != null) _player.InputEnabled = false;
        }

        // ponytail: last-updated device wins; a drifting stick can claim it, compare real button presses if that bites.
        static bool LastUsedGamepad()
        {
            var pad = Gamepad.current;
            if (pad == null) return false;
            double kbm = Math.Max(Keyboard.current?.lastUpdateTime ?? 0, Mouse.current?.lastUpdateTime ?? 0);
            return pad.lastUpdateTime > kbm;
        }

        void NextZone()
        {
            _zoneWidth = 0.2f - 0.04f * _beat; // narrows each beat
            _zoneStart = UnityEngine.Random.Range(0.35f, 0.95f - _zoneWidth);
            _startedAt = Time.unscaledTime;
        }

        float Marker => (Time.unscaledTime - _startedAt) / SweepSeconds;

        void Update()
        {
            if (!_running) return;
            if (_gamepad) UpdateGamepad();
            else UpdateKeyboard();
        }

        void UpdateKeyboard()
        {
            bool pressed = (Keyboard.current?.spaceKey.wasPressedThisFrame ?? false)
                           || (Mouse.current?.leftButton.wasPressedThisFrame ?? false);
            float m = Marker;
            if (pressed)
            {
                if (m < _zoneStart || m > _zoneStart + _zoneWidth) { Finish(false); return; }
                if (++_beat >= Beats) { Finish(true); return; }
                NextZone();
            }
            else if (m > 1f) Finish(false);
        }

        void UpdateGamepad()
        {
            var pad = Gamepad.current;
            if (pad == null) { _gamepad = false; NextZone(); return; } // unplugged mid-QTE: fall back to keyboard

            if (Time.unscaledTime - _startedAt > ComboSeconds) { Finish(false); return; }

            int pressed = pad.buttonSouth.wasPressedThisFrame ? 0
                        : pad.buttonEast.wasPressedThisFrame ? 1
                        : pad.buttonWest.wasPressedThisFrame ? 2
                        : pad.buttonNorth.wasPressedThisFrame ? 3 : -1;
            if (pressed < 0) return;
            if (pressed != _combo[_comboIndex]) { Finish(false); return; }
            if (++_comboIndex >= ComboLength) Finish(true);
        }

        void Finish(bool passed)
        {
            _running = false;
            _endedAt = Time.unscaledTime;
            Time.timeScale = 1f;
            if (_player != null) _player.InputEnabled = true; // Health.Died turns it back off on a fail
            var cb = _onDone;
            _onDone = null;
            cb?.Invoke(passed);
        }

        void OnDestroy()
        {
            if (_running) Time.timeScale = 1f;
            if (_instance == this) _instance = null;
        }

        void OnGUI()
        {
            if (!_running || Event.current.type != EventType.Repaint) return;
            _big ??= new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = true };
            _small ??= new GUIStyle(_big) { fontSize = 18 };

            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.4f, 0f, 0f, 0.35f));
            GUI.Label(new Rect(0, cy - 140f, Screen.width, 50f), "GRABBED!", _big);

            if (_gamepad) DrawCombo(cx, cy);
            else DrawBar(cx, cy);
        }

        void DrawBar(float cx, float cy)
        {
            const float w = 420f, h = 26f;
            var bar = new Rect(cx - w * 0.5f, cy - h * 0.5f, w, h);
            Fill(bar, new Color(0f, 0f, 0f, 0.7f));
            Fill(new Rect(bar.x + w * _zoneStart, bar.y, w * _zoneWidth, h), new Color(0.2f, 0.9f, 0.3f, 0.9f));
            Fill(new Rect(bar.x + w * Mathf.Clamp01(Marker) - 2f, bar.y - 6f, 4f, h + 12f), Color.white);
            GUI.Label(new Rect(0, cy + 30f, Screen.width, 30f), $"SPACE / CLICK in the green   {_beat}/{Beats}", _small);
        }

        void DrawCombo(float cx, float cy)
        {
            const float size = 64f, gap = 16f;
            float x = cx - (ComboLength * size + (ComboLength - 1) * gap) * 0.5f;
            for (int i = 0; i < ComboLength; i++)
            {
                var r = new Rect(x + i * (size + gap), cy - size * 0.5f, size, size);
                Fill(r, i < _comboIndex ? new Color(0.2f, 0.9f, 0.3f, 0.9f) : new Color(0f, 0f, 0f, 0.7f));
                GUI.Label(r, ButtonNames[_combo[i]], _big);
            }
            float left = 1f - (Time.unscaledTime - _startedAt) / ComboSeconds;
            Fill(new Rect(cx - 160f, cy + 50f, 320f * Mathf.Clamp01(left), 6f), Color.white);
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
