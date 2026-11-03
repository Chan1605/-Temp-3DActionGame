using System.Collections.Generic;
using DigitalRuby.ThunderAndLightning;
using UnityEngine;

// P3 무게별 피드백: 피격 이벤트만 구독해서 히트스톱 · 카메라 흔들림 · 피격 이펙트를 낸다 (전투 로직과 분리)
public class HitFeedback : MonoBehaviour
{
    // 피격 이펙트 방식: 번개 스파크 / 팩 프리팹 / 둘 다 (A/B 비교용)
    public enum EffectMode { LightningSparks, PrefabEffect, Both }

    [System.Serializable]
    public class WeightSetting
    {
        public AttackWeight weight;
        public float hitStop;
        public float shake;
        public float shakeTime = 0.1f;
        public GameObject effect;
        public float effectScale = 1f;

        // 스파크: 맞은 지점에서 튀는 짧은 번개 수 · 길이, 강공격은 베는 방향으로 긴 한 줄 추가
        public int sparkCount = 3;
        public float sparkRadius = 0.45f;
        public float streakLength;
    }

    [SerializeField] WeaponHitbox weapon;
    [SerializeField] PlayerParry parry;
    [SerializeField] PlayerHealth health;
    [SerializeField] PlayerGuard guard; // [변경]
    [SerializeField] PlayerSkill skill;
    [SerializeField] ThirdPersonCamera followCamera;

    [SerializeField] bool enableHitStop = true;
    [SerializeField] bool enableEffects = true;
    [SerializeField] EffectMode effectMode = EffectMode.LightningSparks;
    [SerializeField] float hitStopScale = 0.02f;
    [SerializeField] float effectLifetime = 3f;

    [SerializeField] List<WeightSetting> settings = new List<WeightSetting>
    {
        new WeightSetting { weight = AttackWeight.Light, hitStop = 0.04f, shake = 0.02f, effectScale = 0.7f, sparkCount = 3, sparkRadius = 0.45f }, // 스파크 값 추가
        new WeightSetting { weight = AttackWeight.Heavy, hitStop = 0.08f, shake = 0.08f, effectScale = 0.35f, sparkCount = 6, sparkRadius = 0.8f, streakLength = 1.4f },
        new WeightSetting { weight = AttackWeight.Parry, hitStop = 0f, shake = 0f, effectScale = 0.4f, sparkCount = 8, sparkRadius = 1f },
    };

    [Header("Player Hit")]
    [SerializeField] GameObject playerHitEffect;
    [SerializeField] float playerHitScale = 1f;
    [SerializeField] float playerHitStop = 0.05f;
    [SerializeField] int playerSparkCount = 4;
    [SerializeField] float playerSparkRadius = 0.6f;

    // [변경] 가드로 막았을 때: 칼끼리 부딪힌 짧은 스파크
    [Header("Block")]
    [SerializeField] int blockSparkCount = 4;
    [SerializeField] float blockSparkRadius = 0.5f;
    [SerializeField] float blockHitStop = 0.03f;

    // ===== 번개 스파크 =====
    [Header("Lightning Sparks")]
    [SerializeField] GameObject boltPrefab;
    [SerializeField] float sparkSpread = 0.9f;
    [SerializeField] LightningBoltStyle sparkStyle = DefaultSparkStyle();
    [SerializeField] LightningBoltStyle streakStyle = DefaultStreakStyle();
    [SerializeField] LightningBoltStyle playerSparkStyle = DefaultPlayerSparkStyle();

    LightningBoltPrefabScriptBase sparkBolt;
    LightningBoltPrefabScriptBase streakBolt;
    LightningBoltPrefabScriptBase playerSparkBolt;

    float hitStopEnd;
    bool hitStopping;
    float savedTimeScale = 1f;

    bool UseSparks => effectMode != EffectMode.PrefabEffect;
    bool UsePrefab => effectMode != EffectMode.LightningSparks;

    void Awake()
    {
        if (weapon == null) weapon = GetComponentInChildren<WeaponHitbox>();
        if (parry == null) parry = GetComponent<PlayerParry>();
        if (health == null) health = GetComponent<PlayerHealth>();
        if (guard == null) guard = GetComponent<PlayerGuard>(); // [변경]
        if (skill == null) skill = GetComponent<PlayerSkill>();
        if (followCamera == null) followCamera = FindAnyObjectByType<ThirdPersonCamera>();

        // 번개 오브젝트는 종류별로 하나씩 만들어 두고 Trigger만 반복 (UseGameTime=false라 히트스톱 중에도 정상 속도로 보임)
        sparkBolt = LightningFactory.Create(boltPrefab, "HitSparkLightning", sparkStyle);
        streakBolt = LightningFactory.Create(boltPrefab, "HitStreakLightning", streakStyle);
        playerSparkBolt = LightningFactory.Create(boltPrefab, "PlayerHitSparkLightning", playerSparkStyle);
    }

