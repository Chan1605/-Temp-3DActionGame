using UnityEngine;

[CreateAssetMenu(menuName = "Combat/Skill Data", fileName = "SkillData")]
public class SkillData : ScriptableObject
{
    [Header("Animation")]
    [SerializeField] string stanceState = "Skill_Stance";
    [SerializeField] string chargeState = "Skill_Charge";
    [SerializeField] string releaseState = "Skill_Release";

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

    public float RangeAt(int chargeLevel) => baseRange + rangePerLevel * chargeLevel;
}
