using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 1차 HUD: 체력·뇌기(좌하단), 일섬 쿨타임(우하단), 구역 배너(상단 중앙), 웨이브(우상단), 사망·돌파 메시지(중앙)
// 화면 요소는 Tools/Setup/HUD가 만들고 여기 연결함. 전투 코드는 HUD를 모르고, HUD가 값을 읽기만 함
// 연출을 DOTween으로: 피격 흔들림·잔상 막대, 뇌기 칸 채움 펀치, 일섬 준비 알림, 차지 단계 펀치, 배너·웨이브·메시지 등장/퇴장
public class GameHUD : MonoBehaviour
{
    [Header("Sources")]
    [SerializeField] PlayerHealth health;
    [SerializeField] PlayerSkill skill;
    [SerializeField] PlayerThunder thunderSource; // P4 뇌기
    [SerializeField] PlayerKanpa kanpaSource;     // 뇌락 입력 안내

    [Header("Health")]
    [SerializeField] RectTransform hpFill;
    [SerializeField] RectTransform hpTrail;
    [SerializeField] Text hpText;
    [SerializeField] float trailDelay = 0.4f;
    [SerializeField] float trailSpeed = 0.8f; // 잔상 막대가 줄어드는 시간(초)으로 사용
    [SerializeField, Range(0f, 1f)] float lowHealth = 0.3f; // 이하이면 체력 막대 심장박동

    [Header("Thunder")]
    [SerializeField] RectTransform[] thunderFills;
    [SerializeField, Range(0f, 4f)] float previewThunder;
    [SerializeField] bool showThunder; // P4 전까지 빈 칸이 고장처럼 보여 숨김. SetThunder가 처음 불리면 자동으로 켜짐

    [Header("Skill")]
    [SerializeField] Image skillCooldown;
    [SerializeField] Text skillLabel;
    [SerializeField] Color skillReadyColor = Color.white;
    [SerializeField] Color skillCoolColor = new Color(1f, 1f, 1f, 0.35f);

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

    static readonly Color FlashWhite = new Color(1f, 1f, 1f, 1f);
    static readonly Color Gold = new Color(0.95f, 0.8f, 0.45f);

    float thunder = -1f;
    float shownThunder;      // 화면에 보이는 뇌기 (트윈으로 실제 값을 따라감)
    int fullCells;
    float lastHealth = -1f;
    bool skillReady;
    bool skillWasActive;
    bool charging;
    int chargeLevel;
    int lastWave = -1;
    int lastAlive = -1;
    bool waveShown;
    bool deadShown;
    int shownHp = -1, shownHpMax = -1, shownWaveKey = int.MinValue; // 텍스트 캐시
    string deathSubDefault;
    Text followPrompt;      // 간파 직후 "우클릭 뇌락!" (런타임 생성 → HUD 셋업 재실행 불필요)
    CanvasGroup followGroup;
    Tween followLoop;
    Text markHint;            // 일섬 슬롯 위 "흐트러짐 · Q" (흐트러진 적이 있고 쓸 수 있을 때)
    CanvasGroup markHintGroup;
    CanvasGroup blockedGroup; // 뇌인 없이 Q를 눌렀을 때 화면 안내
    Sequence blockedSeq;
    bool markHintShown;

    Image hpImage;
    Color hpColor;
    Color chargeLabelColor = Color.white;
    Tween trailTween, thunderTween, heartbeat, chargeLoop;
    Sequence bannerSeq, clearSeq;
    Vector2 waveHome;
    RectTransform waveRect, bannerLineL, bannerLineR, bannerTitle, bannerNumber;
    float bannerLineWidth = 140f;
    Vector2 bannerNumberHome;
    Vector2 clearHome;
    bool clearHomeSet;
    Color[] thunderColors;

