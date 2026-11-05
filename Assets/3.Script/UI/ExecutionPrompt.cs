using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 처형 가능한 적 머리 위에 "좌클릭 처형" 안내. 나타날 때 튀어나오고, 떠 있는 동안 깜빡이듯 밝기가 오르내림
// HUD 캔버스 아래 전체 화면 RectTransform에 붙임 (Player Execution 셋업이 만들어 줌)
public class ExecutionPrompt : MonoBehaviour
{
    [SerializeField] PlayerExecution execution;
    [SerializeField] Font font;
    [SerializeField] string label = "좌클릭  처형";
    [SerializeField] Color color = new Color(1f, 0.3f, 0.25f, 1f);
    [SerializeField] int fontSize = 24;
    [SerializeField] float height = 2.3f;

    RectTransform area;
    RectTransform rect;
    Text text;
    Camera cam;
    EnemySwordsman shown;
    Tween pulse;

    void Awake()
    {
        area = (RectTransform)transform;
        if (execution == null) execution = FindAnyObjectByType<PlayerExecution>();
        Build();
    }

    void LateUpdate()
    {
        EnemySwordsman candidate = execution != null && !execution.IsActive ? execution.Candidate : null;
        if (candidate != shown) Show(candidate);
        if (shown == null) return;

        if (cam == null) cam = Camera.main;
        if (cam == null) return;
        Vector3 screen = cam.WorldToScreenPoint(shown.transform.position + Vector3.up * height);
        text.enabled = screen.z > 0f;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(area, screen, null, out Vector2 local);
        rect.anchoredPosition = local;
    }

    void Show(EnemySwordsman candidate)
    {
        shown = candidate;
        pulse?.Kill();
        rect.gameObject.SetActive(candidate != null);
        if (candidate == null) return;

        rect.localScale = Vector3.one * 0.4f;
        text.color = color;
        pulse = DOTween.Sequence().SetUpdate(true).SetLink(rect.gameObject)
            .Append(rect.DOScale(1f, 0.2f).SetEase(Ease.OutBack))
            .Append(DOTween.To(() => text.color.a, a => text.color = new Color(color.r, color.g, color.b, a), 0.45f, 0.35f)
                .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo));
    }

    void Build()
    {
        var go = new GameObject("Prompt", typeof(RectTransform));
        rect = (RectTransform)go.transform;
        rect.SetParent(transform, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(240f, 40f);

        text = go.AddComponent<Text>();
        text.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = label;
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        outline.effectDistance = new Vector2(2f, -2f);
        go.SetActive(false);
    }
}
