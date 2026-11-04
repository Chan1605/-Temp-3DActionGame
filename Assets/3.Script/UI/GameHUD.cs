using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// 1차 HUD: 체력·뇌기(좌하단), 일섬 쿨타임(우하단), 구역 배너(상단 중앙), 웨이브(우상단), 사망·돌파 메시지(중앙)
// 화면 요소는 Tools/Setup/HUD가 만들고 여기 연결함. 전투 코드는 HUD를 모르고, HUD가 값을 읽기만 함
public class GameHUD : MonoBehaviour
{
    [Header("Sources")]
    [SerializeField] PlayerHealth health;
    [SerializeField] PlayerSkill skill;
    [SerializeField] PlayerThunder thunderSource; // P4 뇌기

    [Header("Health")]
    [SerializeField] RectTransform hpFill;
    [SerializeField] RectTransform hpTrail;
    [SerializeField] Text hpText;
    [SerializeField] float trailDelay = 0.4f;
    [SerializeField] float trailSpeed = 0.8f;

    // 뇌기는 P4에서 연결 예정. 그 전에는 previewThunder로 배치만 확인
    [Header("Thunder")]
    [SerializeField] RectTransform[] thunderFills;
    [SerializeField, Range(0f, 4f)] float previewThunder;
    [SerializeField] bool showThunder; // P4 전까지 빈 칸이 고장처럼 보여 숨김. SetThunder가 처음 불리면 자동으로 켜짐

    [Header("Skill")]
    [SerializeField] Image skillCooldown;
    [SerializeField] Text skillLabel;
    [SerializeField] Color skillReadyColor = Color.white;
    [SerializeField] Color skillCoolColor = new Color(1f, 1f, 1f, 0.35f);

    // [변경] 우클릭 차지 강타 게이지
    [Header("Charge")]
    [SerializeField] PlayerChargeAttack chargeSource;
    [SerializeField] CanvasGroup chargeGauge;
    [SerializeField] RectTransform chargeFill;
    [SerializeField] Text chargeLabel;
    [SerializeField] Color[] chargeLevelColors = { new Color(0.45f, 0.7f, 1f), new Color(0.45f, 0.95f, 1f), new Color(1f, 0.9f, 0.45f) };

    [Header("Zone")]
    [SerializeField] CanvasGroup zoneBanner;
    [SerializeField] Text zoneNumber;
    [SerializeField] Text zoneTitle;
    [SerializeField] float bannerHold = 2.2f;

    [Header("Wave")]
    [SerializeField] CanvasGroup wavePanel;
    [SerializeField] Text waveText;
    [SerializeField] Text enemyText;

    [Header("Messages")]
    [SerializeField] CanvasGroup deathPanel;
    [SerializeField] CanvasGroup clearPanel;
    [SerializeField] float clearHold = 1.6f;

    float trail = 1f;
    float trailWait;
    float thunder = -1f; // 0 이상이면 SetThunder로 받은 실제 값
    Coroutine bannerRoutine;
    Coroutine clearRoutine;

    void Awake()
    {
        if (health == null) health = FindAnyObjectByType<PlayerHealth>();
        if (skill == null) skill = FindAnyObjectByType<PlayerSkill>();
        if (thunderSource == null) thunderSource = FindAnyObjectByType<PlayerThunder>();
        if (thunderSource != null) showThunder = true;

        SetThunderVisible(showThunder);
        SetAlpha(zoneBanner, 0f);
        SetAlpha(wavePanel, 0f);
        SetAlpha(deathPanel, 0f);
        SetAlpha(clearPanel, 0f);
        SetAlpha(chargeGauge, 0f); // [변경]
        if (chargeSource == null) chargeSource = FindAnyObjectByType<PlayerChargeAttack>();
    }

    void OnEnable()
    {
        ZoneArena.AnyEntered += OnZoneEntered;
        ZoneArena.AnyCleared += OnZoneCleared;
    }

    void OnDisable()
    {
        ZoneArena.AnyEntered -= OnZoneEntered;
        ZoneArena.AnyCleared -= OnZoneCleared;
    }

    // P4 뇌기 구현 후 플레이어 쪽에서 호출 (0~4칸, 소수 = 채워지는 중인 칸)
    public void SetThunder(float cells)
    {
        thunder = Mathf.Clamp(cells, 0f, thunderFills != null ? thunderFills.Length : 4);
        if (!showThunder) SetThunderVisible(showThunder = true);
    }

    // 칸 테두리(채움 막대의 부모)째로 켜고 끔
    void SetThunderVisible(bool visible)
    {
        if (thunderFills == null) return;
        foreach (RectTransform fill in thunderFills)
            if (fill != null && fill.parent != null) fill.parent.gameObject.SetActive(visible);
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime; // 히트스톱·슬로 중에도 UI는 정상 속도
        UpdateHealth(dt);
        UpdateThunder();
        UpdateSkill();
        UpdateCharge(dt); // [변경]
        UpdateWave();
        if (deathPanel != null) Fade(deathPanel, health != null && health.IsDead ? 1f : 0f, 2.5f, dt);
    }