    void Awake()
    {
        if (health == null) health = FindAnyObjectByType<PlayerHealth>();
        if (skill == null) skill = FindAnyObjectByType<PlayerSkill>();
        if (thunderSource == null) thunderSource = FindAnyObjectByType<PlayerThunder>();
        if (thunderSource != null) showThunder = true;
        if (chargeSource == null) chargeSource = FindAnyObjectByType<PlayerChargeAttack>();
        if (kanpaSource == null) kanpaSource = FindAnyObjectByType<PlayerKanpa>();
        CreateFollowPrompt();
        CreateMarkGuide();
        CreateBossBar();
        CreateEnchantRing(); // 뇌검 각성(R) 링

        SetThunderVisible(showThunder);
        SetAlpha(zoneBanner, 0f);
        SetAlpha(wavePanel, 0f);
        SetAlpha(deathPanel, 0f);
        SetAlpha(clearPanel, 0f);
        SetAlpha(chargeGauge, 0f);

        // 셋업을 다시 돌리지 않아도 되게 배너·웨이브 하위 요소는 이름으로 찾음
        if (hpFill != null && (hpImage = hpFill.GetComponent<Image>()) != null) hpColor = hpImage.color;
        if (chargeLabel != null) chargeLabelColor = chargeLabel.color;
        if (wavePanel != null)
        {
            waveRect = (RectTransform)wavePanel.transform;
            waveHome = waveRect.anchoredPosition;
        }
        if (zoneBanner != null)
        {
            bannerLineL = zoneBanner.transform.Find("Line_L") as RectTransform;
            bannerLineR = zoneBanner.transform.Find("Line_R") as RectTransform;
            bannerTitle = zoneTitle != null ? zoneTitle.rectTransform : null;
            bannerNumber = zoneNumber != null ? zoneNumber.rectTransform : null;
            if (bannerLineL != null) bannerLineWidth = bannerLineL.sizeDelta.x;
            if (bannerNumber != null) bannerNumberHome = bannerNumber.anchoredPosition;
        }

        // 칸 번쩍임 뒤 돌아갈 원래 색
        if (thunderFills != null)
        {
            thunderColors = new Color[thunderFills.Length];
            for (int i = 0; i < thunderFills.Length; i++)
            {
                Image img = thunderFills[i] != null ? thunderFills[i].GetComponent<Image>() : null;
                thunderColors[i] = img != null ? img.color : Color.white;
            }
        }
    }

    // PlayerThunder.Awake에서 시작 뇌기가 정해진 뒤 맞춤 (Awake 순서 무관)
    void Start()
    {
        shownThunder = thunderSource != null ? thunderSource.Gauge : previewThunder;
        fullCells = Mathf.FloorToInt(shownThunder + 0.001f);
    }

    void OnEnable()
    {
        ZoneArena.AnyEntered += OnZoneEntered;
        ZoneArena.AnyCleared += OnZoneCleared;
        if (health != null) health.Damaged += OnDamaged;
        if (thunderSource != null) thunderSource.GaugeChanged += OnGaugeChanged;
        if (thunderSource != null) thunderSource.MaxGaugeChanged += OnMaxGaugeChanged; // [변경]
        if (kanpaSource != null)
        {
            kanpaSource.FollowReady += ShowFollowPrompt;
            kanpaSource.FollowClosed += HideFollowPrompt;
        }
        if (skill != null) skill.Blocked += ShowBlocked;
    }

    void OnDisable()
    {
        ZoneArena.AnyEntered -= OnZoneEntered;
        ZoneArena.AnyCleared -= OnZoneCleared;
        if (health != null) health.Damaged -= OnDamaged;
        if (thunderSource != null) thunderSource.GaugeChanged -= OnGaugeChanged;
        if (thunderSource != null) thunderSource.MaxGaugeChanged -= OnMaxGaugeChanged; // [변경]
        if (kanpaSource != null)
        {
            kanpaSource.FollowReady -= ShowFollowPrompt;
            kanpaSource.FollowClosed -= HideFollowPrompt;
        }
        if (skill != null) skill.Blocked -= ShowBlocked;
    }

    // ===== 뇌락 안내 =====
    void CreateFollowPrompt()
    {
        var go = new GameObject("FollowPrompt", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, -120f);
        rt.sizeDelta = new Vector2(520f, 64f);

        followGroup = go.AddComponent<CanvasGroup>();
        followGroup.alpha = 0f;
        followGroup.blocksRaycasts = false;

        followPrompt = go.AddComponent<Text>();
        followPrompt.font = hpText != null ? hpText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        followPrompt.fontSize = 36;
        followPrompt.fontStyle = FontStyle.Bold;
        followPrompt.alignment = TextAnchor.MiddleCenter;
        followPrompt.horizontalOverflow = HorizontalWrapMode.Overflow;
        followPrompt.raycastTarget = false;
        followPrompt.color = Gold;
        followPrompt.text = "우클릭  뇌락!";
        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(2f, -2f);
    }

