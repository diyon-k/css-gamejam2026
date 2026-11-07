using System;
using System.Collections.Generic;
using System.IO;
using Blink.Player;
using Blink.UI;
using NUnit.Framework;
using UnityEngine;

namespace Blink.Tests
{
    public class BlinkUiInputTests
    {
        static InputGlyph G(string path) => BlinkInput.GlyphForPath(path);

        [Test]
        public void FaceButtonsGoByPosition()
        {
            Assert.AreEqual(InputGlyph.Face(PadDirection.Up), G("<Gamepad>/buttonNorth"));
            Assert.AreEqual(InputGlyph.Face(PadDirection.Right), G("<Gamepad>/buttonEast"));
            Assert.AreEqual(InputGlyph.Face(PadDirection.Down), G("<Gamepad>/buttonSouth"));
            Assert.AreEqual(InputGlyph.Face(PadDirection.Left), G("<Gamepad>/buttonWest"));
            // Specific pads map the same way: no brand-specific labels.
            Assert.AreEqual(InputGlyph.Face(PadDirection.Up), G("<DualShockGamepad>/buttonNorth"));
            Assert.AreEqual(InputGlyph.Face(PadDirection.Down), G("<XInputController>/buttonSouth"));
        }

        [Test]
        public void ShouldersTriggersSticksAndDpad()
        {
            Assert.AreEqual(new InputGlyph(GlyphShape.Shoulder, "LB"), G("<Gamepad>/leftShoulder"));
            Assert.AreEqual(new InputGlyph(GlyphShape.Shoulder, "RB"), G("<Gamepad>/rightShoulder"));
            Assert.AreEqual(new InputGlyph(GlyphShape.Trigger, "LT"), G("<Gamepad>/leftTrigger"));
            Assert.AreEqual(new InputGlyph(GlyphShape.Trigger, "RT"), G("<Gamepad>/rightTrigger"));
            Assert.AreEqual(new InputGlyph(GlyphShape.Stick, "L"), G("<Gamepad>/leftStickPress"));
            Assert.AreEqual(new InputGlyph(GlyphShape.Stick, "R"), G("<Gamepad>/rightStick"));
            Assert.AreEqual(new InputGlyph(GlyphShape.DPad, "", PadDirection.Left), G("<Gamepad>/dpad/left"));
            Assert.AreEqual(new InputGlyph(GlyphShape.DPad, "", PadDirection.Up), G("<Gamepad>/dpad/up"));
            Assert.AreEqual(new InputGlyph(GlyphShape.DPad, ""), G("<Gamepad>/dpad"));
            Assert.AreEqual(new InputGlyph(GlyphShape.Pill, "Start"), G("<Gamepad>/start"));
            Assert.AreEqual(GlyphShape.Pill, G("<Gamepad>/somethingNew").Shape);
            Assert.IsTrue(G("<Gamepad>/buttonSouth").IsGamepad);
            Assert.IsFalse(InputGlyph.Key("E").IsGamepad);
        }

        [Test]
        public void KeyboardAndMouseLabels()
        {
            Assert.AreEqual("E", BlinkInput.KeyLabelForPath("<Keyboard>/e"));
            Assert.AreEqual("R", BlinkInput.KeyLabelForPath("<Keyboard>/r"));
            Assert.AreEqual("1", BlinkInput.KeyLabelForPath("<Keyboard>/1"));
            Assert.AreEqual("Space", BlinkInput.KeyLabelForPath("<Keyboard>/space"));
            Assert.AreEqual("Shift", BlinkInput.KeyLabelForPath("<Keyboard>/leftShift"));
            Assert.AreEqual("LMB", BlinkInput.KeyLabelForPath("<Mouse>/leftButton"));
            Assert.AreEqual("F1", BlinkInput.KeyLabelForPath("<Keyboard>/f1"));
        }

        [Test]
        public void PicksTheBindingForTheDevice()
        {
            // Like Player/Interact in the actions asset: grouped bindings.
            var interact = new List<(string, string, bool)>
            {
                ("<Keyboard>/e", ";Keyboard&Mouse", false),
                ("<Gamepad>/buttonNorth", ";Gamepad", false),
            };
            Assert.AreEqual("<Gamepad>/buttonNorth", BlinkInput.PickBindingPath(interact, true));
            Assert.AreEqual("<Keyboard>/e", BlinkInput.PickBindingPath(interact, false));

            // Like PlayerWeapons' reload: built in code, no groups.
            var reload = new List<(string, string, bool)> { ("<Keyboard>/r", null, false), ("<Gamepad>/buttonWest", "", false) };
            Assert.AreEqual("<Gamepad>/buttonWest", BlinkInput.PickBindingPath(reload, true));
            Assert.AreEqual("<Keyboard>/r", BlinkInput.PickBindingPath(reload, false));

            // Composites are skipped; a wildcard path counts by its group; nothing usable gives null.
            var move = new List<(string, string, bool)> { ("Dpad", "", true), ("<Keyboard>/w", ";Keyboard&Mouse", true) };
            Assert.IsNull(BlinkInput.PickBindingPath(move, false));
            var submit = new List<(string, string, bool)> { ("*/{Submit}", "Keyboard&Mouse;Gamepad", false) };
            Assert.AreEqual("*/{Submit}", BlinkInput.PickBindingPath(submit, true));
            Assert.IsNull(BlinkInput.PickBindingPath(new List<(string, string, bool)> { ("<Keyboard>/e", "", false) }, true));
            Assert.IsNull(BlinkInput.PickBindingPath(null, true));
        }

