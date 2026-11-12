using UnityEngine;
using UnityEngine.UI;

// 자유 연습장 규칙 (Training 씬, Tools/Setup/Training Scene이 배치)
//  스킬 전부 해금은 SkillTreeMenu.unlockAllAtStart(셋업이 켬)로 → 수련 패시브까지 적용
//  뇌기 항상 가득 · 맞으면 잠깐 뒤 체력 회복 · 쓰러지면 그 자리에서 부활 (구역이 없어 부활시켜 줄 곳이 없음) · 왼쪽 위 안내 문구
public class TrainingMode : MonoBehaviour
{
    [SerializeField] Font font;
    [SerializeField] float healDelay = 1.5f; // 마지막 피격 뒤 이 시간이 지나면 체력 가득
    [SerializeField] float reviveDelay = 2f;

    PlayerThunder thunder;
    PlayerHealth health;
    PlayerSkill skill;
    PlayerUltimate ultimate;
    float lastHealth;
    float sinceHit;

    void Start()
    {
        thunder = FindAnyObjectByType<PlayerThunder>();
        health = FindAnyObjectByType<PlayerHealth>();
        skill = FindAnyObjectByType<PlayerSkill>();
        ultimate = FindAnyObjectByType<PlayerUltimate>();
        if (health != null)
        {
            lastHealth = health.Health;
            health.Died += OnDied;
        }
        BuildHint();
    }

    void OnDestroy()
    {
        if (health != null) health.Died -= OnDied;
    }

    void OnDied() => Invoke(nameof(Revive), reviveDelay);

    void Revive()
    {
        if (health == null) return;
        health.Revive();
        lastHealth = health.Health;
    }

    void LateUpdate()
    {
        // 발도 중엔 채우지 않음 → 표식 · 연쇄 연출이 평소처럼 보임
        if (thunder != null && (skill == null || !skill.IsActive) && thunder.Gauge < thunder.MaxGauge)
            thunder.Add(thunder.MaxGauge - thunder.Gauge);

        if (ultimate != null && !ultimate.Active && ultimate.Gauge < PlayerUltimate.MaxBars) ultimate.SetGauge(PlayerUltimate.MaxBars); // [변경] 궁극기도 항상 3칸

        if (health == null || health.IsDead) return;
        if (health.Health < lastHealth) sinceHit = 0f;
        lastHealth = health.Health;
        sinceHit += Time.deltaTime;
        if (sinceHit >= healDelay && health.Health < health.MaxHealth)
        {
            health.HealFull();
            lastHealth = health.Health;
        }
    }

    void BuildHint()
    {
        Canvas canvas = UIKit.CreateCanvas(transform, "TrainingHint", 20);
        Text t = UIKit.Text(canvas.transform, "연습장  ·  스킬 전부 해금  ·  뇌기 · 궁극기 무한  ·  ESC → QUIT GAME → Main Menu",
            font, 20, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleLeft);
        UIKit.Place(t.rectTransform, 40f, 40f, 1200f, 30f);
        t.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(1.5f, -1.5f);
    }
}