    void ShowFollowPrompt(float duration)
    {
        if (followGroup == null) return;
        Transform t = followGroup.transform;
        followLoop?.Kill();
        t.DOKill();
        UITween.Fade(followGroup, 1f, 0.08f);
        t.localScale = Vector3.one * 0.6f;
        t.DOScale(1f, 0.18f).SetEase(Ease.OutBack).SetUpdate(true).SetLink(t.gameObject)
            .OnComplete(() => followLoop = t.DOScale(1.12f, 0.12f).SetLoops(-1, LoopType.Yoyo).SetUpdate(true).SetLink(t.gameObject));
    }

    void HideFollowPrompt(bool used)
    {
        if (followGroup == null) return;
        Transform t = followGroup.transform;
        followLoop?.Kill();
        followLoop = null;
        t.DOKill();
        if (used) t.DOScale(1.5f, 0.2f).SetEase(Ease.OutQuad).SetUpdate(true).SetLink(t.gameObject);
        UITween.Fade(followGroup, 0f, used ? 0.2f : 0.25f);
    }

    public void SetThunder(float cells)
    {
        thunder = Mathf.Clamp(cells, 0f, thunderFills != null ? thunderFills.Length : 4);
        if (!showThunder) SetThunderVisible(showThunder = true);
        OnGaugeChanged(thunder);
    }

    void SetThunderVisible(bool visible)
    {
        if (thunderFills == null) return;
        foreach (RectTransform fill in thunderFills)
            if (fill != null && fill.parent != null) fill.parent.gameObject.SetActive(visible);
    }

    void Update()
    {
        UpdateHealth();
        UpdateThunderBars();
        UpdateSkill();
        UpdateCharge();
        UpdateWave();
        UpdateDeath();
    }

    // ===== 체력 =====
    void UpdateHealth()
    {
        if (health == null || hpFill == null) return;

        float ratio = health.MaxHealth > 0f ? Mathf.Clamp01(health.Health / health.MaxHealth) : 0f;
        SetFill(hpFill, ratio);
        // 성능: 숫자가 바뀔 때만 문자열을 새로 만듦 (매 프레임 문자열 할당 → GC 끊김 방지)
        int hp = Mathf.CeilToInt(health.Health), hpMax = Mathf.CeilToInt(health.MaxHealth);
        if (hpText != null && (hp != shownHp || hpMax != shownHpMax))
        {
            shownHp = hp;
            shownHpMax = hpMax;
            hpText.text = $"{hp} / {hpMax}";
        }

        // 회복(부활 등)은 잔상 없이 바로 올림
        if (lastHealth >= 0f && ratio > lastHealth + 0.0001f && hpTrail != null)
        {
            trailTween?.Kill();
            SetFill(hpTrail, ratio);
            UITween.Punch(hpFill.parent, 0.06f, 0.35f);
        }
        lastHealth = ratio;

        // 체력이 낮으면 막대가 붉게 두근거림
        bool low = ratio > 0f && ratio <= lowHealth;
        if (low && heartbeat == null && hpImage != null)
        {
            heartbeat = DOTween.Sequence()
                .Append(DOTween.To(() => hpImage.color, c => hpImage.color = c, new Color(1f, 0.55f, 0.45f), 0.18f))
                .Append(DOTween.To(() => hpImage.color, c => hpImage.color = c, hpColor, 0.5f))
                .Join(hpFill.parent.DOScale(1.03f, 0.12f).SetLoops(2, LoopType.Yoyo))
                .AppendInterval(0.25f)
                .SetLoops(-1).SetUpdate(true).SetLink(gameObject);
        }
        else if (!low && heartbeat != null)
        {
            heartbeat.Kill();
            heartbeat = null;
            hpImage.color = hpColor;
            hpFill.parent.localScale = Vector3.one;
        }
    }

    // 핵심: 맞으면 막대 흔들림 + 흰 번쩍임, 잔상 막대는 잠깐 멈췄다가 따라 내려감
    void OnDamaged(DamageInfo info)
    {
        if (hpFill == null) return;
        UITween.Shake(hpFill.parent, 7f, 0.25f);
        if (hpImage != null && heartbeat == null) UITween.Flash(hpImage, FlashWhite, hpColor, 0.25f);
        if (hpText != null) UITween.Punch(hpText.transform, 0.2f, 0.25f);

        if (hpTrail == null || health == null) return;
        float ratio = health.MaxHealth > 0f ? Mathf.Clamp01(health.Health / health.MaxHealth) : 0f;
        trailTween?.Kill();
        trailTween = UITween.Fill(hpTrail, ratio, Mathf.Max(0.1f, trailSpeed * 0.6f), Ease.InOutQuad).SetDelay(trailDelay);
    }

