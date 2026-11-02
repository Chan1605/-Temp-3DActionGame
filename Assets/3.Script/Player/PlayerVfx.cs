using System.Collections;
using DigitalRuby.ThunderAndLightning;
using UnityEngine;

// 전투 이벤트를 받아 이펙트만 담당. 전투·스킬 코드는 이 컴포넌트를 모름 (이벤트로만 연결)
public class PlayerVfx : MonoBehaviour
{
    // [변경] 스타일 클래스는 LightningFactory.cs의 LightningBoltStyle로 이동 (HitFeedback과 공용)

    [SerializeField] PlayerCombat combat;
    [SerializeField] PlayerSkill skill;
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
    [SerializeField] LightningBoltStyle bladeStyle = DefaultBladeStyle(); // [변경] 타입 이름만 변경 (저장된 값은 유지)

    [Header("Dash")]
    [SerializeField] float dashHeight = 1f;
    [SerializeField] LightningBoltStyle dashStyle = DefaultDashStyle(); // [변경] 타입 이름만 변경 (저장된 값은 유지)

    [Header("Strike")]
    [SerializeField] float strikeHeight = 12f;
    [SerializeField] float strikeSpread = 2f;
    [SerializeField] float strikeFlickerDelay = 0.07f;
    [SerializeField] int impactArcs = 4;
    [SerializeField] float impactRadius = 1.6f;
    [SerializeField] LightningBoltStyle strikeStyle = DefaultStrikeStyle(); // [변경] 타입 이름만 변경 (저장된 값은 유지)

    // Q 차지: 자세에서는 칼날이 약하게 튀고, 단계가 오를수록 빈도가 늘고 땅에서 칼로 번개가 끌려 올라옴
    [Header("Charge")]
    [SerializeField] float[] chargeBladeInterval = { 0.12f, 0.06f, 0.035f };
    [SerializeField] float[] chargeGroundInterval = { 0f, 0.18f, 0.08f };
    [SerializeField] float chargeGroundRadius = 1.3f;
    [SerializeField] int levelUpArcs = 5;
    [SerializeField] LightningBoltStyle chargeStyle = DefaultChargeStyle(); // [변경] 타입 이름만 변경 (저장된 값은 유지)

    LightningBoltPrefabScriptBase bladeBolt;
    LightningBoltPrefabScriptBase dashBolt;
    LightningBoltPrefabScriptBase strikeBolt;
    LightningBoltPrefabScriptBase chargeBolt;

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

        bladeBolt = CreateBolt("BladeLightning", bladeStyle);
        dashBolt = CreateBolt("DashLightning", dashStyle);
        strikeBolt = CreateBolt("StrikeLightning", strikeStyle);
        chargeBolt = CreateBolt("ChargeLightning", chargeStyle);
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
    void OnTargetCut(Vector3 position)
    {
        if (!strikeLightning || strikeBolt == null) return;

        Vector2 offset = Random.insideUnitCircle * strikeSpread;
        Vector3 sky = position + new Vector3(offset.x, strikeHeight, offset.y);
        Vector3 ground = position + Vector3.up * 0.1f;

        strikeBolt.Trigger(sky, ground);
        StartCoroutine(StrikeFollowUp(sky, ground));
    }

    IEnumerator StrikeFollowUp(Vector3 sky, Vector3 ground)
    {
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
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    // [변경] 생성 로직은 LightningFactory로 이동
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