    void OnEnable()
    {
        if (weapon != null) weapon.OnHit += OnWeaponHit;
        if (parry != null) parry.Parried += OnParried;
        if (health != null) health.Damaged += OnPlayerDamaged;
        if (guard != null) guard.Blocked += OnBlocked; // [변경]
    }

    void OnDisable()
    {
        if (weapon != null) weapon.OnHit -= OnWeaponHit;
        if (parry != null) parry.Parried -= OnParried;
        if (health != null) health.Damaged -= OnPlayerDamaged;
        if (guard != null) guard.Blocked -= OnBlocked; // [변경]
        EndHitStop();
    }

    void Update()
    {
        if (hitStopping && Time.unscaledTime >= hitStopEnd) EndHitStop();
    }

    void OnWeaponHit(IDamageable target, DamageInfo info, HitResult result)
    {
        if (result != HitResult.Hit) return;

        WeightSetting s = Find(info.Weight);
        if (s == null) return;

        Vector3 dir = info.Direction.sqrMagnitude > 0.0001f ? info.Direction : transform.forward;
        if (UsePrefab) Spawn(s.effect, info.HitPoint, Quaternion.LookRotation(-dir, Vector3.up), s.effectScale);
        // 맞은 면에서 공격자 쪽(카메라 쪽)으로 튀어야 잘 보임 → -dir 방향으로 분사
        if (UseSparks) Sparks(sparkBolt, info.HitPoint, -dir, s.sparkCount, s.sparkRadius, s.streakLength, dir);
        Shake(s.shake, s.shakeTime);
        HitStop(s.hitStop);
    }

