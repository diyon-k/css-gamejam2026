using System;
using Blink.Interaction;
using UnityEngine;

namespace Blink.World
{
    public enum DoorResult { None, Opened, Closed, Rattled, Unlocked, Blocked, KeyUsed }

    /// <summary>
    /// The state and maths of a hinged door, with no scene objects (unit tested). The angle is signed degrees from closed:
    /// positive swings toward the door's front (its modelled swing side), negative toward its back.
    /// A press opens it away from whoever pressed (so the leaf never sweeps through them), or the other way if that side is
    /// in the way; closes it; rattles it if it is locked. Keys go into the lock as they are brought (the player has one hand,
    /// so one key at a time): each press with keys uses them (<see cref="DoorResult.KeyUsed"/>), and the one that makes
    /// <see cref="KeysRequired"/> unlocks it (<see cref="DoorResult.Unlocked"/>). Every move is refused
    /// (Blocked) when the caller's <c>sweepBlocked(target)</c> says the leaf would hit someone on the way there.
    /// Time is whatever the caller passes to <see cref="Step"/>.
    /// </summary>
    public sealed class DoorModel
    {
        const float Eps = 0.01f;

        /// <summary>How far it opens toward its front (the side it's modelled swinging to).</summary>
        public float OpenAngle { get; }
        /// <summary>How far it opens toward its back. Often less: the hinge sits on the front face, so the leaf meets the jamb.</summary>
        public float BackAngle { get; }
        public float Seconds { get; }
        /// <summary>Ease-out-back strength when opening (see Openable.Ease); closing is a plain ease.</summary>
        public float Overshoot { get; set; } = 0.3f;
        public bool Locked { get; private set; }
        /// <summary>Keys that unlock it. 0 = no key ever does (story lock).</summary>
        public int KeysRequired { get; }
        /// <summary>Keys already turned in the lock.</summary>
        public int KeysUsed { get; private set; }
        /// <summary>How many keys the last press put in the lock (the caller takes them from the player).</summary>
        public int LastKeysUsed { get; private set; }
        public float Angle { get; private set; }
        public float Target { get; private set; }
        public bool IsOpen => Mathf.Abs(Target) > Eps;
        /// <summary>Swinging, or waiting for the hand to reach the handle before it swings.</summary>
        public bool Moving => _p < 1f;
        public float RattleSeconds { get; set; } = 0.35f;
        public float RattleDegrees { get; set; } = 1.8f;
        public float RattleHz { get; set; } = 14f;

        float _from, _p = 1f, _wait, _rattleLeft, _rattleDir = 1f;

        /// <param name="backAngle">Degrees it opens toward its back; negative = the same as <paramref name="openAngle"/>.</param>
        public DoorModel(bool locked, int keysRequired, float openAngle = 90f, float seconds = 0.5f, float backAngle = -1f)
        {
            Locked = locked;
            KeysRequired = Mathf.Max(0, keysRequired);
            OpenAngle = openAngle;
            BackAngle = backAngle < 0f ? openAngle : backAngle;
            Seconds = Mathf.Max(0.01f, seconds);
        }

        /// <summary>Which way to swing so the leaf goes away from <paramref name="side"/> (&gt;0 = front, &lt;0 = back).</summary>
        public static float TargetAwayFrom(float side, float openAngle) => side > Eps ? -openAngle : openAngle;

        /// <summary>The open target away from <paramref name="side"/>, using each side's own angle.</summary>
        public float AwayFrom(float side) => side > Eps ? -BackAngle : OpenAngle;

        public int KeysLeft => Mathf.Max(0, KeysRequired - KeysUsed);
        public bool HasEnoughKeys(int held) => KeysRequired > 0 && held >= KeysLeft;
        public bool CanUnlock(int held) => Locked && HasEnoughKeys(held);

        /// <summary>
        /// The prompt for the player holding <paramref name="held"/> keys: "Locked · 1/3 keys", "Use key · 1/3 keys" while
        /// holding one that won't finish it, "Unlock door" when it will. A one-key door just says "Locked".
        /// </summary>
        public string Prompt(int held) =>
            !Locked ? (IsOpen ? "Close door" : "Open door")
            : KeysRequired == 0 ? "Locked"
            : HasEnoughKeys(held) ? "Unlock door"
            : KeysRequired == 1 ? "Locked"
            : held > 0 ? $"Use key · {KeysUsed}/{KeysRequired} keys"
            : $"Locked · {KeysUsed}/{KeysRequired} keys";

