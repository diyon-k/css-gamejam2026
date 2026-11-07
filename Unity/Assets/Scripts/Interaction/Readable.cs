using Blink.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blink.Interaction
{
    /// <summary>
    /// A note, letter or framed photo. Interact holds it up full screen; interact again (no need to aim at it: it stays
    /// the <see cref="Interactor"/>'s target while held), B on a gamepad, or walking away puts it down. The B press that
    /// puts it down doesn't also crouch (<see cref="ClaimsCrouch"/>). It slides up into view and back down rather than popping. World time keeps running while you read: reading is an eyes-open risk, not a pause.
    /// </summary>
    public sealed class Readable : MonoBehaviour, IInteractable
    {
        [SerializeField] string verb = "Read";
        [SerializeField] string title = "Note";
        [SerializeField, TextArea(4, 14)] string body;
        [Tooltip("Optional picture shown above the text (photos).")]
        [SerializeField] Texture2D image;
        [Tooltip("Walk further than this from it and it's put down.")]
        [SerializeField, Min(0.5f)] float maxDistance = 3f;
        [Tooltip("Seconds (real time) to raise it into view or lower it away.")]
        [SerializeField, Min(0.01f)] float raiseSeconds = 0.3f;
        [Header("Look (presentation only)")]
        [Tooltip("Real seconds for the paper to lift from the bottom of the screen to the face, or drop away again.")]
        [SerializeField, Min(0.01f)] float liftSeconds = 0.45f;
        [Tooltip("Picks this note's worn, torn paper. 0 = from its title and text, so each note looks different.")]
        [SerializeField] int paperSeed;

        static Readable _open; // one thing in your hands at a time
        static bool _eastSinceClose; // B put a note down and is still held

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _open = null;
            _eastSinceClose = false;
        }

        /// <summary>The note held up right now, or null.</summary>
        public static Readable Current => _open;

        /// <summary>
        /// True while pad B (East, also Crouch) belongs to putting a note down: while a note is up, and until the B that
        /// put it down is released. The player controller ignores Crouch meanwhile.
        /// </summary>
        public static bool ClaimsCrouch
        {
            get
            {
                var pad = Gamepad.current;
                bool east = pad != null && pad.buttonEast.isPressed;
                if (!east) _eastSinceClose = false;
                return east && (_open != null || _eastSinceClose);
            }
        }
        Transform _reader;
        float _shown; // 0 put down .. 1 held up; eases both ways so it never pops
        float _lift;  // presentation only: the paper's own lift/drop timeline, so it can finish dropping after _shown

        public void Setup(string verb, string title, string body, Texture2D image = null)
        {
            this.verb = verb;
            this.title = title;
            this.body = body;
            this.image = image;
        }

        int PaperSeed => paperSeed != 0 ? paperSeed : NotePaper.SeedFor(title, body);

        // Bake this note's paper on a worker thread now, so holding it up never waits on it.
        void Start() => NotePaper.Prewarm(PaperSeed);

        public string Prompt(Interactor who) => _open == this ? "Put down" : $"{verb} {title.ToLowerInvariant()}";
        public bool CanInteract(Interactor who) => true;

        public void Interact(Interactor who)
        {
            if (_open == this) { _open = null; return; }
            _open = this;
            _reader = who.transform;
        }

        void Update()
        {
            if (_open == this && (_reader == null || (_reader.position - transform.position).sqrMagnitude > maxDistance * maxDistance))
                _open = null;
            // Pad B puts it down too, the usual "back" button.
            if (_open == this && Gamepad.current is { } pad && pad.buttonEast.wasPressedThisFrame)
            {
                _open = null;
                _eastSinceClose = true;
            }
            _shown = Mathf.MoveTowards(_shown, _open == this ? 1f : 0f, Time.unscaledDeltaTime / raiseSeconds);
            _lift = Mathf.MoveTowards(_lift, _open == this ? 1f : 0f, Time.unscaledDeltaTime / liftSeconds);
        }

        void OnDisable()
        {
            if (_open == this) _open = null;
            _shown = 0f;
            _lift = 0f;
        }

        // Worn paper lifted to the face in the BlinkTheme style; the "[E]/[X] put down" prompt comes from the Interactor.
        void OnGUI()
        {
            if ((_shown <= 0f && _lift <= 0f) || Event.current.type != EventType.Repaint) return;
            BlinkWidgets.NoteCard(_lift, _shown, _open == this, PaperSeed, title, body, image);
        }
    }
}
