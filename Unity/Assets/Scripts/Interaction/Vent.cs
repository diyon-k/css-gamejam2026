using System.Collections;
using Blink.Combat;
using Blink.Player;
using Blink.UI;
using Blink.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blink.Interaction
{
    /// <summary>
    /// A crawl route between two rooms (bungalow_layout.VENTS, set up by Editor/BungalowVents.cs). Interact at either end's
    /// trigger box: the screen dips to black and you're in the duct. Forward / back crawls along <see cref="path"/> (eye
    /// points, end 0 to end 1) on real time, like the ladder. At an end, Interact peeks out through the grate (the view goes
    /// dark round a low gap) and Interact again climbs out; back from a peek drops you into the duct again. Getting in or
    /// out clanks the grate (a <see cref="Noise"/>), so clowns nearby come to look.
    /// While <see cref="Inside"/> is set, zombies can't see you (you're under the floor and they can't path there).
    /// </summary>
    public sealed class Vent : MonoBehaviour, IInteractable
    {
        [Tooltip("World eye points from end 0 to end 1.")]
        public Vector3[] path = new Vector3[0];
        [Tooltip("Per end: where the eye stops to look out (over the grate). Equal to the path end = no peek, you climb straight out.")]
        public Vector3[] peek = new Vector3[2];
        [Tooltip("Per end: the world floor point you climb out onto.")]
        public Vector3[] exit = new Vector3[2];
        [Tooltip("Crawl speed, metres per second.")]
        public float crawlSpeed = 0.9f;
        [Tooltip("Head bob while crawling: metres up and down, and bobs per metre.")]
        public float bob = 0.02f, bobsPerMetre = 1.6f;
        [Tooltip("Seconds to fade to black (and back) getting in, out or up to the grate.")]
        public float fadeSeconds = 0.25f;
        [Tooltip("Audible range of the grate clank, metres along the navmesh.")]
        public float clank = 8f;
        [Tooltip("The gap you see through when peeking out, as a share of the screen (wide and low, like under a bed).")]
        public Vector2 peekGap = new Vector2(0.9f, 0.22f);

        /// <summary>The vent the player is in, or null. Zombies treat the player as unseen while set.</summary>
        public static Vent Inside { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Inside = null;

        static readonly object BackPrompt = new object();

        PlayerController _player;
        Transform _eye;
        CharacterController _cc;
        Health _health;
        WeaponKind? _stowed;
        Coroutine _move;
        InputAction _look, _moveAction, _interact, _jump;
        int _entered, _peekEnd = -1;   // the end you came in at; the end you're peeking out of (-1 = crawling)
        float _s, _length, _bobPhase;  // distance along the path
        float _dark;                   // 0..1 overlay
        bool _gap;                     // overlay shows the peek gap (else fully black)

        float Dt => PlayerController.RealDeltaTime(Time.deltaTime, Time.timeScale);

        /// <summary>The end (0 or 1) whose <see cref="exit"/> is closer to <paramref name="p"/>.</summary>
        public int NearestEnd(Vector3 p) => (p - exit[0]).sqrMagnitude <= (p - exit[1]).sqrMagnitude ? 0 : 1;

        public string Prompt(Interactor who) => CanInteract(who) ? "Crawl into the vent" : null;
        public bool CanInteract(Interactor who) => Inside == null && HidingSpot.Occupant == null && who.Player != null && path.Length >= 2;
        public void Interact(Interactor who) => _move = StartCoroutine(Enter(who, NearestEnd(who.transform.position)));

        IEnumerator Enter(Interactor who, int end)
        {
            _player = who.Player;
            _eye = who.Eye;
            _cc = _player.GetComponent<CharacterController>();
            _health = _player.GetComponent<Health>();
            Inside = this;
            _player.InputEnabled = false;
            if (_cc != null) _cc.enabled = false;
            if (_health != null) _health.Died += Abandon;
            var actions = InputSystem.actions;
            _look = actions?.FindAction("Player/Look");
            _moveAction = actions?.FindAction("Player/Move");
            _interact = actions?.FindAction("Player/Interact");
            _jump = actions?.FindAction("Player/Jump");

            Carryable.DropHeld(); // no crawling with a chair in your arms
            var weapons = PlayerWeapons.Instance;
            _stowed = null;
            if (weapons != null && weapons.ActiveKind is var held && held != WeaponKind.Hands && weapons.Select(WeaponKind.Hands))
                _stowed = held;

            _length = 0f;
            for (int i = 1; i < path.Length; i++) _length += Vector3.Distance(path[i - 1], path[i]);
            _entered = end;
            Noise.Make(exit[end], clank);

            _gap = false;
            yield return Fade(1f);
            _s = end == 0 ? 0f : _length;
            Vector3 along = Flat(Tangent(_s) * (end == 0 ? 1f : -1f));
            if (along.sqrMagnitude > 1e-4f) _player.transform.rotation = Quaternion.LookRotation(along);
            _player.AddLook(0f, -_player.Pitch);
            PlaceEye(PointAt(_s));
            yield return Fade(0f);
            _move = null;
        }

        IEnumerator PeekOut(int end)
        {
            _gap = false;
            yield return Fade(1f);
            PlaceEye(peek[end]);
            _peekEnd = end;
            _gap = true; // the gap opens straight away: the dark around it stays
            _move = null;
        }

        IEnumerator BackDown()
        {
            _gap = false;   // already dark round the gap, so this just closes it
            PlaceEye(PointAt(_s));
            _peekEnd = -1;
            yield return Fade(0f);
            _move = null;
        }

        IEnumerator ClimbOut(int end)
        {
            _gap = false;
            yield return Fade(1f);
            Noise.Make(exit[end], clank);
            var body = _player.transform;
            Vector3 away = Flat(exit[end] - peek[end]);
            body.SetPositionAndRotation(exit[end], away.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(away) : body.rotation);
            _player.AddLook(0f, -_player.Pitch);
            Release();
            yield return Fade(0f);
        }

        void Update()
        {
            if (Inside != this || _move != null || _player == null) return;
            bool interact = _interact != null && _interact.WasPressedThisFrame();
            float push = _moveAction != null ? _moveAction.ReadValue<Vector2>().y : 0f;
            Look();

            if (_peekEnd >= 0)
            {
                BlinkPrompts.Show(this, _interact, "E", "Climb out");
                BlinkPrompts.Show(BackPrompt, _moveAction, "S", "Back down", order: -1);
                if (interact) _move = StartCoroutine(ClimbOut(_peekEnd));
                else if (push < -0.5f || (_jump != null && _jump.WasPressedThisFrame())) _move = StartCoroutine(BackDown());
                return;
            }

            int atEnd = _s <= 1e-3f ? 0 : _s >= _length - 1e-3f ? 1 : -1;
            if (atEnd >= 0)
            {
                bool peeks = (peek[atEnd] - path[atEnd == 0 ? 0 : path.Length - 1]).sqrMagnitude > 1e-4f;
                BlinkPrompts.Show(this, _interact, "E", peeks ? "Peek out" : "Climb out");
                if (interact)
                {
                    _move = StartCoroutine(peeks ? PeekOut(atEnd) : ClimbOut(atEnd));
                    return;
                }
            }

            if (Mathf.Abs(push) < 0.1f) return;
            // Forward crawls the way you're looking along the duct; on a vertical stretch, away from where you came in.
            Vector3 t = Tangent(_s);
            Vector3 look = Flat(_eye.forward);
            float sign = Flat(t).sqrMagnitude > 0.05f ? Mathf.Sign(Vector3.Dot(Flat(t), look)) : _entered == 0 ? 1f : -1f;
            float ds = push * sign * crawlSpeed * Dt;
            _s = Mathf.Clamp(_s + ds, 0f, _length);
            _bobPhase += Mathf.Abs(ds) * bobsPerMetre * Mathf.PI * 2f;
            PlaceEye(PointAt(_s) + Vector3.up * (Mathf.Sin(_bobPhase) * bob));
        }

        void Look()
        {
            if (_look == null) return;
            Vector2 delta = _look.ReadValue<Vector2>();
            // ponytail: copies PlayerController's default sensitivities, as HidingSpot does; expose them there if they get tuned.
            delta *= _look.activeControl?.device is Gamepad ? 180f * Time.unscaledDeltaTime : 0.1f;
            _player.AddLook(delta.x, -delta.y);
        }

        /// <summary>Moves the body so the camera lands on <paramref name="eye"/> (the eye's offset is read live, crouched or not).</summary>
        void PlaceEye(Vector3 eye)
        {
            var body = _player.transform;
            body.position = eye - (_eye.position - body.position);
        }

        Vector3 PointAt(float s)
        {
            for (int i = 1; i < path.Length; i++)
            {
                float seg = Vector3.Distance(path[i - 1], path[i]);
                if (s <= seg || i == path.Length - 1) return Vector3.Lerp(path[i - 1], path[i], seg > 0f ? Mathf.Clamp01(s / seg) : 1f);
                s -= seg;
            }
            return path[0];
        }

        Vector3 Tangent(float s)
        {
            for (int i = 1; i < path.Length; i++)
            {
                float seg = Vector3.Distance(path[i - 1], path[i]);
                if (s < seg || i == path.Length - 1) return (path[i] - path[i - 1]).normalized;
                s -= seg;
            }
            return Vector3.forward;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        IEnumerator Fade(float to)
        {
            while (!Mathf.Approximately(_dark, to))
            {
                _dark = Mathf.MoveTowards(_dark, to, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeSeconds));
                yield return null;
            }
        }

        void Release()
        {
            if (_health != null) _health.Died -= Abandon;
            if (_cc != null) _cc.enabled = true;
            if (_stowed is { } kind && PlayerWeapons.Instance != null) PlayerWeapons.Instance.Select(kind);
            _stowed = null;
            if (!(_health is { IsDead: true })) _player.InputEnabled = true;
            if (Inside == this) Inside = null;
            _peekEnd = -1;
            _move = null;
        }

        // Killed inside somehow: hand the body back to the respawn flow, don't leave zombies blind.
        void Abandon()
        {
            if (_move != null) StopCoroutine(_move);
            _dark = 0f;
            _gap = false;
            Release();
        }

        void OnDisable()
        {
            if (Inside == this) Abandon();
        }

        void OnGUI()
        {
            if (_dark > 0f) PeekOverlay.Draw(_gap ? peekGap : Vector2.zero, _dark);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            for (int i = 1; i < path.Length; i++) Gizmos.DrawLine(path[i - 1], path[i]);
            for (int k = 0; k < 2 && k < peek.Length && k < exit.Length; k++)
            {
                Gizmos.DrawWireSphere(peek[k], 0.08f);
                Gizmos.DrawWireSphere(exit[k], 0.15f);
            }
        }
    }
}
