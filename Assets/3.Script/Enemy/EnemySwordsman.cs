using UnityEngine;

// partial: 방패병(EnemySwordsman.Guard.cs) · 돌격병(EnemySwordsman.Charge.cs) · 처형(EnemySwordsman.Execution.cs) · 보스(EnemySwordsman.Boss.cs) 행동을 파일로 나눔. 종류는 EnemyData.Kind
[RequireComponent(typeof(CharacterController))]
public partial class EnemySwordsman : MonoBehaviour, IDamageable, IAttackThreat, IStaggerable // 흐트러짐 = 일섬(Q) 즉사·연쇄 대상
{
    enum State { Idle, Chase, Windup, Attack, Stun, Dead, ChargeWindup, Charge, Executed, // 처형 중
        BossGrabWindup, BossGrab, BossLeapWindup, BossLeapAir, BossLeapLand, BossRoar, // 보스 패턴
        Airborne, Downed } // 띄워짐 · 쓰러짐 (EnemySwordsman.Air.cs)

    static readonly int MoveBlendHash = Animator.StringToHash("MoveBlend");
    static readonly int LocomotionHash = Animator.StringToHash("Locomotion");
    static readonly int DeathHash = Animator.StringToHash("Death");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    // 순서: 앞, 뒤, 왼쪽, 오른쪽에서 맞음
    static readonly int[] LightHitHashes =
    {
        Animator.StringToHash("Hit_F"), Animator.StringToHash("Hit_B"),
        Animator.StringToHash("Hit_L"), Animator.StringToHash("Hit_R")
    };
    static readonly int[] HeavyHitHashes =
    {
        Animator.StringToHash("HitLarge_F"), Animator.StringToHash("HitLarge_B"),
        Animator.StringToHash("HitLarge_L"), Animator.StringToHash("HitLarge_R")
    };

    [SerializeField] EnemyData data;
    [SerializeField] Animator animator;
    [SerializeField] WeaponHitbox weapon;

    [Header("Look")]
    // 흑검답게 더 어둡게, 예고는 흰색 대신 주황(HDR) → 같은 모델인 플레이어와 확실히 구분
    [SerializeField] Color bodyTint = new Color(0.18f, 0.18f, 0.22f);
    [SerializeField] Color windupColor = new Color(1.6f, 0.7f, 0.15f);
    [SerializeField] Color hitColor = new Color(1f, 0.25f, 0.2f);
    [SerializeField] Color parryColor = new Color(1f, 0.95f, 0.6f);
    [SerializeField] Color armorColor = new Color(0.75f, 0.8f, 0.95f); // 아머로 버틴 피격은 회색빛 → "안 먹혔다"가 읽히게
    [SerializeField] float hitFlashTime = 0.1f;
    [SerializeField] float comboTellTime = 0.25f; // 연타 다음 타 직전 예고 발광 시간(적 시간 기준)
    [SerializeField] bool showWindupTell; // 잡몹은 예고 없음이 기본 (보스는 잡기·특수기만 예고 예정)
    [SerializeField] bool showComboTell;
    [SerializeField] bool trainer; // 간파 수련용: 구역 전투에 끼지 않음 (튜토리얼 배치용)

    [SerializeField] float gravity = -20f;
    [SerializeField] float knockbackTime = 0.15f;
    [SerializeField] bool logState;

    // 저스트 회피·간파 판정에서 "나를 노리는 공격"으로 볼 범위 (사거리 + 여유 거리, 정면 기준 각도)
    [Header("Threat Judge")]
    [SerializeField] float threatRangeMargin = 1f;
    [SerializeField] float threatAngle = 60f;

    CharacterController controller;
    Transform target;
    Renderer[] bodyRenderers;
    MaterialPropertyBlock block;

    State state;
    float stateTime;
    float localTime; // 적 전용 시계 (CombatTime 슬로가 반영된 시간)
    float health;
    float attackReadyTime;
    float stunDuration;

    AttackData currentAttack;
    bool swingActive;
    float stepTraveled;