    // ===== 뇌기 =====
    // [변경] 뇌기 칸 수가 늘면(수련 · 뇌기 확장) 마지막 칸을 복제해 붙이고, 전체 폭은 체력바와 같게 다시 나눔
    void OnMaxGaugeChanged(float max)
    {
        if (thunderFills == null || thunderFills.Length == 0 || thunderFills[0] == null) return;
        int want = Mathf.Max(1, Mathf.CeilToInt(max - 0.001f));
        if (want <= thunderFills.Length) return;

        var first = (RectTransform)thunderFills[0].parent;
        var last = (RectTransform)thunderFills[thunderFills.Length - 1].parent;
        float total = last.anchoredPosition.x + last.sizeDelta.x - first.anchoredPosition.x;
        float gap = thunderFills.Length > 1
            ? ((RectTransform)thunderFills[1].parent).anchoredPosition.x - first.anchoredPosition.x - first.sizeDelta.x
            : 8f;

        var fills = new List<RectTransform>(thunderFills);
        var colors = new List<Color>(thunderColors);
        while (fills.Count < want)
        {
            var cell = Instantiate(last, last.parent);
            cell.name = $"Thunder_{fills.Count + 1}";
            cell.localScale = Vector3.one;
            fills.Add((RectTransform)cell.Find(thunderFills[0].name));
            colors.Add(colors[colors.Count - 1]);
        }

        float width = (total - gap * (want - 1)) / want;
        for (int i = 0; i < want; i++)
        {
            var cell = (RectTransform)fills[i].parent;
            cell.anchoredPosition = new Vector2(first.anchoredPosition.x + i * (width + gap), first.anchoredPosition.y);
            cell.sizeDelta = new Vector2(width, cell.sizeDelta.y);
        }
        thunderFills = fills.ToArray();
        thunderColors = colors.ToArray();
    }

    void OnGaugeChanged(float value)
    {
        thunderTween?.Kill();
        thunderTween = DOTween.To(() => shownThunder, v => shownThunder = v, value, 0.25f)
            .SetEase(Ease.OutCubic).SetUpdate(true).SetLink(gameObject);

        // 칸이 새로 꽉 차면 그 칸 펀치 + 번쩍, 소모로 줄면 남은 칸들이 살짝 움찔
        int cells = Mathf.FloorToInt(value + 0.001f);
        if (thunderFills != null)
        {
            if (cells > fullCells)
            {
                for (int i = fullCells; i < cells && i < thunderFills.Length; i++)
                    PulseCell(i, i - fullCells);
            }
            else if (cells < fullCells)
            {
                for (int i = cells; i < fullCells && i < thunderFills.Length; i++)
                    if (thunderFills[i] != null) UITween.Shake(thunderFills[i].parent, 4f, 0.2f);
            }
        }
        fullCells = cells;
    }

    void PulseCell(int index, int order)
    {
        RectTransform fill = thunderFills[index];
        if (fill == null) return;
        Transform cell = fill.parent;
        var image = fill.GetComponent<Image>();
        float delay = order * 0.06f;

        cell.DOKill(true);
        cell.localScale = Vector3.one;
        cell.DOPunchScale(new Vector3(0.25f, 0.6f, 0f), 0.35f, 6, 0.5f).SetDelay(delay).SetUpdate(true).SetLink(cell.gameObject);
        if (image != null)
        {
            Color baseColor = thunderColors[index];
            DOTween.Kill(image);
            DOTween.Sequence()
                .AppendInterval(delay)
                .AppendCallback(() => image.color = FlashWhite)
                .Append(DOTween.To(() => image.color, c => image.color = c, baseColor, 0.3f))
                .SetUpdate(true).SetTarget(image).SetLink(image.gameObject);
        }
    }

