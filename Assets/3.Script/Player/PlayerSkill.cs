using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public partial class PlayerSkill : MonoBehaviour // 발도 자세는 PlayerSkill.Stance.cs
{
    enum Phase { Ready, Holding, Dashing, ChainWindow, Finishing, Stance } // Stance 추가

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
    [SerializeField] PlayerThunder thunder; // 있으면 뇌기 소모형 일섬 + 흐트러진 적만 즉사·연쇄
    [SerializeField] PlayerStance stance;   // 일섬이 끝나면 납도 → 평상시 자세 (락온 중이면 그대로 전투 자세)
    [SerializeField] bool sheatheAfterSkill = true;
    [SerializeField] bool sheatheOnLand = true;  // 마지막 돌진이 끝나자마자 납도 → 칼이 들어가는 순간 일괄 낙뢰 (허공 베기 모션 없음)

    [SerializeField] float aimTurnSpeed = 720f; // 자세·차지 중 고정한 대상을 따라 도는 속도(도/초, 실제 시간)
    // 끄면(기본) 대상 제약 없음: 아무 적이나 겨냥·연쇄하고 보스 외에는 전부 즉사. 켜면 흐트러진 적만 즉사·연쇄 (A/B 비교용)
    [SerializeField] bool staggerOnly = false;
    [SerializeField] bool tapChain = false; // 탭 Q는 한 명만 켜면 예전 연쇄
    [SerializeField] float tapChainCost = 0.5f; // 탭 Q 연격 한 번마다 드는 뇌기
    [SerializeField] bool hideBodyWhileDashing = true;
    [SerializeField] float landPoseFade = 0.15f; // [변경] 착지하면 돌진 모션(처형 클립)을 끊고 전투 대기 자세로
    [SerializeField] LayerMask obstacleMask = ~0;

    readonly Collider[] overlapBuffer = new Collider[128]; // 32칸이면 바위·풀 콜라이더로 꽉 차 적을 못 찾는 경우가 있었음
    readonly RaycastHit[] castBuffer = new RaycastHit[16];
    readonly List<PendingHit> pendingHits = new List<PendingHit>();

    CharacterController controller;
    Renderer[] bodyRenderers;
    bool[] bodyRendererStates;
    bool bodyHidden;
    int stanceHash, chargeHash, releaseHash; // 잔심 자세(Skill_Finish) 삭제 → 착지 즉시 납도로 확정
    bool sheathedEarly;  // 착지 직후 이미 납도를 시작함 → 끝낼 때 다시 틀지 않음

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
    IDamageable aimTarget; // Q를 누른 순간 자동으로 고른 대상 (방향키와 무관)

    float savedTimeScale = 1f;
    bool timeScaleOverridden;

    public bool IsActive => phase != Phase.Ready;
    public bool IsInvulnerable => phase == Phase.Dashing;
    public TrailRenderer DashTrail => dashTrail; // 차지 섬격도 같은 궤적을 씀
    // HUD 쿨타임 표시용 (1 = 방금 사용, 0 = 사용 가능)
    public float CooldownRatio => data != null && data.Cooldown > 0f ? Mathf.Clamp01(cooldownTimer / data.Cooldown) : 0f;
    bool UsesGauge => thunder != null && thunder.UseGaugeForSkill;
    public bool HasStaggerTarget { get; private set; } // HUD 안내용: 사거리 안에 흐트러진 적이 있음 (매 프레임 갱신)
    public event System.Action Blocked; // 뇌기가 모자란데 Q를 누름

    // 이펙트(PlayerVfx)용 이벤트: 돌진 시작·끝 지점, 일괄 베기로 베인 대상 위치
    public event System.Action<Vector3, Vector3> Dashed;
    public event System.Action<Vector3> TargetCut;

    // Q를 누르고 있는 동안(자세·차지) 이펙트용 이벤트
    public event System.Action HoldStarted;
    public event System.Action<int> ChargeLevelChanged;
    public event System.Action HoldEnded;

    void Awake()
    {
        if (stance == null) stance = GetComponent<PlayerStance>();
        controller = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (stats == null) stats = GetComponent<PlayerStats>();
        if (locomotion == null) locomotion = GetComponent<PlayerLocomotion>();
        if (combat == null) combat = GetComponent<PlayerCombat>();
        if (followCamera == null) followCamera = FindAnyObjectByType<ThirdPersonCamera>();
        if (thunder == null) thunder = GetComponent<PlayerThunder>();
        if (enchant == null) enchant = GetComponent<PlayerEnchant>(); // 발도 자세 (각성·저스트 회피 진입)
        if (dodge == null) dodge = GetComponent<PlayerDodge>();
        if (GetComponent<DrawStanceFx>() == null) gameObject.AddComponent<DrawStanceFx>(); // 흑백·표식 연출은 실행 중 자동 부착

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
        if (dodge != null) dodge.JustDodged += OnJustDodged;
    }

    void OnDisable()
    {
        if (IsActive) EndSkill();
        RestoreTimeScale();
        if (dodge != null) dodge.JustDodged -= OnJustDodged;
        CloseEnchantWindow();
    }

    void Update()
    {
        if (GamePause.IsPaused) return; // 일시정지 중엔 입력·진행을 멈춤 (이전 반영분이 덮어써져 다시 넣음)
        if (data == null || skillAction == null) return;

        float dt = Time.unscaledDeltaTime;
        if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;
        UpdateEnchantWindow(dt); // 각성 중 발도 시간
        UpdateHitStop(dt);
        ThunderMark.ShowIcons = staggerOnly; // 흐트러짐 표시는 흐트러짐 모드에서만
        if (phase == Phase.Ready) HasStaggerTarget = staggerOnly && NearestStaggered() != null;

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
            case Phase.Stance:
                UpdateStance(dt);
                break;
        }
    }

    void TryBegin()
    {
        // 각성 중 · 저스트 회피 직후면 바로 발도 자세 (비용은 InstantStanceEntry에서 확인)
        StanceEntry instant = InstantStanceEntry();
        bool needsQCost = instant == StanceEntry.None;

        // 뇌기 소모형: 즉발 비용(1칸)이 없으면 시작 안 함. 쿨타임은 쓰지 않음
        if (needsQCost && UsesGauge)
        {
            if (thunder.MaxAffordableLevel(0) < 0)
            {
                Debug.Log($"[Skill] 뇌기 부족 ({thunder.Gauge:0.0} / {thunder.SkillCost(0)})");
                Blocked?.Invoke(); // 안내는 뇌기 부족일 때만 (뇌인 조건 삭제)
                return;
            }
        }
        else if (needsQCost && cooldownTimer > 0f)
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

        aimTarget = FindTarget(transform.position, transform.forward, data.RangeAt(data.MaxChargeLevel), 360f, false); // 주변 360° 자동 선택
        if (staggerOnly && (aimTarget == null || StaggerRank((Component)aimTarget) == 0)) aimTarget = NearestStaggered() ?? aimTarget; // 흐트러짐 모드에서만 흐트러진 적 우선
        FaceAimTarget(true, 0f); // 누른 순간 그 대상 쪽으로 바로 돌아서 차징 자세

        if (instant != StanceEntry.None)
        {
            EnterStance(instant);
            return;
        }
        animator.CrossFadeInFixedTime(stanceHash, 0.05f, 0, 0f);
        HoldStarted?.Invoke();
    }

    // 짧게 떼면 즉발(0단계), 길게 누르면 차지 단계가 올라가며 사거리 증가
    void UpdateHolding(float dt)
    {
        holdTime += dt;
        FaceAimTarget(true, dt); // 자세·차지 중에는 고정한 대상을 자동으로 따라 돎

        // 발도 자세를 쓸 수 있으면(2칸) 차지 대신 일정 시간 누르면 발도 자세. 짧게 떼면 기존 즉발
        if (CanHoldIntoStance)
        {
            if (holdTime >= stanceEnterTime) EnterStance(StanceEntry.Hold);
            else if (!skillAction.action.IsPressed()) Release();
            return;
        }

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

    // 핵심: 방향키를 보지 않고 자동 조준. 누를 때 고른 대상(표식 우선 → 가까운 순)을 바라보고,
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

        // 바라보던 대상이 이번 단계 사거리 안이면 그 대상에게, 아니면 정면 원뿔에서 다시 찾고 없으면 정면으로 헛돌진
        Vector3 dir = transform.forward;
        float range = data.RangeAt(chargeLevel);
        IDamageable target = IsValidTarget(aimTarget) && Flat(((Component)aimTarget).transform.position - transform.position).magnitude <= range
            ? aimTarget
            : FindTarget(transform.position, dir, range, data.SearchAngle, false);
        aimTarget = null;
        StartDash(target, dir);
    }

    bool IsValidTarget(IDamageable t) => t != null && (t as Object) != null && t.CanBeTargeted;

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
        ArmPass(target, start, end); // 대상 옆을 지나는 순간 슬로 (PlayerSkill.PassSlow.cs)
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
        dashTimer += PassDashDelta(dt); // 지나가는 순간엔 돌진 진행을 늦춤
        float t = Mathf.Clamp01(dashTimer / data.DashDuration);
        transform.position = Vector3.Lerp(dashStart, dashEnd, t);

        if (t < 1f) return;

        controller.enabled = true;
        SetBodyVisible(true);
        if (dashTrail != null) dashTrail.emitting = false;
        if (followCamera != null) followCamera.Shake(0.12f, 0.12f);

        if (dashCount >= ChainLimit()) // 탭 Q 연격 횟수는 스킬트리 해금 단계를 따름
        {
            BeginFinish();
            return;
        }

        if (!stanceChain) PlayLandPose(); // [변경] 연격 입력을 기다리는 동안 처형 모션이 이어지던 문제
        phase = Phase.ChainWindow;
        windowTimer = 0f;
    }

    // 돌진 상태(Skill_Release)는 처형 클립을 같이 써서, 착지 뒤에도 그대로 두면 처형 동작이 보임 → 전투 대기 자세로 넘김
    void PlayLandPose()
    {
        if (animator != null) animator.CrossFadeInFixedTime(LocomotionHash, landPoseFade, 0, 0f);
    }

    // 착지 후 창 안에 다시 누르면 아직 베지 않은 가장 가까운 적에게 연쇄 돌진
    void UpdateChainWindow(float dt)
    {
        windowTimer += dt;

        if (stanceChain) // 표식 연쇄는 입력 없이 자동
        {
            UpdateStanceChain();
            return;
        }

        if (skillAction.action.WasPressedThisFrame())
        {
            IDamageable next = FindTarget(transform.position, transform.forward, data.ChainRange, 360f, UsesGauge && staggerOnly); // 흐트러짐 모드에서만 흐트러진 적에게만 연쇄
            // 연격 한 번마다 뇌기 tapChainCost. 모자라면 이어지지 않음
            if (next != null && (!UsesGauge || tapChainCost <= 0f || thunder.TrySpend(tapChainCost)))
            {
                StartDash(next, transform.forward);
                return;
            }
            if (next != null) Blocked?.Invoke();
        }

        if (windowTimer >= data.ChainWindow) BeginFinish();
    }

    // 이번 일섬에서 이어 갈 수 있는 최대 돌진 수
    //  - 발도 자세 표식 연쇄: 찍은 표식 수 (최소 데이터 최대치)
    //  - 탭 Q: 스킬트리가 있으면 1 + 연격 단계(0~3), 없으면 예전처럼 tapChain 토글
    int ChainLimit()
    {
        if (stanceChain) return Mathf.Max(data.MaxChain, stanceMarks.Count); // 표식 연쇄는 찍은 만큼 (각성 발도 무제한 표식이 6번째에서 끊기던 문제)
        if (SkillTree.Active) return Mathf.Min(1 + SkillTree.ChainLevel, data.MaxChain);
        return tapChain ? data.MaxChain : 1;
    }

    void BeginFinish()
    {
        phase = Phase.Finishing;
        finishTimer = 0f;
        killsApplied = false;

        // 착지하자마자 납도 (락온 중이면 SheatheNow가 거절 → Release 자세 유지) // 잔심 자세 분기 삭제
        sheathedEarly = sheatheOnLand && sheatheAfterSkill && stance != null && stance.SheatheNow();
        if (!sheathedEarly) PlayLandPose(); // [변경] 락온 중 등 납도를 못 하면 처형 모션 대신 전투 대기 자세
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

        float end = data.KillDelay + data.RecoveryTime; // 잔심 자세 시간 삭제
        if (killsApplied && finishTimer >= end) EndSkill(true); // 끝까지 마친 경우만 납도
    }

    void ApplyPendingHits()
    {
        int applied = 0;
        foreach (PendingHit hit in pendingHits)
        {
            if (hit.Target == null || (hit.Target as Object) == null) continue;

            // 핵심: 흐트러진 적은 즉사, 아니면 큰 피해만. 보스는 즉사 없이 체간을 크게 (무너져 있으면 피해도 크게)
            // 쿨타임형(A/B)이면 예전처럼 데이터 값 그대로
            bool staggered = !UsesGauge || !staggerOnly || PlayerThunder.IsStaggered((Component)hit.Target); // 제약 없음 모드면 모두 즉사 대상
            bool boss = UsesGauge && hit.Target is EnemySwordsman enemy && enemy.IsBoss;
            float damage = !UsesGauge ? data.Damage
                : boss ? thunder.NormalSkillDamage * (staggered ? thunder.BossStaggerDamageScale : 1f)
                : staggered ? data.Damage : thunder.NormalSkillDamage;
            var info = new DamageInfo
            {
                Attacker = gameObject,
                Damage = damage,
                PostureDamage = data.PostureDamage * (boss ? thunder.BossPostureScale : 1f),
                Parryable = false,
                Weight = AttackWeight.Skill,
                InstantKill = staggered && !boss && data.InstantKill,
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

    void EndSkill(bool finished = false)
    {
        if (phase == Phase.Holding || phase == Phase.Stance) HoldEnded?.Invoke(); // 차지·발도 자세 도중 끊긴 경우에도 이펙트 정리 // Stance 포함
        ClearStance(); // 적 정지 해제 · 표식 정리
        ClearPassSlow();
        phase = Phase.Ready;
        RestoreTimeScale();
        hitStopTimer = 0f;

        if (controller != null) controller.enabled = true;
        SetBodyVisible(true);
        if (dashTrail != null) dashTrail.emitting = false;

        if (locomotion != null) locomotion.MovementLocked = false;
        if (combat != null) combat.InputLocked = false;
        // 이미 착지 때 납도를 시작했으면 그 모션을 그대로 둠
        bool sheathed = sheathedEarly || (finished && sheatheAfterSkill && stance != null && stance.SheatheNow());
        sheathedEarly = false;
        if (!sheathed && animator != null) animator.CrossFadeInFixedTime(LocomotionHash, 0.2f, 0, 0f);

        cooldownTimer = UsesGauge ? 0f : data.Cooldown;
    }

    // 흐트러진 적 → 나머지 순으로 고르고(같은 순위면 가까운 순), staggeredOnly면 흐트러진 적만
    IDamageable FindTarget(Vector3 origin, Vector3 preferDir, float range, float maxAngle, bool staggeredOnly)
    {
        int count = Physics.OverlapSphereNonAlloc(origin, range, overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
        IDamageable best = null;
        float bestDist = float.MaxValue;
        int bestRank = -1;
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

            int rank = StaggerRank((Component)candidate);
            if (staggeredOnly && rank == 0) continue;
            if (rank < bestRank || (rank == bestRank && dist >= bestDist)) continue;

            bestDist = dist;
            best = candidate;
            bestRank = rank;
        }

        return best;
    }

    int StaggerRank(Component c) => staggerOnly && PlayerThunder.IsStaggered(c) ? 1 : 0; // 제약 없음 모드면 순위 없이 가까운 순

    // 최대 사거리 안에서 흐트러진 가장 가까운 적 (살아 있는 검병 목록을 직접 봄 → 물리 검색 없음)
    IDamageable NearestStaggered()
    {
        if (data == null) return null;
        IDamageable best = null;
        float bestDist = data.RangeAt(data.MaxChargeLevel);
        foreach (EnemySwordsman enemy in EnemySwordsman.Alive)
        {
            if (enemy == null || !enemy.IsStaggered || !enemy.CanBeTargeted) continue;
            float d = Flat(enemy.transform.position - transform.position).magnitude;
            if (d > bestDist) continue;
            bestDist = d;
            best = enemy;
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
