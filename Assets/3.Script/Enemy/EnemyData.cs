using UnityEngine;

[CreateAssetMenu(menuName = "Combat/Enemy Data", fileName = "EnemyData")]
public class EnemyData : ScriptableObject
{
    [Header("Stats")]
    [SerializeField] float maxHealth = 60f;

    [Header("Move")]
    [SerializeField] float detectRange = 15f;
    [SerializeField] float attackRange = 2.2f;
    [SerializeField] float walkSpeed = 1.6f;
    [SerializeField] float runSpeed = 4f;
    [SerializeField] float runDistance = 5f;
    [SerializeField] float turnSpeed = 540f;

    // windupTime = 공격 예고(몸이 밝아짐) 시간, 플레이어가 보고 반응할 수 있는 구간
    [Header("Attack")]
    [SerializeField] AttackData firstAttack;
    [SerializeField] float windupTime = 0.5f;
    [SerializeField] float attackCooldown = 1.5f;
    [SerializeField] bool armorDuringSwing = true;

    [Header("Hit Reaction")]
    [SerializeField] float lightStunTime = 0.3f;
    [SerializeField] float heavyStunTime = 0.6f;
    [SerializeField] float heavyDamageThreshold = 18f;
    [SerializeField] float lightKnockback = 0.15f;
    [SerializeField] float heavyKnockback = 0.6f;
    [SerializeField] float parryStunTime = 1.2f; // [변경] 간파당했을 때 경직

    [Header("Death")]
    [SerializeField] float respawnTime = 3f;

    public float MaxHealth => maxHealth;

    public float DetectRange => detectRange;
    public float AttackRange => attackRange;
    public float WalkSpeed => walkSpeed;
    public float RunSpeed => runSpeed;
    public float RunDistance => runDistance;
    public float TurnSpeed => turnSpeed;

    public AttackData FirstAttack => firstAttack;
    public float WindupTime => windupTime;
    public float AttackCooldown => attackCooldown;
    public bool ArmorDuringSwing => armorDuringSwing;

    public float LightStunTime => lightStunTime;
    public float HeavyStunTime => heavyStunTime;
    public float HeavyDamageThreshold => heavyDamageThreshold;
    public float LightKnockback => lightKnockback;
    public float HeavyKnockback => heavyKnockback;
    public float ParryStunTime => parryStunTime; // [변경]

    public float RespawnTime => respawnTime;
}