    void UpdateThunderBars()
    {
        if (thunderFills == null) return;
        float value = thunderSource != null || thunder >= 0f ? shownThunder : previewThunder;
        for (int i = 0; i < thunderFills.Length; i++)
            if (thunderFills[i] != null) SetFill(thunderFills[i], Mathf.Clamp01(value - i));
    }

    // ===== 일섬 =====
    void UpdateSkill()
    {
        if (skill == null || skillCooldown == null) return;

        float ratio = skill.CooldownRatio;
        if (thunderSource != null && thunderSource.UseGaugeForSkill)
            ratio = 1f - Mathf.Clamp01(thunderSource.Gauge / thunderSource.SkillCost(0)); // 각성 중 칸 표시는 남은 시간이라 실제 뇌기로 판단
        skillCooldown.fillAmount = ratio;

        Transform slot = skillCooldown.transform.parent;
        bool ready = ratio <= 0f; // 뇌기만 차면 준비 (뇌인 조건 삭제)
        SetMarkHint(ready && skill.HasStaggerTarget && !skill.IsActive); // 지금 Q로 끝낼 적이 있음
        if (ready && !skillReady)
        {
            // 쓸 수 있게 된 순간: 슬롯 튀어오름 + 글자 금색 번쩍
            UITween.Punch(slot, 0.22f, 0.4f);
            if (skillLabel != null) UITween.Flash(skillLabel, Gold, skillReadyColor, 0.5f);
        }
        else if (!ready && skillReady && skillLabel != null)
        {
            DOTween.Kill(skillLabel);
            skillLabel.color = skillCoolColor;
        }
        skillReady = ready;
        if (!ready && skillLabel != null && !DOTween.IsTweening(skillLabel)) skillLabel.color = skillCoolColor;

        // 일섬을 누른 순간 슬롯이 눌림
        bool active = skill.IsActive;
        if (active && !skillWasActive)
        {
            slot.DOKill(true);
            slot.localScale = Vector3.one;
            slot.DOScale(0.85f, 0.06f).SetLoops(2, LoopType.Yoyo).SetUpdate(true).SetLink(slot.gameObject);
        }
        skillWasActive = active;
    }

    // 뇌검 각성 링: Q 슬롯 바로 위에 같은 크기로 (각성 중 남은 시간이 줄어드는 링, 평소엔 R 준비 표시) // 왼쪽은 키 안내 글자와 겹쳐 위로
    void CreateEnchantRing()
    {
        if (skillCooldown == null || GetComponentInChildren<EnchantRing>(true) != null) return;
        var enchant = FindAnyObjectByType<PlayerEnchant>();
        if (enchant == null) return;
        var slot = (RectTransform)skillCooldown.transform.parent;
        var go = new GameObject("EnchantRing", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(slot.parent, false);
        rt.anchorMin = slot.anchorMin;
        rt.anchorMax = slot.anchorMax;
        rt.pivot = slot.pivot;
        float size = Mathf.Max(slot.rect.width, slot.rect.height);
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = slot.anchoredPosition + new Vector2(0f, slot.rect.height + 16f);
        go.AddComponent<EnchantRing>().Build(hpText != null ? hpText.font : null, enchant, thunderSource);
    }

    // 보스 체력 막대: 화면 전체를 덮는 자식으로 만들고 BossHealthBar가 아래 가운데에 그림
    void CreateBossBar()
    {
        if (GetComponentInChildren<BossHealthBar>(true) != null) return;
        var go = new GameObject("BossHealthBar", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        go.AddComponent<BossHealthBar>().Build(hpText != null ? hpText.font : null);
    }

    // ===== 뇌인 안내 =====
    // 슬롯 위 작은 글씨(상시) + Q를 눌렀는데 막혔을 때 화면 중앙 아래 안내(잠깐)
    void CreateMarkGuide()
    {
        if (skillCooldown != null)
        {
            var slot = (RectTransform)skillCooldown.transform.parent;
            markHint = NewGuideText("MarkHint", slot, 22, new Color(1f, 0.92f, 0.55f), "흐트러짐 · Q");
            var rt = markHint.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, 22f);
            markHintGroup = markHint.gameObject.AddComponent<CanvasGroup>();
            markHintGroup.alpha = 0f;
        }

        Text blocked = NewGuideText("MarkBlocked", transform, 30, Gold, "뇌기가 부족합니다\n<size=22>간파 · 저스트 회피 · 처형으로 뇌기를 모은 뒤 Q</size>");
        blocked.rectTransform.anchorMin = blocked.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        blocked.rectTransform.anchoredPosition = new Vector2(0f, -190f);
        blocked.rectTransform.sizeDelta = new Vector2(620f, 90f);
        blockedGroup = blocked.gameObject.AddComponent<CanvasGroup>();
        blockedGroup.alpha = 0f;
        blockedGroup.blocksRaycasts = false;
    }

    Text NewGuideText(string name, Transform parent, int size, Color color, string content)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.sizeDelta = new Vector2(260f, size * 1.6f);
        var text = go.AddComponent<Text>();
        text.font = hpText != null ? hpText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.supportRichText = true;
        text.raycastTarget = false;
        text.color = color;
        text.text = content;
        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(2f, -2f);
        return text;
    }

