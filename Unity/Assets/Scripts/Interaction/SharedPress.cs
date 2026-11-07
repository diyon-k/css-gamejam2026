namespace Blink.Interaction
{
    /// <summary>Who one press of a shared button belongs to.</summary>
    public enum PressOwner { None, Interact, Reload }

    /// <summary>
    /// One gamepad button doing two jobs, shooter style: X (West) interacts when something usable is in focus, otherwise it
    /// reloads. The owner is decided once, on the frame the button goes down, and kept until it is released, so a single
    /// press never does both, even if the focus appears or disappears while it is held. Pure, for tests;
    /// <see cref="Interactor"/> runs the live one.
    /// </summary>
    public struct SharedPress
    {
        bool _down;

        /// <summary>Who the current press belongs to (<see cref="PressOwner.None"/> while the button is up).</summary>
        public PressOwner Owner { get; private set; }

        /// <summary>True on the step the button went down.</summary>
        public bool JustPressed { get; private set; }

        /// <summary>The rule: interact when something usable is in focus, else reload.</summary>
        public static PressOwner Decide(bool interactFocus) => interactFocus ? PressOwner.Interact : PressOwner.Reload;

        /// <summary>Advances one frame with the button's state and whether something usable is in focus now.</summary>
        public PressOwner Step(bool down, bool interactFocus)
        {
            JustPressed = down && !_down;
            if (JustPressed) Owner = Decide(interactFocus);
            else if (!down) Owner = PressOwner.None;
            _down = down;
            return Owner;
        }
    }
}