        [Test]
        public void DeviceFollowsRealInputOnly()
        {
            Assert.IsTrue(BlinkInput.NextDevice(false, gamepadActive: true, keyboardMouseActive: false));
            Assert.IsFalse(BlinkInput.NextDevice(true, gamepadActive: false, keyboardMouseActive: true));
            Assert.IsTrue(BlinkInput.NextDevice(true, false, false), "idle keeps the current device");
            Assert.IsFalse(BlinkInput.NextDevice(false, false, false));
            Assert.IsTrue(BlinkInput.NextDevice(true, true, true), "both at once keeps the current device");
            Assert.IsTrue(BlinkInput.IsGamepadPath("<Gamepad>/buttonSouth"));
            Assert.IsTrue(BlinkInput.IsGamepadPath("<DualSenseGamepadHID>/buttonSouth"));
            Assert.IsFalse(BlinkInput.IsGamepadPath("<Keyboard>/e"));
            Assert.IsFalse(BlinkInput.IsGamepadPath("*/{Submit}"));
        }

        [Test]
        public void GrabQteGlyphsMatchTheButtonsItReads()
        {
            // GrabQte reads W/A/S/D and the face buttons by the same positions; the glyph must show that position.
            Assert.AreEqual(PadDirection.Up, GrabQte.PadPosition(GrabKey.Up));
            Assert.AreEqual(PadDirection.Left, GrabQte.PadPosition(GrabKey.Left));
            Assert.AreEqual(PadDirection.Down, GrabQte.PadPosition(GrabKey.Down));
            Assert.AreEqual(PadDirection.Right, GrabQte.PadPosition(GrabKey.Right));
            Assert.AreEqual(G("<Gamepad>/buttonNorth").Direction, GrabQte.PadPosition(GrabKey.Up));
            Assert.AreEqual(G("<Gamepad>/buttonWest").Direction, GrabQte.PadPosition(GrabKey.Left));
            Assert.AreEqual(G("<Gamepad>/buttonSouth").Direction, GrabQte.PadPosition(GrabKey.Down));
            Assert.AreEqual(G("<Gamepad>/buttonEast").Direction, GrabQte.PadPosition(GrabKey.Right));
        }

        [Serializable] class ActionsFile { public ActionMap[] maps; }
        [Serializable] class ActionMap { public string name; public Binding[] bindings; }
        [Serializable] class Binding { public string path, groups, action; public bool isComposite, isPartOfComposite; }

        [Test]
        public void EveryPlayerActionHasAGamepadBinding()
        {
            string path = Path.Combine(Application.dataPath, "InputSystem_Actions.inputactions");
            var file = JsonUtility.FromJson<ActionsFile>(File.ReadAllText(path));
            var player = Array.Find(file.maps, m => m.name == "Player");
            Assert.IsNotNull(player);
            foreach (var action in new[] { "Interact", "Attack", "Previous", "Next", "Jump", "Crouch" })
            {
                var list = new List<(string, string, bool)>();
                foreach (var b in player.bindings)
                    if (b.action == action) list.Add((b.path, b.groups, b.isComposite || b.isPartOfComposite));
                Assert.IsNotNull(BlinkInput.PickBindingPath(list, true), action + " has no gamepad binding");
                Assert.IsNotNull(BlinkInput.PickBindingPath(list, false), action + " has no keyboard/mouse binding");
            }
            // Weapon switching on the bumpers and Y, as PlayerWeapons does it.
            Assert.IsTrue(Array.Exists(player.bindings, b => b.action == "Previous" && b.path == "<Gamepad>/leftShoulder"));
            Assert.IsTrue(Array.Exists(player.bindings, b => b.action == "Next" && b.path == "<Gamepad>/rightShoulder"));
            Assert.IsTrue(Array.Exists(player.bindings, b => b.action == "Next" && b.path == "<Gamepad>/buttonNorth"));

            // Shooter layout: X (West) interacts (and reloads, in code); Y is no longer Interact; A jumps, B crouches.
            var interactPad = new List<(string, string, bool)>();
            foreach (var b in player.bindings)
                if (b.action == "Interact") interactPad.Add((b.path, b.groups, false));
            Assert.AreEqual("<Gamepad>/buttonWest", BlinkInput.PickBindingPath(interactPad, true));
            Assert.IsFalse(Array.Exists(player.bindings, b => b.action == "Interact" && b.path == "<Gamepad>/buttonNorth"));
            Assert.AreEqual(InputGlyph.Face(PadDirection.Left), BlinkInput.GlyphForPath(BlinkInput.PickBindingPath(interactPad, true)));
            Assert.IsTrue(Array.Exists(player.bindings, b => b.action == "Jump" && b.path == "<Gamepad>/buttonSouth"));
            Assert.IsTrue(Array.Exists(player.bindings, b => b.action == "Crouch" && b.path == "<Gamepad>/buttonEast"));
        }