    // 핵심: 실제 체력(빨강)은 바로 줄고, 뒤의 밝은 막대는 잠깐 멈췄다 따라 내려가 "깎인 양"을 보여줌
    void UpdateHealth(float dt)
    {
        if (health == null || hpFill == null) return;

        float ratio = health.MaxHealth > 0f ? Mathf.Clamp01(health.Health / health.MaxHealth) : 0f;
        SetFill(hpFill, ratio);

        if (ratio >= trail)
        {
            trail = ratio;
            trailWait = trailDelay;
        }
        else if (trailWait > 0f) trailWait -= dt;
        else trail = Mathf.MoveTowards(trail, ratio, trailSpeed * dt);

        if (hpTrail != null) SetFill(hpTrail, trail);
        if (hpText != null) hpText.text = $"{Mathf.CeilToInt(health.Health)} / {Mathf.CeilToInt(health.MaxHealth)}";
    }

    void UpdateThunder()
    {
        if (thunderFills == null) return;
        float value = thunderSource != null ? thunderSource.Gauge : thunder >= 0f ? thunder : previewThunder;
        for (int i = 0; i < thunderFills.Length; i++)
            if (thunderFills[i] != null) SetFill(thunderFills[i], Mathf.Clamp01(value - i));
    }

    void UpdateSkill()
    {
        if (skill == null || skillCooldown == null) return;
        float ratio = skill.CooldownRatio;
        // 뇌기 소모형이면 즉발 비용(2칸)까지 모인 정도를 원형으로 표시 (어두운 부분 = 모자란 만큼)
        if (thunderSource != null && thunderSource.UseGaugeForSkill)
            ratio = 1f - Mathf.Clamp01(thunderSource.Gauge / thunderSource.SkillCost(0));
        skillCooldown.fillAmount = ratio;
        if (skillLabel != null) skillLabel.color = ratio > 0f ? skillCoolColor : skillReadyColor;
    }

    // [변경] 차지 중에만 나타나고, 단계마다 색이 바뀜 (3단계 = 금색)
    void UpdateCharge(float dt)
    {
        if (chargeSource == null || chargeGauge == null) return;

        bool charging = chargeSource.IsCharging;
        Fade(chargeGauge, charging ? 1f : 0f, charging ? 8f : 4f, dt);
        if (!charging) return;

        if (chargeFill != null)
        {
            SetFill(chargeFill, chargeSource.ChargeRatio);
            var image = chargeFill.GetComponent<Image>();
            if (image != null && chargeLevelColors.Length > 0)
                image.color = chargeLevelColors[Mathf.Clamp(chargeSource.Level - 1, 0, chargeLevelColors.Length - 1)];
        }
        if (chargeLabel != null) chargeLabel.text = chargeSource.Level >= chargeSource.MaxLevel ? "풀차지" : $"차지 {chargeSource.Level}";
    }

    void UpdateWave()
    {
        if (wavePanel == null) return;

        ZoneArena zone = ZoneArena.Fighting;
        Fade(wavePanel, zone != null ? 1f : 0f, 4f, Time.unscaledDeltaTime);
        if (zone == null) return;

        if (waveText != null)
            waveText.text = zone.CurrentWave > 0 && zone.WaveCount > 0 ? $"웨이브 {zone.CurrentWave} / {zone.WaveCount}" : "전투";
        if (enemyText != null) enemyText.text = $"남은 적 {zone.AliveCount}";
    }

    void OnZoneEntered(ZoneArena zone)
    {
        if (zoneBanner == null) return;
        if (zoneNumber != null) zoneNumber.text = zone.DisplayNumber;
        if (zoneTitle != null) zoneTitle.text = zone.DisplayName;
        if (bannerRoutine != null) StopCoroutine(bannerRoutine);
        bannerRoutine = StartCoroutine(Pulse(zoneBanner, bannerHold));
    }

    void OnZoneCleared(ZoneArena zone)
    {
        if (clearPanel == null) return;
        if (clearRoutine != null) StopCoroutine(clearRoutine);
        clearRoutine = StartCoroutine(Pulse(clearPanel, clearHold));
    }

    // 나타남 0.4초 → 유지 → 사라짐 0.6초
    static IEnumerator Pulse(CanvasGroup group, float hold)
    {
        for (float t = 0f; t < 0.4f; t += Time.unscaledDeltaTime)
        {
            group.alpha = t / 0.4f;
            yield return null;
        }
        group.alpha = 1f;
        yield return new WaitForSecondsRealtime(hold);
        for (float t = 0f; t < 0.6f; t += Time.unscaledDeltaTime)
        {
            group.alpha = 1f - t / 0.6f;
            yield return null;
        }
        group.alpha = 0f;
    }

    // 막대는 이미지 크기가 아니라 오른쪽 앵커를 옮겨서 채움 → 해상도가 바뀌어도 비율 유지
    static void SetFill(RectTransform fill, float ratio)
    {
        Vector2 max = fill.anchorMax;
        if (Mathf.Approximately(max.x, ratio)) return;
        max.x = ratio;
        fill.anchorMax = max;
    }

    static void Fade(CanvasGroup group, float target, float speed, float dt) => group.alpha = Mathf.MoveTowards(group.alpha, target, speed * dt);

    static void SetAlpha(CanvasGroup group, float alpha)
    {
        if (group != null) group.alpha = alpha;
    }
}