        /// <summary>
        /// One press from someone on <paramref name="side"/> of the door. <paramref name="sweepBlocked"/> (may be null)
        /// answers "would swinging from <see cref="Angle"/> to this target hit someone?". <paramref name="delay"/>: seconds
        /// before the leaf starts to move (the hand reaching the handle).
        /// </summary>
        public DoorResult Interact(float side, int keysHeld, Func<float, bool> sweepBlocked, float delay = 0f)
        {
            LastKeysUsed = 0;
            if (Locked)
            {
                Rattle(side);
                if (KeysRequired == 0 || keysHeld <= 0) return DoorResult.Rattled;
                LastKeysUsed = Mathf.Min(keysHeld, KeysLeft);
                KeysUsed += LastKeysUsed;
                if (KeysLeft > 0) return DoorResult.KeyUsed;
                Locked = false;
                return DoorResult.Unlocked;
            }
            if (IsOpen)
            {
                if (sweepBlocked != null && sweepBlocked(0f)) return DoorResult.Blocked;
                return Close(delay) ? DoorResult.Closed : DoorResult.None;
            }
            return Open(side, sweepBlocked, delay) ? DoorResult.Opened : DoorResult.Blocked;
        }

        /// <summary>
        /// Opens away from <paramref name="side"/>, or the other way if that sweep is blocked. False if locked or both
        /// ways are blocked.
        /// </summary>
        public bool Open(float side, Func<float, bool> sweepBlocked = null, float delay = 0f)
        {
            if (!TryPickOpenTarget(side, sweepBlocked, out float target)) return false;
            SetTarget(target, delay);
            return true;
        }

        /// <summary>The open target <see cref="Open"/> would use, without moving. False if locked or both ways are blocked.</summary>
        public bool TryPickOpenTarget(float side, Func<float, bool> sweepBlocked, out float target)
        {
            target = 0f;
            if (Locked) return false;
            float away = AwayFrom(side), other = away > 0f ? -BackAngle : OpenAngle;
            if (sweepBlocked == null || !sweepBlocked(away)) { target = away; return true; }
            if (!sweepBlocked(other)) { target = other; return true; }
            return false;
        }

        public bool Close(float delay = 0f)
        {
            if (Locked || !IsOpen) return false;
            SetTarget(0f, delay);
            return true;
        }

        public void Unlock() => Locked = false;

        /// <summary>Puts keys in the lock without a press (a save, or a puzzle that hands one over).</summary>
        public void SetKeysUsed(int used) => KeysUsed = Mathf.Clamp(used, 0, KeysRequired);

        /// <summary>Jump to a state with no animation. 0 = closed.</summary>
        public void Snap(float angleSigned)
        {
            Angle = Target = _from = angleSigned;
            _p = 1f;
            _wait = 0f;
        }

        void SetTarget(float t, float delay)
        {
            _from = Angle;
            Target = t;
            _p = 0f;
            _wait = Mathf.Max(0f, delay);
        }

        public void Rattle(float side)
        {
            _rattleLeft = RattleSeconds;
            _rattleDir = side > 0f ? -1f : 1f; // knocks away from whoever is trying it
        }

        public void Step(float dt)
        {
            _rattleLeft = Mathf.Max(0f, _rattleLeft - dt);
            if (_p >= 1f) return;
            if (_wait > 0f)
            {
                _wait -= dt;
                if (_wait > 0f) return;
                dt = -_wait; // the rest of this step swings
                _wait = 0f;
            }
            _p = Mathf.Min(1f, _p + dt / Seconds);
            bool opening = Mathf.Abs(Target) > Mathf.Abs(_from);
            Angle = Mathf.LerpUnclamped(_from, Target, Openable.Ease(_p, opening ? Overshoot : 0f));
            if (_p >= 1f) Angle = Target;
        }

        /// <summary>The pose angle: <see cref="Angle"/> plus the rattle knock.</summary>
        public float PoseAngle
        {
            get
            {
                if (_rattleLeft <= 0f) return Angle;
                float k = Mathf.Abs(Mathf.Sin(_rattleLeft * RattleHz * Mathf.PI)) * (_rattleLeft / RattleSeconds);
                return Angle + _rattleDir * RattleDegrees * k;
            }
        }

        public bool Animating => Moving || _rattleLeft > 0f;

        /// <summary>
        /// True if a leaf of <paramref name="length"/> turning about <paramref name="hinge"/> from heading <paramref name="fromHeading"/>
        /// to <paramref name="toHeading"/> (degrees, 0 = +z, positive toward +x; the plan is (x, z)) sweeps over a disc at
        /// <paramref name="p"/> with <paramref name="radius"/>. The turn is the short way round (under 180 degrees).
        /// Someone behind the starting pose (the side the leaf moves away from) is never hit, even touching it.
        /// </summary>
        public static bool SweepHits(Vector2 hinge, float length, float fromHeading, float toHeading, Vector2 p, float radius)
        {
            var v = p - hinge;
            float dist = v.magnitude;
            if (dist > length + radius) return false;
            if (dist <= radius) return true;
            float pad = Mathf.Asin(Mathf.Clamp01(radius / dist)) * Mathf.Rad2Deg;
            float sweep = Mathf.DeltaAngle(fromHeading, toHeading);
            float t = Mathf.DeltaAngle(fromHeading, Heading(v));
            return sweep >= 0f ? t >= 0f && t <= sweep + pad : t <= 0f && t >= sweep - pad;
        }

        /// <summary>Plan (x, z) vector to a heading in degrees: 0 = +z, positive toward +x (a yaw).</summary>
        public static float Heading(Vector2 v) => Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg;
    }
}