    float moveSpeed;
    float verticalVelocity;
    Vector3 knockbackVelocity;
    float knockbackTimer;
    float hitFlashTimer;
    Color flashColor;

    Vector3 homePosition;
    Quaternion homeRotation;
    bool respawnEnabled = true; // 구역 웨이브로 나온 적은 부활하지 않음
    bool alerted;               // 감지 거리와 상관없이 플레이어를 쫓음

    public bool CanBeTargeted => state != State.Dead;
    public bool IsWindingUp => state == State.Windup;
    public bool IsSwinging => swingActive;
    public event System.Action<EnemySwordsman> Died;
    // 경험치용 전역 처치 이벤트. 부활하는 테스트용 적은 제외 (무한 경험치 방지)
    public static event System.Action<EnemySwordsman> AnyKilled;
    public bool IsTrainer => trainer;

    // 흐트러짐: 강경직(패링·간파·가드 붕괴 포함) · 돌격 탈진 · 공중에 뜸 · 쓰러짐 · 처형 가능 · 보스 체간 붕괴 (약경직은 제외)
    public bool IsStaggered => state != State.Dead &&
        (state == State.Airborne || state == State.Downed || (state == State.Stun && (stunHeavy || exhausted)) || IsExecutable || IsPostureBroken);
    bool stunHeavy;

    // 웨이브로 나온 적: 쓰러지고 corpseDelay 뒤 땅으로 가라앉아 사라짐 (시체 누적 → 가독성·성능 저하 방지)
    bool temporary;
    const float CorpseDelay = 2f;
    const float CorpseSinkTime = 1f;
    public void MarkTemporary() => temporary = true;

    System.Collections.IEnumerator SinkAndDestroy()
    {
        yield return new WaitForSeconds(CorpseDelay);
        Vector3 from = transform.position;
        for (float t = 0f; t < CorpseSinkTime; t += Time.deltaTime)
        {
            transform.position = from + Vector3.down * (1.5f * t / CorpseSinkTime);
            yield return null;
        }
        Destroy(gameObject);
    }
    public int XpReward => data != null ? Mathf.RoundToInt(data.XpReward * xpScale) : 0; // 시련 배율 반영

    // 시련 모드: 웨이브가 오를수록 체력·공격력·경험치 배율. 생성 직후(Start 전) 호출
    float healthScale = 1f, damageScale = 1f, xpScale = 1f;
    public void ApplyScaling(float health, float damage, float xp)
    {
        healthScale = Mathf.Max(0.1f, health);
        damageScale = Mathf.Max(0.1f, damage);
        xpScale = Mathf.Max(0f, xp);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => AnyKilled = null;

    // ===== 구역(ZoneArena)용 =====
    public void DisableRespawn() => respawnEnabled = false;

    public void Alert()
    {
        alerted = true;
        if (state == State.Idle) SetState(State.Chase);
    }

    // 등장 직후 잠깐 무방비 (낙뢰와 함께 나타나 자리 잡는 시간). 경직 상태를 재사용하고 끝나면 추적
    public void HoldFor(float seconds)
    {
        if (state == State.Dead) return;
        moveSpeed = 0f;
        stunDuration = seconds;
        stunHeavy = false; // 등장 직후 대기는 흐트러짐이 아님
        attackReadyTime = localTime + seconds + 0.5f;
        SetState(State.Stun);
    }

    // 플레이어가 죽어 구역을 다시 시작할 때: 살아 있든 죽었든 처음 자리·체력으로
    public void ResetToHome()
    {
        if (swingActive) EndSwing();
        currentAttack = null;
        knockbackTimer = 0f;
        alerted = false;
        controller.enabled = false;
        Respawn();
    }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (weapon == null) weapon = GetComponentInChildren<WeaponHitbox>();

        bodyRenderers = animator != null ? animator.GetComponentsInChildren<Renderer>() : new Renderer[0];
        block = new MaterialPropertyBlock();

        homePosition = transform.position;
        homeRotation = transform.rotation;
    }

