using System.Collections;
using DigitalRuby.ThunderAndLightning;
using UnityEngine;

// 전투 이벤트를 받아 이펙트만 담당. 전투·스킬 코드는 이 컴포넌트를 모름 (이벤트로만 연결)
public class PlayerVfx : MonoBehaviour
{
    // 스타일 클래스는 LightningFactory.cs의 LightningBoltStyle로 이동 (HitFeedback과 공용)

    [SerializeField] PlayerCombat combat;
    [SerializeField] PlayerSkill skill;
    [SerializeField] PlayerChargeAttack heavyCharge; // 우클릭 차지도 Q 차지와 같은 번개 연출
    [SerializeField] PlayerEnchant enchant; // 뇌검 각성: 칼에 계속 흐르는 번개 · 적중 낙뢰 · 방전
    [SerializeField] SwordTrail trail;
    [SerializeField] BoxCollider blade;
    [SerializeField] GameObject boltPrefab;

    [Header("Toggle")]
    [SerializeField] bool swordTrail = true;
    [SerializeField] bool bladeLightning = true;
    [SerializeField] bool dashLightning = true;
    [SerializeField] bool strikeLightning = true;
    [SerializeField] bool chargeLightning = true;

    [Header("Blade")]
    [SerializeField] float bladeInterval = 0.035f;
    [SerializeField] LightningBoltStyle bladeStyle = DefaultBladeStyle(); // 타입 이름만 변경 (저장된 값은 유지)

    [Header("Dash")]
    [SerializeField] float dashHeight = 1f;
    [SerializeField] LightningBoltStyle dashStyle = DefaultDashStyle(); // 타입 이름만 변경 (저장된 값은 유지)

    [Header("Strike")]
    [SerializeField] float strikeHeight = 12f;
    [SerializeField] float strikeSpread = 2f;
    [SerializeField] float strikeFlickerDelay = 0.07f;
    [SerializeField] float strikeStagger = 0.08f; // 한 번에 여러 명이 베이면 낙뢰를 이 간격으로 차례차례 (동시에 떨어져 화면이 하얗게 타는 것 방지)
    [SerializeField] int impactArcs = 4;
    [SerializeField] float impactRadius = 1.6f;
    [SerializeField] LightningBoltStyle strikeStyle = DefaultStrikeStyle(); // 타입 이름만 변경 (저장된 값은 유지)

    // Q 차지: 자세에서는 칼날이 약하게 튀고, 단계가 오를수록 빈도가 늘고 땅에서 칼로 번개가 끌려 올라옴
    [Header("Charge")]
    [SerializeField] float[] chargeBladeInterval = { 0.12f, 0.06f, 0.035f };
    [SerializeField] float[] chargeGroundInterval = { 0f, 0.18f, 0.08f };
    [SerializeField] float chargeGroundRadius = 1.3f;
    [SerializeField] int levelUpArcs = 5;
    [SerializeField] LightningBoltStyle chargeStyle = DefaultChargeStyle(); // 타입 이름만 변경 (저장된 값은 유지)

    // [변경] 발도 자세: 주변 공중·땅에서 칼로 번개가 끌려 모임. 자세 시간이 지날수록·표식이 늘수록 빽빽해짐
    [Header("Draw Stance")]
    [SerializeField] bool stanceGather = true;
    [SerializeField] float gatherRadius = 4f;
    [SerializeField] Vector2 gatherHeight = new Vector2(0.1f, 2.8f);
    [SerializeField] Vector2 gatherInterval = new Vector2(0.09f, 0.025f); // 처음 → 가득 찼을 때
    [SerializeField] int gatherBurst = 2;
    [SerializeField] int gatherEnterArcs = 10; // 자세에 들어가는 순간 사방에서 한꺼번에
    [SerializeField] LightningBoltStyle gatherStyle = DefaultGatherStyle();
    LightningBoltPrefabScriptBase gatherBolt;
    float gatherTimer;
    bool gathering;

    LightningBoltPrefabScriptBase bladeBolt;
    LightningBoltPrefabScriptBase dashBolt;
    LightningBoltPrefabScriptBase strikeBolt;
    LightningBoltPrefabScriptBase chargeBolt;

    // 뇌검 각성 연출 (기존 번개 스타일 재사용)
    [Header("Enchant")]
    [SerializeField] float enchantBladeInterval = 0.07f;
    [SerializeField] float enchantStrikeHeight = 5f;
    [SerializeField] int enchantStartArcs = 6;
    [SerializeField] int dischargeArcs = 10;
    bool enchanted;
    float enchantTimer;

    bool bladeActive;
    float bladeTimer;

