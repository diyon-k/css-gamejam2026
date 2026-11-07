using UnityEngine;

namespace Blink.Interaction
{
    /// <summary>
    /// A note, letter or framed photo. Interact holds it up full screen; interact again, or walk away,
    /// to put it down. World time keeps running while you read: reading is an eyes-open risk, not a pause.
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

        static Readable _open; // one thing in your hands at a time
        Transform _reader;
        GUIStyle _titleStyle, _bodyStyle;

        public void Setup(string verb, string title, string body, Texture2D image = null)
        {
            this.verb = verb;
            this.title = title;
            this.body = body;
            this.image = image;
        }

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
        }

        void OnDisable()
        {
            if (_open == this) _open = null;
        }

        // ponytail: IMGUI like the rest of the HUD, swap for a Canvas when the HUD gets one.
        void OnGUI()
        {
            if (_open != this || Event.current.type != EventType.Repaint) return;
            float w = Screen.width, h = Screen.height;
            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);

            float pw = Mathf.Min(w * 0.6f, h * 0.9f), ph = h * 0.86f;
            var paper = new Rect((w - pw) / 2f, (h - ph) / 2f, pw, ph);
            GUI.color = new Color(0.86f, 0.82f, 0.72f);
            GUI.DrawTexture(paper, Texture2D.whiteTexture);
            GUI.color = Color.white;

            int fs = Mathf.RoundToInt(h * 0.024f);
            _titleStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
            _bodyStyle ??= new GUIStyle(GUI.skin.label) { wordWrap = true, fontStyle = FontStyle.Italic };
            _titleStyle.fontSize = Mathf.RoundToInt(fs * 1.3f);
            _bodyStyle.fontSize = fs;
            _titleStyle.normal.textColor = _bodyStyle.normal.textColor = new Color(0.15f, 0.1f, 0.06f);

            var inner = new Rect(paper.x + pw * 0.08f, paper.y + ph * 0.05f, pw * 0.84f, ph * 0.9f);
            GUI.Label(new Rect(inner.x, inner.y, inner.width, fs * 2f), title, _titleStyle);
            float y = inner.y + fs * 2.4f;
            if (image != null)
            {
                float ih = Mathf.Min(inner.height * 0.6f, inner.width * image.height / image.width);
                GUI.DrawTexture(new Rect(inner.x, y, inner.width, ih), image, ScaleMode.ScaleToFit);
                y += ih + fs;
            }
            GUI.Label(new Rect(inner.x, y, inner.width, inner.yMax - y), body, _bodyStyle);
        }
    }
}
