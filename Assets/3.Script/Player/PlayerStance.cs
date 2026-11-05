using UnityEngine;
using UnityEngine.InputSystem;

// 평상시 / 전투 자세: 로코모션 블렌드 트리의 Stance 파라미터(0 평상시, 1 전투)로 대기·걷기·달리기 클립을 바꿈
// 공격·패링·스킬·피격 등 로코모션이 아닌 상태가 재생되면 곧바로 전투 자세 (회피는 자세를 유지: 평상시면 검을 넣은 채 구름)
// 전투 자세로 멈춰 선 채 sheatheDelay초 동안 아무 키도 누르지 않으면 검을 집어넣는 모션(Sheathe) → 평상시 자세
// 다른 스크립트는 그대로 "Locomotion"으로 돌아가면 되고, 어느 자세로 보일지는 이 컴포넌트가 정함
public class PlayerStance : MonoBehaviour
{
    static readonly int StanceHash = Animator.StringToHash("Stance");
    static readonly int LocomotionHash = Animator.StringToHash("Locomotion");
    static readonly int SheatheHash = Animator.StringToHash("Sheathe");
    static readonly int[] NeutralStates = // 이 상태들은 자세를 바꾸지 않음
    {
        LocomotionHash, SheatheHash,
        Animator.StringToHash("Jump_Start"), Animator.StringToHash("Jump_Loop"), Animator.StringToHash("Jump_Land"),
        Animator.StringToHash("Double_Jump"), Animator.StringToHash("Double_Jump_F"), // 점프도 자세별 클립
        // 회피도 자세별 클립(Stance 블렌드)이라 자세를 바꾸지 않음 → 평상시 Shift 달리기가 평상시 달리기로 이어짐
        Animator.StringToHash("Dodge_F"), Animator.StringToHash("Dodge_B"), Animator.StringToHash("Dodge_L"), Animator.StringToHash("Dodge_R")
    };

    [SerializeField] Animator animator;
    [SerializeField] PlayerLocomotion locomotion;
    [SerializeField] PlayerLockOn lockOn; // [변경] 락온 중에는 항상 전투 자세
    [SerializeField] float sheatheDelay = 5f;
    [SerializeField] float sheatheCrossFade = 0.2f;
    [SerializeField] float returnCrossFade = 0.25f;
    [SerializeField, Range(0.5f, 1f)] float sheatheEnd = 0.92f; // 집어넣는 모션의 이 지점에서 대기 자세로 넘김
    [SerializeField] bool startRelaxed = true;
    [SerializeField] bool stayDrawnInZoneFight = true; // 구역 전투 중에는 집어넣지 않음
    [SerializeField] bool logStance;

    bool combat;
    bool sheathing;
    float idleTimer;
    bool hasSheatheState;

    public bool InCombatStance => combat;

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (locomotion == null) locomotion = GetComponent<PlayerLocomotion>();
        if (lockOn == null) lockOn = GetComponent<PlayerLockOn>();
    }

    void Start()
    {
        if (animator == null) return;
        hasSheatheState = animator.HasState(0, SheatheHash);
        SetStance(!startRelaxed);
    }

    void Update()
    {
        if (animator == null) return;

        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
        int current = info.shortNameHash;
        int next = animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0).shortNameHash : 0;

        if (sheathing)
        {
            UpdateSheathe(info, current, next);
            if (sheathing) return;
        }

        // 핵심: 로코모션·점프·집어넣기 외의 상태 = 무언가 행동 중 → 전투 자세 유지, 대기 시간 초기화
        if (IsAction(current) || IsAction(next) || (lockOn != null && lockOn.IsLocked)) // [변경]
        {
            SetStance(true);
            idleTimer = 0f;
            return;
        }

        bool standing = current == LocomotionHash && next == 0 && (locomotion == null || locomotion.CurrentSpeed < 0.1f);
        bool hold = stayDrawnInZoneFight && ZoneArena.Fighting != null;
        if (!combat || !standing || hold || AnyInput())
        {
            idleTimer = 0f;
            return;
        }

        idleTimer += Time.deltaTime;
        if (idleTimer >= sheatheDelay) StartSheathe();
    }

    void StartSheathe()
    {
        idleTimer = 0f;
        SetStance(false); // 모션이 끝나고 로코모션으로 돌아올 때 평상시 클립이 나오도록 미리 바꿔 둠
        if (!hasSheatheState) return;

        sheathing = true;
        animator.CrossFadeInFixedTime(SheatheHash, sheatheCrossFade, 0, 0f);
        if (logStance) Debug.Log("[Stance] 검 집어넣기");
    }

    void UpdateSheathe(AnimatorStateInfo info, int current, int next)
    {
        bool inSheathe = current == SheatheHash || next == SheatheHash;
        if (!inSheathe)
        {
            sheathing = false; // 공격 등 다른 상태가 가져감 → 아래에서 전투 자세로 돌아감
            return;
        }

        // 이동 입력이 들어오면 바로 평상시 걷기로 (공격 입력은 PlayerCombat이 상태를 가져가므로 따로 처리 안 함)
        bool moving = locomotion != null && locomotion.GetInputDirection().sqrMagnitude > 0.0001f;
        bool finished = current == SheatheHash && next == 0 && info.normalizedTime >= sheatheEnd;
        if (moving || finished)
        {
            sheathing = false;
            animator.CrossFadeInFixedTime(LocomotionHash, moving ? 0.15f : returnCrossFade, 0, 0f);
        }
    }

    void SetStance(bool toCombat)
    {
        if (combat == toCombat && animator.GetFloat(StanceHash) == (toCombat ? 1f : 0f)) return;
        combat = toCombat;
        animator.SetFloat(StanceHash, toCombat ? 1f : 0f);
        if (logStance) Debug.Log($"[Stance] {(toCombat ? "전투" : "평상시")} 자세");
    }

    static bool IsAction(int hash)
    {
        if (hash == 0) return false;
        foreach (int neutral in NeutralStates)
            if (hash == neutral) return false;
        return true;
    }

    // 키보드 아무 키 · 마우스 버튼 · 이동 입력 (마우스로 화면만 돌리는 건 입력으로 치지 않음)
    bool AnyInput()
    {
        if (Keyboard.current != null && Keyboard.current.anyKey.isPressed) return true;
        Mouse mouse = Mouse.current;
        if (mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed)) return true;
        return locomotion != null && locomotion.GetInputDirection().sqrMagnitude > 0.0001f;
    }
}