    bool charging;
    int chargeLevel;
    float chargeBladeTimer;
    float chargeGroundTimer;

    void Awake()
    {
        if (combat == null) combat = GetComponent<PlayerCombat>();
        if (skill == null) skill = GetComponent<PlayerSkill>();
        if (heavyCharge == null) heavyCharge = GetComponent<PlayerChargeAttack>();
        if (enchant == null) enchant = GetComponent<PlayerEnchant>();

        bladeBolt = CreateBolt("BladeLightning", bladeStyle);
        dashBolt = CreateBolt("DashLightning", dashStyle);
        strikeBolt = CreateBolt("StrikeLightning", strikeStyle);
        strikeBoltSoft = CreateBolt("StrikeLightningSoft", SoftCopy(strikeStyle));
        chargeBolt = CreateBolt("ChargeLightning", chargeStyle);
        gatherBolt = CreateBolt("GatherLightning", gatherStyle); // [변경]
    }

    void OnEnable()
    {
        if (combat != null)
        {
            combat.AttackStarted += OnAttackStarted;
            combat.AttackSwingFinished += OnSwingFinished;
        }
        if (skill != null)
        {
            skill.Dashed += OnDashed;
            skill.TargetCut += OnTargetCut;
            skill.HoldStarted += OnHoldStarted;
            skill.ChargeLevelChanged += OnChargeLevelChanged;
            skill.HoldEnded += OnHoldEnded;
        }
        if (heavyCharge != null)
        {
            heavyCharge.ChargeStarted += OnHoldStarted;
            heavyCharge.ChargeLevelChanged += OnHeavyChargeLevel;
            heavyCharge.ChargeEnded += OnHoldEnded;
            heavyCharge.Dashed += OnDashed;       // 섬격도 일섬과 같은 번개 궤적 · 낙뢰
            heavyCharge.TargetCut += OnTargetCut;
        }
        if (enchant != null)
        {
            enchant.Started += OnEnchantStarted;
            enchant.Struck += OnEnchantStruck;
            enchant.Discharged += OnDischarged;
            enchant.Ended += OnEnchantEnded;
        }
    }

    void OnDisable()
    {
        if (combat != null)
        {
            combat.AttackStarted -= OnAttackStarted;
            combat.AttackSwingFinished -= OnSwingFinished;
        }
        if (skill != null)
        {
            skill.Dashed -= OnDashed;
            skill.TargetCut -= OnTargetCut;
            skill.HoldStarted -= OnHoldStarted;
            skill.ChargeLevelChanged -= OnChargeLevelChanged;
            skill.HoldEnded -= OnHoldEnded;
        }
        if (heavyCharge != null)
        {
            heavyCharge.ChargeStarted -= OnHoldStarted;
            heavyCharge.ChargeLevelChanged -= OnHeavyChargeLevel;
            heavyCharge.ChargeEnded -= OnHoldEnded;
            heavyCharge.Dashed -= OnDashed;
            heavyCharge.TargetCut -= OnTargetCut;
        }
        if (enchant != null)
        {
            enchant.Started -= OnEnchantStarted;
            enchant.Struck -= OnEnchantStruck;
            enchant.Discharged -= OnDischarged;
            enchant.Ended -= OnEnchantEnded;
        }
        enchanted = false;
        OnSwingFinished();
        OnHoldEnded();
    }

    void Update()
    {
        if (bladeActive && bladeBolt != null && blade != null)
        {
            bladeTimer -= Time.deltaTime;
            if (bladeTimer <= 0f)
            {
                bladeTimer = bladeInterval;
                GetBladeEnds(out Vector3 hilt, out Vector3 tip);
                bladeBolt.Trigger(hilt, tip);
            }
        }

        if (charging) UpdateCharge(Time.unscaledDeltaTime); // 차지 중 timeScale이 느려져도 일정한 빈도
        UpdateGather(Time.unscaledDeltaTime); // [변경]
        if (enchanted) UpdateEnchant(Time.deltaTime);
    }

    // ===== 뇌검 각성 =====
    void OnEnchantStarted()
    {
        enchanted = true;
        enchantTimer = 0f;
        if (chargeBolt == null || blade == null) return;
        // 발동: 칼에서 땅으로 번개가 사방으로 터짐
        GetBladeEnds(out Vector3 hilt, out Vector3 tip);
        Vector3 feet = transform.position + Vector3.up * 0.05f;
        for (int i = 0; i < enchantStartArcs; i++)
        {
            Vector2 d = Random.insideUnitCircle.normalized * Random.Range(1f, 2f);
            chargeBolt.Trigger(Vector3.Lerp(hilt, tip, Random.value), feet + new Vector3(d.x, 0f, d.y));
        }
    }

