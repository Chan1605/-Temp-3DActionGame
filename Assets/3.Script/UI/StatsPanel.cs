using UnityEngine;
using UnityEngine.UI;

// 좌하단 체력 위: 레벨과 성장 스탯 4종. 레벨이 오르면 "성장!" 문구가 잠깐 떠오름
public class StatsPanel : MonoBehaviour
{
    [SerializeField] PlayerStats stats;
    [SerializeField] Text levelText;
    [SerializeField] Text statText;
    [SerializeField] CanvasGroup levelUpGroup;
    [SerializeField] Text levelUpText;
    [SerializeField] float levelUpShowTime = 2.2f;

    float levelUpTimer;

    void Awake()
    {
        if (stats == null) stats = FindAnyObjectByType<PlayerStats>();
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

    void Update()
    {
        if (levelUpGroup == null || levelUpTimer <= 0f) return;
        levelUpTimer -= Time.unscaledDeltaTime;
        // 앞 0.2초 나타나고 마지막 0.5초 사라짐
        float shown = levelUpShowTime - levelUpTimer;
        levelUpGroup.alpha = Mathf.Min(Mathf.Clamp01(shown / 0.2f), Mathf.Clamp01(levelUpTimer / 0.5f));
    }

    void Refresh()
    {
        if (stats == null) return;
        if (levelText != null) levelText.text = $"Lv {stats.Level}";
        if (statText != null)
            statText.text = $"공격력 {stats.AttackPower * 100f:0}%   공속 {stats.AttackSpeed:0.00}\n치명타 {stats.CritChance * 100f:0}%   치명 피해 {stats.CritDamage * 100f:0}%";
    }

    void OnLeveledUp(int level)
    {
        if (levelUpText != null) levelUpText.text = $"레벨업!  Lv {level}";
        levelUpTimer = levelUpShowTime;
    }
}
