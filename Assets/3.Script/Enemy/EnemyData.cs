using UnityEngine;

// 구역 3 적 종류: 같은 EnemySwordsman 상태 머신에 행동만 추가 (방패병 = 정면 가드, 돌격병 = 원거리 돌진)
public enum EnemyKind { Swordsman, Guardian, Charger, Boss } // [변경] Boss: 마지막 구역 보스 (EnemySwordsman.Boss.cs)

[CreateAssetMenu(menuName = "Combat/Enemy Data", fileName = "EnemyData")]
public class EnemyData : ScriptableObject
{
    [Header("Stats")]
    [SerializeField] EnemyKind kind = EnemyKind.Swordsman;
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
    // true면 칼을 휘두르기 시작한 순간부터 연타가 끝날 때까지 아머 → □로는 못 끊고 △ 간파로 맞받아야 함. 예고(Windup) 중에는 경직됨
    [SerializeField] bool armorWholeSwing = true;

    [Header("Hit Reaction")]
    [SerializeField] float lightStunTime = 0.3f;
    [SerializeField] float heavyStunTime = 0.6f;
    [SerializeField] float heavyDamageThreshold = 18f;
    [SerializeField] float lightKnockback = 0.15f;
    [SerializeField] float heavyKnockback = 0.6f;
    [SerializeField] float parryStunTime = 1.2f; // 간파당했을 때 경직

    [Header("Death")]
    [SerializeField] float respawnTime = 3f;
    [SerializeField] int xpReward = 10; // 구역 적을 처치하면 플레이어가 받는 경험치

    // 방패병: 정면 guardAngle 안의 □·△를 막고 가드 수치를 깎음. 0이 되면 가드 붕괴(긴 경직)
    // 간파·패링 반격, 뇌락, 차지 강타 2단계 이상은 한 번에 붕괴. 등 뒤와 벽력일섬은 막지 못함
    [Header("Guardian")]
    [SerializeField] int guardPoints = 4;
    [SerializeField] float guardAngle = 75f;
    [SerializeField] int lightGuardCost = 1;
    [SerializeField] int heavyGuardCost = 2;
    [SerializeField] float guardBreakStun = 2f;
    [SerializeField] float guardRegenDelay = 3f; // 마지막으로 막은 뒤 이 시간이 지나면 1칸씩 회복
    [SerializeField] float guardRegenInterval = 0.8f;
    [SerializeField] float guardHitPause = 0.3f;   // 막은 뒤 잠깐 멈칫

    // 돌격병: 거리를 벌린 채 예고(발광) → 일직선 돌진 → 도착하면 내려베기. 끝나면 숨을 고르는 동안 무방비
    [Header("Charger")]
    [SerializeField] AttackData chargeAttack;
    [SerializeField] float chargeMinDistance = 4.5f;
    [SerializeField] float chargeMaxDistance = 13f;
    [SerializeField] float chargeWindup = 0.9f;
    [SerializeField] float chargeSpeed = 13f;
    [SerializeField] float chargeMaxTravel = 12f;
    [SerializeField] float chargeStopDistance = 1.8f;
    [SerializeField] float chargeHomingTime = 0.2f; // 돌진 초반만 플레이어 쪽으로 살짝 꺾음
    [SerializeField] float chargeTurnSpeed = 180f;
    [SerializeField] float chargeCooldown = 2.5f;
    [SerializeField] float exhaustTime = 1.4f;

    // [변경] 보스: 경직 없이 버티다 체간(자세)이 가득 차면 붕괴 → 처형(즉사 대신 최대 체력 비율 피해)
    // 체력 phase2Ratio 이하에서 포효 → 2페이즈(빨라짐 + 검기). 패턴: 연속 베기 · 잡기 · 도약 내려찍기 · 검기
    [Header("Boss")]
    [SerializeField] string displayName = "흑뢰";
    [SerializeField] float maxPosture = 100f;
    [SerializeField] float postureRegenDelay = 2.5f;  // 마지막으로 체간이 깎인 뒤 이 시간이 지나면 회복 시작
    [SerializeField] float postureRegenRate = 10f;    // 초당
    [SerializeField] float postureBreakStun = 3.5f;   // 체간 붕괴 = 처형 기회 시간
    [SerializeField] float parryPosture = 30f;        // 간파·패링 한 번에 쌓이는 체간
    [SerializeField, Range(0.05f, 1f)] float executionDamageRatio = 0.25f;
    [SerializeField, Range(0.01f, 1f)] float instantKillDamageRatio = 0.12f; // 일섬 즉사(뇌인 가득)는 보스에게 이 비율 피해
    [SerializeField, Range(0.1f, 0.9f)] float phase2Ratio = 0.5f;
    [SerializeField] float phase2Speed = 1.15f;       // 2페이즈 동작 배속
    [SerializeField] float phase2CooldownScale = 0.7f;
    [SerializeField] float roarTime = 1.8f;
    [SerializeField] float roarPushRadius = 4f;
    [SerializeField] float introTime = 1.6f;

