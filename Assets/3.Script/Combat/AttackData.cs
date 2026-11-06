using UnityEngine;

[CreateAssetMenu(menuName = "Combat/Attack Data", fileName = "AttackData")]
public class AttackData : ScriptableObject
{
    [SerializeField] AnimationClip clip;
    [SerializeField] string stateName;

    [Header("Damage")]
    [SerializeField] float damage = 10f;
    [SerializeField] float postureDamage = 5f;
    [SerializeField] bool parryable = true;
    [SerializeField] AttackWeight weight = AttackWeight.Light; // P3 무게별 피드백
    [SerializeField] bool knockDown; // 맞으면 넘어짐 (적 공격 전용)

    // 모든 타이밍은 클립 길이 기준 정규화 시간(0~1)
    [Header("Timing")]
    [SerializeField, Range(0f, 1f)] float hitStart = 0.25f;
    [SerializeField, Range(0f, 1f)] float hitEnd = 0.45f;
    [SerializeField, Range(0f, 1f)] float inputOpen = 0.2f;
    [SerializeField, Range(0f, 1f)] float comboTransition = 0.5f;
    [SerializeField, Range(0f, 1f)] float recoveryEnd = 0.9f;
    [SerializeField, Range(0f, 1f)] float moveCancelStart = 0.5f;

    // 공격 초반 정면으로 내딛는 거리(m)와 끝나는 시점(정규화), 거리 0이면 제자리 공격
    [Header("Step")]
    [SerializeField] float stepDistance = 0.6f;
    [SerializeField, Range(0f, 1f)] float stepEnd = 0.3f;

    // 이 공격 도중 약/강 입력 시 이어질 다음 공격, 비어 있으면 그 입력으로는 콤보가 끝남
    // [변경] 공중 콤보: launchHeight > 0이면 띄우기, airAttack이면 공중에서 쓰는 공격 (체공 + 떠 있는 적 떠받침)
    [Header("Air")]
    [SerializeField] float launchHeight;
    [SerializeField] bool airAttack;

    [Header("Combo")]
    [SerializeField] AttackData nextLight;
    [SerializeField] AttackData nextHeavy;

    int stateHash;

    public AnimationClip Clip => clip;
    public int StateHash => stateHash;
    public float Duration => clip != null ? clip.length : 1f;

    public float Damage => damage;
    public float PostureDamage => postureDamage;
    public bool Parryable => parryable;
    public AttackWeight Weight => weight;
    public bool KnockDown => knockDown;

    public float HitStart => hitStart;
    public float HitEnd => hitEnd;
    public float InputOpen => inputOpen;
    public float ComboTransition => comboTransition;
    public float RecoveryEnd => recoveryEnd;
    public float MoveCancelStart => moveCancelStart;
    public float StepDistance => stepDistance;
    public float StepEnd => stepEnd;
    public float LaunchHeight => launchHeight; // [변경]
    public bool AirAttack => airAttack;

    public AttackData GetNext(AttackInput input) => input == AttackInput.Light ? nextLight : nextHeavy;

    void OnEnable() => stateHash = Animator.StringToHash(stateName);
    void OnValidate() => stateHash = Animator.StringToHash(stateName);
}
