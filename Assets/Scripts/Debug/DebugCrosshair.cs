using Sanctify.Cameras;
using Sanctify.Characters.Player;
using UnityEngine;

namespace Sanctify.Debugging
{
    /// <summary>
    /// Draws a small dot where the focus ray goes: the centre of the 4:3 view, or the cursor
    /// while the interact mode is on. While a prop is focused, the dot changes colour and the
    /// prop's focus text shows below it.
    /// </summary>
    public sealed class DebugCrosshair : MonoBehaviour
    {
        [SerializeField, Min(1f)] float size = 4f;
        [SerializeField, Min(1f)] float cursorSize = 8f;
        [SerializeField] Color color = Color.white;
        [SerializeField] Color focusColor = new(1f, 0.8f, 0.3f);
        [SerializeField] bool outline = true;

        PlayerInteractor _interactor;
        PlayerInteractMode _interactMode;
        FixedAspectRenderer _aspect;
        GUIStyle _labelStyle;

        void Awake()
        {
            _interactor = GetComponentInParent<PlayerInteractor>();
            _interactMode = GetComponentInParent<PlayerInteractMode>();
            _aspect = GetComponent<FixedAspectRenderer>();
        }

        void OnGUI()
        {
            Vector2 viewport = _interactMode != null ? _interactMode.DisplayCursor : new Vector2(0.5f, 0.5f);
            Vector2 at = _aspect != null
                ? _aspect.ViewportToGuiPoint(viewport)
                : new Vector2(Screen.width * viewport.x, Screen.height * (1f - viewport.y));
            float x = at.x;
            float y = at.y;
            float dot = _interactMode != null && _interactMode.IsActive ? cursorSize : size;

            Color previous = GUI.color;
            var focus = _interactor != null ? _interactor.FocusProp : null;

            if (outline)
            {
                GUI.color = Color.black;
                GUI.DrawTexture(new Rect(x - dot * 0.5f - 1f, y - dot * 0.5f - 1f, dot + 2f, dot + 2f), Texture2D.whiteTexture);
            }

            GUI.color = focus != null ? focusColor : color;
            GUI.DrawTexture(new Rect(x - dot * 0.5f, y - dot * 0.5f, dot, dot), Texture2D.whiteTexture);

            if (focus != null && !string.IsNullOrEmpty(focus.FocusText))
            {
                _labelStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter };
                var rect = new Rect(x - 150f, y + dot + 8f, 300f, 24f);

                GUI.color = Color.black;
                GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), focus.FocusText, _labelStyle);
                GUI.color = focusColor;
                GUI.Label(rect, focus.FocusText, _labelStyle);
            }

            GUI.color = previous;
        }
    }
}
