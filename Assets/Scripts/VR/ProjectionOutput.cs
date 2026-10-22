using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using VRSurgery.Session;
using VRSurgery.Transplant;

namespace VRSurgery.VR
{
    /// <summary>
    /// How the projected image is laid onto the real mannequin. Saved on the stand's PC and read
    /// back at every start, so the lining-up is done once per installation, not once per run.
    /// </summary>
    [Serializable]
    public class ProjectionCalibration
    {
        /// <summary>Shift of the image, as a fraction of the screen's width and height.</summary>
        public float offsetX;
        public float offsetY;

        public float scale = 1f;

        /// <summary>Degrees, counter-clockwise.</summary>
        public float rotation;

        public bool mirrorX;
        public bool mirrorY;

        public const string FileName = "calibracao_projetor.json";

        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        public static ProjectionCalibration Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    ProjectionCalibration loaded = JsonUtility.FromJson<ProjectionCalibration>(File.ReadAllText(path));
                    if (loaded != null && loaded.scale > 0.05f) { return loaded; }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Projetor] calibração ilegível em {path}: {exception.Message}");
            }

            return new ProjectionCalibration();
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(this, true));
        }
    }

    /// <summary>
    /// Puts the overhead camera's picture on the projector aimed at the mannequin, and lets the
    /// operator line it up with the real body on site.
    ///
    /// The camera draws into a texture and this shows the texture on the projector's display,
    /// moved, scaled, turned and mirrored as calibrated. Transforming the finished picture rather
    /// than the camera keeps every option honest: mirroring a camera's projection turns the whole
    /// scene inside out, mirroring a picture does not.
    ///
    /// Keys (PC keyboard, operator only):
    ///   C — calibration on/off: a test pattern (the body's outline and a 10 cm grid) over the picture;
    ///   arrows move, + and − scale, [ and ] turn, X and Y mirror, Enter saves — while calibrating;
    ///   P — swaps what the main monitor shows, the surgeon's view or this overhead view. With
    ///       Windows set to "Duplicate" there is one screen for both, and this is how to choose.
    ///
    /// When a turn ends, the time it took goes on the picture in large figures, to the
    /// thousandth, over a dimmed body, and stays until the next visitor starts: the whole queue
    /// reads it off the mannequin. It sits inside the calibrated picture, so it lands where the
    /// body does.
    /// </summary>
    public class ProjectionOutput : MonoBehaviour
    {
        [SerializeField] private Camera projection;

        [Tooltip("0-based: 1 is \"Display 2\", the projector.")]
        [SerializeField, Min(0)] private int displayIndex = 1;

        [Tooltip("Optional: the patient's body, for the outline in the test pattern.")]
        [SerializeField] private PatientBodyVolume body;

        [SerializeField] private Vector2Int fallbackResolution = new Vector2Int(1920, 1080);

        [Header("Calibration steps")]
        [Tooltip("Fraction of the screen per second while an arrow is held.")]
        [SerializeField, Min(0.001f)] private float moveSpeed = 0.08f;

        [SerializeField, Min(0.01f)] private float scaleSpeed = 0.25f;
        [SerializeField, Min(1f)] private float turnSpeed = 15f;

        [Tooltip("Spacing of the test grid, in metres on the patient.")]
        [SerializeField, Min(0.02f)] private float gridMetres = 0.1f;

        [Header("Finishing time")]
        [Tooltip("Optional: the stand's session, for the time a turn took.")]
        [SerializeField] private EventSessionController session;

        [SerializeField] private Color finishColour = new Color(1f, 0.85f, 0.2f);

        [Tooltip("How bright the body stays behind the time, so the figures stand out.")]
        [SerializeField, Range(0f, 1f)] private float bodyBehindTime = 0.3f;

        private RenderTexture _texture;
        private Vector2 _size = new Vector2(1920f, 1080f);
        private Canvas _canvas;
        private RectTransform _picture;
        private RawImage _pictureImage;
        private Text _finish;
        private Text _finishTitle;
        private GameObject _pattern;
        private Camera _clear;
        private string _path;

        public ProjectionCalibration Calibration { get; private set; } = new ProjectionCalibration();

        /// <summary>True while the test pattern is up and the keys move the picture.</summary>
        public bool Calibrating { get; private set; }

        /// <summary>True while the overhead view is on the main monitor instead of the projector.</summary>
        public bool OnMainMonitor { get; private set; }

        /// <summary>True when the projector's display exists; with one screen only the main monitor can show it.</summary>
        public bool ProjectorConnected => Display.displays.Length > displayIndex;

        /// <summary>Raised whenever calibration or the monitor swap changes, for the operator's HUD.</summary>
        public event Action Changed;

        /// <summary>The large line on the picture right now ("3:12,457"), or empty while a turn runs.</summary>
        public string FinishShown => _finish != null && _finish.gameObject.activeSelf ? _finish.text : string.Empty;

        private void Start()
        {
            _path = ProjectionCalibration.FilePath;
            Calibration = ProjectionCalibration.Load(_path);

            // At the stand the notebook may drive the projector alone ("second screen only", or
            // the lid closed): the one screen there is then the projector, and it shows the
            // patient from above. P still swaps. Not in the Editor, whose Game view is one screen.
            if (!Application.isEditor && !ProjectorConnected) { OnMainMonitor = true; }
            Build();
            Apply();
        }

        private void OnDestroy()
        {
            if (Calibrating) { AnyCalibrating = false; }
            if (projection != null && projection.targetTexture == _texture) { projection.targetTexture = null; }
            if (_texture != null) { _texture.Release(); Destroy(_texture); }
            _texture = null;
        }

        private void Build()
        {
            int width = fallbackResolution.x, height = fallbackResolution.y;
            if (ProjectorConnected)
            {
                Display display = Display.displays[displayIndex];
                if (display.systemWidth > 0 && display.systemHeight > 0)
                {
                    width = display.systemWidth;
                    height = display.systemHeight;
                }
            }

            _size = new Vector2(width, height);

            // No graphics device (the headless test runner): there is nothing to project and a
            // render texture cannot even be created; the rest still works without it.
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                _texture = new RenderTexture(width, height, 24) { name = "Projecao" };
            }

            if (projection != null)
            {
                projection.targetTexture = _texture;
                projection.aspect = width / (float)height;
            }

            // Something has to clear the projector to black every frame; the picture goes on top.
            GameObject clearHolder = new GameObject("ProjetorFundo");
            clearHolder.transform.SetParent(transform, false);
            _clear = clearHolder.AddComponent<Camera>();
            _clear.cullingMask = 0;
            _clear.clearFlags = CameraClearFlags.SolidColor;
            _clear.backgroundColor = Color.black;
            _clear.depth = -100f;
            _clear.targetDisplay = displayIndex;
            UnityEngine.Rendering.Universal.UniversalAdditionalCameraData data =
                clearHolder.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.allowXRRendering = false;

            GameObject canvasHolder = new GameObject("ProjetorSaida", typeof(RectTransform));
            canvasHolder.transform.SetParent(transform, false);
            _canvas = canvasHolder.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;
            CanvasScaler scaler = canvasHolder.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(width, height);
            scaler.matchWidthOrHeight = 0.5f;

            Image black = new GameObject("Preto", typeof(RectTransform)).AddComponent<Image>();
            black.transform.SetParent(canvasHolder.transform, false);
            black.color = Color.black;
            black.raycastTarget = false;
            Stretch(black.rectTransform);

            RawImage picture = new GameObject("Imagem", typeof(RectTransform)).AddComponent<RawImage>();
            picture.transform.SetParent(canvasHolder.transform, false);
            picture.texture = _texture;
            picture.raycastTarget = false;
            _picture = picture.rectTransform;
            _pictureImage = picture;
            Stretch(_picture);

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _finishTitle = FinishLine("TempoFinalTitulo", font, new Vector2(0.1f, 0.68f), new Vector2(0.9f, 0.84f), 220);
            _finishTitle.color = Color.white;
            _finish = FinishLine("TempoFinal", font, new Vector2(0.03f, 0.28f), new Vector2(0.97f, 0.70f), 700);
            _finish.color = finishColour;
            RefreshFinish();

            RawImage pattern = new GameObject("PadraoDeTeste", typeof(RectTransform)).AddComponent<RawImage>();
            pattern.transform.SetParent(_picture, false);
            pattern.texture = TestPattern(width / 2, height / 2);
            pattern.raycastTarget = false;
            Stretch(pattern.rectTransform);
            _pattern = pattern.gameObject;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>One line of the finishing time, as large as its box allows, inside the picture.</summary>
        private Text FinishLine(string name, Font font, Vector2 anchorMin, Vector2 anchorMax, int largest)
        {
            Text text = new GameObject(name, typeof(RectTransform)).AddComponent<Text>();
            text.transform.SetParent(_picture, false);
            text.rectTransform.anchorMin = anchorMin;
            text.rectTransform.anchorMax = anchorMax;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            text.font = font;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 40;
            text.resizeTextMaxSize = largest;
            text.raycastTarget = false;
            text.gameObject.SetActive(false);
            return text;
        }

        /// <summary>
        /// The time the last turn took, from the moment it ends until the next visitor starts
        /// (the session forgets its result only then). A turn that ran out of time has no time
        /// to show, only that it ran out.
        /// </summary>
        private void RefreshFinish()
        {
            if (_finish == null) { return; }

            bool ended = session != null && session.HasResult;
            bool won = ended && session.LastResult.Succeeded;
            string big = !ended ? string.Empty : won ? session.LastResult.ElapsedWithMillis : "TEMPO ESGOTADO";

            if (_finish.text != big) { _finish.text = big; }
            if (_finish.gameObject.activeSelf != ended) { _finish.gameObject.SetActive(ended); }

            if (_finishTitle.text != "TEMPO") { _finishTitle.text = "TEMPO"; }
            if (_finishTitle.gameObject.activeSelf != won) { _finishTitle.gameObject.SetActive(won); }

            if (_pictureImage != null)
            {
                float light = ended ? bodyBehindTime : 1f;
                _pictureImage.color = new Color(light, light, light, 1f);
            }
        }

        private void Update()
        {
            RefreshFinish();

            Keyboard keys = Keyboard.current;
            if (keys == null) { return; }

            if (keys.cKey.wasPressedThisFrame) { ToggleCalibration(); }
            if (keys.pKey.wasPressedThisFrame) { SwapMonitor(); }
            if (!Calibrating) { return; }

            float dt = Time.unscaledDeltaTime;
            bool changed = false;
            if (keys.leftArrowKey.isPressed) { Calibration.offsetX -= moveSpeed * dt; changed = true; }
            if (keys.rightArrowKey.isPressed) { Calibration.offsetX += moveSpeed * dt; changed = true; }
            if (keys.downArrowKey.isPressed) { Calibration.offsetY -= moveSpeed * dt; changed = true; }
            if (keys.upArrowKey.isPressed) { Calibration.offsetY += moveSpeed * dt; changed = true; }

            bool bigger = keys.equalsKey.isPressed || keys.numpadPlusKey.isPressed;
            bool smaller = keys.minusKey.isPressed || keys.numpadMinusKey.isPressed;
            if (bigger) { Calibration.scale *= 1f + scaleSpeed * dt; changed = true; }
            if (smaller) { Calibration.scale = Mathf.Max(0.1f, Calibration.scale / (1f + scaleSpeed * dt)); changed = true; }

            if (keys.leftBracketKey.isPressed) { Calibration.rotation += turnSpeed * dt; changed = true; }
            if (keys.rightBracketKey.isPressed) { Calibration.rotation -= turnSpeed * dt; changed = true; }

            if (keys.xKey.wasPressedThisFrame) { Calibration.mirrorX = !Calibration.mirrorX; changed = true; }
            if (keys.yKey.wasPressedThisFrame) { Calibration.mirrorY = !Calibration.mirrorY; changed = true; }

            if (keys.enterKey.wasPressedThisFrame || keys.numpadEnterKey.wasPressedThisFrame) { Save(); }
            if (changed) { Apply(); }
        }

        /// <summary>C: the test pattern and the calibration keys, on or off.</summary>
        public void ToggleCalibration()
        {
            Calibrating = !Calibrating;
            AnyCalibrating = Calibrating;
            Apply();
        }

        /// <summary>P: the overhead view onto the main monitor, or back to the projector.</summary>
        public void SwapMonitor()
        {
            OnMainMonitor = !OnMainMonitor;
            Apply();
        }

        /// <summary>
        /// True while any projector is being calibrated: the arrow keys belong to it then, not to
        /// the operator's height adjustment.
        /// </summary>
        public static bool AnyCalibrating { get; private set; }

        /// <summary>Writes the calibration to the PC, where the next start will read it.</summary>
        public void Save()
        {
            try
            {
                Calibration.Save(_path ?? ProjectionCalibration.FilePath);
                Debug.Log($"[Projetor] calibração salva em {_path}");
                LastSaved = DateTime.Now;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Projetor] não deu para salvar a calibração: {exception.Message}");
            }

            Changed?.Invoke();
        }

        /// <summary>When the calibration was last written, for the operator's HUD.</summary>
        public DateTime LastSaved { get; private set; }

        /// <summary>Puts the picture where the calibration says, on the screen the swap says.</summary>
        public void Apply()
        {
            if (_canvas == null) { return; }

            int target = OnMainMonitor || !ProjectorConnected ? 0 : displayIndex;
            bool shown = OnMainMonitor || ProjectorConnected;
            _canvas.targetDisplay = target;
            _canvas.enabled = shown;
            if (_clear != null) { _clear.enabled = ProjectorConnected && !OnMainMonitor; }

            // With nowhere to show it — one screen, top view not swapped in — the overhead camera
            // would render the patient a second time every frame for nobody.
            if (projection != null) { projection.enabled = shown; }

            // In the canvas's reference units, which are the projector's pixels (see Build).
            _picture.anchoredPosition = new Vector2(Calibration.offsetX * _size.x, Calibration.offsetY * _size.y);
            _picture.localScale = new Vector3(Calibration.scale * (Calibration.mirrorX ? -1f : 1f),
                Calibration.scale * (Calibration.mirrorY ? -1f : 1f), 1f);
            _picture.localEulerAngles = new Vector3(0f, 0f, Calibration.rotation);
            if (_pattern != null) { _pattern.SetActive(Calibrating); }

            Changed?.Invoke();
        }

        /// <summary>The body's outline and a grid on the patient, drawn where the camera sees them.</summary>
        private Texture2D TestPattern(int width, int height)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = "PadraoDeTeste" };
            Color32[] pixels = new Color32[width * height];
            texture.SetPixels32(pixels);

            if (projection != null)
            {
                Color32 grid = new Color32(90, 90, 90, 255);
                Color32 outline = new Color32(255, 255, 255, 255);
                Color32 window = new Color32(255, 210, 60, 255);

                Vector3 centre = projection.transform.position;
                float reach = projection.orthographicSize * Mathf.Max(1f, projection.aspect) * 1.5f;
                for (float x = Mathf.Floor((centre.x - reach) / gridMetres) * gridMetres; x <= centre.x + reach; x += gridMetres)
                {
                    Line(pixels, width, height, new Vector3(x, 1f, centre.z - reach), new Vector3(x, 1f, centre.z + reach), grid, 1);
                }

                for (float z = Mathf.Floor((centre.z - reach) / gridMetres) * gridMetres; z <= centre.z + reach; z += gridMetres)
                {
                    Line(pixels, width, height, new Vector3(centre.x - reach, 1f, z), new Vector3(centre.x + reach, 1f, z), grid, 1);
                }

                if (body != null)
                {
                    body.ForEachOutlineEdge((a, b) => Line(pixels, width, height, a, b, outline, 2));

                    Vector2 c = body.WindowCentre, h = body.WindowHalfSize;
                    Vector3 p0 = new Vector3(c.x - h.x, 1f, c.y - h.y), p1 = new Vector3(c.x + h.x, 1f, c.y - h.y);
                    Vector3 p2 = new Vector3(c.x + h.x, 1f, c.y + h.y), p3 = new Vector3(c.x - h.x, 1f, c.y + h.y);
                    Line(pixels, width, height, p0, p1, window, 2);
                    Line(pixels, width, height, p1, p2, window, 2);
                    Line(pixels, width, height, p2, p3, window, 2);
                    Line(pixels, width, height, p3, p0, window, 2);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private void Line(Color32[] pixels, int width, int height, Vector3 from, Vector3 to, Color32 colour, int thickness)
        {
            Vector3 a = projection.WorldToViewportPoint(from);
            Vector3 b = projection.WorldToViewportPoint(to);
            float ax = a.x * width, ay = a.y * height, bx = b.x * width, by = b.y * height;
            int steps = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(bx - ax), Mathf.Abs(by - ay)));

            for (int s = 0; s <= steps; s++)
            {
                float t = steps == 0 ? 0f : s / (float)steps;
                int px = Mathf.RoundToInt(Mathf.Lerp(ax, bx, t));
                int py = Mathf.RoundToInt(Mathf.Lerp(ay, by, t));
                for (int dy = 0; dy < thickness; dy++)
                {
                    for (int dx = 0; dx < thickness; dx++)
                    {
                        int x = px + dx, y = py + dy;
                        if (x >= 0 && y >= 0 && x < width && y < height) { pixels[y * width + x] = colour; }
                    }
                }
            }
        }

        public void Bind(Camera overhead, PatientBodyVolume patient)
        {
            projection = overhead;
            body = patient;
        }

        public void BindSession(EventSessionController stand) => session = stand;
    }
}
