using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerDodge : MonoBehaviour
{
    static readonly int LocomotionHash = Animator.StringToHash("Locomotion");

    // 순서: 앞, 뒤, 왼쪽, 오른쪽
    static readonly int[] DodgeHashes =
    {
        Animator.StringToHash("Dodge_F"), Animator.StringToHash("Dodge_B"),
        Animator.StringToHash("Dodge_L"), Animator.StringToHash("Dodge_R")
    };

    [SerializeField] InputActionReference dodgeAction;

    [SerializeField] Animator animator;
    [SerializeField] PlayerLocomotion locomotion;
    [SerializeField] PlayerCombat combat;
    [SerializeField] PlayerSkill skill;
    [SerializeField] PlayerHealth health;
    [SerializeField] PlayerParry parry;
    [SerializeField] PlayerChargeAttack charge; // [변경] 가드 대신: 회피 후반에 우클릭을 누르고 있으면 차지로 바로 넘어감
    [SerializeField] PlayerAfterimage afterimage;
    [SerializeField] ThirdPersonCamera followCamera;

    [Header("Move")]
    [SerializeField] float distance = 4f;
    [SerializeField] float moveTime = 0.4f;
    [SerializeField] float recoveryTime = 0.15f;
    // 0 = 일정한 속도, 1 = 처음에 빠르고 끝에서 완전히 멈춤. 끝 속도가 0이면 다음 동작 전에 한 번 멈춘 느낌이 남
    [SerializeField, Range(0f, 1f)] float easeOut = 0.6f;

    // true: 입력 방향으로 몸을 돌려 앞구르기 / false: 바라보는 방향 유지, 앞·뒤·좌·우 회피 모션
    [SerializeField] bool turnToDirection;

    [Header("Invulnerable")]
    [SerializeField] float invulnerableStart = 0f;
    [SerializeField] float invulnerableEnd = 0.3f;

    // 저스트 회피 타이밍 (둘 중 하나만 만족해도 인정)
    //  1) 회피를 누른 순간: 적 판정 시작 justWindow초 전 ~ 판정 시작 후 justLateWindow초 사이
    //  2) 회피 시작 후 avoidedHitWindow초 안에 무적으로 공격을 흘림
    [Header("Just Dodge")]
    [SerializeField] float justWindow = 0.2f;
    [SerializeField] float justLateWindow = 0.1f;
    [SerializeField] float avoidedHitWindow = 0.2f;
    [SerializeField] float threatRadius = 4f;
    [SerializeField] float slowScale = 0.2f;
    [SerializeField] float slowTime = 1f;
    [SerializeField] float afterimageTime = 0.5f;
    [SerializeField] float justInvulnerableTime = 0.5f;

    [Header("Limit")]
    [SerializeField] float cooldown = 0.15f;
    [SerializeField] int maxChain = 2;
    [SerializeField] float chainResetTime = 0.8f;
    [SerializeField] float attackCancelTime = 0.1f;

    // 회피 → 다른 행동 연결
    //  inputBuffer: 회피 입력을 이 시간만큼 기억 (연속 회피·공격 직후 회피가 씹히지 않게)
    //  actionCancelTime: 이 시점부터 공격·점프·가드·간파 입력이 있으면 남은 회피를 끊고 바로 넘어감 (무적 끝 무렵)
    //  이동 거리를 다 간 뒤(moveTime)에는 이동 입력만 있어도 후딜레이 없이 이동으로 넘어감
    [Header("Buffer & Cancel")]
    [SerializeField] float inputBuffer = 0.2f;
    [SerializeField] float actionCancelTime = 0.3f;

    // 팩 회피 클립은 앞 0.2초가 준비 동작이라 그 뒤부터 재생해 입력 즉시 몸이 움직이게 함
    [Header("Animation")]
    [SerializeField] float clipStartOffset = 0.15f;
    [SerializeField] float crossFade = 0.05f;
    [SerializeField] bool logDodge;

    CharacterController controller;

    bool dodging;
    float timer;
    Vector3 direction;
    float traveled;
    float readyTime;
    int chainCount;
    float lastDodgeTime = float.NegativeInfinity;
    float lastPressTime = float.NegativeInfinity;
    bool justTriggered;
    float justInvulnerableUntil;

    readonly Collider[] overlapBuffer = new Collider[32];
    readonly HashSet<IAttackThreat> seen = new HashSet<IAttackThreat>();

    public bool IsDodging => dodging;
    public bool CanCancel => dodging && timer >= actionCancelTime;
    // 저스트 회피 직후에는 무적을 조금 더 유지 (슬로 중 연속 공격에 바로 맞지 않게)
    public bool IsInvulnerable => (dodging && timer >= invulnerableStart && timer <= invulnerableEnd)
                                  || Time.unscaledTime < justInvulnerableUntil;
    public event Action<Vector3> Dodged;
    public event Action JustDodged; // 이후 뇌기 +1 등에 연결

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (locomotion == null) locomotion = GetComponent<PlayerLocomotion>();
        if (combat == null) combat = GetComponent<PlayerCombat>();
        if (skill == null) skill = GetComponent<PlayerSkill>();
        if (health == null) health = GetComponent<PlayerHealth>();
        if (parry == null) parry = GetComponent<PlayerParry>();
        if (charge == null) charge = GetComponent<PlayerChargeAttack>(); // [변경]
        if (afterimage == null) afterimage = GetComponent<PlayerAfterimage>();
        if (followCamera == null) followCamera = FindAnyObjectByType<ThirdPersonCamera>();
    }

    void OnEnable()
    {
        if (dodgeAction != null) dodgeAction.action.Enable();
    }

    void OnDisable()
    {
        if (dodging) EndDodge(true);
    }

    void Update()
    {
        // 회피 도중 스킬이 시작되면 회피만 조용히 끝내고 제어권은 스킬에 넘김
        if (dodging && skill != null && skill.IsActive)
        {
            EndDodge(false);
            return;
        }

        // 누른 순간이 아니라 버퍼 안에서 회피 가능해지는 첫 프레임에 발동
        if (dodgeAction != null && dodgeAction.action.WasPressedThisFrame()) lastPressTime = Time.time;
        if (Time.time - lastPressTime <= inputBuffer && CanDodge())
        {
            lastPressTime = float.NegativeInfinity;
            StartDodge();
        }

        if (!dodging) return;
        UpdateDodge(Time.deltaTime);
        if (dodging) TryCancelIntoAction();
    }

    bool CanDodge()
    {
        if (Time.time < readyTime) return false;
        if (health != null && health.IsStunned) return false;
        if (skill != null && skill.IsActive) return false;
        if (parry != null && parry.IsActive) return false; // 간파 반격 중에는 회피 불가
        if (locomotion != null && !locomotion.IsGrounded) return false; // 공중 회피는 v1 범위 밖

        // 연속 회피는 maxChain회까지, 잠시 쉬면 다시 채워짐
        if (Time.time - lastDodgeTime > chainResetTime) chainCount = 0;
        if (chainCount >= maxChain) return false;

        // 회피 중 재입력은 이동이 거의 끝난 뒤에만 받음
        if (dodging && timer < moveTime * 0.7f) return false;

        // 공격은 시작 직후 아주 짧은 구간만 지나면 언제든 회피로 끊을 수 있음 (판정 중 포함)
        if (combat != null && combat.IsAttacking && combat.AttackElapsed < attackCancelTime) return false;

        return true;
    }

    void StartDodge()
    {
        Vector3 input = locomotion != null ? locomotion.GetInputDirection() : Vector3.zero;
        direction = input.sqrMagnitude > 0.0001f ? input.normalized : -transform.forward;

        if (combat != null)
        {
            if (combat.IsAttacking) combat.CancelAttack();
            combat.InputLocked = true;
        }
        if (locomotion != null) locomotion.MovementLocked = true;

        int clipIndex;
        if (turnToDirection)
        {
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            clipIndex = 0;
        }
        else
        {
            clipIndex = DirectionIndex(transform.InverseTransformDirection(direction));
        }

        animator.CrossFadeInFixedTime(DodgeHashes[clipIndex], crossFade, 0, clipStartOffset);

        dodging = true;
        timer = 0f;
        traveled = 0f;
        chainCount++;
        lastDodgeTime = Time.time;
        justTriggered = false;

        Dodged?.Invoke(direction);
        if (IsThreatNearby()) TriggerJustDodge(); // 회피 시작 시점에 곧 맞을 공격이 있었는지 확인
        if (logDodge) Debug.Log($"[Dodge] {(turnToDirection ? "F(회전)" : "FBLR"[clipIndex].ToString())} · 연속 {chainCount}");
    }

    // 처음에 빠르고 끝에서 멈추듯 감속 (ease-out)
    void UpdateDodge(float dt)
    {
        timer += dt;

        float p = Mathf.Clamp01(timer / moveTime);
        float eased = Mathf.Lerp(p, 1f - (1f - p) * (1f - p), easeOut); // 끝 속도가 0이 되지 않게 선형과 섞음
        float delta = distance * eased - traveled;
        if (delta > 0f && controller.enabled)
        {
            controller.Move(direction * delta);
            traveled += delta;
        }

        if (timer >= moveTime + recoveryTime) EndDodge(true);
    }

    // 핵심: 회피가 끝날 때까지 기다리지 않고, 다음 행동 입력이 있으면 그 행동에 바로 넘김
    void TryCancelIntoAction()
    {
        if (timer < actionCancelTime) return;

        bool wantsAction = (combat != null && combat.HasBufferedPress)
                           || (locomotion != null && locomotion.HasBufferedJump)
                           || (charge != null && charge.IsHeld); // [변경]
        if (wantsAction)
        {
            EndDodge(true, false); // 다음 행동이 자기 애니메이션으로 바로 전환하므로 로코모션 크로스페이드는 생략
            return;
        }

        if (timer >= moveTime && locomotion != null && locomotion.GetInputDirection().sqrMagnitude > 0.0001f)
            EndDodge(true);
    }

    // PlayerHealth가 회피 무적으로 공격을 흘렸을 때 호출. 회피 직후라면 저스트 회피로 인정
    public void NotifyAvoidedHit()
    {
        if (dodging && timer <= avoidedHitWindow) TriggerJustDodge(); // 전용 값으로 분리
    }

    // 반경 안의 적 중 justWindow초 안에 이 위치를 때릴 공격이 있는지
    bool IsThreatNearby()
    {
        seen.Clear();
        int count = Physics.OverlapSphereNonAlloc(transform.position, threatRadius, overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider col = overlapBuffer[i];
            if (col.transform.IsChildOf(transform)) continue;

            IAttackThreat threat = col.GetComponentInParent<IAttackThreat>();
            if (threat == null || !seen.Add(threat)) continue;
            if (threat.IsAboutToHit(transform.position, justWindow, justLateWindow)) return true;
        }
        return false;
    }

    // 적만 느려지고(플레이어는 그대로) 잔상이 남음
    void TriggerJustDodge()
    {
        if (justTriggered) return;
        justTriggered = true;

        CombatTime.SlowEnemies(slowScale, slowTime);
        justInvulnerableUntil = Time.unscaledTime + justInvulnerableTime;
        if (afterimage != null) afterimage.Emit(afterimageTime);
        if (followCamera != null) followCamera.Shake(0.06f, 0.1f);

        JustDodged?.Invoke();
        Debug.Log("[Dodge] 저스트 회피");
    }

    // 피격·스킬로 끊길 때는 restoreControl = false, 잠금 해제와 애니메이션은 끊은 쪽이 담당
    public void Interrupt() => EndDodge(false);

    void EndDodge(bool restoreControl, bool playLocomotion = true)
    {
        if (!dodging) return;
        dodging = false;
        readyTime = Time.time + cooldown;

        if (!restoreControl) return;

        if (combat != null) combat.InputLocked = false;
        if (locomotion != null)
        {
            locomotion.MovementLocked = false;
            locomotion.CarryMomentum(); // 속도 0에서 다시 가속하지 않고 입력 방향 속도로 이어감
        }
        if (playLocomotion) animator.CrossFadeInFixedTime(LocomotionHash, 0.15f, 0, 0f);
    }

    static int DirectionIndex(Vector3 local)
    {
        if (Mathf.Abs(local.z) >= Mathf.Abs(local.x)) return local.z >= 0f ? 0 : 1;
        return local.x < 0f ? 2 : 3;
    }
}