    void Start()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) target = player.transform;

        health = data != null ? data.MaxHealth * healthScale : 1f;
        ApplyColor(bodyTint);
        InitGuard();
        InitBoss();
        if (animator != null && IsGuardian) PlayMoveState(0f);
    }

    void Update()
    {
        if (data == null || target == null) return;
        if (UpdateFreeze()) return; // 피격 순간 이 적만 아주 짧게 멈춤 (로직도 같이 멈춰 판정이 어긋나지 않음)

        // 적은 CombatTime 배속으로 움직임 → 저스트 회피·간파 슬로가 플레이어를 빼고 적용됨
        float scale = CombatTime.EnemyScale;
        float dt = Time.deltaTime * scale * BossSpeed; // 보스 2페이즈는 동작 전체가 빨라짐
        if (animator != null) animator.speed = scale * (state == State.Executed ? executedSpeed : BossSpeed); // 처형 짝 모션은 플레이어와 같은 배속
        localTime += dt;
        stateTime += dt;
        UpdateGuard(dt);
        UpdateBoss(dt);

        switch (state)
        {
            case State.Idle: UpdateIdle(dt); break; // 수련장 복귀 이동에 dt 필요
            case State.Chase: UpdateChase(dt); break;
            case State.Windup: UpdateWindup(dt); break;
            case State.Attack: UpdateAttack(dt); break;
            case State.Stun: UpdateStun(); break;
            case State.Dead: UpdateDead(); break;
            case State.ChargeWindup: UpdateChargeWindup(dt); break;
            case State.Charge: UpdateCharge(dt); break;
            case State.Executed: UpdateExecuted(dt); break; // 자리로 미끄러진 뒤 모션이 끝날 때까지 대기
            case State.BossGrabWindup: UpdateBossGrabWindup(dt); break;
            case State.BossGrab: UpdateBossGrab(dt); break;
            case State.BossLeapWindup: UpdateBossLeapWindup(dt); break;
            case State.BossLeapAir: UpdateBossLeapAir(dt); break;
            case State.BossLeapLand: UpdateBossLeapLand(); break;
            case State.BossRoar: UpdateBossRoar(); break;
            case State.Airborne: UpdateAirborne(); break;
            case State.Downed: UpdateDowned(); break;
        }

        Move(dt);
        UpdateAnimator(dt);
        UpdateColor(dt);
    }

    void UpdateIdle(float dt)
    {
        moveSpeed = 0f;
        if (UpdateTraining(dt)) return; // 허수아비 · 묶인 수련 상대는 제자리로
        if (alerted || DistanceToTarget() <= data.DetectRange) SetState(State.Chase);
    }

    // 사거리 밖이면 다가가고, 사거리 안에서 쿨타임이 끝나면 예고 동작으로 들어감
    void UpdateChase(float dt)
    {
        if (UpdateTraining(dt)) return; // 허수아비는 쫓지 않고, 수련 상대는 묶인 범위 밖이면 돌아감
        float dist = DistanceToTarget();
        if (!alerted && dist > data.DetectRange * 1.2f)
        {
            SetState(State.Idle);
            return;
        }

        FaceTarget(dt, 1f);
        if (UpdateBossChase(dist)) return; // 보스: 등장 · 체간 붕괴 · 포효 · 도약 · 검기

        // 방패병은 막은 직후 잠깐 멈칫, 돌격병은 거리가 맞으면 돌진 예고
        if (guardPauseTimer > 0f)
        {
            moveSpeed = 0f;
            return;
        }
        if (CanTakeToken() && TryStartCharge(dist)) // 돌진도 공격 차례가 있어야
        {
            TryTakeToken();
            return;
        }
        if (UpdateCircling(dist, dt)) return; // 차례가 없으면 거리를 두고 옆걸음으로 맴돎

        if (dist <= data.AttackRange)
        {
            moveSpeed = 0f;
            if (localTime >= attackReadyTime && TryTakeToken())
            {
                if (IsGuardian) LowerGuardForAttack(); // 예고 동안은 가드를 내림 (이때 □로 끊을 수 있음)
                if (TryStartGrab()) return; // 보스: 일정 확률로 잡기
                SetState(State.Windup); // 적 시계 기준
            }
            return;
        }

        moveSpeed = dist > data.RunDistance && !GuardReady ? data.RunSpeed : data.WalkSpeed; // 가드 중엔 걷기만
    }

    void UpdateWindup(float dt)
    {
        moveSpeed = 0f;
        FaceTarget(dt, 1f);
        if (stateTime >= data.WindupTime) StartAttack(data.FirstAttack);
    }

    void StartAttack(AttackData attack)
    {
        if (attack == null)
        {
            EndAttack();
            return;
        }

        if (swingActive) EndSwing();
        currentAttack = attack;
        stepTraveled = 0f;
        SetState(State.Attack);
        OnBossAttackStarted(attack); // 보스: 연타 끊기·지연 베기 결정
        animator.CrossFadeInFixedTime(attack.StateHash, 0.08f, 0, 0f);
    }

    // 판정·다음 타 전환 타이밍은 플레이어와 같은 AttackData 정규화 시간을 그대로 사용
    void UpdateAttack(float dt)
    {
        moveSpeed = 0f;
        if (UpdateBossHold(dt)) return; // 지연 베기: 칼을 든 채 멈춤
        float t = stateTime / currentAttack.Duration;

        // 판정 전까지만 살짝 추적해서, 휘두르기 시작한 뒤에는 피할 수 있게 함
        if (t < currentAttack.HitStart) FaceTarget(dt, 0.3f);
        ApplyStep(t);

        bool inHitWindow = t >= currentAttack.HitStart && t <= currentAttack.HitEnd;
        if (inHitWindow && !swingActive) BeginSwing();
        else if (!inHitWindow && swingActive) EndSwing();

        if (t >= currentAttack.ComboTransition && WillChain()) // 이어치기 조건을 WillChain으로 통일 (간파 미리보기와 같은 조건)
        {
            StartAttack(currentAttack.GetNext(AttackInput.Light));
            return;
        }

        if (t >= currentAttack.RecoveryEnd) EndAttack();
    }

    void EndAttack()
    {
        if (swingActive) EndSwing();
        bool wasCharge = currentAttack != null && currentAttack == data.ChargeAttack;
        currentAttack = null;
        attackReadyTime = localTime + data.AttackCooldown;
        if (wasCharge)
        {
            Exhaust();
            return;
        }
        PlayMoveState(0.2f); // 방패병은 가드 자세로 돌아감
        SetState(State.Chase);
    }

    void UpdateStun()
    {
        moveSpeed = 0f;
        if (stateTime < stunDuration) return;

        exhausted = false;
        PlayMoveState(0.15f);
        SetState(State.Chase);
    }

    void UpdateDead()
    {
        UpdateDeadFall(); // 공중에서 죽으면 바닥까지 떨어진 뒤 충돌을 끔
        if (respawnEnabled && stateTime >= data.RespawnTime) Respawn();
    }

    public HitResult ReceiveDamage(DamageInfo info)
    {
        if (state == State.Dead) return HitResult.Ignored;
        if (TryBlock(info)) return HitResult.Blocked; // 방패병 정면 가드 (붕괴 조건이면 가드를 깨고 피해는 그대로)
        if (IsBoss) return BossReceiveDamage(info); // 보스는 경직 없이 체간만 쌓임

        DamageEvents.Raise(transform.position + Vector3.up * 2f, info); // 데미지 숫자
        health -= info.Damage;

        if (info.InstantKill || health <= 0f)
        {
            Flash(hitColor);
            Die();
            return HitResult.Hit;
        }

        if (state == State.Executed) return HitResult.Hit; // 처형 중에는 다른 피해로 끊기지 않음
        if (HandleAirHit(info)) return HitResult.Hit; // 떠 있으면 떠받침·내리꽂힘, 쓰러져 있으면 피해만

        // 휘두르는 중에는 경직 없이 맞기만 함 (토글로 비교)
        if (((data.ArmorDuringSwing && IsArmored()) || SparArmored) && !info.BreakArmor) // [변경] 수련 상대는 예고부터 아머 (간파·패링 연습용)
        {
            Flash(armorColor);
            Flinch(info.Direction, info.Weight != AttackWeight.Light); // 경직 없이 상체만 움찔
            Log($"아머 피격 {info.Damage} (체력 {health})");
            return HitResult.Hit;
        }

        Flash(hitColor);
        if (TryAirReaction(info)) return HitResult.Hit; // 띄우기 → 떠오름, 내려찍기 충격파 → 넘어짐

        bool heavy = exhausted || info.Weight != AttackWeight.Light || info.Damage >= data.HeavyDamageThreshold; // 돌격 뒤 탈진 중엔 항상 강경직
        EnterStun(heavy, info.Direction);
        return HitResult.Hit;
    }

    // 공격 상태에서 이번 타 판정이 끝나기 전이거나 다음 타로 이어질 예정이면 아머 (예고 단계는 제외)
    bool IsArmored()
    {
        if (swingActive || state == State.Charge) return true; // 돌진 중에는 끊기지 않음 (예고 중엔 끊김)
        if (!data.ArmorWholeSwing || state != State.Attack || currentAttack == null) return false;
        return stateTime <= currentAttack.HitEnd * currentAttack.Duration || WillChain();
    }

    // 경직 시간을 직접 받을 수 있게 하고, 이미 경직 중이면 남은 시간보다 짧아지지 않게 함
    void EnterStun(bool heavy, Vector3 hitDirection, float overrideDuration = -1f)
    {
        if (swingActive) EndSwing();
        currentAttack = null;

        float duration = overrideDuration > 0f ? overrideDuration : heavy ? data.HeavyStunTime : data.LightStunTime;
        float remaining = state == State.Stun ? stunDuration - stateTime : 0f;
        stunDuration = Mathf.Max(duration, remaining);
        stunHeavy = heavy || (state == State.Stun && stunHeavy); // 강경직 중 약하게 맞아도 흐트러짐 유지
        int dirIndex = HitDirectionIndex(hitDirection);
        if (localTime >= animLockUntil) animator.Play(heavy ? HeavyHitHashes[dirIndex] : LightHitHashes[dirIndex], 0, 0f); // 가드 붕괴 모션은 끊지 않음

        Vector3 push = Flat(hitDirection);
        if (push.sqrMagnitude > 0.0001f)
        {
            float distance = heavy ? data.HeavyKnockback : data.LightKnockback;
            knockbackVelocity = push.normalized * (distance / knockbackTime);
            knockbackTimer = knockbackTime;
        }

        // 경직이 풀리자마자 바로 휘두르지 않도록 최소 예고 시간을 다시 보장
        attackReadyTime = Mathf.Max(attackReadyTime, localTime + stunDuration);
        SetState(State.Stun);
        Log($"{(heavy ? "강" : "약")} 경직 {stunDuration:F2}초 (체력 {health})");
    }

    void Die()
    {
        if (swingActive) EndSwing();
        currentAttack = null;
        knockbackTimer = 0f;
        moveSpeed = 0f;

        deadFalling = state == State.Airborne;
        if (!deadFalling) controller.enabled = false;
        BossOnDie();
        if (!executed) animator.CrossFadeInFixedTime(DeathHash, 0.05f, 0, 0f); // 처형 짝 모션에 쓰러지는 동작이 들어 있음
        SetState(State.Dead);
        Log("사망");
        Died?.Invoke(this);
        if (!respawnEnabled) AnyKilled?.Invoke(this);
        if (temporary) StartCoroutine(SinkAndDestroy());
    }

    void Respawn()
    {
        transform.SetPositionAndRotation(homePosition, homeRotation);
        controller.enabled = true;
        health = data.MaxHealth * healthScale;
        verticalVelocity = 0f;
        attackReadyTime = localTime + 1f;
        exhausted = false;
        executed = false;
        executableUntil = -1f;
        ResetAirState();
        InitGuard();
        InitBoss();

        animator.Play(LocomotionHash, 0, 0f);
        moveAnimGuard = false; // 방패병이면 다음 프레임 UpdateGuard가 가드 자세로 돌림
        SetState(State.Idle);
    }

    void BeginSwing()
    {
        swingActive = true;
        if (IsWaveAttack(currentAttack)) // 보스 검기: 칼 판정 대신 날아가는 참격
        {
            FireWave();
            return;
        }
        if (weapon == null) return;

        weapon.BeginSwing(new DamageInfo
        {
            Attacker = gameObject,
            Damage = currentAttack.Damage * damageScale,
            PostureDamage = currentAttack.PostureDamage,
            Parryable = currentAttack.Parryable,
            Weight = currentAttack.Weight, // 플레이어 피격·사망 모션 구분용
            KnockDown = currentAttack.KnockDown
        });
    }

    void EndSwing()
    {
        swingActive = false;
        if (weapon != null) weapon.EndSwing();
    }

    void ApplyStep(float t)
    {
        if (currentAttack.StepEnd <= 0f || currentAttack.StepDistance <= 0f) return;

        float p = Mathf.Clamp01(t / currentAttack.StepEnd);
        float eased = 1f - (1f - p) * (1f - p);
        float targetDistance = currentAttack.StepDistance * eased;

        float delta = targetDistance - stepTraveled;
        if (delta <= 0f) return;

        stepTraveled = targetDistance;
        if (controller.enabled) controller.Move(transform.forward * delta);
    }

    void Move(float dt)
    {
        if (!controller.enabled) return;

        if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
        verticalVelocity += gravity * AirGravityFactor * dt; // 떠 있는 동안 느리게 떨어짐

        Vector3 velocity = transform.forward * moveSpeed + strafeVelocity + Vector3.up * verticalVelocity; // 맴돌기 옆걸음
        if (knockbackTimer > 0f)
        {
            velocity += knockbackVelocity;
            knockbackTimer -= dt;
        }

        controller.Move(velocity * dt);
    }

    void UpdateAnimator(float dt)
    {
        if (animator == null) return;

        float blend = moveSpeed <= data.WalkSpeed
            ? moveSpeed / Mathf.Max(data.WalkSpeed, 0.01f)
            : 1f + Mathf.Clamp01((moveSpeed - data.WalkSpeed) / Mathf.Max(data.RunSpeed - data.WalkSpeed, 0.01f));
        animator.SetFloat(MoveBlendHash, blend, 0.1f, dt);
    }

    // 예고 동작 동안 몸이 점점 밝아짐 → 피격 시 잠깐 붉게
    void UpdateColor(float dt)
    {
        if (hitFlashTimer > 0f)
        {
            hitFlashTimer -= dt;
            ApplyColor(flashColor);
            return;
        }
        if (BossColor(out Color bossColor)) // 보스 예고 색 (잡기 붉게 · 도약 주황 · 포효 · 체간 붕괴)
        {
            ApplyColor(bossColor);
            return;
        }

        // 돌격병 예고는 옵션과 상관없이 항상 발광 (먼 거리에서 오는 공격이라 읽혀야 함), 탈진은 칙칙한 색
        if (state == State.ChargeWindup || state == State.Charge)
        {
            float k = state == State.Charge ? 1f : Mathf.Clamp01(stateTime / Mathf.Max(data.ChargeWindup, 0.01f));
            ApplyColor(Color.Lerp(BaseTint, windupColor, k * k));
            return;
        }
        if (exhausted)
        {
            ApplyColor(exhaustColor);
            return;
        }

        if (state == State.Windup && showWindupTell)
        {
            float k = Mathf.Clamp01(stateTime / Mathf.Max(data.WindupTime, 0.01f));
            ApplyColor(Color.Lerp(BaseTint, windupColor, k * k));
            return;
        }

        // B: 다음 타로 넘어가기 직전 comboTellTime 동안 첫 타 예고와 같은 주황으로 차오름
        float untilChain = showComboTell ? TimeUntilChain() : -1f;
        if (untilChain >= 0f && untilChain <= comboTellTime)
        {
            float k = 1f - untilChain / Mathf.Max(comboTellTime, 0.01f);
            ApplyColor(Color.Lerp(BaseTint, windupColor, k * k));
            return;
        }

        ApplyColor(BaseTint); // 보스 2페이즈는 붉은 몸
    }

    // 성능: 같은 색이면 건너뜀 (예전엔 적마다 매 프레임 PropertyBlock을 다시 넣었음)
    Color appliedColor = new Color(-1f, -1f, -1f, -1f);

    void ApplyColor(Color color)
    {
        if (color == appliedColor) return;
        appliedColor = color;
        foreach (Renderer r in bodyRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            r.SetPropertyBlock(block);
        }
    }

    // 피격·간파 플래시 색을 구분
    void Flash(Color color)
    {
        flashColor = color;
        hitFlashTimer = hitFlashTime;
    }

    // ===== IAttackThreat: 저스트 회피·간파 판정 (여기부터 ReceiveParry까지 새로 추가) =====

    public bool IsParryable
    {
        get
        {
            AttackData attack = UpcomingAttack(); // 1타 판정이 끝났으면 다음 타 기준
            return attack != null && attack.Parryable;
        }
    }

    // 판정 시작 earlyWindow초 전 ~ 판정 시작 후 lateWindow초(모두 실제 시간) 사이에 이 위치를 노리는 공격이 있으면 true
    public bool IsAboutToHit(Vector3 targetPosition, float earlyWindow, float lateWindow)
    {
        if (data == null || state == State.Dead) return false;

        // 적 시간이 느려져 있으면 실제로 걸리는 시간은 그만큼 길어짐
        float toReal = 1f / Mathf.Max(CombatTime.EnemyScale, 0.01f);

        float untilHit = TimeUntilHit();
        if (untilHit < 0f) return false;
        if (untilHit * toReal > earlyWindow) return false;

        // 이미 판정 중이면 판정 시작 후 얼마나 지났는지로 늦은 입력을 거름
        if (untilHit <= 0f && TimeSinceHitStart() * toReal > lateWindow) return false;

        Vector3 to = Flat(targetPosition - transform.position);
        if (IsBossRanged) return (Flat(targetPosition - leapTo).magnitude <= data.LeapRadius + threatRangeMargin); // 도약은 착지 범위로 판정
        if (!IsCharging && to.magnitude > data.AttackRange + threatRangeMargin) return false; // 돌진은 멀리서 오므로 거리 조건 제외
        return Vector3.Angle(transform.forward, to) <= threatAngle;
    }

    // 간파 v2용: 적 시간을 실제 시간으로 환산
    public float SecondsUntilHit()
    {
        if (data == null || state == State.Dead) return -1f;
        float untilHit = TimeUntilHit();
        if (untilHit < 0f) return -1f;
        return untilHit / Mathf.Max(CombatTime.EnemyScale, 0.01f);
    }

    // 현재 타의 판정이 켜진 뒤 지난 적 시간(초). 판정 전이면 0
    float TimeSinceHitStart()
    {
        if (state != State.Attack || currentAttack == null) return 0f;
        return Mathf.Max(0f, stateTime - currentAttack.HitStart * currentAttack.Duration);
    }

    // 다음 판정이 켜지기까지 남은 적 시간(초). 0이면 지금 판정 중, -1이면 들어올 공격 없음
    float TimeUntilHit()
    {
        float untilCharge = ChargeTimeUntilHit();
        if (untilCharge >= 0f) return untilCharge;
        float untilBoss = BossTimeUntilHit();
        if (untilBoss >= 0f) return untilBoss;

        if (state == State.Windup && data.FirstAttack != null)
            return (data.WindupTime - stateTime) + data.FirstAttack.HitStart * data.FirstAttack.Duration;

        if (state == State.Attack && currentAttack != null)
        {
            float hitStartTime = currentAttack.HitStart * currentAttack.Duration;
            float hitEndTime = currentAttack.HitEnd * currentAttack.Duration;
            if (stateTime <= hitEndTime) return Mathf.Max(0f, hitStartTime - stateTime) + BossHoldLeft(); // 멈칫하는 동안은 그만큼 늦게 맞음

            // A: 이번 타 판정이 끝났어도 다음 타가 이어질 예정이면 "전환까지 + 다음 타 판정 시작"까지를 돌려줌
            //        → 판정이 클립 맨 앞에 있는 2타도 직전 간파 구간이 생김
            float untilChain = TimeUntilChain();
            if (untilChain >= 0f)
            {
                AttackData next = currentAttack.GetNext(AttackInput.Light);
                return untilChain + next.HitStart * next.Duration + BossPendingDelay();
            }
        }

        return -1f;
    }

    // 다음 타로 넘어가기까지 남은 적 시간(초). 이어질 다음 타가 없으면 -1
    float TimeUntilChain()
    {
        if (state != State.Attack || currentAttack == null || !WillChain()) return -1f;

        float hitEndTime = currentAttack.HitEnd * currentAttack.Duration;
        if (stateTime <= hitEndTime) return -1f;

        return Mathf.Max(0f, currentAttack.ComboTransition * currentAttack.Duration - stateTime);
    }

    // 연타를 이어갈 조건 (UpdateAttack과 간파 판정이 같이 사용)
    bool WillChain() =>
        currentAttack != null && currentAttack.GetNext(AttackInput.Light) != null
        && (DistanceToTarget() <= data.AttackRange * 1.5f || IsWaveAttack(currentAttack)) // 검기 연속은 거리와 상관없이
        && !BossStopsCombo();

    // 간파 대상이 될 공격: 예고 중이면 첫 타, 다음 타 대기 중이면 다음 타, 아니면 현재 타
    AttackData UpcomingAttack()
    {
        if (data == null) return null;
        if (IsCharging) return data.ChargeAttack;
        AttackData bossAttack = BossUpcomingAttack(); // 잡기·도약은 패링 불가 데이터
        if (bossAttack != null) return bossAttack;
        if (state == State.Windup || currentAttack == null) return data.FirstAttack;
        return TimeUntilChain() >= 0f ? currentAttack.GetNext(AttackInput.Light) : currentAttack;
    }

    public HitResult ReceiveParry(GameObject parrier)
    {
        if (state == State.Dead || !IsParryable) return HitResult.Ignored;
        if (IsBoss) return BossReceiveParry(parrier);

        Vector3 dir = Flat(transform.position - parrier.transform.position);
        EnterStun(true, dir.normalized, data.ParryStunTime);
        if (IsGuardian) BreakGuard(dir.normalized); // 방패병 공격을 간파·패링하면 가드까지 무너짐
        OpenExecution(data.ParryStunTime); // 패링 뒤 처형 가능 (간파는 PlayerKanpa가 바로 닫음 → 뇌락으로)
        Flash(parryColor);
        Log("간파당함");
        return HitResult.Countered;
    }

    void FaceTarget(float dt, float speedScale) => TurnToward(dt, data.TurnSpeed * speedScale);

    void TurnToward(float dt, float degreesPerSecond)
    {
        Vector3 to = Flat(target.position - transform.position);
        if (to.sqrMagnitude < 0.0001f) return;

        Quaternion want = Quaternion.LookRotation(to.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, want, degreesPerSecond * dt);
    }

    // 공격이 들어온 방향(공격자 → 나)을 몸 기준 앞·뒤·왼·오로 분류
    int HitDirectionIndex(Vector3 hitDirection)
    {
        Vector3 from = -Flat(hitDirection);
        if (from.sqrMagnitude < 0.0001f) return 0;

        float forward = Vector3.Dot(transform.forward, from.normalized);
        if (forward >= 0.5f) return 0;
        if (forward <= -0.5f) return 1;
        return Vector3.Dot(transform.right, from) < 0f ? 2 : 3;
    }

    float DistanceToTarget() => Flat(target.position - transform.position).magnitude;

    void SetState(State next)
    {
        if (logState && next != state) Log($"{state} → {next}");
        state = next;
        stateTime = 0f;
        OnTokenStateChanged(next); // 공격이 끝나면 공격 차례를 반납
    }

    void Log(string message)
    {
        if (logState) Debug.Log($"[Swordsman] {name} · {message}", this);
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
