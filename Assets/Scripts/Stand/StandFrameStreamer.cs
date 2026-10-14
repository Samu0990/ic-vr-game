using System;
using System.Threading;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VRSurgery.Stand
{
    /// <summary>
    /// Films the patient from straight above and hands the picture, as JPEG, to the stand link —
    /// the image the projector lays over the physical mannequin.
    ///
    /// The headset is the only machine that has the operation, so it is the one that has to
    /// draw it; the notebook just shows what arrives. Sending the picture instead of the state
    /// is what keeps the projection faithful: the cut skin, the bleeding, the retractor, the
    /// heart beating — whatever the game draws, the mannequin gets, with nothing to keep in step.
    ///
    /// The cost is kept off the frame the visitor sees. The camera renders only on the frames a
    /// picture is due (ten a second by default, and none at all with no notebook connected),
    /// the pixels come back through an asynchronous GPU readback, and the JPEG is encoded on a
    /// worker thread.
    /// </summary>
    public class StandFrameStreamer : MonoBehaviour
    {
        [Tooltip("The scene's overhead projection camera; this copies its settings.")]
        [SerializeField] private Camera source;

        [Tooltip("The patient's skin: the picture is framed on it.")]
        [SerializeField] private Renderer framingTarget;

        [Tooltip("Where the framing starts along the body, from the feet (0) to the head (1). " +
                 "The stand's mannequin is a torso with a head, so the legs are left out.")]
        [SerializeField, Range(0f, 0.9f)] private float upperBodyFrom = 0.45f;

        [SerializeField] private bool headTowardPositiveZ = true;
        [SerializeField, Range(1f, 1.5f)] private float margin = 1.08f;

        [SerializeField, Min(64)] private int width = 768;
        [SerializeField, Min(64)] private int height = 480;
        [SerializeField, Range(1f, 30f)] private float framesPerSecond = 10f;
        [SerializeField, Range(10, 100)] private int jpegQuality = 70;

        [Tooltip("Some graphics APIs hand the readback back upside down; the notebook can also flip.")]
        [SerializeField] private bool flipVertical;

        private Camera _camera;
        private RenderTexture _target;
        private Texture2D _syncReadback;
        private byte[] _raw;
        private float _timer;
        private bool _armed;
        private volatile bool _busy;
        private bool _asyncSupported;
        private bool _destroyed;

        /// <summary>Raised on a worker thread with each encoded picture.</summary>
        public event Action<byte[]> FrameEncoded;

        /// <summary>Set by the stand link: pictures are only made while someone wants them.</summary>
        public bool Wanted { get; set; }

        public int Width => width;
        public int Height => height;
        public Camera StreamCamera => _camera;

        public void Bind(Camera projectionCamera, Renderer patientSkin)
        {
            source = projectionCamera;
            framingTarget = patientSkin;
        }

        private void Start()
        {
            _asyncSupported = SystemInfo.supportsAsyncGPUReadback;

            _target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "StandStream",
                antiAliasing = 1,
            };

            GameObject holder = new GameObject("StandStreamCamera");
            holder.transform.SetParent(transform, false);
            _camera = holder.AddComponent<Camera>();

            if (source != null)
            {
                _camera.CopyFrom(source);
            }
            else
            {
                _camera.orthographic = true;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = Color.black;
                holder.transform.rotation = Quaternion.Euler(90f, 90f, 0f);
            }

            _camera.targetTexture = _target;
            _camera.enabled = false;

            UniversalAdditionalCameraData data = holder.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.allowXRRendering = false;
            data.renderShadows = false;

            Frame();
        }

        /// <summary>
        /// Points the camera straight down at the upper body. The picture's long side runs along
        /// the patient, the way the mannequin lies on the bench; the notebook turns and scales it
        /// onto the real one.
        /// </summary>
        private void Frame()
        {
            if (framingTarget == null) { return; }

            Bounds body = framingTarget.bounds;
            float from = headTowardPositiveZ
                ? Mathf.Lerp(body.min.z, body.max.z, upperBodyFrom)
                : Mathf.Lerp(body.max.z, body.min.z, upperBodyFrom);
            float to = headTowardPositiveZ ? body.max.z : body.min.z;

            float halfLength = Mathf.Abs(to - from) * 0.5f;
            float halfWidth = body.extents.x;
            float centreZ = (from + to) * 0.5f;

            Transform t = _camera.transform;
            float cameraY = source != null ? source.transform.position.y : body.max.y + 1.5f;
            t.position = new Vector3(body.center.x, cameraY, centreZ);
            t.rotation = Quaternion.Euler(90f, 90f, 0f);

            float aspect = (float)width / height;
            _camera.orthographic = true;
            _camera.aspect = aspect;
            _camera.orthographicSize = Mathf.Max(halfWidth, halfLength / aspect) * margin;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = cameraY + 1f;
        }

        private void Update()
        {
            if (_camera == null) { return; }

            if (_armed)
            {
                // A câmera desenhou no quadro anterior: agora os pixels podem ser lidos.
                _armed = false;
                _camera.enabled = false;
                Capture();
                return;
            }

            if (!Wanted || _busy)
            {
                _timer = 0f;
                return;
            }

            _timer += Time.unscaledDeltaTime;
            if (_timer < 1f / framesPerSecond) { return; }

            _timer = 0f;
            _camera.enabled = true;
            _armed = true;
        }

        private void Capture()
        {
            _busy = true;

            if (_asyncSupported)
            {
                AsyncGPUReadback.Request(_target, 0, TextureFormat.RGBA32, OnReadback);
                return;
            }

            // Sem leitura assíncrona: lê na hora, mais devagar, para não pesar.
            try
            {
                if (_syncReadback == null) { _syncReadback = new Texture2D(width, height, TextureFormat.RGB24, false); }
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = _target;
                _syncReadback.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                _syncReadback.Apply(false);
                RenderTexture.active = previous;
                FrameEncoded?.Invoke(_syncReadback.EncodeToJPG(jpegQuality));
            }
            finally
            {
                _busy = false;
            }
        }

        private void OnReadback(AsyncGPUReadbackRequest request)
        {
            if (_destroyed || request.hasError)
            {
                _busy = false;
                return;
            }

            var data = request.GetData<byte>();
            if (_raw == null || _raw.Length != data.Length) { _raw = new byte[data.Length]; }
            data.CopyTo(_raw);

            byte[] raw = _raw;
            int w = width;
            int h = height;
            int quality = jpegQuality;
            bool flip = flipVertical;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (flip) { FlipRows(raw, w, h); }
                    byte[] jpeg = ImageConversion.EncodeArrayToJPG(raw, GraphicsFormat.R8G8B8A8_UNorm, (uint)w, (uint)h, 0, quality);
                    FrameEncoded?.Invoke(jpeg);
                }
                catch (Exception error)
                {
                    Debug.LogWarning($"[Estande] Quadro descartado: {error.Message}");
                }
                finally
                {
                    _busy = false;
                }
            });
        }

        private static void FlipRows(byte[] pixels, int width, int height)
        {
            int stride = width * 4;
            byte[] row = new byte[stride];
            for (int top = 0, bottom = height - 1; top < bottom; top++, bottom--)
            {
                Buffer.BlockCopy(pixels, top * stride, row, 0, stride);
                Buffer.BlockCopy(pixels, bottom * stride, pixels, top * stride, stride);
                Buffer.BlockCopy(row, 0, pixels, bottom * stride, stride);
            }
        }

        private void OnDestroy()
        {
            _destroyed = true;
            if (_camera != null) { _camera.targetTexture = null; }
            if (_target != null) { _target.Release(); Destroy(_target); }
            if (_syncReadback != null) { Destroy(_syncReadback); }
        }
    }
}
