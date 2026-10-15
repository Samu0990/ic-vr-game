using UnityEngine;
using UnityEngine.UI;

namespace VRSurgery.Stand
{
    /// <summary>
    /// Builds the notebook's screens in code, with the built-in font the game's own HUD uses.
    /// No prefab and no scene editing: the stand app is one empty scene that assembles itself,
    /// so there is nothing to drift out of step with the code.
    /// </summary>
    public static class StandUi
    {
        private static Font _font;

        public static Font Font => _font != null ? _font : (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        public static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out Color c) ? c : Color.magenta;

        public static Canvas Canvas(string name, Transform parent, Vector2 reference, int order = 0)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;

            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            if (reference == Vector2.zero)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            }
            else
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = reference;
                scaler.matchWidthOrHeight = 0.5f;
            }

            return canvas;
        }

        public static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin = default, Vector2 offsetMax = default)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return rect;
        }

        public static Image Panel(string name, Transform parent, Color colour, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin = default, Vector2 offsetMax = default)
        {
            RectTransform rect = Rect(name, parent, anchorMin, anchorMax, offsetMin, offsetMax);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;
            return image;
        }

        public static Text Label(string name, Transform parent, string text, int size, Color colour,
            TextAnchor alignment, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin = default,
            Vector2 offsetMax = default, FontStyle style = FontStyle.Normal)
        {
            RectTransform rect = Rect(name, parent, anchorMin, anchorMax, offsetMin, offsetMax);
            Text label = rect.gameObject.AddComponent<Text>();
            label.font = Font;
            label.text = text;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = colour;
            label.alignment = alignment;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.raycastTarget = false;
            return label;
        }

        /// <summary>Plain camera that only clears the screen; the canvas draws on top of it.</summary>
        public static Camera ClearingCamera(Transform parent, Color background)
        {
            GameObject go = new GameObject("Camera");
            go.transform.SetParent(parent, false);
            Camera cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.cullingMask = 0;
            cam.orthographic = true;
            return cam;
        }

        /// <summary>"2:58,4" — minutes, seconds and tenths, with the Brazilian decimal comma.</summary>
        public static string Time(float seconds)
        {
            if (seconds < 0f) { seconds = 0f; }
            int tenths = Mathf.RoundToInt(seconds * 10f);
            int minutes = tenths / 600;
            int secs = tenths / 10 % 60;
            return $"{minutes}:{secs:00},{tenths % 10}";
        }

        /// <summary>"2:31" for a running clock.</summary>
        public static string Clock(float seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{total / 60}:{total % 60:00}";
        }

        public static string Stage(string stage) => stage switch
        {
            "Idle" => "Preparação",
            "SkinIncision" => "Corte da pele",
            "OpenChest" => "Abertura do tórax",
            "OpenPericardium" => "Abertura do pericárdio",
            "GoOnBypass" => "Circulação extracorpórea",
            "RemoveNativeHeart" => "Retirada do coração doente",
            "PlaceDonorHeart" => "Coração do doador",
            "ConnectVessels" => "Conexão dos vasos",
            "Restart" => "Saída de bomba",
            "CloseSkin" => "Fechamento da pele",
            "Complete" => "Cirurgia concluída",
            _ => string.IsNullOrEmpty(stage) ? "—" : stage,
        };
    }
}