        [Test]
        public void PadXInteractsWhenFocusedElseReloads()
        {
            Assert.AreEqual(Blink.Interaction.PressOwner.Interact, Blink.Interaction.SharedPress.Decide(true));
            Assert.AreEqual(Blink.Interaction.PressOwner.Reload, Blink.Interaction.SharedPress.Decide(false));

            var press = new Blink.Interaction.SharedPress();
            Assert.AreEqual(Blink.Interaction.PressOwner.None, press.Step(false, true), "nothing owned while up");
            Assert.AreEqual(Blink.Interaction.PressOwner.Interact, press.Step(true, true));
            Assert.IsTrue(press.JustPressed);
            Assert.AreEqual(Blink.Interaction.PressOwner.Interact, press.Step(true, false), "losing focus mid-hold doesn't turn it into a reload");
            Assert.IsFalse(press.JustPressed);
            Assert.AreEqual(Blink.Interaction.PressOwner.None, press.Step(false, false));

            Assert.AreEqual(Blink.Interaction.PressOwner.Reload, press.Step(true, false));
            Assert.AreEqual(Blink.Interaction.PressOwner.Reload, press.Step(true, true), "focus appearing mid-hold (a door) doesn't start interacting");
            Assert.AreEqual(Blink.Interaction.PressOwner.None, press.Step(false, true));
            Assert.AreEqual(Blink.Interaction.PressOwner.Interact, press.Step(true, true), "the next press is decided afresh");
        }

        [Test]
        public void OnePressNeverDoesBoth()
        {
            // Over any sequence, each press has exactly one owner from its first frame to release.
            var rng = new System.Random(7);
            var press = new Blink.Interaction.SharedPress();
            var owner = Blink.Interaction.PressOwner.None;
            for (int i = 0; i < 2000; i++)
            {
                bool down = rng.NextDouble() < 0.6, focus = rng.NextDouble() < 0.5;
                var o = press.Step(down, focus);
                if (!down) Assert.AreEqual(Blink.Interaction.PressOwner.None, o);
                else if (press.JustPressed) { Assert.AreNotEqual(Blink.Interaction.PressOwner.None, o); owner = o; }
                else Assert.AreEqual(owner, o);
            }
        }

        [Test]
        public void NoteSeedsAreStableAndDiffer()
        {
            Assert.AreEqual(NotePaper.SeedFor("Note", "Hello"), NotePaper.SeedFor("Note", "Hello"));
            Assert.AreNotEqual(NotePaper.SeedFor("Note", "Hello"), NotePaper.SeedFor("Note", "Goodbye"));
            Assert.AreNotEqual(NotePaper.SeedFor("ab", "c"), NotePaper.SeedFor("a", "bc"));
            Assert.GreaterOrEqual(NotePaper.SeedFor(null, null), 0);
        }

        [Test]
        public void NoteLiftsFromBelowAndSettles()
        {
            const float tilt = 1.5f;
            var start = NotePaper.LiftPose(0f, true, tilt, 0f);
            Assert.AreEqual(1f, start.Drop, 1e-4f, "starts a screen below");
            Assert.Less(start.Scale, 1f);
            Assert.Greater(Mathf.Abs(start.Angle - tilt), 3f, "starts more tilted");

            var rest = NotePaper.LiftPose(1f, true, tilt, 0f);
            Assert.AreEqual(0f, rest.Drop, 1e-4f);
            Assert.AreEqual(1f, rest.Scale, 1e-4f);
            Assert.AreEqual(tilt, rest.Angle, 1e-3f);

            // A small overshoot past the reading position, never a big one.
            float minDrop = 1f;
            for (int i = 0; i <= 100; i++) minDrop = Mathf.Min(minDrop, NotePaper.LiftPose(i / 100f, true, tilt, 0f).Drop);
            Assert.Less(minDrop, 0f);
            Assert.Greater(minDrop, -0.12f);

            // Dropping: still at rest at t=1, gone below at t=0, accelerating down.
            Assert.AreEqual(0f, NotePaper.LiftPose(1f, false, tilt, 0f).Drop, 1e-4f);
            Assert.AreEqual(1f, NotePaper.LiftPose(0f, false, tilt, 0f).Drop, 1e-4f);
            Assert.Less(NotePaper.LiftPose(0.75f, false, tilt, 0f).Drop, 0.25f);

            // Idle sway is tiny and only once it's up.
            for (float t = 0f; t < 10f; t += 0.37f)
            {
                var held = NotePaper.LiftPose(1f, true, tilt, t);
                Assert.Less(Mathf.Abs(held.Angle - tilt), 0.3f);
                Assert.Less(held.Sway.magnitude, 2.5f);
                Assert.AreEqual(Vector2.zero, NotePaper.LiftPose(0.5f, true, tilt, t).Sway);
            }
        }
    }
}