    void SetMarkHint(bool show)
    {
        if (markHintGroup == null || show == markHintShown) return;
        markHintShown = show;
        if (show && markHint != null) markHint.transform.DOPunchScale(Vector3.one * 0.3f, 0.3f, 6).SetUpdate(true).SetLink(markHint.gameObject); // 뜨는 순간 튐
        DOTween.Kill(markHintGroup);
        DOTween.To(() => markHintGroup.alpha, a => markHintGroup.alpha = a, show ? 1f : 0f, 0.2f)
            .SetTarget(markHintGroup).SetUpdate(true).SetLink(markHintGroup.gameObject);
    }

    void ShowBlocked()
    {
        if (skillCooldown != null)
        {
            Transform slot = skillCooldown.transform.parent;
            slot.DOKill(true);
            slot.DOShakePosition(0.3f, new Vector3(8f, 0f, 0f), 30, 0f).SetUpdate(true).SetLink(slot.gameObject);
        }
        if (markHint != null) markHint.transform.DOPunchScale(Vector3.one * 0.35f, 0.3f, 6).SetUpdate(true).SetLink(markHint.gameObject);
        if (blockedGroup == null) return;

        // 핵심: 연타해도 처음부터 다시 (떠 있는 시간 연장)
        blockedSeq?.Kill();
        Transform t = blockedGroup.transform;
        t.localScale = Vector3.one * 0.85f;
        blockedSeq = DOTween.Sequence().SetUpdate(true).SetLink(blockedGroup.gameObject)
            .Append(DOTween.To(() => blockedGroup.alpha, a => blockedGroup.alpha = a, 1f, 0.1f))
            .Join(t.DOScale(1f, 0.18f).SetEase(Ease.OutBack))
            .AppendInterval(1.2f)
            .Append(DOTween.To(() => blockedGroup.alpha, a => blockedGroup.alpha = a, 0f, 0.35f));
    }

    // ===== 차지 =====
    void UpdateCharge()
    {
        if (chargeSource == null || chargeGauge == null) return;

        bool now = chargeSource.IsCharging;
        Transform gauge = chargeGauge.transform;
        if (now && !charging)
        {
            // 아래에서 살짝 커지며 등장
            UITween.Fade(chargeGauge, 1f, 0.12f);
            gauge.DOKill(true);
            gauge.localScale = Vector3.one * 0.8f;
            gauge.DOScale(1f, 0.25f).SetEase(Ease.OutBack).SetUpdate(true).SetLink(gauge.gameObject);
            chargeLevel = 0;
            if (chargeLabel != null) chargeLabel.text = "";
        }
        else if (!now && charging)
        {
            UITween.Fade(chargeGauge, 0f, 0.25f);
            gauge.DOScale(1.15f, 0.25f).SetEase(Ease.OutQuad).SetUpdate(true).SetLink(gauge.gameObject);
            chargeLoop?.Kill();
            chargeLoop = null;
        }
        charging = now;
        if (!now) return;

        int level = chargeSource.Level;
        Color color = chargeLevelColors.Length > 0 ? chargeLevelColors[Mathf.Clamp(level - 1, 0, chargeLevelColors.Length - 1)] : Color.white;
        if (chargeFill != null)
        {
            SetFill(chargeFill, chargeSource.ChargeRatio);
            var image = chargeFill.GetComponent<Image>();
            if (image != null) image.color = color;
        }

        if (level != chargeLevel)
        {
            if (chargeLabel != null && level >= chargeSource.MaxLevel) // "차지 1·2" 글자 없이 풀차지만 표시
            {
                chargeLabel.text = "풀차지";
                UITween.Punch(chargeLabel.transform, 0.35f, 0.3f);
                UITween.Flash(chargeLabel, color, chargeLabelColor, 0.35f);
            }
            if (level > 1) UITween.Punch(gauge, 0.12f, 0.25f);

            // 풀차지 동안 게이지 전체가 맥박처럼 뜀
            if (level >= chargeSource.MaxLevel && chargeLoop == null && chargeLabel != null)
                chargeLoop = chargeLabel.transform.DOScale(1.12f, 0.22f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine)
                    .SetUpdate(true).SetLink(chargeLabel.gameObject);
            chargeLevel = level;
        }
    }

