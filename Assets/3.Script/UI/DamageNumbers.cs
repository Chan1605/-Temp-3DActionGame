using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 맞은 적 머리 위로 피해 숫자를 띄움. 일반 = 흰색, 치명타 = 금색 + 크게 튀어오름, 일섬 즉사 = 하늘색 999, 처형 = 표시 안 함
// 월드 위치를 매 프레임 화면 좌표로 바꿔 따라가고, 실제 시간 기준이라 히트스톱·슬로 중에도 일정하게 사라짐
public class DamageNumbers : MonoBehaviour
{
    class Entry
    {
        public Text Text;
        public RectTransform Rect;
        public Vector3 World;
        public float Age;
        public float Life;
        public float Scale;
        public Vector2 Offset;
        public Color Color;
    }

    [SerializeField] Font font;
    [SerializeField] int poolSize = 24;
    [SerializeField] int fontSize = 30;
    [SerializeField] float lifetime = 0.7f;
    [SerializeField] float rise = 70f;              // 화면 픽셀(1080 기준)
    [SerializeField] float critScale = 1.6f;
    [SerializeField] Color normalColor = new Color(1f, 1f, 1f, 1f);
    [SerializeField] Color critColor = new Color(1f, 0.78f, 0.25f, 1f);
    [SerializeField] Color killColor = new Color(0.55f, 0.85f, 1f, 1f);
    [SerializeField] bool showEnemyOnly = true;     // 플레이어가 준 피해만 (Attacker 태그 Player)

    readonly List<Entry> pool = new List<Entry>();
    RectTransform area;
    Camera cam;
    int next;

    void Awake()
    {
        area = (RectTransform)transform;
        cam = Camera.main;
        for (int i = 0; i < poolSize; i++) pool.Add(Create(i));
    }

    void OnEnable()
    {
        DamageEvents.Dealt += OnDealt;
        DamageEvents.Labeled += OnLabeled;
    }

    void OnDisable()
    {
        DamageEvents.Dealt -= OnDealt;
        DamageEvents.Labeled -= OnLabeled;
    }

    // 글자 알림은 치명타 크기로 조금 더 오래
    void OnLabeled(Vector3 position, string label, Color color)
    {
        if (!GameSettings.ShowDamageNumbers) return; // [변경]
        Entry e = pool[next];
        next = (next + 1) % pool.Count;
        e.Text.text = label;
        e.Color = color;
        e.Scale = critScale;
        e.World = position;
        e.Age = 0f;
        e.Life = lifetime * 1.3f;
        e.Offset = new Vector2(0f, 20f);
        e.Text.gameObject.SetActive(true);
        Place(e, 0f);
    }

    void OnDealt(Vector3 position, DamageInfo info)
    {
        if (!GameSettings.ShowDamageNumbers) return; // [변경] 설정에서 끄면 숫자를 띄우지 않음
        if (showEnemyOnly && (info.Attacker == null || !info.Attacker.CompareTag("Player"))) return;
        if (info.Weight == AttackWeight.Execution) return; // 처형은 숫자 없이 연출만

        Entry e = pool[next];
        next = (next + 1) % pool.Count;

        if (info.InstantKill)
        {
            e.Text.text = "999"; // 일섬 즉사 (주석 처리 시 풀에 남아 있던 이전 숫자가 그대로 보였음)
            e.Color = killColor;
            e.Scale = critScale;
        }
        else
        {
            e.Text.text = Mathf.RoundToInt(info.Damage).ToString();
            e.Color = info.Critical ? critColor : normalColor;
            e.Scale = info.Critical ? critScale : 1f;
        }

        e.World = position;
        e.Age = 0f;
        e.Life = info.Critical || info.InstantKill ? lifetime * 1.3f : lifetime;
        e.Offset = new Vector2(Random.Range(-30f, 30f), Random.Range(-8f, 8f)); // 연타가 겹치지 않게 살짝 흩뿌림
        e.Text.gameObject.SetActive(true);
        Place(e, 0f);
    }

    void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        float dt = Time.unscaledDeltaTime;

        foreach (Entry e in pool)
        {
            if (!e.Text.gameObject.activeSelf) continue;
            e.Age += dt;
            float k = e.Age / e.Life;
            if (k >= 1f)
            {
                e.Text.gameObject.SetActive(false);
                continue;
            }
            Place(e, k);
        }
    }

    // 처음 0.12초는 크게 튀었다가 제 크기로, 이후 위로 떠오르며 마지막 40%에서 사라짐
    void Place(Entry e, float k)
    {
        if (cam == null) return;

        Vector3 screen = cam.WorldToScreenPoint(e.World);
        if (screen.z < 0f)
        {
            e.Text.gameObject.SetActive(false);
            return;
        }
        RectTransformUtility.ScreenPointToLocalPointInRectangle(area, screen, null, out Vector2 local);

        float eased = 1f - (1f - k) * (1f - k);
        e.Rect.anchoredPosition = local + e.Offset + Vector2.up * rise * eased;

        float pop = e.Age < 0.12f ? Mathf.Lerp(1.5f, 1f, e.Age / 0.12f) : 1f;
        e.Rect.localScale = Vector3.one * e.Scale * pop;

        Color c = e.Color;
        c.a = k < 0.6f ? 1f : 1f - (k - 0.6f) / 0.4f;
        e.Text.color = c;
    }

    Entry Create(int index)
    {
        var go = new GameObject($"Damage_{index}", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(200f, 50f);

        var text = go.AddComponent<Text>();
        text.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;

        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(2f, -2f);

        go.SetActive(false);
        return new Entry { Text = text, Rect = rt };
    }
}
