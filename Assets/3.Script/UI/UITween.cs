using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// HUD용 DOTween 묶음. 전부 실제 시간(SetUpdate(true)) → 히트스톱·간파 슬로 중에도 UI는 정상 속도
// DOTween UI 모듈 설정 여부와 상관없게 코어 API(DOTween.To · Transform 단축)만 사용
public static class UITween
{
    public static Tween Fade(CanvasGroup group, float to, float duration, Ease ease = Ease.OutQuad)
    {
        group.DOKill();
        return DOTween.To(() => group.alpha, a => group.alpha = a, to, duration)
            .SetEase(ease).SetUpdate(true).SetTarget(group).SetLink(group.gameObject);
    }

    // standalone=false면 Sequence 안에 넣을 용도 (업데이트 방식·수명은 Sequence가 결정)
    public static Tween AnchorPos(RectTransform rt, Vector2 to, float duration, Ease ease = Ease.OutCubic, bool standalone = true)
    {
        Tween t = DOTween.To(() => rt.anchoredPosition, p => rt.anchoredPosition = p, to, duration).SetEase(ease);
        return standalone ? Own(t, rt) : t;
    }

    // 막대 채움: 오른쪽 앵커(anchorMax.x)를 움직임
    public static Tween Fill(RectTransform fill, float to, float duration, Ease ease = Ease.OutCubic)
    {
        return DOTween.To(() => fill.anchorMax.x, x => fill.anchorMax = new Vector2(x, fill.anchorMax.y), to, duration)
            .SetEase(ease).SetUpdate(true).SetTarget(fill).SetLink(fill.gameObject);
    }

    public static Tween Width(RectTransform rt, float to, float duration, Ease ease = Ease.OutCubic, bool standalone = true)
    {
        Tween t = DOTween.To(() => rt.sizeDelta.x, w => rt.sizeDelta = new Vector2(w, rt.sizeDelta.y), to, duration).SetEase(ease);
        return standalone ? Own(t, rt) : t;
    }

    public static Tween Color(Graphic g, UnityEngine.Color to, float duration, bool standalone = true)
    {
        Tween t = DOTween.To(() => g.color, c => g.color = c, to, duration);
        return standalone ? Own(t, g) : t;
    }

    static Tween Own(Tween t, Component owner) => t.SetUpdate(true).SetTarget(owner).SetLink(owner.gameObject);

    // 순간 다른 색으로 번쩍였다가 원래 색으로
    public static void Flash(Graphic g, UnityEngine.Color flash, UnityEngine.Color back, float duration = 0.25f)
    {
        if (g == null) return;
        DOTween.Kill(g);
        g.color = flash;
        Color(g, back, duration).SetEase(Ease.OutQuad);
    }

    public static void Punch(Transform t, float amount = 0.15f, float duration = 0.3f)
    {
        if (t == null) return;
        t.DOKill(true);
        t.localScale = Vector3.one;
        t.DOPunchScale(Vector3.one * amount, duration, 6, 0.6f).SetUpdate(true).SetLink(t.gameObject);
    }

    // 흔들림이 겹쳐도 제자리를 잃지 않게 이전 흔들림은 끝까지 완료(원위치)시키고 새로 시작
    static readonly System.Collections.Generic.Dictionary<Transform, Tween> shakes = new System.Collections.Generic.Dictionary<Transform, Tween>();

    public static void Shake(Transform t, float strength = 6f, float duration = 0.25f)
    {
        if (t == null) return;
        if (shakes.TryGetValue(t, out Tween old) && old.IsActive()) old.Complete();
        shakes[t] = t.DOShakePosition(duration, strength, 24, 90f, false, true).SetUpdate(true).SetLink(t.gameObject);
    }
}
