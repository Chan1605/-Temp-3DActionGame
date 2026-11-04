using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerSkill : MonoBehaviour
{
    enum Phase { Ready, Holding, Dashing, ChainWindow, Finishing }

    struct PendingHit
    {
        public IDamageable Target;
        public Vector3 Point;
        public Vector3 Direction;
    }

    static readonly int LocomotionHash = Animator.StringToHash("Locomotion");

    [SerializeField] InputActionReference skillAction;
    [SerializeField] SkillData data;

    [SerializeField] Animator animator;
    [SerializeField] PlayerLocomotion locomotion;
    [SerializeField] PlayerCombat combat;
    [SerializeField] ThirdPersonCamera followCamera;
    [SerializeField] TrailRenderer dashTrail;
    [SerializeField] PlayerStats stats; // 있으면 일섬 피해에도 공격력·치명타 적용
    [SerializeField] PlayerThunder thunder; // 있으면 뇌기 소모형 일섬 + 표식된 적만 즉사·연쇄

    [SerializeField] float aimTurnSpeed = 720f; // [변경] 자세·차지 중 고정한 대상을 따라 도는 속도(도/초, 실제 시간)
    [SerializeField] bool hideBodyWhileDashing = true;
    [SerializeField] LayerMask obstacleMask = ~0;

    readonly Collider[] overlapBuffer = new Collider[32];
    readonly RaycastHit[] castBuffer = new RaycastHit[16];
    readonly List<PendingHit> pendingHits = new List<PendingHit>();

    CharacterController controller;
    Renderer[] bodyRenderers;
    bool[] bodyRendererStates;
    bool bodyHidden;
    int stanceHash, chargeHash, releaseHash;

    Phase phase = Phase.Ready;
    float holdTime;
    int chargeLevel;
    int dashCount;
    Vector3 dashStart, dashEnd;
    float dashTimer;
    float windowTimer;
    float finishTimer;
    bool killsApplied;
    float cooldownTimer;
    float hitStopTimer;
    IDamageable aimTarget; // [변경] Q를 누른 순간 자동으로 고른 대상 (방향키와 무관)

    float savedTimeScale = 1f;
    bool timeScaleOverridden;

    public bool IsActive => phase != Phase.Ready;
    public bool IsInvulnerable => phase == Phase.Dashing;
    // HUD 쿨타임 표시용 (1 = 방금 사용, 0 = 사용 가능)
    public float CooldownRatio => data != null && data.Cooldown > 0f ? Mathf.Clamp01(cooldownTimer / data.Cooldown) : 0f;
    bool UsesGauge => thunder != null && thunder.UseGaugeForSkill;

    // 이펙트(PlayerVfx)용 이벤트: 돌진 시작·끝 지점, 일괄 베기로 베인 대상 위치
    public event System.Action<Vector3, Vector3> Dashed;
    public event System.Action<Vector3> TargetCut;

    // Q를 누르고 있는 동안(자세·차지) 이펙트용 이벤트
    public event System.Action HoldStarted;
    public event System.Action<int> ChargeLevelChanged;
    public event System.Action HoldEnded;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (stats == null) stats = GetComponent<PlayerStats>();
        if (locomotion == null) locomotion = GetComponent<PlayerLocomotion>();
        if (combat == null) combat = GetComponent<PlayerCombat>();
        if (followCamera == null) followCamera = FindAnyObjectByType<ThirdPersonCamera>();
        if (thunder == null) thunder = GetComponent<PlayerThunder>();

        bodyRenderers = CollectBodyRenderers();

        if (data != null)
        {
            stanceHash = Animator.StringToHash(data.StanceState);
            chargeHash = Animator.StringToHash(data.ChargeState);
            releaseHash = Animator.StringToHash(data.ReleaseState);
        }
    }

    void OnEnable()
    {
        if (skillAction != null) skillAction.action.Enable();
    }

    void OnDisable()
    {
        if (IsActive) EndSkill();
        RestoreTimeScale();
    }

    void Update()
    {
        if (data == null || skillAction == null) return;

        float dt = Time.unscaledDeltaTime;
        if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;
        UpdateHitStop(dt);

        switch (phase)
        {
            case Phase.Ready:
                if (skillAction.action.WasPressedThisFrame()) TryBegin();
                break;
            case Phase.Holding:
                UpdateHolding(dt);
                break;
            case Phase.Dashing:
                UpdateDash(dt);
                break;
            case Phase.ChainWindow:
                UpdateChainWindow(dt);
                break;
            case Phase.Finishing:
                UpdateFinishing(dt);
                break;
        }
    }

    void TryBegin()
    {
        // 뇌기 소모형: 즉발 비용(2칸)이 없으면 시작 안 함. 쿨타임은 쓰지 않음
        if (UsesGauge)
        {
            if (thunder.MaxAffordableLevel(0) < 0)
            {
                Debug.Log($"[Skill] 뇌기 부족 ({thunder.Gauge:0.0} / {thunder.SkillCost(0)})");
                return;
            }
        }
        else if (cooldownTimer > 0f)
        {
            Debug.Log($"[Skill] 쿨타임 {cooldownTimer:F1}초");
            return;
        }

        if (locomotion != null && !locomotion.IsGrounded) return; // 공중 일섬은 v1 범위 밖

        // 콤보 중에는 다음 타 전환 구간 이후에만 캔슬해서 들어감
        if (combat != null)
        {
            if (combat.IsAttacking && !combat.CanCancel) return;
            combat.CancelAttack();
            combat.InputLocked = true;
        }

        if (locomotion != null) locomotion.MovementLocked = true;

        phase = Phase.Holding;
        holdTime = 0f;
        chargeLevel = 0;
        dashCount = 0;
        pendingHits.Clear();

        aimTarget = FindTarget(transform.position, transform.forward, data.RangeAt(data.MaxChargeLevel), 360f, false); // [변경] 주변 360° 자동 선택
        FaceAimTarget(true, 0f); // [변경] 누른 순간 그 대상 쪽으로 바로 돌아서 차징 자세
        animator.CrossFadeInFixedTime(stanceHash, 0.05f, 0, 0f);
        HoldStarted?.Invoke();
    }

    // 짧게 떼면 즉발(0단계), 길게 누르면 차지 단계가 올라가며 사거리 증가
    void UpdateHolding(float dt)
    {
        holdTime += dt;
        FaceAimTarget(true, dt); // [변경] 자세·차지 중에는 고정한 대상을 자동으로 따라 돎

        int level = holdTime < data.ChargeStartTime
            ? 0
            : Mathf.Min(data.MaxChargeLevel, 1 + (int)((holdTime - data.ChargeStartTime) / data.ChargeLevelTime));
        if (UsesGauge) level = Mathf.Min(level, Mathf.Max(0, thunder.MaxAffordableLevel(data.MaxChargeLevel))); // 뇌기만큼만 차지

        if (level > chargeLevel)
        {
            if (chargeLevel == 0)
            {
                animator.CrossFadeInFixedTime(chargeHash, 0.1f, 0, 0f);
                SetTimeScale(data.ChargeTimeScale);
            }

            chargeLevel = level;
            ChargeLevelChanged?.Invoke(chargeLevel);
            if (followCamera != null) followCamera.Shake(0.04f * level, 0.12f);
            Debug.Log($"[Skill] 차지 {chargeLevel}단계");
        }

        if (!skillAction.action.IsPressed()) Release();
    }

    // [변경] 핵심: 방향키를 보지 않고 자동 조준. 누를 때 고른 대상(표식 우선 → 가까운 순)을 바라보고,
    // 그 대상이 쓰러지면 다시 고름. 대상이 없으면 지금 방향 유지
    void FaceAimTarget(bool snap, float dt)
    {
        if (!IsValidTarget(aimTarget))
            aimTarget = FindTarget(transform.position, transform.forward, data.RangeAt(data.MaxChargeLevel), 360f, false);
        if (aimTarget == null) return;

        Vector3 to = Flat(((Component)aimTarget).transform.position - transform.position);
        if (to.sqrMagnitude < 0.0001f) return;

        Quaternion want = Quaternion.LookRotation(to.normalized, Vector3.up);
        transform.rotation = snap ? want : Quaternion.RotateTowards(transform.rotation, want, aimTurnSpeed * dt);
    }

    void Release()
    {
        RestoreTimeScale();
        HoldEnded?.Invoke();
        if (UsesGauge) thunder.TrySpend(thunder.SkillCost(chargeLevel)); // 놓는 순간 단계별 비용 차감

        // [변경] 바라보던 대상이 이번 단계 사거리 안이면 그 대상에게, 아니면 정면 원뿔에서 다시 찾고 없으면 정면으로 헛돌진
        Vector3 dir = transform.forward;
        float range = data.RangeAt(chargeLevel);
        IDamageable target = IsValidTarget(aimTarget) && Flat(((Component)aimTarget).transform.position - transform.position).magnitude <= range
            ? aimTarget
            : FindTarget(transform.position, dir, range, data.SearchAngle, false);
        aimTarget = null;
        StartDash(target, dir);
    }

    bool IsValidTarget(IDamageable t) => t != null && (t as Object) != null && t.CanBeTargeted; // [변경]

    void StartDash(IDamageable target, Vector3 fallbackDir)
    {
        Vector3 start = transform.position;
        Vector3 end;

        if (target != null)
        {
            Vector3 targetPos = ((Component)target).transform.position;
            targetPos.y = start.y;
            Vector3 toTarget = Flat(targetPos - start).normalized;
            end = targetPos + toTarget * data.BehindDistance;
        }
        else
        {
            // 적이 없어도 정면으로 돌진 (헛스윙)
            end = start + Flat(fallbackDir).normalized * data.WhiffDistance;
        }

        end = ClampByObstacles(start, end);

        Vector3 dashDir = Flat(end - start);
        if (dashDir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(dashDir.normalized, Vector3.up);

        CollectPathHits(start, end);

        dashStart = start;
        dashEnd = end;
        dashTimer = 0f;
        dashCount++;
        phase = Phase.Dashing;
        Dashed?.Invoke(start, end);

        // 적을 통과해야 하므로 돌진 중에는 CharacterController를 끄고 직접 이동
        controller.enabled = false;
        SetBodyVisible(!hideBodyWhileDashing);
        if (dashTrail != null)
        {
            dashTrail.Clear();
            dashTrail.emitting = true;
        }

        animator.CrossFadeInFixedTime(releaseHash, 0.03f, 0, 0f);
        if (followCamera != null) followCamera.HoldFollow(0.12f);
    }

    void UpdateDash(float dt)
    {
        dashTimer += dt;
        float t = Mathf.Clamp01(dashTimer / data.DashDuration);
        transform.position = Vector3.Lerp(dashStart, dashEnd, t);

        if (t < 1f) return;

        controller.enabled = true;
        SetBodyVisible(true);
        if (dashTrail != null) dashTrail.emitting = false;
        if (followCamera != null) followCamera.Shake(0.12f, 0.12f);

        if (dashCount >= data.MaxChain)
        {
            BeginFinish();
            return;
        }

        phase = Phase.ChainWindow;
        windowTimer = 0f;
    }

    // 착지 후 창 안에 다시 누르면 아직 베지 않은 가장 가까운 적에게 연쇄 돌진
    void UpdateChainWindow(float dt)
    {
        windowTimer += dt;

        if (skillAction.action.WasPressedThisFrame())
        {
            IDamageable next = FindTarget(transform.position, transform.forward, data.ChainRange, 360f, UsesGauge); // 연쇄는 표식된 적만
            if (next != null)
            {
                StartDash(next, transform.forward);
                return;
            }
        }

        if (windowTimer >= data.ChainWindow) BeginFinish();
    }

    void BeginFinish()
    {
        phase = Phase.Finishing;
        finishTimer = 0f;
        killsApplied = false;
    }

    // 칼을 거둔 뒤 한 박자 늦게 베인 대상들에 일괄 적용
    void UpdateFinishing(float dt)
    {
        finishTimer += dt;

        if (!killsApplied && finishTimer >= data.KillDelay)
        {
            ApplyPendingHits();
            killsApplied = true;
        }

        if (killsApplied && finishTimer >= data.KillDelay + data.RecoveryTime) EndSkill();
    }

    void ApplyPendingHits()
    {
        int applied = 0;
        foreach (PendingHit hit in pendingHits)
        {
            if (hit.Target == null || (hit.Target as Object) == null) continue;

            // 핵심: 뇌인 3칸(표식)이면 즉사, 아니면 큰 피해만. 쿨타임형(A/B)이면 예전처럼 데이터 값 그대로
            bool marked = !UsesGauge || PlayerThunder.IsMarked((Component)hit.Target);
            var info = new DamageInfo
            {
                Attacker = gameObject,
                Damage = marked ? data.Damage : thunder.UnmarkedSkillDamage,
                PostureDamage = data.PostureDamage,
                Parryable = false,
                Weight = AttackWeight.Skill,
                InstantKill = marked && data.InstantKill,
                HitPoint = hit.Point,
                Direction = hit.Direction
            };
            if (stats != null) info = stats.Modify(info);
            hit.Target.ReceiveDamage(info);
            applied++;
            TargetCut?.Invoke(((Component)hit.Target).transform.position);
        }

        if (applied > 0)
        {
            SetTimeScale(0.05f);
            hitStopTimer = data.HitStopTime;
            if (followCamera != null) followCamera.Shake(0.25f, 0.2f);
        }

        Debug.Log($"[Skill] 벽력일섬 {dashCount}연 · {applied}명 베기");
    }

    void EndSkill()
    {
        if (phase == Phase.Holding) HoldEnded?.Invoke(); // 차지 도중 끊긴 경우에도 이펙트 정리
        phase = Phase.Ready;
        RestoreTimeScale();
        hitStopTimer = 0f;

        if (controller != null) controller.enabled = true;
        SetBodyVisible(true);
        if (dashTrail != null) dashTrail.emitting = false;

        if (locomotion != null) locomotion.MovementLocked = false;
        if (combat != null) combat.InputLocked = false;
        if (animator != null) animator.CrossFadeInFixedTime(LocomotionHash, 0.2f, 0, 0f);

        cooldownTimer = UsesGauge ? 0f : data.Cooldown;
    }

    // 표식된 적을 먼저 고르고(같은 조건이면 가까운 순), markedOnly면 표식된 적만
    IDamageable FindTarget(Vector3 origin, Vector3 preferDir, float range, float maxAngle, bool markedOnly)
    {
        int count = Physics.OverlapSphereNonAlloc(origin, range, overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
        IDamageable best = null;
        float bestDist = float.MaxValue;
        bool bestMarked = false;
        Vector3 forward = Flat(preferDir).normalized;

        for (int i = 0; i < count; i++)
        {
            Collider col = overlapBuffer[i];
            if (col.transform.IsChildOf(transform)) continue;

            IDamageable candidate = col.GetComponentInParent<IDamageable>();
            if (candidate == null || !candidate.CanBeTargeted || IsPending(candidate)) continue;

            Vector3 to = Flat(((Component)candidate).transform.position - origin);
            float dist = to.magnitude;
            if (dist < 0.01f) continue;
            if (maxAngle < 360f && Vector3.Angle(forward, to) > maxAngle * 0.5f) continue;

            bool marked = UsesGauge && PlayerThunder.IsMarked((Component)candidate);
            if (markedOnly && !marked) continue;
            if (bestMarked && !marked) continue;

            if (dist < bestDist || (marked && !bestMarked))
            {
                bestDist = dist;
                best = candidate;
                bestMarked = marked;
            }
        }

        return best;
    }

    // 벽에 막히면 그 앞에서 멈춤. 적과 바닥은 무시
    Vector3 ClampByObstacles(Vector3 start, Vector3 end)
    {
        Vector3 delta = Flat(end - start);
        float distance = delta.magnitude;
        if (distance < 0.01f) return start;

        Vector3 dir = delta / distance;
        float radius = controller.radius * 0.9f;
        Vector3 p0 = start + Vector3.up * (radius + 0.1f);
        Vector3 p1 = start + Vector3.up * (controller.height - radius);

        int count = Physics.CapsuleCastNonAlloc(p0, p1, radius, dir, castBuffer, distance, obstacleMask, QueryTriggerInteraction.Ignore);
        float allowed = distance;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = castBuffer[i];
            if (hit.collider.transform.IsChildOf(transform)) continue;
            if (hit.collider.GetComponentInParent<IDamageable>() != null) continue;
            if (hit.normal.y > 0.7f) continue;

            allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - 0.05f));
        }

        return start + dir * allowed;
    }

    void CollectPathHits(Vector3 start, Vector3 end)
    {
        Vector3 up = Vector3.up * 1f;
        int count = Physics.OverlapCapsuleNonAlloc(start + up, end + up, data.PathRadius, overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
        Vector3 dir = Flat(end - start).normalized;

        for (int i = 0; i < count; i++)
        {
            Collider col = overlapBuffer[i];
            if (col.transform.IsChildOf(transform)) continue;

            IDamageable target = col.GetComponentInParent<IDamageable>();
            if (target == null || !target.CanBeTargeted || IsPending(target)) continue;

            pendingHits.Add(new PendingHit
            {
                Target = target,
                Point = ((Component)target).transform.position,
                Direction = dir
            });
        }
    }

    bool IsPending(IDamageable target)
    {
        foreach (PendingHit hit in pendingHits)
            if (hit.Target == target) return true;
        return false;
    }

    void UpdateHitStop(float dt)
    {
        if (hitStopTimer <= 0f) return;
        hitStopTimer -= dt;
        if (hitStopTimer <= 0f) RestoreTimeScale();
    }

    // timeScale 변경은 이 두 메서드로만 → 스킬이 어떻게 끝나도 원래 값으로 복구
    void SetTimeScale(float scale)
    {
        if (!timeScaleOverridden)
        {
            savedTimeScale = Time.timeScale;
            timeScaleOverridden = true;
        }
        Time.timeScale = scale;
    }

    void RestoreTimeScale()
    {
        if (!timeScaleOverridden) return;
        Time.timeScale = savedTimeScale;
        timeScaleOverridden = false;
    }

    Renderer[] CollectBodyRenderers()
    {
        if (animator == null) return new Renderer[0];

        var list = new List<Renderer>();
        foreach (Renderer r in animator.GetComponentsInChildren<Renderer>(true))
        {
            if (r is TrailRenderer || r is ParticleSystemRenderer) continue;
            list.Add(r);
        }
        return list.ToArray();
    }

    // 숨기기 전 상태를 기억했다가 그대로 복원 (원래 꺼져 있던 렌더러는 계속 꺼진 채로)
    void SetBodyVisible(bool visible)
    {
        if (bodyRenderers == null || visible != bodyHidden) return;

        if (!visible)
        {
            bodyRendererStates ??= new bool[bodyRenderers.Length];
            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                if (bodyRenderers[i] == null) continue;
                bodyRendererStates[i] = bodyRenderers[i].enabled;
                bodyRenderers[i].enabled = false;
            }
        }
        else
        {
            for (int i = 0; i < bodyRenderers.Length; i++)
                if (bodyRenderers[i] != null) bodyRenderers[i].enabled = bodyRendererStates[i];
        }

        bodyHidden = !visible;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
