using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace VRSurgery.Stand
{
    /// <summary>
    /// The projector's screen: the overhead picture of the patient, laid over the mannequin.
    ///
    /// The projector sits wherever the stand allows, so the picture has to be moved onto the
    /// mannequin by hand, on the day, from the keyboard: shift, zoom, turn, mirror. The setting
    /// is saved as it changes and comes back next time the app opens. A test pattern (G) shows
    /// the picture's outline and centre so the alignment can be done before the headset is even
    /// switched on.
    /// </summary>
    public class ProjectorView : MonoBehaviour
    {
        [Serializable]
        public class Calibration
        {
            public float offsetX;
            public float offsetY;
            public float scale = 1f;
            public int quarterTurns;
            public bool mirrorX;
            public bool mirrorY;
        }

        private StandLinkClient _client;
        private string _calibrationPath;
        private Calibration _calibration = new Calibration();

        private RectTransform _frame;
        private RawImage _picture;
        private Texture2D _texture;
        private GameObject _pattern;
        private GameObject _help;
        private Text _status;
        private bool _patternForced;
        private float _frameAspect = 768f / 480f;
        private int _framesShown;

        public Calibration Current => _calibration;

        public static ProjectorView Create(GameObject host, StandLinkClient client, string dataFolder)
        {
            ProjectorView view = host.AddComponent<ProjectorView>();
            view._client = client;
            view._calibrationPath = Path.Combine(dataFolder, "calibracao_projetor.json");
            view.Build();
            return view;
        }

        private void Build()
        {
            LoadCalibration();
            StandUi.ClearingCamera(transform, Color.black);
            Canvas canvas = StandUi.Canvas("Projetor", transform, Vector2.zero);

            _frame = StandUi.Rect("Quadro", canvas.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            _picture = _frame.gameObject.AddComponent<RawImage>();
            _picture.color = Color.white;
            _picture.raycastTarget = false;
            _texture = new Texture2D(2, 2, TextureFormat.RGB24, false) { name = "ImagemDoOculos" };
            _picture.texture = _texture;
            _picture.enabled = false;

            BuildPattern();

            _status = StandUi.Label("Estado", canvas.transform, string.Empty, 16, new Color(1f, 1f, 1f, 0.55f),
                TextAnchor.LowerLeft, Vector2.zero, new Vector2(1f, 0f), new Vector2(12f, 10f), new Vector2(-12f, 60f));

            _help = StandUi.Panel("Ajuda", canvas.transform, new Color(0f, 0f, 0f, 0.82f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -400f), new Vector2(560f, -16f)).gameObject;
            StandUi.Label("Texto", _help.transform,
                "PROJETOR — ajuste no manequim\n\n" +
                "Setas      mover (Shift: mais rápido)\n" +
                "+  /  -    zoom (Shift: mais rápido)\n" +
                "R          girar 90°\n" +
                "H  /  V    espelhar na horizontal / vertical\n" +
                "G          padrão de teste\n" +
                "0          voltar ao ajuste inicial\n" +
                "F9 / F10   mover a janela de monitor\n" +
                "F11        tela cheia\n" +
                "F1         esconder esta ajuda\n\n" +
                "O ajuste é salvo sozinho.",
                18, Color.white, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(18f, 14f), new Vector2(-18f, -14f));
            _help.SetActive(false);

            _client.FrameReceived += ShowFrame;
            Apply();
        }

        /// <summary>Outline, centre cross and a head marker, drawn inside the picture so they move with it.</summary>
        private void BuildPattern()
        {
            _pattern = new GameObject("PadraoDeTeste", typeof(RectTransform));
            RectTransform root = (RectTransform)_pattern.transform;
            root.SetParent(_frame, false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            Color line = new Color(0.35f, 1f, 0.9f, 0.9f);
            const float w = 4f;
            StandUi.Panel("Topo", root, line, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -w), Vector2.zero);
            StandUi.Panel("Base", root, line, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, w));
            StandUi.Panel("Esquerda", root, line, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(w, 0f));
            StandUi.Panel("Direita", root, line, new Vector2(1f, 0f), Vector2.one, new Vector2(-w, 0f), Vector2.zero);
            StandUi.Panel("CruzH", root, line, new Vector2(0.45f, 0.5f), new Vector2(0.55f, 0.5f), new Vector2(0f, -w / 2), new Vector2(0f, w / 2));
            StandUi.Panel("CruzV", root, line, new Vector2(0.5f, 0.42f), new Vector2(0.5f, 0.58f), new Vector2(-w / 2, 0f), new Vector2(w / 2, 0f));

            // A câmera do óculos põe a cabeça do paciente à esquerda da imagem.
            StandUi.Label("Cabeca", root, "CABEÇA", 28, line, TextAnchor.MiddleLeft,
                new Vector2(0f, 0.4f), new Vector2(0.3f, 0.6f), new Vector2(16f, 0f), Vector2.zero, FontStyle.Bold);
        }

        private void ShowFrame(byte[] jpeg)
        {
            if (!_texture.LoadImage(jpeg, false)) { return; }

            float aspect = (float)_texture.width / Mathf.Max(1, _texture.height);
            if (Mathf.Abs(aspect - _frameAspect) > 0.001f)
            {
                _frameAspect = aspect;
                Apply();
            }

            _picture.enabled = true;
            _framesShown++;
        }

        private void Update()
        {
            HandleKeys();

            bool live = _client.IsConnected && _framesShown > 0;
            if (!_client.IsConnected) { _picture.enabled = false; }
            _pattern.SetActive(_patternForced || !live);

            _status.text = live && !_help.activeSelf
                ? string.Empty
                : _client.IsConnected
                    ? $"Óculos conectado ({_client.QuestAddress}) — esperando a imagem…   F1: ajuda"
                    : "Procurando o óculos na rede…   F1: ajuda";
        }

        private void HandleKeys()
        {
            Keyboard k = Keyboard.current;
            if (k == null) { return; }

            bool fast = k.shiftKey.isPressed;
            float step = fast ? 0.02f : 0.004f;
            float zoom = fast ? 1.05f : 1.01f;
            bool changed = false;

            if (k.leftArrowKey.isPressed) { _calibration.offsetX -= step; changed = true; }
            if (k.rightArrowKey.isPressed) { _calibration.offsetX += step; changed = true; }
            if (k.upArrowKey.isPressed) { _calibration.offsetY += step; changed = true; }
            if (k.downArrowKey.isPressed) { _calibration.offsetY -= step; changed = true; }
            if (k.equalsKey.isPressed || k.numpadPlusKey.isPressed) { _calibration.scale *= zoom; changed = true; }
            if (k.minusKey.isPressed || k.numpadMinusKey.isPressed) { _calibration.scale /= zoom; changed = true; }
            if (k.rKey.wasPressedThisFrame) { _calibration.quarterTurns = (_calibration.quarterTurns + 1) % 4; changed = true; }
            if (k.hKey.wasPressedThisFrame) { _calibration.mirrorX = !_calibration.mirrorX; changed = true; }
            if (k.vKey.wasPressedThisFrame) { _calibration.mirrorY = !_calibration.mirrorY; changed = true; }
            if (k.digit0Key.wasPressedThisFrame || k.numpad0Key.wasPressedThisFrame) { _calibration = new Calibration(); changed = true; }
            if (k.gKey.wasPressedThisFrame) { _patternForced = !_patternForced; }
            if (k.f1Key.wasPressedThisFrame) { _help.SetActive(!_help.activeSelf); }

            if (changed)
            {
                _calibration.scale = Mathf.Clamp(_calibration.scale, 0.1f, 10f);
                Apply();
                _saveDue = Time.unscaledTime + 0.5f;
            }

            // Grava meio segundo depois da última tecla, não a cada quadro com a seta apertada.
            if (_saveDue > 0f && Time.unscaledTime >= _saveDue)
            {
                _saveDue = 0f;
                SaveCalibration();
            }
        }

        private float _saveDue;

        /// <summary>Fits the picture to the screen, then applies the hand-made adjustment on top.</summary>
        private void Apply()
        {
            float screenW = Mathf.Max(1, Screen.width);
            float screenH = Mathf.Max(1, Screen.height);

            // Depois de girar 90° o lado comprido fica na vertical: encaixa pelo outro lado.
            bool sideways = _calibration.quarterTurns % 2 == 1;
            float fitW = sideways ? screenH : screenW;
            float fitH = sideways ? screenW : screenH;
            Vector2 size = fitW / fitH > _frameAspect
                ? new Vector2(fitH * _frameAspect, fitH)
                : new Vector2(fitW, fitW / _frameAspect);

            _frame.sizeDelta = size;
            _frame.anchoredPosition = new Vector2(_calibration.offsetX * screenW, _calibration.offsetY * screenH);
            _frame.localEulerAngles = new Vector3(0f, 0f, _calibration.quarterTurns * 90f);
            _frame.localScale = new Vector3(
                (_calibration.mirrorX ? -1f : 1f) * _calibration.scale,
                (_calibration.mirrorY ? -1f : 1f) * _calibration.scale, 1f);
        }

        private int _lastScreenW;
        private int _lastScreenH;

        private void LateUpdate()
        {
            if (Screen.width != _lastScreenW || Screen.height != _lastScreenH)
            {
                _lastScreenW = Screen.width;
                _lastScreenH = Screen.height;
                Apply();
            }
        }

        private void LoadCalibration()
        {
            try
            {
                if (File.Exists(_calibrationPath))
                {
                    _calibration = JsonUtility.FromJson<Calibration>(File.ReadAllText(_calibrationPath)) ?? new Calibration();
                }
            }
            catch (Exception error)
            {
                Debug.LogWarning($"[Estande] Ajuste do projetor ilegível, começando do zero: {error.Message}");
                _calibration = new Calibration();
            }
        }

        private void SaveCalibration()
        {
            try { File.WriteAllText(_calibrationPath, JsonUtility.ToJson(_calibration, true)); }
            catch (Exception error) { Debug.LogWarning($"[Estande] Não deu para salvar o ajuste do projetor: {error.Message}"); }
        }

        private void OnDestroy()
        {
            if (_saveDue > 0f) { SaveCalibration(); }
            if (_client != null) { _client.FrameReceived -= ShowFrame; }
            if (_texture != null) { Destroy(_texture); }
        }
    }
}
