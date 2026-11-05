using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 락온 대상 가슴 위치에 마름모 표시. 잡는 순간 크게 튀었다가 제자리, 잡고 있는 동안 천천히 숨쉬듯 커졌다 작아짐
// HUD 캔버스 아래 전체 화면 RectTransform에 붙임 (Player Lock On 셋업이 만들어 줌)
public class LockOnMarker : MonoBehaviour
{
    [SerializeField] PlayerLockOn lockOn;
    [SerializeField] Color color = new Color(1f, 0.45f, 0.25f, 0.95f);
    [SerializeField] float size = 16f;
    [SerializeField] float height = 1.25f;

    RectTransform area;
    RectTransform marker;
    Image image;
    Camera cam;
    Transform target;
    Tween pulse;

    void Awake()
    {
        area = (RectTransform)transform;
        if (lockOn == null) lockOn = FindAnyObjectByType<PlayerLockOn>();
        Build();
    }

    void OnEnable()
    {
        if (lockOn != null) lockOn.TargetChanged += OnTargetChanged;
    }

    void OnDisable()
    {
        if (lockOn != null) lockOn.TargetChanged -= OnTargetChanged;
    }

    void OnTargetChanged(Transform t)
    {
        target = t;
        pulse?.Kill();
        marker.gameObject.SetActive(t != null);
        if (t == null) return;

        // [변경] 무한 반복은 Sequence 안에 넣을 수 없어(DOTween 경고) 튀어나온 뒤 따로 시작
        marker.localScale = Vector3.one * 2.2f;
        pulse = marker.DOScale(1f, 0.18f).SetEase(Ease.OutBack).SetUpdate(true).SetLink(marker.gameObject)
            .OnComplete(() => pulse = marker.DOScale(1.25f, 0.6f).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true).SetLink(marker.gameObject));
        Place();
    }

    void LateUpdate()
    {
        if (target == null)
        {
            if (marker.gameObject.activeSelf) marker.gameObject.SetActive(false);
            return;
        }
        Place();
    }

    void Place()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null || target == null) return;

        Vector3 screen = cam.WorldToScreenPoint(target.position + Vector3.up * height);
        image.enabled = screen.z > 0f;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(area, screen, null, out Vector2 local);
        marker.anchoredPosition = local;
    }

    // 흰 사각형을 45도 돌린 마름모 + 검은 외곽선
    void Build()
    {
        var go = new GameObject("Marker", typeof(RectTransform));
        marker = (RectTransform)go.transform;
        marker.SetParent(transform, false);
        marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0.5f);
        marker.sizeDelta = new Vector2(size, size);
        marker.localRotation = Quaternion.Euler(0f, 0f, 45f);

        image = go.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        go.SetActive(false);
    }
}
