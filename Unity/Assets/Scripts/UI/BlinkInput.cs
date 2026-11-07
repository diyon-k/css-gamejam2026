using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blink.UI
{
    /// <summary>How a prompt glyph is drawn.</summary>
    public enum GlyphShape
    {
        /// <summary>A keyboard or mouse key: a rounded key cap with its label.</summary>
        Key,
        /// <summary>A gamepad face button: a circle with a four-dot diagram, the button's position filled.</summary>
        FaceButton,
        /// <summary>A bumper: a pill ("LB", "RB").</summary>
        Shoulder,
        /// <summary>A trigger: a pill rounded on top ("LT", "RT").</summary>
        Trigger,
        /// <summary>The d-pad: a circle with a cross, the direction filled.</summary>
        DPad,
        /// <summary>A stick or stick click: a circle with "L" or "R".</summary>
        Stick,
        /// <summary>Anything else on a pad (start, select): a pill with a word.</summary>
        Pill,
    }

    /// <summary>A position on the pad: the face buttons and the d-pad go by position, not by printed letter.</summary>
    public enum PadDirection { None, Up, Right, Down, Left }

    /// <summary>What to draw for one input: shape, label and (for face buttons and the d-pad) which position.</summary>
    public readonly struct InputGlyph : IEquatable<InputGlyph>
    {
        public readonly GlyphShape Shape;
        public readonly string Label;
        public readonly PadDirection Direction;

        public InputGlyph(GlyphShape shape, string label, PadDirection direction = PadDirection.None)
        {
            Shape = shape;
            Label = label ?? "";
            Direction = direction;
        }

        public bool IsGamepad => Shape != GlyphShape.Key;

        public static InputGlyph Key(string label) => new(GlyphShape.Key, label);
        public static InputGlyph Face(PadDirection position) => new(GlyphShape.FaceButton, "", position);

        public bool Equals(InputGlyph other) => Shape == other.Shape && Label == other.Label && Direction == other.Direction;
        public override bool Equals(object obj) => obj is InputGlyph g && Equals(g);
        public override int GetHashCode() => ((int)Shape * 31 + (int)Direction) * 31 + Label.GetHashCode();
        public override string ToString() => $"{Shape}({Label}{(Direction != PadDirection.None ? " " + Direction : "")})";
    }

    /// <summary>
    /// Which device the player is using (keyboard and mouse, or a gamepad), switching live on real input, and the glyph to
    /// show for an <see cref="InputAction"/> on that device, read from the action's actual bindings. Gamepad glyphs are
    /// device-neutral: face buttons and the d-pad by position, "LB"/"RB"/"LT"/"RT" pills, "L"/"R" sticks.
    /// The string-only helpers are pure, for tests.
    /// </summary>
    public static class BlinkInput
    {
        /// <summary>True when the last real input came from a gamepad.</summary>
        public static bool UsingGamepad { get; private set; }

        /// <summary>Raised when <see cref="UsingGamepad"/> flips.</summary>
        public static event Action<bool> DeviceChanged;

        static readonly Dictionary<(InputAction, bool), InputGlyph> Cache = new();
        static readonly List<(string, string, bool)> Scratch = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            UsingGamepad = false;
            DeviceChanged = null;
            Cache.Clear();
            InputSystem.onAfterUpdate -= Poll;
            InputSystem.onAfterUpdate += Poll;
            InputSystem.onActionChange -= OnActionChange;
            InputSystem.onActionChange += OnActionChange;
        }

        /// <summary>Shows glyphs for this device until the next real input says otherwise (previews, tests).</summary>
        public static void SetDevice(bool gamepad)
        {
            if (gamepad == UsingGamepad) return;
            UsingGamepad = gamepad;
            DeviceChanged?.Invoke(gamepad);
        }

        static void OnActionChange(object _, InputActionChange change)
        {
            if (change == InputActionChange.BoundControlsChanged) Cache.Clear();
        }

        static void Poll()
        {
            if (!Application.isPlaying) return;
            bool next = NextDevice(UsingGamepad, GamepadActive(Gamepad.current), KeyboardMouseActive());
            if (next != UsingGamepad) SetDevice(next);
        }

        /// <summary>
        /// The device to show after this input update: whichever was actually used; when both or neither were, no change.
        /// </summary>
        public static bool NextDevice(bool usingGamepad, bool gamepadActive, bool keyboardMouseActive) =>
            gamepadActive == keyboardMouseActive ? usingGamepad : gamepadActive;

        /// <summary>Real gamepad use: a button held, a trigger pulled or a stick pushed past a dead zone (drift ignored).</summary>
        static bool GamepadActive(Gamepad p)
        {
            if (p == null) return false;
            const float stick = 0.35f * 0.35f, trigger = 0.3f;
            return p.leftStick.ReadValue().sqrMagnitude > stick || p.rightStick.ReadValue().sqrMagnitude > stick ||
                   p.leftTrigger.ReadValue() > trigger || p.rightTrigger.ReadValue() > trigger ||
                   p.dpad.ReadValue().sqrMagnitude > 0.25f ||
                   p.buttonSouth.isPressed || p.buttonEast.isPressed || p.buttonWest.isPressed || p.buttonNorth.isPressed ||
                   p.leftShoulder.isPressed || p.rightShoulder.isPressed || p.startButton.isPressed || p.selectButton.isPressed ||
                   p.leftStickButton.isPressed || p.rightStickButton.isPressed;
        }

        static bool KeyboardMouseActive()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.anyKey.isPressed) return true;
            var m = Mouse.current;
            return m != null && (m.leftButton.isPressed || m.rightButton.isPressed || m.middleButton.isPressed ||
                                 m.delta.ReadValue().sqrMagnitude > 9f || m.scroll.ReadValue().sqrMagnitude > 0.01f);
        }

        /// <summary>
        /// The glyph for <paramref name="action"/> on the device in use, from its bindings (overrides included). Falls back to
        /// the keyboard binding when there is no gamepad one, and to <paramref name="fallbackKey"/> when there are none.
        /// </summary>
        public static InputGlyph GlyphFor(InputAction action, string fallbackKey)
        {
            bool pad = UsingGamepad;
            if (action == null) return InputGlyph.Key(fallbackKey);
            if (Cache.TryGetValue((action, pad), out var cached)) return cached;

            Scratch.Clear();
            foreach (var b in action.bindings)
                Scratch.Add((b.effectivePath, b.groups, b.isComposite || b.isPartOfComposite));
            string path = PickBindingPath(Scratch, pad);
            InputGlyph glyph;
            if (pad && path != null) glyph = GlyphForPath(path);
            else
            {
                string keyPath = pad ? PickBindingPath(Scratch, false) : path;
                glyph = keyPath != null ? InputGlyph.Key(KeyLabelForPath(keyPath)) : InputGlyph.Key(fallbackKey);
            }
            Cache[(action, pad)] = glyph;
            return glyph;
        }

        // ------------------------------------------------------------ pure helpers (tested)

        /// <summary>
        /// First binding path for the device: a binding in the "Gamepad" (or "Keyboard&amp;Mouse") control-scheme group, or
        /// one whose path names such a device. Composites and their parts are skipped. Null if none.
        /// </summary>
        public static string PickBindingPath(IReadOnlyList<(string path, string groups, bool composite)> bindings, bool gamepad)
        {
            if (bindings == null) return null;
            foreach (var (path, groups, composite) in bindings)
            {
                if (composite || string.IsNullOrEmpty(path)) continue;
                bool padPath = IsGamepadPath(path);
                bool keyPath = path.StartsWith("<Keyboard>", StringComparison.OrdinalIgnoreCase) ||
                               path.StartsWith("<Mouse>", StringComparison.OrdinalIgnoreCase);
                if (gamepad ? padPath : keyPath) return path;
                if (!padPath && !keyPath && InGroup(groups, gamepad ? "Gamepad" : "Keyboard&Mouse")) return path;
            }
            return null;
        }

        static bool InGroup(string groups, string group)
        {
            if (string.IsNullOrEmpty(groups)) return false;
            foreach (var g in groups.Split(';'))
                if (string.Equals(g.Trim(), group, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>True for paths on a gamepad layout (generic or a specific pad).</summary>
        public static bool IsGamepadPath(string path)
        {
            if (string.IsNullOrEmpty(path) || path[0] != '<') return false;
            int end = path.IndexOf('>');
            if (end < 0) return false;
            string layout = path.Substring(1, end - 1);
            return layout.IndexOf("Gamepad", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   layout.IndexOf("XInputController", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   layout.IndexOf("SwitchProController", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>The control part of a binding path: "&lt;Gamepad&gt;/dpad/up" gives "dpad/up".</summary>
        static string ControlOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            int slash = path.IndexOf('/');
            return slash < 0 ? path : path.Substring(slash + 1);
        }

        /// <summary>The device-neutral gamepad glyph for a binding path.</summary>
        public static InputGlyph GlyphForPath(string path)
        {
            string c = ControlOf(path).ToLowerInvariant();
            switch (c)
            {
                case "buttonnorth": return InputGlyph.Face(PadDirection.Up);
                case "buttoneast": return InputGlyph.Face(PadDirection.Right);
                case "buttonsouth": return InputGlyph.Face(PadDirection.Down);
                case "buttonwest": return InputGlyph.Face(PadDirection.Left);
                case "leftshoulder": return new InputGlyph(GlyphShape.Shoulder, "LB");
                case "rightshoulder": return new InputGlyph(GlyphShape.Shoulder, "RB");
                case "lefttrigger": return new InputGlyph(GlyphShape.Trigger, "LT");
                case "righttrigger": return new InputGlyph(GlyphShape.Trigger, "RT");
                case "dpad": return new InputGlyph(GlyphShape.DPad, "");
                case "dpad/up": return new InputGlyph(GlyphShape.DPad, "", PadDirection.Up);
                case "dpad/right": return new InputGlyph(GlyphShape.DPad, "", PadDirection.Right);
                case "dpad/down": return new InputGlyph(GlyphShape.DPad, "", PadDirection.Down);
                case "dpad/left": return new InputGlyph(GlyphShape.DPad, "", PadDirection.Left);
                case "leftstick":
                case "leftstickpress":
                case "leftstickbutton": return new InputGlyph(GlyphShape.Stick, "L");
                case "rightstick":
                case "rightstickpress":
                case "rightstickbutton": return new InputGlyph(GlyphShape.Stick, "R");
                case "start": return new InputGlyph(GlyphShape.Pill, "Start");
                case "select": return new InputGlyph(GlyphShape.Pill, "Select");
            }
            return new InputGlyph(GlyphShape.Pill, Capitalise(c.Replace('/', ' ')));
        }

        /// <summary>A short key-cap label for a keyboard or mouse binding path: "&lt;Keyboard&gt;/e" gives "E".</summary>
        public static string KeyLabelForPath(string path)
        {
            string c = ControlOf(path);
            switch (c.ToLowerInvariant())
            {
                case "space": return "Space";
                case "leftshift":
                case "rightshift":
                case "shift": return "Shift";
                case "leftctrl":
                case "rightctrl":
                case "ctrl": return "Ctrl";
                case "leftalt":
                case "rightalt":
                case "alt": return "Alt";
                case "tab": return "Tab";
                case "enter":
                case "numpadenter": return "Enter";
                case "escape": return "Esc";
                case "backspace": return "Bksp";
                case "leftbutton": return "LMB";
                case "rightbutton": return "RMB";
                case "middlebutton": return "MMB";
                case "uparrow": return "Up";
                case "downarrow": return "Down";
                case "leftarrow": return "Left";
                case "rightarrow": return "Right";
            }
            if (c.Length == 1) return c.ToUpperInvariant();
            return Capitalise(c);
        }

        static string Capitalise(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
