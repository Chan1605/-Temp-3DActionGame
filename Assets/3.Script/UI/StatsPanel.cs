using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 좌하단 체력 위: 레벨과 성장 스탯 4종. 레벨이 오르면 "레벨업!" 문구가 떠오름
// [변경] DOTween 연출: 경험치 막대가 차오르고(레벨업이면 끝까지 찼다가 비워진 뒤 다시), 레벨 글자 펀치, 알림이 튀어나와 위로 사라짐
public class StatsPanel : MonoBehaviour
{
    [SerializeField] PlayerStats stats;
    [SerializeField] Text levelText;
    [SerializeField] RectTransform xpFill;
    [SerializeField] Text statText;
    [SerializeField] CanvasGroup levelUpGroup;
    [SerializeField] Text levelUpText;
    [SerializeField] float levelUpShowTime = 2.2f;

    static readonly Color Gold = new Color(1f, 0.85f, 0.4f);

    int shownLevel = -1;
    float shownXp = -1f;
    Color levelColor, statColor;
    Sequence xpSeq, popupSeq;
    Vector2 popupHome;

    void Awake()
    {
        if (stats == null) stats = FindAnyObjectByType<PlayerStats>();
        if (levelText != null) levelColor = levelText.color;
        if (statText != null) statColor = statText.color;
        if (levelUpGroup != null)
        {
            popupHome = ((RectTransform)levelUpGroup.transform).anchoredPosition;
            levelUpGroup.alpha = 0f;
        }
    }

    void OnEnable()
    {
        if (stats == null) return;
        stats.StatsChanged += Refresh;
        stats.LeveledUp += OnLeveledUp;
        Refresh();
    }

    void OnDisable()
    {
        if (stats == null) return;
        stats.StatsChanged -= Refresh;
        stats.LeveledUp -= OnLeveledUp;
    }

    void Refresh()
    {
        if (stats == null) return;

        bool max = stats.Level >= stats.MaxLevel;
        string xpLabel = !stats.UsesXp ? "" : max ? "  <size=14>MAX</size>" : $"  <size=14>{stats.Xp} / {stats.XpToNext}</size>";
        if (levelText != null) levelText.text = $"Lv {stats.Level}{xpLabel}";
        if (statText != null)
            statText.text = $"공격력 {stats.AttackPower * 100f:0}%   공속 {stats.AttackSpeed:0.00}\n치명타 {stats.CritChance * 100f:0}%   치명 피해 {stats.CritDamage * 100f:0}%";

        AnimateXp(max ? 1f : stats.XpToNext > 0 ? (float)stats.Xp / stats.XpToNext : 0f, stats.Level);
    }

    // [변경] 핵심: 같은 레벨이면 목표까지 차오르고, 레벨이 올랐으면 끝까지 → 비우기 → 남은 양까지
    void AnimateXp(float target, int level)
    {
        if (xpFill == null) return;

        if (shownLevel < 0)
        {
            SetXp(target);
            shownLevel = level;
            shownXp = target;
            return;
        }

        bool leveled = level > shownLevel;
        if (!leveled && Mathf.Approximately(target, shownXp)) return;

        xpSeq?.Kill();
        xpSeq = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
        if (leveled)
        {
            xpSeq.Append(XpTween(1f, 0.2f))
                .AppendCallback(() => SetXp(0f))
                .Append(XpTween(target, 0.35f));
        }
        else
        {
            xpSeq.Append(XpTween(target, 0.3f));
            if (xpFill.parent != null) UITween.Punch(xpFill.parent, 0.08f, 0.2f);
        }

        shownLevel = level;
        shownXp = target;
    }

    Tween XpTween(float to, float duration) =>
        DOTween.To(() => xpFill.anchorMax.x, SetXp, to, duration).SetEase(Ease.OutCubic);

    void SetXp(float x) => xpFill.anchorMax = new Vector2(x, xpFill.anchorMax.y);

    void OnLeveledUp(int level)
    {
        // 레벨 글자 · 스탯 글자 번쩍임
        if (levelText != null)
        {
            UITween.Punch(levelText.transform, 0.35f, 0.4f);
            UITween.Flash(levelText, Color.white, levelColor, 0.6f);
        }
        if (statText != null) UITween.Flash(statText, Gold, statColor, 0.8f);

        if (levelUpGroup == null) return;
        if (levelUpText != null) levelUpText.text = $"레벨업!  Lv {level}";

        // 알림: 작게 튀어나와 커지고 → 머문 뒤 → 위로 떠오르며 사라짐
        var rect = (RectTransform)levelUpGroup.transform;
        popupSeq?.Kill();
        rect.anchoredPosition = popupHome;
        rect.localScale = Vector3.one * 0.5f;
        levelUpGroup.alpha = 0f;

        popupSeq = DOTween.Sequence().SetUpdate(true).SetLink(gameObject)
            .Append(DOTween.To(() => levelUpGroup.alpha, a => levelUpGroup.alpha = a, 1f, 0.12f))
            .Join(rect.DOScale(1f, 0.4f).SetEase(Ease.OutBack, 2.5f))
            .AppendInterval(Mathf.Max(0f, levelUpShowTime - 0.9f))
            .Append(DOTween.To(() => levelUpGroup.alpha, a => levelUpGroup.alpha = a, 0f, 0.5f))
            .Join(UITween.AnchorPos(rect, popupHome + new Vector2(0f, 36f), 0.5f, Ease.InQuad, false));
    }
}
