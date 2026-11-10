using UnityEngine;

public class PlayerHealth : MonoBehaviour, IDamageable
{
    static readonly int HitHash = Animator.StringToHash("Hit");
    static readonly int LocomotionHash = Animator.StringToHash("Locomotion");
    static readonly int DeathKnockDownHash = Animator.StringToHash("Death_KnockDown");
    static readonly int DeathCombatHash = Animator.StringToHash("Death_Combat");
    // 피격 반응 (순서: 앞, 뒤, 왼쪽, 오른쪽에서 맞음). 상태가 없으면 기존 Hit으로
    static readonly int[] HitHashes = Hashes("Hit_F", "Hit_B", "Hit_L", "Hit_R");
    static readonly int[] HeavyHitHashes = Hashes("HitLarge_F", "HitLarge_B", "HitLarge_L", "HitLarge_R");
    static readonly int[] AirHitHashes = Hashes("HitAir_F", "HitAir_B", "HitAir_L", "HitAir_R");
    static readonly int KnockDownHash = Animator.StringToHash("KnockDown_Start");
    static readonly int GetUpHash = Animator.StringToHash("Get_Up");
    static readonly int GrabbedHash = Animator.StringToHash("Grabbed"); // 보스 잡기 짝 모션 (처형 당하는 쪽 클립)
    static readonly int[] GrabbedHashes = // 잡기 3종 (보스 Boss_Grab_N과 짝)
    {
        Animator.StringToHash("Grabbed_1"), Animator.StringToHash("Grabbed_2"), Animator.StringToHash("Grabbed_3")
    };

    [SerializeField] float maxHealth = 100f;
    [SerializeField] float stunTime = 0.35f;
    [SerializeField] float invulnerableTime = 0.6f;
    [SerializeField] float knockDownDamage = 20f; // 마지막 일격이 강공격이거나 이 피해 이상이면 넉다운 사망, 아니면 전투 사망

    // 일반 = 맞은 방향 피격, 강공격(또는 heavyHitDamage 이상) = 크게 휘청, 넉다운 공격 = 쓰러졌다 기상 (그동안 무적)
    [Header("Hit Reaction")]
    [SerializeField] float heavyStunTime = 0.6f;
    [SerializeField] float heavyHitDamage = 15f;
    [SerializeField] float knockDownTime = 1.1f; // 쓰러져 있는 시간
    [SerializeField] float getUpTime = 0.8f;
    [SerializeField] float dodgeOutAfter = 0.15f; // 일반·강 피격 경직이 이만큼 지나면 회피로 빠져나올 수 있음 (넉다운·잡힘은 불가)

    [SerializeField] Animator animator;
    [SerializeField] PlayerCombat combat;
    [SerializeField] PlayerLocomotion locomotion;
    [SerializeField] PlayerSkill skill;
    [SerializeField] PlayerDodge dodge; // 회피 무적 판정
    [SerializeField] PlayerParry parry; // 패링 반격 중 무적
    [SerializeField] PlayerKanpa kanpa; // △ 초반에 맞으면 간파
    [SerializeField] ThirdPersonCamera followCamera;
    [SerializeField] PlayerExecution execution; // 처형 중 무적 · 다른 행동 막기
    [SerializeField] PlayerChargeAttack charge; // 섬격 중 무적

    float health;
    float stunTimer;
    float stunStartedAt = -1f;
    float invulnerableTimer;
    bool dead;
    bool knockedDown;
    bool grabbed; // 보스에게 잡힘: 모든 입력·피격을 막고 보스가 놓을 때까지 짝 모션

