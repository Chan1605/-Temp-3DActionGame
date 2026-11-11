using UnityEngine;

[CreateAssetMenu(menuName = "Combat/Skill Data", fileName = "SkillData")]
public class SkillData : ScriptableObject
{
    [Header("Animation")]
    [SerializeField] string stanceState = "Skill_Stance";
    [SerializeField] string chargeState = "Skill_Charge";
    [SerializeField] string releaseState = "Skill_Release";
    // 잔심 자세(finishState·finishDuration) 삭제 → 착지 즉시 납도로 확정

    // 이 시간보다 짧게 누르면 즉발, 길게 누르면 차지
    [Header("Charge")]
    [SerializeField] float chargeStartTime = 0.2f;
    [SerializeField] float chargeLevelTime = 0.5f;
    [SerializeField] int maxChargeLevel = 2;
    [SerializeField, Range(0.05f, 1f)] float chargeTimeScale = 0.4f;

    [Header("Dash")]
    [SerializeField] float baseRange = 10f;
    [SerializeField] float rangePerLevel = 4f;
    [SerializeField, Range(10f, 360f)] float searchAngle = 70f;
    [SerializeField] float behindDistance = 1.5f;
    [SerializeField] float whiffDistance = 6f;
    [SerializeField] float dashDuration = 0.1f;
    [SerializeField] float pathRadius = 0.8f;

    [Header("Chain")]
    [SerializeField] int maxChain = 6;
    [SerializeField] float chainRange = 12f;
    [SerializeField] float chainWindow = 0.4f;

    [Header("Finish")]
    [SerializeField] float killDelay = 0.25f;
    [SerializeField] float recoveryTime = 0.3f;
    [SerializeField] float hitStopTime = 0.08f;
    [SerializeField] float cooldown = 3f;

    [Header("Damage")]
    [SerializeField] float damage = 100f;
    [SerializeField] float postureDamage = 100f;
    [SerializeField] bool instantKill = true;

    [Header("Tap Chain")] // [변경] 일섬 밸런스를 이 에셋 하나로 (예전엔 PlayerSkill 컴포넌트)
    [SerializeField] float tapChainCost = 0.5f; // 탭 Q 연격 한 번마다 드는 뇌기

    [Header("Draw Stance")]
    [SerializeField] float stanceCost = 2f; // 발도 자세 뇌기 비용
    [SerializeField] float stanceEnterTime = 0.25f; // 이만큼 누르고 있으면 진입 (짧게 떼면 탭 Q)
    [SerializeField] float stanceDuration = 2.5f; // 자세 유지 시간 (실제 시간) · 각성 발도도 같이
    [SerializeField] float stanceEnemyScale = 0.05f; // 자세 중 적 속도
    [SerializeField] float stanceRange = 20f; // 조준 사거리
    [SerializeField] float markTime = 0.2f; // 조준점에 이 시간 올려두면 표식
    [SerializeField] float reticleRadius = 0.08f; // 조준점 반경 (화면 높이 대비)
    [SerializeField] int baseMarks = 3; // 스킬트리 '표식 확장' 전 최대 표식
    [SerializeField] int maxMarks = 6; // '표식 확장' 후 최대 표식
    [SerializeField] float stanceChainDelay = 0.05f; // 표식 사이 다음 돌진까지 간격

    [Header("Enchant Draw")]
    [SerializeField] float enchantStanceSeconds = 3.5f; // 각성에서 떼어 쓰는 발도 시간 (그동안 발도 무제한)
    [SerializeField] int enchantMaxMarks = 0; // 각성 발도 최대 표식. 0 = 제한 없음

    [Header("Dodge Draw")]
    [SerializeField] float dodgeStanceWindow = 0.9f; // 저스트 회피 뒤 Q 입력 시간
    [SerializeField] float dodgeStanceCost = 0f; // 비용 (기본 무료)
    [SerializeField] int dodgeAutoMarks = 3; // 자동 표식 최대 인원 (가까운 순)
    [SerializeField] float dodgeAutoRange = 12f; // 자동 표식 범위
    [SerializeField] float dodgeDrawDelay = 0.12f; // 풀차지 연출 뒤 발도까지 (실제 시간)