    void OnEnchantEnded() => enchanted = false;

    // 지속 중: 칼날을 따라 짧은 번개가 계속 흐름
    void UpdateEnchant(float dt)
    {
        if (bladeBolt == null || blade == null) return;
        enchantTimer -= dt;
        if (enchantTimer > 0f) return;
        enchantTimer = enchantBladeInterval;
        GetBladeEnds(out Vector3 hilt, out Vector3 tip);
        bladeBolt.Trigger(hilt, tip);
    }

    // 적중마다 작은 낙뢰 (일섬 낙뢰보다 낮고 한 번만)
    void OnEnchantStruck(Vector3 position)
    {
        if (strikeBolt == null) return;
        Vector2 o = Random.insideUnitCircle;
        Vector3 ground = position + Vector3.up * 0.1f;
        strikeBolt.Trigger(position + new Vector3(o.x, enchantStrikeHeight, o.y), ground);
        if (chargeBolt == null) return;
        for (int i = 0; i < 2; i++)
        {
            Vector2 d = Random.insideUnitCircle.normalized;
            chargeBolt.Trigger(ground, ground + new Vector3(d.x, 0.05f, d.y));
        }
    }

    // 종료 방전: 발밑에서 반경까지 번개가 둥글게 퍼지고 머리 위로 낙뢰
    void OnDischarged(Vector3 center, float radius)
    {
        Vector3 ground = center + Vector3.up * 0.1f;
        if (chargeBolt != null)
        {
            for (int i = 0; i < dischargeArcs; i++)
            {
                float a = (i + Random.value * 0.5f) / dischargeArcs * Mathf.PI * 2f;
                chargeBolt.Trigger(ground, ground + new Vector3(Mathf.Cos(a), 0.05f, Mathf.Sin(a)) * radius);
            }
        }
        OnTargetCut(center);
    }

    void OnAttackStarted(AttackData attack, AttackInput input)
    {
        if (swordTrail && trail != null) trail.SetEmitting(true);

        bladeActive = bladeLightning && input == AttackInput.Heavy;
        bladeTimer = 0f;
    }

    void OnSwingFinished()
    {
        if (trail != null) trail.SetEmitting(false);
        bladeActive = false;
    }

    void OnDashed(Vector3 start, Vector3 end)
    {
        if (!dashLightning || dashBolt == null) return;

        Vector3 up = Vector3.up * dashHeight;
        dashBolt.Trigger(start + up, end + up);
    }

    // 낙뢰: 하늘→적 본줄기 + 짧은 시간차 재점멸 + 착탄 지점에서 땅을 타고 퍼지는 잔번개
    int strikeFrame = -1;
    int strikeOrder;

    // 한 번에 여럿을 베면 앞의 몇 개만 빛·번짐을 세게, 나머지는 빛 없는 약한 번개 (화면이 하얗게 타는 것 방지)
    [SerializeField] int brightStrikes = 3;
    [SerializeField, Range(0f, 1f)] float softStrikeGlow = 0.35f;
    LightningBoltPrefabScriptBase strikeBoltSoft;

    LightningBoltStyle SoftCopy(LightningBoltStyle s)
    {
        var c = JsonUtility.FromJson<LightningBoltStyle>(JsonUtility.ToJson(s));
        c.lightRange = 0f;
        c.glowIntensity *= softStrikeGlow;
        c.intensity *= Mathf.Lerp(softStrikeGlow, 1f, 0.5f);
        return c;
    }

    void OnTargetCut(Vector3 position)
    {
        if (!strikeLightning || strikeBolt == null) return;

        // 같은 프레임에 들어온 낙뢰는 순서대로 지연
        if (Time.frameCount != strikeFrame)
        {
            strikeFrame = Time.frameCount;
            strikeOrder = 0;
        }
        float delay = strikeOrder++ * strikeStagger;

        Vector2 offset = Random.insideUnitCircle * strikeSpread;
        Vector3 sky = position + new Vector3(offset.x, strikeHeight, offset.y);
        Vector3 ground = position + Vector3.up * 0.1f;
        StartCoroutine(StrikeFollowUp(sky, ground, delay, strikeOrder > brightStrikes));
    }