    [Header("Boss · Grab (회피만 가능)")]
    [SerializeField] AttackData grabAttack;
    [SerializeField, Range(0f, 1f)] float grabChance = 0.3f; // 가까이서 공격할 때 잡기를 고를 확률
    [SerializeField] float grabWindup = 0.7f;
    [SerializeField] float grabRange = 2.3f;
    [SerializeField] float grabLungeSpeed = 6f;
    [SerializeField] float grabLungeTime = 0.35f;
    [SerializeField] float grabDamage = 35f;
    [SerializeField] float grabCooldown = 8f;
    [SerializeField] float grabMissStun = 1.2f;       // 헛잡으면 이만큼 무방비

    [Header("Boss · Leap Smash")]
    [SerializeField] AttackData leapAttack;
    [SerializeField] float leapMinDistance = 5f;
    [SerializeField] float leapMaxDistance = 15f;
    [SerializeField] float leapWindup = 0.9f;
    [SerializeField] float leapAirTime = 0.8f;
    [SerializeField] float leapHeight = 5f;
    [SerializeField] float leapRadius = 3.2f;
    [SerializeField] float leapDamage = 30f;
    [SerializeField] float leapCooldown = 9f;
    [SerializeField] float leapRecovery = 1.3f;

    [Header("Boss · Sword Wave (2페이즈)")]
    [SerializeField] AttackData waveAttack; // 연속이면 nextLight로 이어짐 (한 번 벨 때마다 검기 하나)
    [SerializeField] float waveMinDistance = 4f;
    [SerializeField] float waveSpeed = 16f;
    [SerializeField] float waveRange = 22f;
    [SerializeField] float waveWidth = 2.2f;
    [SerializeField] float waveDamage = 16f;
    [SerializeField] float waveCooldown = 6f;

    public EnemyKind Kind => kind;
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
    public bool ArmorWholeSwing => armorWholeSwing;

    public float LightStunTime => lightStunTime;
    public float HeavyStunTime => heavyStunTime;
    public float HeavyDamageThreshold => heavyDamageThreshold;
    public float LightKnockback => lightKnockback;
    public float HeavyKnockback => heavyKnockback;
    public float ParryStunTime => parryStunTime;

    public float RespawnTime => respawnTime;
    public int XpReward => xpReward;

    public int GuardPoints => guardPoints;
    public float GuardAngle => guardAngle;
    public int LightGuardCost => lightGuardCost;
    public int HeavyGuardCost => heavyGuardCost;
    public float GuardBreakStun => guardBreakStun;
    public float GuardRegenDelay => guardRegenDelay;
    public float GuardRegenInterval => guardRegenInterval;
    public float GuardHitPause => guardHitPause;

    public AttackData ChargeAttack => chargeAttack;
    public float ChargeMinDistance => chargeMinDistance;
    public float ChargeMaxDistance => chargeMaxDistance;
    public float ChargeWindup => chargeWindup;
    public float ChargeSpeed => chargeSpeed;
    public float ChargeMaxTravel => chargeMaxTravel;
    public float ChargeStopDistance => chargeStopDistance;
    public float ChargeHomingTime => chargeHomingTime;
    public float ChargeTurnSpeed => chargeTurnSpeed;
    public float ChargeCooldown => chargeCooldown;
    public float ExhaustTime => exhaustTime;

    public string DisplayName => displayName;
    public float MaxPosture => maxPosture;
    public float PostureRegenDelay => postureRegenDelay;
    public float PostureRegenRate => postureRegenRate;
    public float PostureBreakStun => postureBreakStun;
    public float ParryPosture => parryPosture;
    public float ExecutionDamageRatio => executionDamageRatio;
    public float InstantKillDamageRatio => instantKillDamageRatio;
    public float Phase2Ratio => phase2Ratio;
    public float Phase2Speed => phase2Speed;
    public float Phase2CooldownScale => phase2CooldownScale;
    public float RoarTime => roarTime;
    public float RoarPushRadius => roarPushRadius;
    public float IntroTime => introTime;

    public AttackData GrabAttack => grabAttack;
    public float GrabChance => grabChance;
    public float GrabWindup => grabWindup;
    public float GrabRange => grabRange;
    public float GrabLungeSpeed => grabLungeSpeed;
    public float GrabLungeTime => grabLungeTime;
    public float GrabDamage => grabDamage;
    public float GrabCooldown => grabCooldown;
    public float GrabMissStun => grabMissStun;

    public AttackData LeapAttack => leapAttack;
    public float LeapMinDistance => leapMinDistance;
    public float LeapMaxDistance => leapMaxDistance;
    public float LeapWindup => leapWindup;
    public float LeapAirTime => leapAirTime;
    public float LeapHeight => leapHeight;
    public float LeapRadius => leapRadius;
    public float LeapDamage => leapDamage;
    public float LeapCooldown => leapCooldown;
    public float LeapRecovery => leapRecovery;

    public AttackData WaveAttack => waveAttack;
    public float WaveMinDistance => waveMinDistance;
    public float WaveSpeed => waveSpeed;
    public float WaveRange => waveRange;
    public float WaveWidth => waveWidth;
    public float WaveDamage => waveDamage;
    public float WaveCooldown => waveCooldown;
}
