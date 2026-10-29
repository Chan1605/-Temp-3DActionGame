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

    float savedTimeScale = 1f;
    bool timeScaleOverridden;

    public bool IsActive => phase != Phase.Ready;
    public bool IsInvulnerable => phase == Phase.Dashing;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (locomotion == null) locomotion = GetComponent<PlayerLocomotion>();
        if (combat == null) combat = GetComponent<PlayerCombat>();
        if (followCamera == null) followCamera = FindAnyObjectByType<ThirdPersonCamera>();

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
        if (cooldownTimer > 0f)
        {
            Debug.Log($"[Skill] 쿨타임 {cooldownTimer:F1}초");
            return;
        }

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

        animator.CrossFadeInFixedTime(stanceHash, 0.05f, 0, 0f);
    }

    // 짧게 떼면 즉발(0단계), 길게 누르면 차지 단계가 올라가며 사거리 증가
    void UpdateHolding(float dt)
    {
        holdTime += dt;

        int level = holdTime < data.ChargeStartTime
            ? 0
            : Mathf.Min(data.MaxChargeLevel, 1 + (int)((holdTime - data.ChargeStartTime) / data.ChargeLevelTime));

        if (level > chargeLevel)
        {
            if (chargeLevel == 0)
            {
                animator.CrossFadeInFixedTime(chargeHash, 0.1f, 0, 0f);
                SetTimeScale(data.ChargeTimeScale);
            }

            chargeLevel = level;
            if (followCamera != null) followCamera.Shake(0.04f * level, 0.12f);
            Debug.Log($"[Skill] 차지 {chargeLevel}단계");
        }

        if (!skillAction.action.IsPressed()) Release();
    }

    void Release()
    {
        RestoreTimeScale();

        Vector3 dir = locomotion != null ? locomotion.GetInputDirection() : Vector3.zero;
        if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;

        float range = data.RangeAt(chargeLevel);
        IDamageable target = FindTarget(transform.position, dir, range, data.SearchAngle);
        StartDash(target, dir);
    }

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
            IDamageable next = FindTarget(transform.position, transform.forward, data.ChainRange, 360f);
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

            hit.Target.ReceiveDamage(new DamageInfo
            {
                Attacker = gameObject,
                Damage = data.Damage,
                PostureDamage = data.PostureDamage,
                Parryable = false,
                InstantKill = data.InstantKill,
                HitPoint = hit.Point,
                Direction = hit.Direction
            });
            applied++;
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
        phase = Phase.Ready;
        RestoreTimeScale();
        hitStopTimer = 0f;

        if (controller != null) controller.enabled = true;
        SetBodyVisible(true);
        if (dashTrail != null) dashTrail.emitting = false;

        if (locomotion != null) locomotion.MovementLocked = false;
        if (combat != null) combat.InputLocked = false;
        if (animator != null) animator.CrossFadeInFixedTime(LocomotionHash, 0.2f, 0, 0f);

        cooldownTimer = data.Cooldown;
    }

    IDamageable FindTarget(Vector3 origin, Vector3 preferDir, float range, float maxAngle)
    {
        int count = Physics.OverlapSphereNonAlloc(origin, range, overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
        IDamageable best = null;
        float bestDist = float.MaxValue;
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

            if (dist < bestDist)
            {
                bestDist = dist;
                best = candidate;
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
