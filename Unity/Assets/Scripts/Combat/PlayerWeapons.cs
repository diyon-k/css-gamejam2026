using System;
using System.Collections.Generic;
using Blink.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blink.Combat
{
    /// <summary>
    /// The player's weapon hub (on the Player root): owns the loadout (hands, pistol, axe), the input, the
    /// lower/raise switch and which weapon is out, and re-raises every weapon's events as weapon-agnostic ones so
    /// effects, HUD, camera, haptics and audio listen in one place (<see cref="Instance"/>).
    ///
    /// Slots are GameObjects under the camera (<c>Camera/Weapons/Holster/{Hands, Gun, Axe}</c>); only the
    /// one in hand is active. A switch lowers the holster out of view, swaps the active slot at the bottom and
    /// raises the new one (unscaled time: it's the player's hands, outside world time). A reload is dropped when
    /// its weapon is put away; a swing in progress finishes before the switch starts.
    ///
    /// Controls: 1–3 pick a slot, the mouse wheel and the gamepad shoulders or d-pad left/right cycle,
    /// Attack (left click / right trigger) uses the weapon, R / gamepad West reloads, G / d-pad down drops the melee
    /// item in hand. The melee slot holds one item at a time (the axe, or a golf club, plank...: see
    /// <see cref="MeleeWeapon.Equipped"/>); with nothing in it the slot is skipped.
    ///
    /// The pistol keeps its own <see cref="Gun"/> events (the pistol view model animator listens to those);
    /// this only turns off its input and calls it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerWeapons : MonoBehaviour
    {
        /// <summary>The player's weapons (the last one enabled).</summary>
        public static PlayerWeapons Instance { get; private set; }

        /// <summary>The keyboard key that drops the melee item, for prompts.</summary>
        public const string DropKeyName = "G";

        [Header("Slots")]
        [SerializeField] Gun pistol;
        [SerializeField] MeleeWeapon axe;
        [Tooltip("Root of the empty-hands view model.")]
        [SerializeField] GameObject hands;
        [Tooltip("Slot order for keys 1, 2, 3 and for cycling. Weapons that aren't assigned are left out.")]
        [SerializeField] WeaponKind[] loadout = { WeaponKind.Hands, WeaponKind.Pistol, WeaponKind.Axe };
        [SerializeField] WeaponKind startWeapon = WeaponKind.Pistol;
        [Tooltip("Off: the pistol slot stays locked until a Pickup with 'Gives Pistol' is found. On, with one hand, the " +
                 "starting pistol has no pickup to drop, so it can never be swapped for anything else.")]
        [SerializeField] bool startsWithPistol;
        [Tooltip("On: the axe starts in the hands. Off: no axe, unless 'Axe Starts At' puts it in the level.")]
        [SerializeField] bool startsWithAxe;
        [Tooltip("Set: the axe starts lying here instead of in the hands (pick it up like any melee prop).")]
        [SerializeField] Transform axeStartsAt;
        [Tooltip("One hand (see Hand): the player holds one thing at a time, so there is no weapon switching. Picking " +
                 "something up swaps it for whatever is held, and G drops it.")]
        [SerializeField] bool oneHanded = true;

        [Header("Switching (unscaled seconds)")]
        [Tooltip("Parent of every slot. Lowered and tilted out of view during a switch. Must not be animated.")]
        [SerializeField] Transform holster;
        [Tooltip("Defaults to the camera under this object.")]
        [SerializeField] Camera aimCamera;
        [SerializeField, Min(0f)] float lowerSeconds = 0.12f;
        [SerializeField, Min(0f)] float raiseSeconds = 0.14f;
        [Tooltip("Metres the holster drops when fully lowered.")]
        [SerializeField] float lowerDrop = 0.16f;
        [Tooltip("Degrees the holster tips down (pitch) and rolls when fully lowered.")]
        [SerializeField] Vector2 lowerPitchRoll = new(30f, -10f);
        [Tooltip("Shortest gap between two mouse-wheel steps, so a fast flick doesn't skip slots.")]
        [SerializeField, Min(0f)] float scrollCooldown = 0.08f;
        [Tooltip("When grabbed, the hands come out to push the zombie off; after breaking free the weapon comes back this long later (lets the shove play).")]
        [SerializeField, Min(0f)] float regrabWeaponDelay = 0.4f;

        [Header("Attack cooldown (world seconds)")]
        [Tooltip("After a melee swing, no melee weapon (axe, picked-up item) can attack again for this long. The pistol ignores it.")]
        [SerializeField, Min(0f)] float attackCooldown = 2f;

        /// <summary>A gun fired (the pistol). Once per trigger pull, after its <see cref="RayFired"/> ray.</summary>
        public event Action<WeaponKind> ShotFired;

        /// <summary>One hitscan ray (one per pistol shot), after its damage. For tracers, blood and hit markers.</summary>
        public event Action<ShotRay> RayFired;

        /// <summary>A melee swing started (whoosh).</summary>
        public event Action<WeaponKind> MeleeSwing;

        /// <summary>A melee swing connected, after its damage. Target null = wall or prop.</summary>
        public event Action<MeleeHitInfo> MeleeHit;

        /// <summary>The trigger was pulled on an empty gun.</summary>
        public event Action<WeaponKind> DryFired;

        public event Action<WeaponKind> ReloadStarted;

        public event Action<WeaponKind> ReloadFinished;

        /// <summary>Args: from, to. The weapon in hand starts going down.</summary>
        public event Action<WeaponKind, WeaponKind> SwitchStarted;

        /// <summary>Args: from, to. The new weapon is now active and coming up (its slot was just enabled).</summary>
        public event Action<WeaponKind, WeaponKind> WeaponSwitched;

        /// <summary>The weapon in hand (or rising into view). Never null once awake.</summary>
        public IWeaponStatus ActiveWeapon => _slots.Count > 0 ? _slots[_switch.Active].Status : HandsStatus.Instance;

        public WeaponKind ActiveKind => ActiveWeapon.Kind;

        /// <summary>True while a switch is pending or the lower/raise is playing (no attacking meanwhile).</summary>
        public bool IsSwitching => _switch.IsSwitching;

        /// <summary>0 = weapon up, 1 = fully lowered.</summary>
        public float Lowered01 => _switch.Lowered01;

        /// <summary>Attack cooldown left, 1 = just attacked, 0 = ready.</summary>
        public float Cooldown01 => attackCooldown > 0f && ActiveKind != WeaponKind.Pistol ?Mathf.Clamp01((_readyAt - Time.time) / attackCooldown) : 0f;

        public int SlotCount => _slots.Count;
        public Gun Pistol => pistol;
        public MeleeWeapon Axe => axe;
        public Transform Holster => holster;

        /// <summary>The reload input (R / gamepad West), so prompts can show its binding for the device in use. Read-only use.</summary>
        public InputAction ReloadInput => _reload;

        /// <summary>The drop input (G / d-pad down), for prompts. Read-only use.</summary>
        public InputAction DropInput => _drop;

        /// <summary>One hand: no weapon switching; a weapon takes the hand like any other item (see <see cref="Interaction.Hand"/>).</summary>
        public bool OneHanded => oneHanded;

        /// <summary>True while the pistol or a melee item is in the hand.</summary>
        public bool HoldsWeapon => (_hasPistol && pistol != null) || (axe != null && axe.Equipped != null);

        /// <summary>The weapon in hand, lower case ("pistol", "axe"), or null.</summary>
        public string HeldWeaponName =>
            axe != null && axe.Equipped != null ? axe.Equipped.DisplayName.ToLowerInvariant()
            : _hasPistol && pistol != null ? (_pistolPickup != null ? _pistolPickup.displayName.ToLowerInvariant() : "pistol")
            : null;

        /// <summary>False if the pistol in hand has no pickup to drop back into the world (the player started with it).</summary>
        public bool CanDropWeapon => !(_hasPistol && pistol != null && _pistolPickup == null);

        readonly List<Slot> _slots = new();
        WeaponSwitchState _switch = new(0);
        IDamageable _owner;
        Vector3 _holsterPosition;
        Quaternion _holsterRotation = Quaternion.identity;
        float _nextScrollTime, _grabDown;
        WeaponKind? _beforeGrab;
        float _restoreAt = float.PositiveInfinity;
        float _readyAt = float.NegativeInfinity;
        bool _subscribed, _brokeFree, _hasPistol = true;
        Interaction.Pickup _pistolPickup; // where the pistol came from; dropping it puts this back on the floor

        InputAction _fire, _reload, _next, _previous, _scroll, _drop;
        InputAction[] _slotKeys = Array.Empty<InputAction>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        void Awake()
        {
            _owner = GetComponent<IDamageable>();
            if (aimCamera == null) aimCamera = GetComponentInChildren<Camera>();
            if (holster != null) holster.GetLocalPositionAndRotation(out _holsterPosition, out _holsterRotation);
            if (pistol != null) pistol.ReadsInput = false; // driven from here
            if (axe != null) axe.Init(); // its slot may start inactive, but it must know what it's holding
            if (axe != null && (axeStartsAt != null || !startsWithAxe) && axe.Drop(null) is { } dropped)
            {
                if (axeStartsAt == null) dropped.gameObject.SetActive(false); // not in this level
                else
                {
                    dropped.transform.SetPositionAndRotation(axeStartsAt.position, axeStartsAt.rotation);
                    var body = dropped.GetComponent<Rigidbody>(); // interpolated: move the body too, or it snaps back
                    body.position = axeStartsAt.position;
                    body.rotation = axeStartsAt.rotation;
                    body.linearVelocity = body.angularVelocity = Vector3.zero;
                    if (axeStartsAt.GetComponentInParent<Interaction.Openable>() != null)
                    {
                        // in a drawer: held still and riding it, and a child of it so the look ray finds it inside
                        body.isKinematic = true;
                        body.interpolation = RigidbodyInterpolation.None;
                        dropped.transform.SetParent(axeStartsAt, true);
                    }
                }
            }

            _hasPistol = startsWithPistol;
            BuildSlots();
            int start = Mathf.Max(0, Has(startWeapon) ? IndexOf(startWeapon) : IndexOf(WeaponKind.Hands));
            _switch = new WeaponSwitchState(start);
            for (int i = 0; i < _slots.Count; i++)
                if (_slots[i].Root != null) _slots[i].Root.SetActive(i == start);

            CreateInput();
        }

        void OnEnable()
        {
            Instance = this;
            Subscribe();
            _fire?.Enable();
            _reload.Enable();
            _next.Enable();
            _previous.Enable();
            _scroll.Enable();
            _drop.Enable();
            foreach (var key in _slotKeys) key.Enable();
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            Unsubscribe();
            _reload.Disable();
            _next.Disable();
            _previous.Disable();
            _scroll.Disable();
            _drop.Disable();
            foreach (var key in _slotKeys) key.Disable();
        }

        void OnDestroy()
        {
            _reload?.Dispose();
            _next?.Dispose();
            _previous?.Dispose();
            _scroll?.Dispose();
            _drop?.Dispose();
            foreach (var key in _slotKeys) key.Dispose();
        }

        void Update()
        {
            bool alive = _owner == null || !_owner.IsDead;
            bool inputFree = alive && !GrabQte.BlocksInput; // clicks and keys belong to the grab QTE
            if (inputFree) ReadSwitchInput();

            if (Time.unscaledTime >= _restoreAt)
            {
                _restoreAt = float.PositiveInfinity;
                if (_beforeGrab is { } kind) Select(kind);
                _beforeGrab = null;
            }

            // Grabbed: the holster stays out of view through the struggle (no arms, just its face); the hands come up
            // on the last press for the break-free shove. Dead: stays down.
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            bool down = (GrabQte.Active && !_brokeFree) || !alive;
            _grabDown = Mathf.MoveTowards(_grabDown, down ? 1f : 0f, dt / Mathf.Max(0.01f, down ? lowerSeconds : raiseSeconds));

            AdvanceSwitch(dt);

            if (!inputFree || _switch.IsSwitching || _slots.Count == 0) return;
            if (Interaction.Carryable.Held != null) return; // hands are full: Attack throws the prop instead
            if (_drop.WasPressedThisFrame()) DropHeld();
            var slot = _slots[_switch.Active];
            if (_fire != null && _fire.WasPressedThisFrame()) Use(slot);
            // Pad X is Reload and Interact: a press that began with something to interact with isn't a reload.
            if (_reload.WasPressedThisFrame() && !Interaction.Interactor.InteractOwns(_reload.activeControl)) Reload(slot);
        }

        /// <summary>Switches to <paramref name="kind"/> if it is in the loadout. False otherwise (or already there).</summary>
        public bool Select(WeaponKind kind)
        {
            int index = IndexOf(kind);
            return index >= 0 && SelectSlot(index);
        }

        /// <summary>Switches to slot <paramref name="index"/> (0-based, loadout order).</summary>
        public bool SelectSlot(int index)
        {
            if (index < 0 || index >= _slots.Count || !Usable(_slots[index])) return false;
            return _switch.Request(index);
        }

        /// <summary>
        /// Selects the next (+1) or previous (-1) slot, wrapping around, from the one already asked for. Skips the
        /// melee slot while it's empty.
        /// </summary>
        public bool Cycle(int direction)
        {
            int step = direction >= 0 ? 1 : -1, index = _switch.Target;
            for (int n = 1; n < _slots.Count; n++)
            {
                index = WeaponSwitchState.Cycle(index, step, _slots.Count);
                if (Usable(_slots[index])) return SelectSlot(index);
            }
            return false;
        }

        /// <summary>Puts a picked-up melee item in the melee slot and brings it out, dropping whatever was in it (with one
        /// hand, whatever was in the hand).</summary>
        public void EquipMelee(Interaction.SwingableProp item)
        {
            if (axe == null || item == null || item.IsHeld) return;
            if (oneHanded && Interaction.Hand.Instance is { } hand) { if (!hand.MakeRoom()) return; }
            else axe.Drop(GetComponent<Collider>()); // swap: the old item falls from the hands, as with G
            axe.Equip(item);
            Select(WeaponKind.Axe);
        }

        /// <summary>
        /// G / d-pad down. With one hand: drops whatever is held (an item, the pistol or a melee item). Otherwise drops the
        /// melee item while it's out.
        /// </summary>
        public void DropHeld()
        {
            if (!oneHanded) { DropMelee(); return; }
            if (Interaction.Hand.Instance is { Item: not null } hand) { hand.DropItem(); return; }
            if (axe != null && axe.IsSwinging) return; // the swing plays out first
            DropWeapon();
        }

        /// <summary>
        /// Lets go of the weapon in hand (one-hand rule): a melee item falls from the hands, the pistol goes back on the floor
        /// as its pickup (ammo kept). Then the empty hands come up. A pistol without a pickup stays (see <see cref="CanDropWeapon"/>).
        /// </summary>
        public void DropWeapon()
        {
            bool dropped = axe != null && axe.Drop(GetComponent<Collider>()) != null;
            if (_hasPistol && pistol != null && _pistolPickup != null)
            {
                pistol.CancelReload();
                _hasPistol = false;
                var eye = aimCamera != null ? aimCamera.transform : transform;
                _pistolPickup.PlaceAt(Interaction.Hand.DropPoint(eye));
                _pistolPickup = null;
                dropped = true;
            }
            if (dropped && ActiveKind != WeaponKind.Hands && !Select(WeaponKind.Hands)) Cycle(1);
        }

        /// <summary>Drops the melee item in hand (only while it's out and not mid-swing), then puts the hands up.</summary>
        public void DropMelee()
        {
            if (axe == null || ActiveKind != WeaponKind.Axe || _switch.IsSwitching || axe.IsSwinging) return;
            if (axe.Drop(GetComponent<Collider>()) == null) return;
            if (!Select(WeaponKind.Hands)) Cycle(1);
        }

        bool Usable(Slot slot) => slot.Kind switch
        {
            WeaponKind.Axe => axe.Equipped != null,
            WeaponKind.Pistol => _hasPistol,
            _ => true,
        };

        /// <summary>True if <paramref name="kind"/> is in the loadout (the pistol only once it's been found).</summary>
        public bool Has(WeaponKind kind) => IndexOf(kind) >= 0 && (kind != WeaponKind.Pistol || _hasPistol);

        /// <summary>
        /// A floor pickup: unlocks the pistol (and brings it out) and/or adds reserve rounds. <paramref name="source"/> is the
        /// pickup it came from, put back on the floor if the pistol is dropped.
        /// </summary>
        public void GivePistol(bool unlock, int rounds, Interaction.Pickup source = null)
        {
            if (pistol == null) return;
            pistol.AddAmmo(rounds);
            if (!unlock || _hasPistol) return;
            _hasPistol = true;
            _pistolPickup = source;
            Select(WeaponKind.Pistol);
        }

        /// <summary>
        /// Runs the switch forward by <paramref name="seconds"/> (Update does this every frame on unscaled time).
        /// Public so the switch can be driven in edit mode.
        /// </summary>
        public void AdvanceSwitch(float seconds)
        {
            if (_slots.Count == 0) return;
            var step = _switch.Advance(seconds, lowerSeconds, raiseSeconds, CanLeave(_slots[_switch.Active]));
            if (step.Started)
            {
                PutAway(_slots[step.From]);
                SwitchStarted?.Invoke(_slots[step.From].Kind, _slots[step.To].Kind);
            }
            if (step.Swapped)
            {
                if (_slots[step.From].Root != null) _slots[step.From].Root.SetActive(false);
                if (_slots[step.To].Root != null) _slots[step.To].Root.SetActive(true);
                WeaponSwitched?.Invoke(_slots[step.From].Kind, _slots[step.To].Kind);
            }
            ApplyHolsterPose();
        }

        /// <summary>Attacks with the weapon in hand, as the Attack button does. Ignored mid-switch.</summary>
        public void UseActive()
        {
            if (_slots.Count > 0 && !_switch.IsSwitching) Use(_slots[_switch.Active]);
        }

        /// <summary>Reloads the weapon in hand, as R does. Ignored mid-switch.</summary>
        public void ReloadActive()
        {
            if (_slots.Count > 0 && !_switch.IsSwitching) Reload(_slots[_switch.Active]);
        }

        void Use(Slot slot)
        {
            if (slot.Kind != WeaponKind.Pistol && Time.time < _readyAt) return; // melee cooling down (OnAxeSwung); the pistol has no cooldown, only Gun.fireInterval
            switch (slot.Kind)
            {
                case WeaponKind.Pistol: pistol.TryFire(); break;
                case WeaponKind.Axe: axe.TrySwing(); break;
                // Hands: nothing (a shove could go here).
            }
        }

        void Reload(Slot slot)
        {
            if (slot.Kind == WeaponKind.Pistol) pistol.TryStartReload();
        }

        // A swing in progress plays out before the axe goes down.
        bool CanLeave(Slot slot) => slot.Kind != WeaponKind.Axe || !axe.IsSwinging;

        // Reloads don't survive being put away.
        void PutAway(Slot slot)
        {
            if (slot.Kind == WeaponKind.Pistol) pistol.CancelReload();
        }

        void ReadSwitchInput()
        {
            if (oneHanded) return; // one thing in hand: nothing to switch to
            for (int i = 0; i < _slotKeys.Length; i++)
                if (_slotKeys[i].WasPressedThisFrame()) SelectSlot(i);
            if (_next.WasPressedThisFrame()) Cycle(1);
            if (_previous.WasPressedThisFrame()) Cycle(-1);

            float wheel = _scroll.ReadValue<float>();
            if (Mathf.Abs(wheel) > 0.01f && Time.unscaledTime >= _nextScrollTime)
            {
                _nextScrollTime = Time.unscaledTime + scrollCooldown;
                Cycle(wheel < 0f ? 1 : -1); // wheel down = next, as in most shooters
            }
        }

        void ApplyHolsterPose()
        {
            if (holster == null) return;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Max(_switch.Lowered01, _grabDown));
            holster.SetLocalPositionAndRotation(
                _holsterPosition + Vector3.down * (lowerDrop * t),
                _holsterRotation * Quaternion.Euler(lowerPitchRoll.x * t, 0f, lowerPitchRoll.y * t));
        }

        void BuildSlots()
        {
            _slots.Clear();
            foreach (var kind in loadout)
            {
                if (IndexOf(kind) >= 0) continue; // listed twice
                switch (kind)
                {
                    case WeaponKind.Hands:
                        _slots.Add(new Slot(kind, hands, HandsStatus.Instance));
                        break;
                    case WeaponKind.Pistol when pistol != null:
                        _slots.Add(new Slot(kind, pistol.gameObject, new PistolStatus(pistol)));
                        break;
                    case WeaponKind.Axe when axe != null:
                        _slots.Add(new Slot(kind, axe.gameObject, axe));
                        break;
                }
            }
        }

        int IndexOf(WeaponKind kind)
        {
            for (int i = 0; i < _slots.Count; i++)
                if (_slots[i].Kind == kind) return i;
            return -1;
        }

        void CreateInput()
        {
            _fire = InputSystem.actions?.FindAction("Player/Attack");
            _reload = new InputAction("WeaponReload", InputActionType.Button);
            _reload.AddBinding("<Keyboard>/r");
            _reload.AddBinding("<Gamepad>/buttonWest");
            _next = new InputAction("WeaponNext", InputActionType.Button);
            _next.AddBinding("<Gamepad>/rightShoulder");
            _next.AddBinding("<Gamepad>/dpad/right");
            _next.AddBinding("<Gamepad>/buttonNorth"); // Y swaps to the next weapon, as in most shooters
            _previous = new InputAction("WeaponPrevious", InputActionType.Button);
            _previous.AddBinding("<Gamepad>/leftShoulder");
            _previous.AddBinding("<Gamepad>/dpad/left");
            _scroll = new InputAction("WeaponScroll", InputActionType.PassThrough, "<Mouse>/scroll/y");
            _drop = new InputAction("WeaponDrop", InputActionType.Button, "<Keyboard>/" + DropKeyName.ToLowerInvariant());
            _drop.AddBinding("<Gamepad>/dpad/down");
            _slotKeys = new InputAction[Mathf.Min(_slots.Count, 9)];
            for (int i = 0; i < _slotKeys.Length; i++)
                _slotKeys[i] = new InputAction("WeaponSlot" + (i + 1), InputActionType.Button, "<Keyboard>/" + (i + 1));
        }

        void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            GrabQte.Began += OnGrabBegan;
            GrabQte.Resolved += OnGrabResolved;
            GrabQte.BrokeFree += OnGrabBrokeFree;
            if (pistol != null)
            {
                pistol.Fired += OnPistolFired;
                pistol.DryFired += OnPistolDryFired;
                pistol.ReloadStarted += OnPistolReloadStarted;
                pistol.ReloadFinished += OnPistolReloadFinished;
            }
            if (axe != null)
            {
                axe.Swung += OnAxeSwung;
                axe.Hit += OnMeleeHit;
            }
        }

        void Unsubscribe()
        {
            if (!_subscribed) return;
            _subscribed = false;
            GrabQte.Began -= OnGrabBegan;
            GrabQte.Resolved -= OnGrabResolved;
            GrabQte.BrokeFree -= OnGrabBrokeFree;
            if (pistol != null)
            {
                pistol.Fired -= OnPistolFired;
                pistol.DryFired -= OnPistolDryFired;
                pistol.ReloadStarted -= OnPistolReloadStarted;
                pistol.ReloadFinished -= OnPistolReloadFinished;
            }
            if (axe != null)
            {
                axe.Swung -= OnAxeSwung;
                axe.Hit -= OnMeleeHit;
            }
        }

        void OnPistolFired(bool hit, RaycastHit info)
        {
            // Gun.Fired is raised in the same frame as its raycast, so the camera's centre ray is the shot's ray.
            var ray = aimCamera != null ? aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)) : new Ray(pistol.transform.position, pistol.transform.forward);
            RayFired?.Invoke(new ShotRay(WeaponKind.Pistol, ray, pistol.Range, hit, info, pistol.Muzzle));
            ShotFired?.Invoke(WeaponKind.Pistol);
        }

        // Hands in, out of view, ready to push the zombie off. Selecting during the QTE works: the switch runs on unscaled time.
        void OnGrabBegan()
        {
            _brokeFree = false;
            if (!Has(WeaponKind.Hands)) return;
            _restoreAt = float.PositiveInfinity;
            _beforeGrab ??= _slots[_switch.Target].Kind;
            Select(WeaponKind.Hands);
        }

        // Up for the shove: the raise lands about as the shove reaches the zombie (GrabShovePose.ContactSeconds).
        void OnGrabBrokeFree() => _brokeFree = true;

        void OnGrabResolved(bool passed)
        {
            if (passed) _restoreAt = Time.unscaledTime + regrabWeaponDelay;
            else _beforeGrab = null; // dead
        }

        void OnPistolDryFired() => DryFired?.Invoke(WeaponKind.Pistol);
        void OnPistolReloadStarted() => ReloadStarted?.Invoke(WeaponKind.Pistol);
        void OnPistolReloadFinished() => ReloadFinished?.Invoke(WeaponKind.Pistol);
        void OnAxeSwung()
        {
            _readyAt = Time.time + attackCooldown;
            MeleeSwing?.Invoke(WeaponKind.Axe);
        }
        void OnMeleeHit(MeleeHitInfo hit) => MeleeHit?.Invoke(hit);

        sealed class Slot
        {
            public readonly WeaponKind Kind;
            public readonly GameObject Root;
            public readonly IWeaponStatus Status;

            public Slot(WeaponKind kind, GameObject root, IWeaponStatus status)
            {
                Kind = kind;
                Root = root;
                Status = status;
            }
        }

        /// <summary>The pistol's status, read straight from its <see cref="Gun"/>.</summary>
        sealed class PistolStatus : IWeaponStatus
        {
            readonly Gun _gun;
            public PistolStatus(Gun gun) => _gun = gun;
            public WeaponKind Kind => WeaponKind.Pistol;
            public bool UsesAmmo => true;
            public int AmmoLoaded => _gun.AmmoInMagazine;
            public int AmmoCapacity => _gun.MagazineSize;
            public int AmmoReserve => _gun.ReserveAmmo;
            public bool IsReloading => _gun.IsReloading;
            public float ReloadProgress01 => _gun.ReloadProgress01;
        }

        /// <summary>Empty hands: nothing to count.</summary>
        sealed class HandsStatus : IWeaponStatus
        {
            public static readonly HandsStatus Instance = new();
            public WeaponKind Kind => WeaponKind.Hands;
            public bool UsesAmmo => false;
            public int AmmoLoaded => 0;
            public int AmmoCapacity => 0;
            public int AmmoReserve => 0;
            public bool IsReloading => false;
            public float ReloadProgress01 => 0f;
        }
    }
}