    IEnumerator StrikeFollowUp(Vector3 sky, Vector3 ground, float delay, bool soft)
    {
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
        // 약한 낙뢰: 빛 없는 번개 한 번만 (재점멸 · 바닥 잔번개 생략)
        if (soft)
        {
            if (strikeBoltSoft != null) strikeBoltSoft.Trigger(sky, ground);
            yield break;
        }
        if (strikeBolt != null) strikeBolt.Trigger(sky, ground);

        yield return new WaitForSecondsRealtime(strikeFlickerDelay);
        if (strikeBolt != null) strikeBolt.Trigger(sky, ground);

        if (chargeBolt == null) yield break;
        for (int i = 0; i < impactArcs; i++)
        {
            Vector2 dir = Random.insideUnitCircle.normalized * Random.Range(impactRadius * 0.5f, impactRadius);
            chargeBolt.Trigger(ground, ground + new Vector3(dir.x, 0.05f, dir.y));
        }
    }

    // ===== Q 차지 =====

    void OnHoldStarted()
    {
        if (!chargeLightning) return;
        charging = true;
        chargeLevel = 0;
        chargeBladeTimer = 0f;
        chargeGroundTimer = 0f;
    }

    void OnChargeLevelChanged(int level)
    {
        if (!charging) return;
        chargeLevel = level;

        // 단계가 오르는 순간 발밑에서 칼로 한꺼번에 끌려 올라오는 번개
        if (chargeBolt == null || blade == null) return;
        GetBladeEnds(out Vector3 hilt, out Vector3 tip);
        for (int i = 0; i < levelUpArcs; i++) chargeBolt.Trigger(RandomGroundPoint(), Vector3.Lerp(hilt, tip, Random.value));
    }

    // 우클릭 차지는 1~3단계, Q 차지 연출은 0~2단계 기준이라 한 칸 내려서 전달
    void OnHeavyChargeLevel(int level) => OnChargeLevelChanged(level - 1);

    void OnHoldEnded()
    {
        charging = false;
        chargeLevel = 0;
    }

    void UpdateCharge(float dt)
    {
        if (blade == null) return;
        GetBladeEnds(out Vector3 hilt, out Vector3 tip);
        int level = Mathf.Clamp(chargeLevel, 0, chargeBladeInterval.Length - 1);

        chargeBladeTimer -= dt;
        if (chargeBladeTimer <= 0f && bladeBolt != null)
        {
            chargeBladeTimer = chargeBladeInterval[level];
            bladeBolt.Trigger(hilt, tip);
        }

        float groundInterval = level < chargeGroundInterval.Length ? chargeGroundInterval[level] : 0f;
        if (groundInterval <= 0f || chargeBolt == null) return;

        chargeGroundTimer -= dt;
        if (chargeGroundTimer <= 0f)
        {
            chargeGroundTimer = groundInterval * Random.Range(0.6f, 1.4f);
            chargeBolt.Trigger(RandomGroundPoint(), Vector3.Lerp(hilt, tip, Random.Range(0.3f, 1f)));
        }
    }

    // [변경] 핵심: 발도 자세(조준) 동안 바깥 링의 공중·땅 지점 → 칼로 자라 들어오는 번개 (growth로 안쪽으로 뻗는 게 보임)
    void UpdateGather(float dt)
    {
        bool active = stanceGather && skill != null && skill.InDrawStance && !skill.StanceAutoDraw;
        if (!active)
        {
            gathering = false;
            return;
        }
        if (gatherBolt == null || blade == null) return;

        if (!gathering)
        {
            gathering = true;
            gatherTimer = gatherInterval.x;
            for (int i = 0; i < gatherEnterArcs; i++) TriggerGather(i / (float)gatherEnterArcs);
            return;
        }

        float marks = skill.StanceMarkCount / (float)Mathf.Max(1, skill.StanceMaxMarks);
        float fill = Mathf.Clamp01((1f - skill.StanceRemaining01) * 0.6f + marks);
        gatherTimer -= dt;
        if (gatherTimer > 0f) return;
        gatherTimer = Mathf.Lerp(gatherInterval.x, gatherInterval.y, fill) * Random.Range(0.7f, 1.3f);
        for (int i = 0; i < gatherBurst; i++) TriggerGather(Random.value);
    }

    // angle01: 링 위 각도 (0~1), 약간 흔들어서 고르게 퍼지게
    void TriggerGather(float angle01)
    {
        GetBladeEnds(out Vector3 hilt, out Vector3 tip);
        float a = (angle01 + Random.Range(-0.05f, 0.05f)) * Mathf.PI * 2f;
        float r = gatherRadius * Random.Range(0.65f, 1f);
        Vector3 from = transform.position + new Vector3(Mathf.Cos(a) * r, Random.Range(gatherHeight.x, gatherHeight.y), Mathf.Sin(a) * r);
        gatherBolt.Trigger(from, Vector3.Lerp(hilt, tip, Random.Range(0.25f, 1f)));
    }

