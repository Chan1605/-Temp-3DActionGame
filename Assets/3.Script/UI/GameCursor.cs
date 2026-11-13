using UnityEngine;
using UnityEngine.InputSystem;

// 마우스 커서: 칼끝처럼 날렵한 포인터 (먹색 몸체 + 금빛 테두리). 누르고 있는 동안은 뇌광색으로 바뀜
// 게임을 켜면 자동으로 한 번 붙고 모든 씬에서 유지 (하드웨어 커서라 성능 부담 없음). 씬 · 인스펙터 연결 필요 없음
public class GameCursor : MonoBehaviour
{
    static readonly Color Ink = new Color(0.07f, 0.075f, 0.095f);
    static readonly Color InkLight = new Color(0.16f, 0.17f, 0.21f);
    static readonly Color Gold = new Color(0.95f, 0.8f, 0.45f);
    static readonly Color BoltInk = new Color(0.12f, 0.2f, 0.32f);
    static readonly Color BoltInkLight = new Color(0.22f, 0.36f, 0.52f);
    static readonly Color BoltEdge = new Color(0.82f, 0.93f, 1f);

    // 32px 기준 꼭짓점 (왼쪽 위 원점): 칼끝 → 오른쪽 날개 → 안쪽 홈 → 꼬리
    static readonly Vector2[] Shape = { new Vector2(1.5f, 1.5f), new Vector2(22.5f, 13f), new Vector2(12.5f, 15.5f), new Vector2(6.5f, 27.5f) };

    Texture2D normal, pressed;
    Vector2 hotspot;
    bool down;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (FindAnyObjectByType<GameCursor>() != null) return;
        var go = new GameObject("GameCursor");
        DontDestroyOnLoad(go);
        go.AddComponent<GameCursor>();
    }

    void Awake()
    {
        int size = Screen.height >= 1400 ? 48 : 32; // 고해상도 모니터에서 너무 작지 않게
        float scale = size / 32f;
        normal = Draw(size, scale, Ink, InkLight, Gold);
        pressed = Draw(size, scale, BoltInk, BoltInkLight, BoltEdge);
        hotspot = Shape[0] * scale;
        Apply();
    }

    void OnDestroy()
    {
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        if (normal != null) Destroy(normal);
        if (pressed != null) Destroy(pressed);
    }

    void Update()
    {
        bool now = Mouse.current != null && Mouse.current.leftButton.isPressed;
        if (now == down) return;
        down = now;
        Apply();
    }

    void Apply() => Cursor.SetCursor(down ? pressed : normal, hotspot, CursorMode.Auto);

    // 핵심: 다각형까지의 거리로 칠함. 한 픽셀을 4x4로 나눠 찍어 테두리를 부드럽게
    static Texture2D Draw(int size, float scale, Color ink, Color inkLight, Color edge)
    {
        var points = new Vector2[Shape.Length];
        for (int i = 0; i < Shape.Length; i++) points[i] = Shape[i] * scale;
        float edgeWidth = 1.5f * scale;
        float halo = 1.2f * scale;

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var pixels = new Color[size * size];
        const int Sub = 4;
        for (int py = 0; py < size; py++)
            for (int px = 0; px < size; px++)
            {
                Color sum = Color.clear;
                for (int sy = 0; sy < Sub; sy++)
                    for (int sx = 0; sx < Sub; sx++)
                    {
                        var p = new Vector2(px + (sx + 0.5f) / Sub, py + (sy + 0.5f) / Sub);
                        float d = EdgeDistance(points, p);
                        Color c;
                        if (Inside(points, p))
                        {
                            if (d < edgeWidth) c = edge;
                            else c = Side(points[0], points[2], p) > 0f ? inkLight : ink; // 칼끝~홈 선으로 나눠 한쪽을 밝게 (각진 느낌)
                        }
                        else if (d < halo) c = new Color(0f, 0f, 0f, 0.55f * (1f - d / halo)); // 밝은 배경에서도 보이게 바깥 그림자
                        else c = Color.clear;
                        sum += new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
                    }
                sum /= Sub * Sub;
                Color result = sum.a > 0f ? new Color(sum.r / sum.a, sum.g / sum.a, sum.b / sum.a, sum.a) : Color.clear;
                pixels[(size - 1 - py) * size + px] = result; // 텍스처는 아래가 원점
            }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    static float Side(Vector2 a, Vector2 b, Vector2 p) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

    static bool Inside(Vector2[] poly, Vector2 p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                inside = !inside;
        return inside;
    }

    static float EdgeDistance(Vector2[] poly, Vector2 p)
    {
        float best = float.MaxValue;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            Vector2 a = poly[j], b = poly[i];
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
        }
        return best;
    }
}