    // ===== 웨이브 =====
    void UpdateWave()
    {
        if (wavePanel == null) return;

        ZoneArena zone = ZoneArena.Fighting;
        bool show = zone != null;
        if (show != waveShown)
        {
            // 오른쪽에서 밀려 들어오고, 끝나면 오른쪽으로 빠짐
            waveShown = show;
            UITween.Fade(wavePanel, show ? 1f : 0f, show ? 0.25f : 0.4f);
            if (waveRect != null)
            {
                DOTween.Kill(waveRect);
                if (show) waveRect.anchoredPosition = waveHome + new Vector2(120f, 0f);
                UITween.AnchorPos(waveRect, show ? waveHome : waveHome + new Vector2(120f, 0f), show ? 0.35f : 0.4f, show ? Ease.OutBack : Ease.InCubic);
            }
            lastWave = -1;
            lastAlive = -1;
            shownWaveKey = int.MinValue;
        }
        if (!show) return;

        int wave = zone.CurrentWave;
        int alive = zone.AliveCount;
        // 성능: 웨이브·남은 적·최고 기록이 바뀔 때만 문자열 갱신
        int key = (wave * 1000 + alive) * 1000 + ZoneArena.TrialBest;
        bool changed = key != shownWaveKey;
        shownWaveKey = key;
        if (waveText != null)
        {
            // 시련은 "시련 N" (끝 없음)
            if (changed)
                waveText.text = zone.Endless ? $"시련 {Mathf.Max(1, wave)}"
                    : wave > 0 && zone.WaveCount > 0 ? $"웨이브 {wave} / {zone.WaveCount}" : "전투";
            if (wave != lastWave && lastWave >= 0)
            {
                UITween.Punch(waveText.transform, 0.3f, 0.4f);
                UITween.Flash(waveText, Gold, Color.white, 0.6f);
            }
        }
        if (enemyText != null)
        {
            if (changed) enemyText.text = zone.Endless ? $"남은 적 {alive}   ·   최고 {ZoneArena.TrialBest}" : $"남은 적 {alive}";
            if (alive < lastAlive) UITween.Punch(enemyText.transform, 0.18f, 0.2f);
        }
        lastWave = wave;
        lastAlive = alive;
    }

    // ===== 구역 배너: 선이 양쪽으로 뻗고 → 번호 내려옴 → 제목이 크게 들어와 자리 잡음 → 위로 사라짐 =====
    void OnZoneEntered(ZoneArena zone)
    {
        if (zoneBanner == null) return;
        if (zoneNumber != null) zoneNumber.text = zone.DisplayNumber;
        if (zoneTitle != null) zoneTitle.text = zone.DisplayName;

        bannerSeq?.Kill();
        var rect = (RectTransform)zoneBanner.transform;
        zoneBanner.alpha = 0f;
        rect.localScale = Vector3.one;
        if (bannerLineL != null) bannerLineL.sizeDelta = new Vector2(0f, bannerLineL.sizeDelta.y);
        if (bannerLineR != null) bannerLineR.sizeDelta = new Vector2(0f, bannerLineR.sizeDelta.y);
        if (bannerTitle != null) bannerTitle.localScale = Vector3.one * 1.35f;
        if (zoneTitle != null) zoneTitle.color = new Color(zoneTitle.color.r, zoneTitle.color.g, zoneTitle.color.b, 0f);

        bannerSeq = DOTween.Sequence().SetUpdate(true).SetLink(gameObject)
            .Append(DOTween.To(() => zoneBanner.alpha, a => zoneBanner.alpha = a, 1f, 0.2f));
        if (bannerLineL != null) bannerSeq.Join(UITween.Width(bannerLineL, bannerLineWidth, 0.45f, Ease.OutCubic, false));
        if (bannerLineR != null) bannerSeq.Join(UITween.Width(bannerLineR, bannerLineWidth, 0.45f, Ease.OutCubic, false));
        if (bannerNumber != null)
        {
            Vector2 home = bannerNumberHome;
            bannerNumber.anchoredPosition = home + new Vector2(0f, 24f);
            bannerSeq.Join(UITween.AnchorPos(bannerNumber, home, 0.4f, Ease.OutCubic, false));
        }
        if (bannerTitle != null)
        {
            bannerSeq.Insert(0.15f, bannerTitle.DOScale(1f, 0.45f).SetEase(Ease.OutBack));
            bannerSeq.Insert(0.15f, UITween.Color(zoneTitle, new Color(zoneTitle.color.r, zoneTitle.color.g, zoneTitle.color.b, 1f), 0.3f, false));
        }
        bannerSeq.AppendInterval(bannerHold)
            .Append(DOTween.To(() => zoneBanner.alpha, a => zoneBanner.alpha = a, 0f, 0.6f))
            .Join(rect.DOScale(0.95f, 0.6f));
    }