    Vector3 RandomGroundPoint()
    {
        Vector2 c = Random.insideUnitCircle.normalized * Random.Range(chargeGroundRadius * 0.6f, chargeGroundRadius);
        return transform.position + new Vector3(c.x, 0.05f, c.y);
    }

    // ===== 스타일 프리셋 =====
    // 돌진·낙뢰도 차지 번개 굵기에 맞춰 가늘게 → 굵기 대신 갈래(forkedness)와 조명으로 존재감을 냄

    static LightningBoltStyle DefaultBladeStyle() => new LightningBoltStyle
    {
        width = new Vector2(0.02f, 0.035f), duration = new Vector2(0.04f, 0.08f), generations = 4,
        chaos = 0.22f, forkedness = 0.25f, intensity = 2.5f, glowIntensity = 1.2f, glowWidth = 4f,
        lightRange = 2.5f, lightIntensity = 0.8f, lightPercent = 0.03f
    };

    static LightningBoltStyle DefaultDashStyle() => new LightningBoltStyle
    {
        width = new Vector2(0.035f, 0.06f), duration = new Vector2(0.15f, 0.25f), count = new Vector2Int(1, 2),
        generations = 6, chaos = 0.25f, forkedness = 0.4f, growth = 0.15f, intensity = 3f, glowIntensity = 1.6f,
        glowWidth = 5f, jitter = 0.3f, lightRange = 6f, lightIntensity = 1.5f, lightPercent = 0.02f
    };

    static LightningBoltStyle DefaultStrikeStyle() => new LightningBoltStyle
    {
        width = new Vector2(0.05f, 0.08f), duration = new Vector2(0.18f, 0.28f), count = new Vector2Int(1, 1),
        generations = 7, chaos = 0.22f, forkedness = 0.55f, growth = 0.35f, intensity = 3.5f, glowIntensity = 2f,
        glowWidth = 5f, jitter = 0.4f, lightRange = 12f, lightIntensity = 3f, lightPercent = 0.05f
    };

    static LightningBoltStyle DefaultChargeStyle() => new LightningBoltStyle
    {
        width = new Vector2(0.03f, 0.06f), duration = new Vector2(0.05f, 0.1f), generations = 5,
        chaos = 0.3f, forkedness = 0.3f, intensity = 2.5f, glowIntensity = 1.5f, glowWidth = 5f,
        lightRange = 3f, lightIntensity = 1f, lightPercent = 0.02f
    };

    // [변경] 모이는 번개: 시작점에서 칼 쪽으로 자라며(growth) 들어옴. 개수가 많아 조명은 끔
    static LightningBoltStyle DefaultGatherStyle() => new LightningBoltStyle
    {
        width = new Vector2(0.025f, 0.045f), duration = new Vector2(0.12f, 0.2f), generations = 5,
        chaos = 0.28f, forkedness = 0.35f, growth = 0.6f, intensity = 2.8f, glowIntensity = 1.6f, glowWidth = 5f,
        lightRange = 0f, lightIntensity = 0f, lightPercent = 0f
    };

    // 이미 씬·프리팹에 저장된 스타일 값은 코드 기본값으로 바뀌지 않으므로, 컴포넌트 메뉴에서 프리셋으로 되돌림
    [ContextMenu("Reset Bolt Styles To Default")]
    void ResetBoltStyles()
    {
#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(this, "Reset Bolt Styles");
#endif
        bladeStyle = DefaultBladeStyle();
        dashStyle = DefaultDashStyle();
        strikeStyle = DefaultStrikeStyle();
        chargeStyle = DefaultChargeStyle();
        gatherStyle = DefaultGatherStyle(); // [변경]
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    // 생성 로직은 LightningFactory로 이동
    LightningBoltPrefabScriptBase CreateBolt(string objectName, LightningBoltStyle style) =>
        LightningFactory.Create(boltPrefab, objectName, style);

    void GetBladeEnds(out Vector3 hilt, out Vector3 tip)
    {
        Vector3 size = blade.size;
        int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
        Vector3 half = Vector3.zero;
        half[axis] = size[axis] * 0.5f;

        Transform t = blade.transform;
        Vector3 a = t.TransformPoint(blade.center + half);
        Vector3 b = t.TransformPoint(blade.center - half);

        Vector3 hand = t.parent != null ? t.parent.position : t.position;
        bool aIsTip = (a - hand).sqrMagnitude >= (b - hand).sqrMagnitude;
        tip = aIsTip ? a : b;
        hilt = aIsTip ? b : a;
    }
}
