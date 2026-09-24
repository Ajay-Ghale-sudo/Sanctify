using UnityEngine;
using UnityEngine.UI;

namespace Sanctify.Cameras
{
    /// <summary>
    /// Renders the camera into a fixed-aspect render texture and shows it on an overlay canvas
    /// with an aspect fitter, so the bars are always correct regardless of window size, render
    /// scale or post-processing. Also gives a cheap path to a low internal resolution later.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public sealed class FixedAspectRenderer : MonoBehaviour
    {
        [Header("Aspect")]
        [SerializeField, Min(1f)] float aspectWidth = 4f;
        [SerializeField, Min(1f)] float aspectHeight = 3f;
        [SerializeField] Color barColor = Color.black;

        [Header("Internal resolution")]
        [Tooltip("Height of the render texture in pixels. 0 = match the screen height (width follows the aspect).")]
        [SerializeField, Min(0)] int internalHeight = 0;
        [SerializeField] FilterMode filterMode = FilterMode.Bilinear;
        [Tooltip("Canvas sorting order. Keep it low so game UI canvases draw on top.")]
        [SerializeField] int canvasSortingOrder = -1000;

        Camera _camera;
        RenderTexture _texture;
        GameObject _canvasRoot;
        RawImage _view;
        int _lastScreenHeight;

        public float TargetAspect => aspectWidth / aspectHeight;
        public RenderTexture Texture => _texture;

        /// <summary>Where the fixed-aspect view sits on screen, in pixels with y up (screen space).</summary>
        public Rect ViewScreenRect
        {
            get
            {
                float screenWidth = Screen.width;
                float screenHeight = Screen.height;
                float target = TargetAspect;
                float width, height;
                if (screenWidth / screenHeight > target)
                {
                    height = screenHeight; // pillarboxed
                    width = height * target;
                }
                else
                {
                    width = screenWidth; // letterboxed
                    height = width / target;
                }
                return new Rect((screenWidth - width) * 0.5f, (screenHeight - height) * 0.5f, width, height);
            }
        }

        /// <summary>Maps a point in the view (0..1, y up) to GUI space (pixels, y down), for OnGUI drawing.</summary>
        public Vector2 ViewportToGuiPoint(Vector2 viewport)
        {
            Rect view = ViewScreenRect;
            return new Vector2(view.x + viewport.x * view.width, Screen.height - (view.y + viewport.y * view.height));
        }

        void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        void OnEnable()
        {
            if (!Application.isPlaying)
                return;
            BuildCanvas();
            EnsureTexture();
        }

        void OnDisable()
        {
            if (_camera != null && _camera.targetTexture == _texture)
                _camera.targetTexture = null;
            if (_texture != null)
            {
                _texture.Release();
                Destroy(_texture);
                _texture = null;
            }
            if (_canvasRoot != null)
            {
                Destroy(_canvasRoot);
                _canvasRoot = null;
            }
        }

        void Update()
        {
            if (internalHeight == 0 && Screen.height != _lastScreenHeight)
                EnsureTexture();
        }

        void EnsureTexture()
        {
            int height = internalHeight > 0 ? internalHeight : Mathf.Max(Screen.height, 1);
            int width = Mathf.Max(Mathf.RoundToInt(height * TargetAspect), 1);
            _lastScreenHeight = Screen.height;

            if (_texture != null && _texture.width == width && _texture.height == height)
                return;

            if (_texture != null)
            {
                _texture.Release();
                Destroy(_texture);
            }

            _texture = new RenderTexture(width, height, 24, RenderTextureFormat.Default)
            {
                name = "Fixed Aspect View",
                filterMode = filterMode,
                antiAliasing = 1,
            };
            _texture.Create();

            _camera.targetTexture = _texture;
            if (_view != null)
                _view.texture = _texture;
        }

        void BuildCanvas()
        {
            _canvasRoot = new GameObject("Fixed Aspect Canvas", typeof(Canvas), typeof(GraphicRaycaster));
            _canvasRoot.hideFlags = HideFlags.DontSave;
            var canvas = _canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = canvasSortingOrder;

            var bars = new GameObject("Bars", typeof(Image));
            bars.transform.SetParent(_canvasRoot.transform, false);
            var barsImage = bars.GetComponent<Image>();
            barsImage.color = barColor;
            barsImage.raycastTarget = false;
            Stretch(bars.GetComponent<RectTransform>());

            var view = new GameObject("View", typeof(RawImage), typeof(AspectRatioFitter));
            view.transform.SetParent(_canvasRoot.transform, false);
            _view = view.GetComponent<RawImage>();
            _view.raycastTarget = false;
            var fitter = view.GetComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = TargetAspect;
            Stretch(view.GetComponent<RectTransform>());
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
