using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class EnemySwordsman : MonoBehaviour, IDamageable, IAttackThreat // 저스트 회피·간파 판정용 인터페이스
{
    enum State { Idle, Chase, Windup, Attack, Stun, Dead }

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

        health = data != null ? data.MaxHealth : 1f;
        ApplyColor(bodyTint);
    }

    void Update()
    {
        if (data == null || target == null) return;

        // 적은 CombatTime 배속으로 움직임 → 저스트 회피·간파 슬로가 플레이어를 빼고 적용됨
        float scale = CombatTime.EnemyScale;
        float dt = Time.deltaTime * scale;
        if (animator != null) animator.speed = scale;
        localTime += dt;
        stateTime += dt;

        switch (state)
        {
            case State.Idle: UpdateIdle(); break;
            case State.Chase: UpdateChase(dt); break;
            case State.Windup: UpdateWindup(dt); break;
            case State.Attack: UpdateAttack(dt); break;
            case State.Stun: UpdateStun(); break;
            case State.Dead: UpdateDead(); break;
        }

        Move(dt);
        UpdateAnimator(dt);
        UpdateColor(dt);
    }

    void UpdateIdle()
    {
        moveSpeed = 0f;
        if (alerted || DistanceToTarget() <= data.DetectRange) SetState(State.Chase);
    }

    // 사거리 밖이면 다가가고, 사거리 안에서 쿨타임이 끝나면 예고 동작으로 들어감
    void UpdateChase(float dt)
    {
        float dist = DistanceToTarget();
        if (!alerted && dist > data.DetectRange * 1.2f)
        {
            SetState(State.Idle);
            return;
        }

        FaceTarget(dt, 1f);

        if (dist <= data.AttackRange)
        {
            moveSpeed = 0f;
            if (localTime >= attackReadyTime) SetState(State.Windup); // 적 시계 기준
            return;
        }

        moveSpeed = dist > data.RunDistance ? data.RunSpeed : data.WalkSpeed;
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
        animator.CrossFadeInFixedTime(attack.StateHash, 0.08f, 0, 0f);
    }

    // 판정·다음 타 전환 타이밍은 플레이어와 같은 AttackData 정규화 시간을 그대로 사용
    void UpdateAttack(float dt)
    {
        moveSpeed = 0f;
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
        currentAttack = null;
        attackReadyTime = localTime + data.AttackCooldown;
        animator.CrossFadeInFixedTime(LocomotionHash, 0.2f, 0, 0f);
        SetState(State.Chase);
    }

    void UpdateStun()
    {
        moveSpeed = 0f;
        if (stateTime < stunDuration) return;

        animator.CrossFadeInFixedTime(LocomotionHash, 0.15f, 0, 0f);
        SetState(State.Chase);
    }

    void UpdateDead()
    {
        if (respawnEnabled && stateTime >= data.RespawnTime) Respawn();
    }

    public HitResult ReceiveDamage(DamageInfo info)
    {
        if (state == State.Dead) return HitResult.Ignored;

        DamageEvents.Raise(transform.position + Vector3.up * 2f, info); // [변경] 데미지 숫자
        health -= info.Damage;

        if (info.InstantKill || health <= 0f)
        {
            Flash(hitColor);
            Die();
            return HitResult.Hit;
        }

        // 휘두르는 중에는 경직 없이 맞기만 함 (토글로 비교)
        if (data.ArmorDuringSwing && IsArmored()) // 판정 중 → 휘두르는 내내(옵션)
        {
            Flash(armorColor);
            Log($"아머 피격 {info.Damage} (체력 {health})");
            return HitResult.Hit;
        }

        Flash(hitColor);

        bool heavy = info.Weight != AttackWeight.Light || info.Damage >= data.HeavyDamageThreshold; // 무게 우선, 피해량은 보조
        EnterStun(heavy, info.Direction);
        return HitResult.Hit;
    }

    // 공격 상태에서 이번 타 판정이 끝나기 전이거나 다음 타로 이어질 예정이면 아머 (예고 단계는 제외)
    bool IsArmored()
    {
        if (swingActive) return true;
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
        int dirIndex = HitDirectionIndex(hitDirection);
        animator.Play(heavy ? HeavyHitHashes[dirIndex] : LightHitHashes[dirIndex], 0, 0f);

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

        controller.enabled = false;
        animator.CrossFadeInFixedTime(DeathHash, 0.05f, 0, 0f);
        SetState(State.Dead);
        Log("사망");
        Died?.Invoke(this);
    }

    void Respawn()
    {
        transform.SetPositionAndRotation(homePosition, homeRotation);
        controller.enabled = true;
        health = data.MaxHealth;
        verticalVelocity = 0f;
        attackReadyTime = localTime + 1f;

        animator.Play(LocomotionHash, 0, 0f);
        SetState(State.Idle);
    }

    void BeginSwing()
    {
        swingActive = true;
        if (weapon == null) return;

        weapon.BeginSwing(new DamageInfo
        {
            Attacker = gameObject,
            Damage = currentAttack.Damage,
            PostureDamage = currentAttack.PostureDamage,
            Parryable = currentAttack.Parryable
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
        verticalVelocity += gravity * dt;

        Vector3 velocity = transform.forward * moveSpeed + Vector3.up * verticalVelocity;
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

        if (state == State.Windup && showWindupTell)
        {
            float k = Mathf.Clamp01(stateTime / Mathf.Max(data.WindupTime, 0.01f));
            ApplyColor(Color.Lerp(bodyTint, windupColor, k * k));
            return;
        }

        // B: 다음 타로 넘어가기 직전 comboTellTime 동안 첫 타 예고와 같은 주황으로 차오름
        float untilChain = showComboTell ? TimeUntilChain() : -1f;
        if (untilChain >= 0f && untilChain <= comboTellTime)
        {
            float k = 1f - untilChain / Mathf.Max(comboTellTime, 0.01f);
            ApplyColor(Color.Lerp(bodyTint, windupColor, k * k));
            return;
        }

        ApplyColor(bodyTint);
    }

    void ApplyColor(Color color)
    {
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
        if (to.magnitude > data.AttackRange + threatRangeMargin) return false;
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
        if (state == State.Windup && data.FirstAttack != null)
            return (data.WindupTime - stateTime) + data.FirstAttack.HitStart * data.FirstAttack.Duration;

        if (state == State.Attack && currentAttack != null)
        {
            float hitStartTime = currentAttack.HitStart * currentAttack.Duration;
            float hitEndTime = currentAttack.HitEnd * currentAttack.Duration;
            if (stateTime <= hitEndTime) return Mathf.Max(0f, hitStartTime - stateTime);

            // A: 이번 타 판정이 끝났어도 다음 타가 이어질 예정이면 "전환까지 + 다음 타 판정 시작"까지를 돌려줌
            //        → 판정이 클립 맨 앞에 있는 2타도 직전 간파 구간이 생김
            float untilChain = TimeUntilChain();
            if (untilChain >= 0f)
            {
                AttackData next = currentAttack.GetNext(AttackInput.Light);
                return untilChain + next.HitStart * next.Duration;
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
        currentAttack != null && currentAttack.GetNext(AttackInput.Light) != null && DistanceToTarget() <= data.AttackRange * 1.5f;

    // 간파 대상이 될 공격: 예고 중이면 첫 타, 다음 타 대기 중이면 다음 타, 아니면 현재 타
    AttackData UpcomingAttack()
    {
        if (data == null) return null;
        if (state == State.Windup || currentAttack == null) return data.FirstAttack;
        return TimeUntilChain() >= 0f ? currentAttack.GetNext(AttackInput.Light) : currentAttack;
    }

    public HitResult ReceiveParry(GameObject parrier)
    {
        if (state == State.Dead || !IsParryable) return HitResult.Ignored;

        Vector3 dir = Flat(transform.position - parrier.transform.position);
        EnterStun(true, dir.normalized, data.ParryStunTime);
        Flash(parryColor);
        Log("간파당함");
        return HitResult.Countered;
    }

    void FaceTarget(float dt, float speedScale)
    {
        Vector3 to = Flat(target.position - transform.position);
        if (to.sqrMagnitude < 0.0001f) return;

        Quaternion want = Quaternion.LookRotation(to.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, want, data.TurnSpeed * speedScale * dt);
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
    }

    void Log(string message)
    {
        if (logState) Debug.Log($"[Swordsman] {name} · {message}", this);
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
