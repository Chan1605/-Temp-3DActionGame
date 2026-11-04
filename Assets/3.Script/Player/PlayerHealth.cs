using UnityEngine;

public class PlayerHealth : MonoBehaviour, IDamageable
{
    static readonly int HitHash = Animator.StringToHash("Hit");
    static readonly int LocomotionHash = Animator.StringToHash("Locomotion");

    [SerializeField] float maxHealth = 100f;
    [SerializeField] float stunTime = 0.35f;
    [SerializeField] float invulnerableTime = 0.6f;

    [SerializeField] Animator animator;
    [SerializeField] PlayerCombat combat;
    [SerializeField] PlayerLocomotion locomotion;
    [SerializeField] PlayerSkill skill;
    [SerializeField] PlayerDodge dodge; // 회피 무적 판정
    [SerializeField] PlayerParry parry; // 간파 반격 중 무적
    [SerializeField] ThirdPersonCamera followCamera;

    float health;
    float stunTimer;
    float invulnerableTimer;
    bool dead;

    public float Health => health;
    public float MaxHealth => maxHealth;
    public bool IsStunned => stunTimer > 0f;
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
        if (followCamera == null) followCamera = FindAnyObjectByType<ThirdPersonCamera>();
        health = maxHealth;
    }

    void Update()
    {
        if (invulnerableTimer > 0f) invulnerableTimer -= Time.deltaTime;
        if (stunTimer <= 0f) return;

        stunTimer -= Time.deltaTime;
        if (stunTimer > 0f) return;

        // 경직 도중 스킬이 시작됐다면 해제·애니메이션 복귀는 스킬 쪽에 맡김
        if (skill != null && skill.IsActive) return;

        if (combat != null) combat.InputLocked = false;
        if (locomotion != null) locomotion.MovementLocked = false;
        animator.CrossFadeInFixedTime(LocomotionHash, 0.15f, 0, 0f);
    }

    public HitResult ReceiveDamage(DamageInfo info)
    {
        if (dead) return HitResult.Ignored;
        // 프로토타입: 스킬 사용 중에는 피격을 무시 (스킬 중 피격 처리는 아직 미정)
        if (skill != null && skill.IsActive) return HitResult.Ignored;
        if (invulnerableTimer > 0f) return HitResult.Ignored;

        // 회피 무적 구간이면 무시, 무적이 끝난 회피 후반에 맞으면 회피를 끊고 피격 처리
        if (parry != null && parry.IsInvulnerable) return HitResult.Ignored;

        // [변경] 가드 제거 (방어는 회피·간파 두 갈래)

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

        animator.Play(HitHash, 0, 0f);
        if (followCamera != null) followCamera.Shake(0.08f, 0.12f);

        stunTimer = stunTime;
        invulnerableTimer = invulnerableTime;
        Damaged?.Invoke(info);
        if (health <= 0f) Die(); // 피격 모션·잠금은 그대로 두고 경직만 무한으로
        return HitResult.Hit;
    }

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
    void Die()
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

        Debug.Log("[Player] 사망");
        Died?.Invoke();
    }

    public void Revive(float invulnerable = 1.5f)
    {
        dead = false;
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
