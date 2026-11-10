using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// 코드로 UI를 만들 때 쓰는 공용 도우미 (1920x1080 기준, 좌상단 원점 · y는 아래로)
public static class UIKit
{
    public static Canvas CreateCanvas(Transform parent, string name, int sortingOrder)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Canvas canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    // x·centerY: 왼쪽 위 기준 픽셀, 세로는 가운데
    public static void Place(RectTransform rect, float x, float centerY, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(x, -centerY);
        rect.sizeDelta = new Vector2(width, height);
    }

    public static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    public static Text Text(Transform parent, string text, Font font, int size, Color color, TextAnchor anchor)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Text t = go.AddComponent<Text>();
        t.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = anchor;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    public static Image Image(Transform parent, Sprite sprite, Color color)
    {
        var go = new GameObject("Image", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Image image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        if (sprite != null && sprite.border != Vector4.zero) image.type = UnityEngine.UI.Image.Type.Sliced;
        return image;
    }

    // 글자 버튼: 평소 dim, 마우스를 올리면 하얗게, 누르면 accent
    public static Button TextButton(Text text, Color dim, Color accent, Action onClick)
    {
        text.raycastTarget = true;
        Button button = text.gameObject.AddComponent<Button>();
        button.targetGraphic = text;
        ColorBlock colors = button.colors;
        colors.normalColor = dim;
        colors.highlightedColor = Color.white;
        colors.selectedColor = dim;
        colors.pressedColor = accent;
        colors.disabledColor = new Color(dim.r, dim.g, dim.b, dim.a * 0.4f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        Navigation nav = button.navigation;
        nav.mode = Navigation.Mode.None;
        button.navigation = nav;
        button.onClick.AddListener(() => onClick());
        return button;
    }

    public static void EnsureEventSystem()
    {
        if (EventSystem.current != null || UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        UnityEngine.Object.DontDestroyOnLoad(go);
    }

    // ===== 코드로 만드는 스프라이트 =====
    // 둥근 사각형. thickness > 0이면 테두리만. 9-slice 테두리 = 반지름
    public static Sprite RoundRect(int width, int height, float radius, float thickness)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, radius, width - radius);
                float cy = Mathf.Clamp(y + 0.5f, radius, height - radius);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                float a = Mathf.Clamp01(radius - d + 0.5f);
                if (thickness > 0f) a *= Mathf.Clamp01(d - (radius - thickness) + 0.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
            new Vector4(radius, radius, radius, radius));
    }

    // 왼쪽 진하고 오른쪽으로 사라지는 가로 그라데이션
    public static Sprite Gradient(int width)
    {
        var tex = new Texture2D(width, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int x = 0; x < width; x++) tex.SetPixel(x, 0, new Color(1f, 1f, 1f, 1f - Mathf.SmoothStep(0f, 1f, x / (float)(width - 1))));
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, width, 1), new Vector2(0f, 0.5f), 100f);
    }
}
