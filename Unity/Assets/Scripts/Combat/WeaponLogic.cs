using System.Collections.Generic;
using UnityEngine;

namespace Blink.Combat
{
    /// <summary>What one <see cref="WeaponSwitchState.Advance"/> call did.</summary>
    public readonly struct WeaponSwitchStep
    {
        /// <summary>The weapon in hand started going down (towards <see cref="To"/>).</summary>
        public readonly bool Started;

        /// <summary>The old weapon is fully down and the new one became active (now rising).</summary>
        public readonly bool Swapped;

        public readonly int From, To;

        public WeaponSwitchStep(bool started, bool swapped, int from, int to)
        {
            Started = started;
            Swapped = swapped;
            From = from;
            To = to;
        }
    }

    /// <summary>
    /// The lower / swap / raise cycle of a weapon switch, by slot index. <see cref="Lowered01"/> goes 0 → 1 while
    /// the old weapon goes down, the active slot swaps at 1, then it goes back to 0 as the new one comes up.
    /// Asking for another slot mid-switch retargets it (from the current height, so nothing snaps); asking for the
    /// one in hand while it's going down brings it back up. The weapon may veto leaving (a swing in progress), in
    /// which case the request waits.
    /// </summary>
    public sealed class WeaponSwitchState
    {
        bool _leaving;

        public WeaponSwitchState(int active)
        {
            Active = active;
            Target = active;
        }

        /// <summary>Slot in hand (or rising into view).</summary>
        public int Active { get; private set; }

        /// <summary>Slot wanted. Equal to <see cref="Active"/> unless a switch is pending or under way.</summary>
        public int Target { get; private set; }

        /// <summary>0 = weapon up and usable, 1 = fully down (out of view).</summary>
        public float Lowered01 { get; private set; }

        /// <summary>True while any part of a switch is pending or playing.</summary>
        public bool IsSwitching => Target != Active || Lowered01 > 0f;

        /// <summary>Asks for <paramref name="slot"/>. Returns false if it is already the target.</summary>
        public bool Request(int slot)
        {
            if (slot == Target) return false;
            Target = slot;
            if (slot == Active) _leaving = false; // changed our mind: raise it again
            return true;
        }

        /// <param name="dt">Seconds since the last call.</param>
        /// <param name="canLeave">False while the weapon in hand must not be put away yet (mid-swing).</param>
        public WeaponSwitchStep Advance(float dt, float lowerSeconds, float raiseSeconds, bool canLeave)
        {
            bool started = false, swapped = false;
            int from = Active;
            dt = Mathf.Max(0f, dt);

            if (Target != Active)
            {
                if (!_leaving)
                {
                    if (Lowered01 <= 0f && !canLeave) return default; // wait for the swing to end
                    _leaving = true;
                    started = true;
                }

                Lowered01 = lowerSeconds > 0f ? Mathf.Min(1f, Lowered01 + dt / lowerSeconds) : 1f;
                if (Lowered01 >= 1f)
                {
                    Active = Target;
                    _leaving = false;
                    swapped = true;
                }
            }
            else if (Lowered01 > 0f)
            {
                Lowered01 = raiseSeconds > 0f ? Mathf.Max(0f, Lowered01 - dt / raiseSeconds) : 0f;
            }

            return new WeaponSwitchStep(started, swapped, from, Target);
        }

        /// <summary>Wraps <paramref name="current"/> + <paramref name="delta"/> into 0..count-1.</summary>
        public static int Cycle(int current, int delta, int count)
        {
            if (count <= 0) return 0;
            int next = (current + delta) % count;
            return next < 0 ? next + count : next;
        }
    }

    /// <summary>One thing a melee sweep touched.</summary>
    public struct MeleeCandidate
    {
        /// <summary>Distance along the sweep, metres.</summary>
        public float Distance;

        /// <summary>It can take damage (a hitbox or anything with an <see cref="IDamageable"/>).</summary>
        public bool Damageable;

        public bool Alive;

        /// <summary>Nothing solid between the eye and the hit point.</summary>
        public bool Visible;

        public MeleeCandidate(float distance, bool damageable, bool alive, bool visible)
        {
            Distance = distance;
            Damageable = damageable;
            Alive = alive;
            Visible = visible;
        }
    }

    /// <summary>Picks what a melee swing hits.</summary>
    public static class MeleeTargeting
    {
        /// <summary>
        /// The nearest living, visible damageable candidate. Failing that, the nearest solid surface (a wall or prop,
        /// reported with <paramref name="isSurface"/> = true, for a thud). -1 if the swing hit nothing.
        /// Dead or hidden damageables never count.
        /// </summary>
        public static int Choose(IReadOnlyList<MeleeCandidate> candidates, out bool isSurface)
        {
            isSurface = false;
            int target = -1, surface = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (c.Damageable)
                {
                    if (c.Alive && c.Visible && (target < 0 || c.Distance < candidates[target].Distance)) target = i;
                }
                else if (surface < 0 || c.Distance < candidates[surface].Distance)
                {
                    surface = i;
                }
            }
            if (target >= 0) return target;
            isSurface = surface >= 0;
            return surface;
        }
    }
}