    [Header("Pass Freeze")]
    [SerializeField] float freezeTime = 0.08f; // 표식한 적을 지날 때 정지 (실제 시간)
    [SerializeField] float lastFreezeTime = 0.14f; // 마지막 대상 정지
    [SerializeField, Range(0f, 1f)] float freezeWorldScale = 0.02f; // 정지 동안 세상 배속 (0에 가까울수록 정지 컷)
    [SerializeField] float freezeBudget = 0.6f; // 일섬 한 번에 더해지는 최대 정지 시간
    [SerializeField] float holdFreezeScale = 1f; // 발동별 정지 배율: Q 홀드
    [SerializeField] float enchantFreezeScale = 0.7f; // 각성 발도
    [SerializeField] float dodgeFreezeScale = 0.6f; // 회피 발도
    [SerializeField] float tapFreezeScale = 0f; // 탭 Q
    [SerializeField] float releaseShake = 0.12f; // 정지가 풀리는 순간 흔들림
    [SerializeField] float lastFreezePunch = 0.3f; // 마지막 대상 펀치 줌

    public string StanceState => stanceState;
    public string ChargeState => chargeState;
    public string ReleaseState => releaseState;

    public float ChargeStartTime => chargeStartTime;
    public float ChargeLevelTime => chargeLevelTime;
    public int MaxChargeLevel => maxChargeLevel;
    public float ChargeTimeScale => chargeTimeScale;

    public float SearchAngle => searchAngle;
    public float BehindDistance => behindDistance;
    public float WhiffDistance => whiffDistance;
    public float DashDuration => dashDuration;
    public float PathRadius => pathRadius;

    public int MaxChain => maxChain;
    public float ChainRange => chainRange;
    public float ChainWindow => chainWindow;

    public float KillDelay => killDelay;
    public float RecoveryTime => recoveryTime;
    public float HitStopTime => hitStopTime;
    public float Cooldown => cooldown;

    public float Damage => damage;
    public float PostureDamage => postureDamage;
    public bool InstantKill => instantKill;

    // [변경] 발도 자세 · 각성 · 회피 발도 · 점찍기 정지
    public float TapChainCost => tapChainCost;
    public float StanceCost => stanceCost;
    public float StanceEnterTime => stanceEnterTime;
    public float StanceDuration => stanceDuration;
    public float StanceEnemyScale => stanceEnemyScale;
    public float StanceRange => stanceRange;
    public float MarkTime => markTime;
    public float ReticleRadius => reticleRadius;
    public int BaseMarks => baseMarks;
    public int MaxMarks => maxMarks;
    public float StanceChainDelay => stanceChainDelay;
    public float EnchantStanceSeconds => enchantStanceSeconds;
    public int EnchantMaxMarks => enchantMaxMarks;
    public float DodgeStanceWindow => dodgeStanceWindow;
    public float DodgeStanceCost => dodgeStanceCost;
    public int DodgeAutoMarks => dodgeAutoMarks;
    public float DodgeAutoRange => dodgeAutoRange;
    public float DodgeDrawDelay => dodgeDrawDelay;
    public float FreezeTime => freezeTime;
    public float LastFreezeTime => lastFreezeTime;
    public float FreezeWorldScale => freezeWorldScale;
    public float FreezeBudget => freezeBudget;
    public float HoldFreezeScale => holdFreezeScale;
    public float EnchantFreezeScale => enchantFreezeScale;
    public float DodgeFreezeScale => dodgeFreezeScale;
    public float TapFreezeScale => tapFreezeScale;
    public float ReleaseShake => releaseShake;
    public float LastFreezePunch => lastFreezePunch;

    public float RangeAt(int chargeLevel) => baseRange + rangePerLevel * chargeLevel;
}