    public float Health => health;
    public float MaxHealth => maxHealth;
    public bool IsStunned => stunTimer > 0f || grabbed || (execution != null && execution.IsActive); // 회피·패링·차지가 이 값으로 막힘 // 잡힘 포함
    public bool IsGrabbed => grabbed;
    // 연타를 끝까지 다 맞지 않게: 경직 후반에는 회피로 끊을 수 있음
    public bool CanDodgeOutOfStun => !dead && !grabbed && !knockedDown && stunTimer > 0f && !float.IsInfinity(stunTimer)
        && (execution == null || !execution.IsActive) && Time.time - stunStartedAt >= dodgeOutAfter;
    public bool CanBeTargeted => !dead;
    public bool IsDead => dead;
    public event System.Action<DamageInfo> Damaged; // 피격 이펙트용
    // 구독자(ZoneArena)가 있으면 사망 → 구역 입구에서 부활, 없으면 예전처럼 테스트용 체력 회복
    public event System.Action Died;

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (combat == null) combat = GetComponent<PlayerCombat>();
        if (locomotion == null) locomotion = GetComponent<PlayerLocomotion>();
        if (skill == null) skill = GetComponent<PlayerSkill>();
        if (dodge == null) dodge = GetComponent<PlayerDodge>();
        if (parry == null) parry = GetComponent<PlayerParry>();
        if (kanpa == null) kanpa = GetComponent<PlayerKanpa>();
        if (execution == null) execution = GetComponent<PlayerExecution>();
        if (charge == null) charge = GetComponent<PlayerChargeAttack>();
        if (followCamera == null) followCamera = FindAnyObjectByType<ThirdPersonCamera>();
        health = maxHealth;
    }

    // 처형이 시작되면 맞고 있던 경직을 지움 (경직이 끝나며 로코모션으로 되돌리는 것 방지)
    public void ClearStun()
    {
        if (dead) return;
        stunTimer = 0f;
        knockedDown = false;
    }

    void Update()
    {
        if (invulnerableTimer > 0f) invulnerableTimer -= Time.deltaTime;
        if (grabbed) return;
        if (execution != null && execution.IsActive) return;
        if (stunTimer <= 0f) return;

        stunTimer -= Time.deltaTime;
        if (stunTimer > 0f) return;

        // 경직 도중 스킬이 시작됐다면 해제·애니메이션 복귀는 스킬 쪽에 맡김
        if (skill != null && skill.IsActive) return;

        // 쓰러진 시간이 끝나면 기상 모션 동안 한 번 더 경직
        if (knockedDown)
        {
            knockedDown = false;
            stunTimer = getUpTime;
            animator.CrossFadeInFixedTime(animator.HasState(0, GetUpHash) ? GetUpHash : LocomotionHash, 0.1f, 0, 0f);
            return;
        }

        if (combat != null) combat.InputLocked = false;
        if (locomotion != null) locomotion.MovementLocked = false;
        animator.CrossFadeInFixedTime(LocomotionHash, 0.15f, 0, 0f);
    }

    public HitResult ReceiveDamage(DamageInfo info)
    {
        if (dead || grabbed) return HitResult.Ignored; // 잡힌 동안은 잡기 피해만 (GrabHit)
        // 프로토타입: 스킬 사용 중에는 피격을 무시 (스킬 중 피격 처리는 아직 미정)
        if (skill != null && skill.IsActive) return HitResult.Ignored;
        if (invulnerableTimer > 0f) return HitResult.Ignored;
        if (execution != null && execution.IsActive) return HitResult.Ignored;
        if (charge != null && charge.IsDashing) return HitResult.Ignored;

        // 회피 무적 구간이면 무시, 무적이 끝난 회피 후반에 맞으면 회피를 끊고 피격 처리
        if (parry != null && parry.IsInvulnerable) return HitResult.Ignored;
        if (kanpa != null && kanpa.IsInvulnerable) return HitResult.Ignored; // 뇌락 착지 전까지 무적
        if (kanpa != null && kanpa.TryCounter(info)) return HitResult.Countered; // 피해 없이 적을 경직

        // 가드 제거 (방어는 회피·간파 두 갈래)

        if (dodge != null && dodge.IsInvulnerable)
        {
            dodge.NotifyAvoidedHit(); // 회피 직후에 흘린 공격이면 저스트 회피로 인정
            Debug.Log("[Player] 회피 성공");
            return HitResult.Ignored;
        }
        if (dodge != null && dodge.IsDodging) dodge.Interrupt();

        TakeDamage(info.Damage); // 가드 깎임 피해와 공용

        if (combat != null)
        {
            combat.CancelAttack();
            combat.InputLocked = true;
        }
        if (locomotion != null) locomotion.MovementLocked = true;

        PlayHitReaction(info);
        Damaged?.Invoke(info);
        if (health <= 0f) Die(info); // 피격 모션 대신 사망 모션
        return HitResult.Hit;
    }

    // ===== 보스 잡기 =====
    // 핵심: 무적(회피·스킬·처형·섬격·패링·뇌락·피격 직후)이면 실패 → 보스는 헛잡음. 성공하면 하던 행동을 끊고 지정 자리에서 짝 모션
    public bool HasGrabbedMotion(int index) => index >= 0 && index < GrabbedHashes.Length && animator.HasState(0, GrabbedHashes[index]);

    public bool TryBeginGrab(GameObject by, Vector3 position, Quaternion rotation, float startTime, float animSpeed, int motionIndex = -1) // 모션 번호 · 시작 시점은 초 단위 (클립 길이가 달라도 보스와 맞물림)
    {
        int grabbedHash = HasGrabbedMotion(motionIndex) ? GrabbedHashes[motionIndex] : GrabbedHash;
        if (dead || grabbed || !animator.HasState(0, grabbedHash)) return false;
        if (dodge != null && dodge.IsInvulnerable)
        {
            dodge.NotifyAvoidedHit(); // 잡기를 흘려도 저스트 회피로 인정
            return false;
        }
        bool invulnerable = invulnerableTimer > 0f || (skill != null && skill.IsActive) || (execution != null && execution.IsActive)
            || (charge != null && charge.IsDashing) || (parry != null && parry.IsInvulnerable) || (kanpa != null && kanpa.IsInvulnerable);
        if (invulnerable) return false;

        grabbed = true;
        stunTimer = 0f;
        knockedDown = false;
        if (dodge != null && dodge.IsDodging) dodge.Interrupt();
        if (parry != null) parry.Interrupt();
        if (charge != null) charge.Cancel();
        if (combat != null)
        {
            combat.CancelAttack();
            combat.InputLocked = true;
        }
        if (locomotion != null) locomotion.MovementLocked = true;
        if (skill != null) skill.enabled = false;

        var cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        if (cc != null) cc.enabled = true;

        animator.speed = animSpeed;
        animator.PlayInFixedTime(grabbedHash, 0, Mathf.Max(0f, startTime));
        if (followCamera != null) followCamera.Shake(0.1f, 0.15f);
        Debug.Log("[Player] 잡힘");
        return true;
    }

    // 보스 칼이 꽂히는 순간: 피해만 (짝 모션은 계속)
    public void GrabHit(DamageInfo info)
    {
        if (!grabbed || dead) return;
        TakeDamage(info.Damage);
        Damaged?.Invoke(info);
        if (followCamera != null) followCamera.Shake(0.22f, 0.25f);
        if (health > 0f) return;

        grabbed = false;
        animator.speed = 1f;
        Die(info, false); // 짝 모션에 쓰러지는 동작이 들어 있음
    }

    // 보스가 놓음 → 쓰러진 채 잠깐 → 기상 (그동안 무적)
    public void ReleaseGrab()
    {
        if (!grabbed) return;
        grabbed = false;
        animator.speed = 1f;
        if (dead) return;

        if (skill != null) skill.enabled = true;
        knockedDown = true;
        stunTimer = 0.35f;
        invulnerableTimer = stunTimer + getUpTime + 0.3f;
    }

    // 핵심: 넉다운 > 공중 > 강공격 > 일반 순으로 반응을 고르고, 경직·무적 시간도 반응에 맞춤
    void PlayHitReaction(DamageInfo info)
    {
        knockedDown = false;
        if (info.KnockDown)
        {
            FaceAttacker(info); // 공격한 쪽을 보고 뒤로 넘어짐
            knockedDown = true;
            PlayOr(KnockDownHash, HeavyHitHashes[0]);
            stunTimer = knockDownTime;
            invulnerableTimer = knockDownTime + getUpTime + 0.2f; // 쓰러진 채 계속 맞지 않게
            if (followCamera != null) followCamera.Shake(0.16f, 0.2f);
            return;
        }

        int dir = HitDirectionIndex(info);
        bool air = locomotion != null && !locomotion.IsGrounded;
        bool heavy = info.Weight != AttackWeight.Light || info.Damage >= heavyHitDamage;

        PlayOr((air ? AirHitHashes : heavy ? HeavyHitHashes : HitHashes)[dir], HitHash);
        stunTimer = heavy ? heavyStunTime : stunTime;
        stunStartedAt = Time.time;
        invulnerableTimer = Mathf.Max(invulnerableTime, stunTimer);
        if (followCamera != null) followCamera.Shake(heavy ? 0.13f : 0.08f, heavy ? 0.16f : 0.12f);
    }

    void PlayOr(int hash, int fallback)
    {
        if (!animator.HasState(0, hash)) hash = animator.HasState(0, fallback) ? fallback : HitHash;
        animator.Play(hash, 0, 0f);
    }

    // 공격한 쪽(없으면 피해 방향의 반대)이 몸 기준 앞·뒤·왼·오 중 어디인지
    int HitDirectionIndex(DamageInfo info)
    {
        Vector3 from = info.Attacker != null ? info.Attacker.transform.position - transform.position : -info.Direction;
        from.y = 0f;
        if (from.sqrMagnitude < 0.0001f) return 0;
        from.Normalize();

        float forward = Vector3.Dot(transform.forward, from);
        if (forward >= 0.5f) return 0;
        if (forward <= -0.5f) return 1;
        return Vector3.Dot(transform.right, from) < 0f ? 2 : 3;
    }

    void FaceAttacker(DamageInfo info)
    {
        if (info.Attacker == null) return;
        Vector3 to = info.Attacker.transform.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
    }

    static int[] Hashes(params string[] names) => System.Array.ConvertAll(names, Animator.StringToHash);

    void TakeDamage(float amount)
    {
        health -= amount;
        Debug.Log($"[Player] 피해 {amount:0.#} (체력 {Mathf.Max(health, 0f):0.#}/{maxHealth})");

        if (health <= 0f && Died == null)
        {
            Debug.Log("[Player] 사망 → 테스트용으로 체력 회복");
            health = maxHealth;
        }
    }

    // ===== 사망·부활 =====
    // 사망: 모든 입력을 막고 경직을 끝나지 않게 둠 (Q 스킬은 경직을 확인하지 않아 컴포넌트를 끔). 부활은 ZoneArena가 호출
    void Die(DamageInfo killingBlow, bool playAnimation = true) // 잡혀서 죽으면 짝 모션 그대로
    {
        dead = true;
        health = 0f;
        stunTimer = float.PositiveInfinity;

        if (combat != null)
        {
            combat.CancelAttack();
            combat.InputLocked = true;
        }
        if (locomotion != null) locomotion.MovementLocked = true;
        if (dodge != null && dodge.IsDodging) dodge.Interrupt();
        if (skill != null) skill.enabled = false;
        if (playAnimation) PlayDeath(killingBlow);

        Debug.Log("[Player] 사망");
        Died?.Invoke();
    }

    // 공격한 쪽을 바라본 채 뒤로 쓰러지게 몸을 돌리고, 강공격이면 넉다운 / 아니면 전투 사망 모션
    void PlayDeath(DamageInfo info)
    {
        knockedDown = false;
        FaceAttacker(info);

        bool heavy = info.Weight != AttackWeight.Light || info.Damage >= knockDownDamage;
        int hash = heavy ? DeathKnockDownHash : DeathCombatHash;
        if (!animator.HasState(0, hash)) hash = heavy ? DeathCombatHash : DeathKnockDownHash; // 하나만 있으면 그걸로
        if (animator.HasState(0, hash)) animator.CrossFadeInFixedTime(hash, 0.08f, 0, 0f);
    }

    // [변경] 수련(체력 단련): 최대 체력을 늘리고 늘어난 만큼 바로 채움
    public void AddMaxHealth(float amount)
    {
        maxHealth += amount;
        if (!dead) health = Mathf.Min(maxHealth, health + amount);
    }

    // 화톳불 휴식: 살아 있으면 체력을 가득
    public void HealFull()
    {
        if (dead) return;
        health = maxHealth;
    }

    public void Revive(float invulnerable = 1.5f)
    {
        dead = false;
        knockedDown = false;
        grabbed = false;
        animator.speed = 1f;
        health = maxHealth;
        stunTimer = 0f;
        invulnerableTimer = invulnerable;

        if (skill != null) skill.enabled = true;
        if (combat != null) combat.InputLocked = false;
        if (locomotion != null) locomotion.MovementLocked = false;
        animator.Play(LocomotionHash, 0, 0f);
        Debug.Log("[Player] 부활");
    }
}