    // 간파는 PlayerParry가 이미 적 정지·흔들림을 내므로 여기서는 이펙트만
    void OnParried(GameObject enemy)
    {
        WeightSetting s = Find(AttackWeight.Parry);
        if (s == null) return;

        Vector3 point = Vector3.Lerp(transform.position, enemy.transform.position, 0.5f) + Vector3.up * 1.2f;
        Vector3 dir = enemy.transform.position - transform.position;
        dir.y = 0f;
        if (UsePrefab) Spawn(s.effect, point, dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir) : Quaternion.identity, s.effectScale);
        if (UseSparks) Sparks(sparkBolt, point, Vector3.zero, s.sparkCount, s.sparkRadius, s.streakLength, dir); // 칼끼리 부딪힌 지점에서 사방으로
        Shake(s.shake, s.shakeTime);
        HitStop(s.hitStop);
    }

    void OnPlayerDamaged(DamageInfo info)
    {
        Vector3 point = transform.position + Vector3.up * 1.2f;
        if (UsePrefab) Spawn(playerHitEffect, point, Quaternion.identity, playerHitScale);
        if (UseSparks) Sparks(playerSparkBolt, point, -info.Direction, playerSparkCount, playerSparkRadius, 0f, info.Direction);
        HitStop(playerHitStop);
    }

    // [변경] 공격자 쪽으로 튀는 파란 스파크 (붉은 피격 스파크와 구분)
    void OnBlocked(DamageInfo info)
    {
        Vector3 point = info.HitPoint.sqrMagnitude > 0.0001f ? info.HitPoint : transform.position + Vector3.up * 1.2f;
        if (UseSparks) Sparks(sparkBolt, point, -info.Direction, blockSparkCount, blockSparkRadius, 0f, info.Direction);
        HitStop(blockHitStop);
    }

    WeightSetting Find(AttackWeight weight)
    {
        foreach (WeightSetting s in settings)
            if (s.weight == weight) return s;
        return null;
    }

    // 핵심: 한 지점에서 짧은 번개 여러 줄을 방사.
    // sprayDir이 0이면 사방, 아니면 그 방향 반구 쪽으로 퍼짐. streak > 0이면 공격 방향에 수직(가로)으로 긴 한 줄을 추가해 베인 느낌을 줌
    void Sparks(LightningBoltPrefabScriptBase bolt, Vector3 point, Vector3 sprayDir, int count, float radius, float streak, Vector3 attackDir)
    {
        if (!enableEffects || bolt == null) return;

        bool omni = sprayDir.sqrMagnitude < 0.0001f;
        Vector3 baseDir = omni ? Vector3.zero : sprayDir.normalized;

        for (int i = 0; i < count; i++)
        {
            Vector3 d = omni ? Random.onUnitSphere : (baseDir + Random.insideUnitSphere * sparkSpread).normalized;
            bolt.Trigger(point, point + d * Random.Range(radius * 0.5f, radius));
        }

        if (streak <= 0f || streakBolt == null) return;

        Vector3 side = Vector3.Cross(Vector3.up, attackDir);
        if (side.sqrMagnitude < 0.0001f) side = transform.right;
        side = (side.normalized + Vector3.up * Random.Range(-0.5f, 0.5f)).normalized;
        float half = streak * 0.5f;
        streakBolt.Trigger(point - side * half, point + side * half);
    }

    void Spawn(GameObject prefab, Vector3 position, Quaternion rotation, float scale)
    {
        if (!enableEffects || prefab == null) return;

        GameObject fx = Instantiate(prefab, position, rotation);

        // 팩 파티클은 Local 스케일 모드라 루트 크기를 바꿔도 안 줄어듦 → 계층 스케일로 바꾼 뒤 크기 조절
        if (!Mathf.Approximately(scale, 1f))
        {
            foreach (ParticleSystem ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            fx.transform.localScale *= scale;
        }

        Destroy(fx, effectLifetime);
    }

    void Shake(float strength, float time)
    {
        if (followCamera != null && strength > 0f) followCamera.Shake(strength, time);
    }

    // 스킬이 timeScale을 쓰는 중(차지 슬로·일섬 히트스톱)에는 건드리지 않음
    void HitStop(float duration)
    {
        if (!enableHitStop || duration <= 0f) return;
        if (skill != null && skill.IsActive) return;

        if (!hitStopping)
        {
            if (!Mathf.Approximately(Time.timeScale, 1f)) return;
            savedTimeScale = Time.timeScale;
            hitStopping = true;
            Time.timeScale = hitStopScale;
        }

        hitStopEnd = Mathf.Max(hitStopEnd, Time.unscaledTime + duration);
    }

    void EndHitStop()
    {
        if (!hitStopping) return;
        hitStopping = false;
        Time.timeScale = savedTimeScale;
    }

    // ===== 스파크 프리셋 ===== 짧고 가늘게, 대신 조명 섬광으로 맞은 순간을 강조
    static LightningBoltStyle DefaultSparkStyle() => new LightningBoltStyle
    {
        width = new Vector2(0.015f, 0.03f), duration = new Vector2(0.05f, 0.09f), generations = 4,
        chaos = 0.3f, forkedness = 0.2f, intensity = 3f, glowIntensity = 1.2f, glowWidth = 4f,
        lightRange = 2.5f, lightIntensity = 1.2f, lightPercent = 0.06f
    };

    static LightningBoltStyle DefaultStreakStyle() => new LightningBoltStyle
    {
        width = new Vector2(0.025f, 0.04f), duration = new Vector2(0.08f, 0.12f), generations = 5,
        chaos = 0.18f, forkedness = 0.35f, intensity = 3.5f, glowIntensity = 1.6f, glowWidth = 5f,
        lightRange = 4f, lightIntensity = 2f, lightPercent = 0.1f
    };

    static LightningBoltStyle DefaultPlayerSparkStyle() => new LightningBoltStyle
    {
        width = new Vector2(0.015f, 0.03f), duration = new Vector2(0.06f, 0.1f), generations = 4,
        chaos = 0.3f, forkedness = 0.2f, intensity = 2.5f, glowIntensity = 1.4f, glowWidth = 4f,
        trunkColor = new Color(1f, 0.85f, 0.9f), glowColor = new Color(0.9f, 0.15f, 0.45f),
        lightRange = 2.5f, lightIntensity = 1f, lightPercent = 0.06f, lightColor = new Color(1f, 0.3f, 0.5f)
    };

    [ContextMenu("Reset Spark Styles To Default")]
    void ResetSparkStyles()
    {
#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(this, "Reset Spark Styles");
#endif
        sparkStyle = DefaultSparkStyle();
        streakStyle = DefaultStreakStyle();
        playerSparkStyle = DefaultPlayerSparkStyle();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
}