    // ===== 구역 돌파: 작게 튀어나와 커지고, 잠시 머문 뒤 위로 흩어짐 =====
    void OnZoneCleared(ZoneArena zone)
    {
        if (clearPanel == null) return;
        clearSeq?.Kill();

        var rect = (RectTransform)clearPanel.transform;
        if (!clearHomeSet)
        {
            clearHome = rect.anchoredPosition;
            clearHomeSet = true;
        }
        Vector2 home = clearHome;
        rect.anchoredPosition = home;
        clearPanel.alpha = 0f;
        rect.localScale = Vector3.one * 0.6f;

        clearSeq = DOTween.Sequence().SetUpdate(true).SetLink(gameObject)
            .Append(DOTween.To(() => clearPanel.alpha, a => clearPanel.alpha = a, 1f, 0.15f))
            .Join(rect.DOScale(1f, 0.45f).SetEase(Ease.OutBack, 2.2f))
            .AppendInterval(clearHold)
            .Append(DOTween.To(() => clearPanel.alpha, a => clearPanel.alpha = a, 0f, 0.5f))
            .Join(UITween.AnchorPos(rect, home + new Vector2(0f, 40f), 0.5f, Ease.InQuad, false))
            .AppendCallback(() => rect.anchoredPosition = home);
    }

    // ===== 사망: 천천히 어두워지며 글자가 크게서 제자리로 =====
    void UpdateDeath()
    {
        if (deathPanel == null || health == null) return;
        bool dead = health.IsDead;
        if (dead == deadShown) return;
        deadShown = dead;

        UITween.Fade(deathPanel, dead ? 1f : 0f, dead ? 0.6f : 0.4f);
        Transform title = deathPanel.transform.Find("Title");

        // 시련 중 사망이면 아래 문구를 기록으로 바꿈 (그 외엔 원래 문구)
        var sub = deathPanel.transform.Find("Sub") != null ? deathPanel.transform.Find("Sub").GetComponent<Text>() : null;
        if (dead && sub != null)
        {
            if (deathSubDefault == null) deathSubDefault = sub.text;
            ZoneArena zone = ZoneArena.Fighting;
            sub.text = zone != null && zone.Endless
                ? $"시련 {Mathf.Max(0, zone.CurrentWave - 1)}웨이브 돌파 · 최고 {ZoneArena.TrialBest}"
                : deathSubDefault;
        }
        if (dead && title != null)
        {
            title.DOKill(true);
            title.localScale = Vector3.one * 1.3f;
            title.DOScale(1f, 1.2f).SetEase(Ease.OutQuart).SetUpdate(true).SetLink(title.gameObject);
        }
    }

    // 막대는 이미지 크기가 아니라 오른쪽 앵커를 옮겨서 채움 → 해상도가 바뀌어도 비율 유지
    static void SetFill(RectTransform fill, float ratio)
    {
        Vector2 max = fill.anchorMax;
        if (Mathf.Approximately(max.x, ratio)) return;
        max.x = ratio;
        fill.anchorMax = max;
    }

    static void SetAlpha(CanvasGroup group, float alpha)
    {
        if (group != null) group.alpha = alpha;
    }
}
