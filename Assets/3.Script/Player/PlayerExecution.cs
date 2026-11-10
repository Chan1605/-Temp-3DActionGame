using System;
using UnityEngine;
using UnityEngine.InputSystem;

// 처형: 처형 가능한 적(가드 붕괴 · 돌격 뒤 탈진 · 패링 경직)이 가까이 앞에 있으면 몸 중앙에 F 표시(ExecutionPrompt)
// 표시가 떠 있을 때 F를 누르면 하던 행동(공격·회피·패링·차지·피격 경직)을 끊고 무조건 처형. 그 판단은 표시와 같은 Candidate로 함
// 적을 모션별로 정해 둔 자리(거리·옆 · 각도)로 짧게 미끄러뜨려 짝 모션(Execution_N ↔ Executed_N)을 같이 재생
// 연출: 측면 시네마틱 카메라 · 느린 재생 · 타격 순간 정지 → 슬로. 처형 중에는 무적, 다른 행동이 막힘
// 자리·타격 시점은 Tools/Animation/Execution Tuner 창에서 두 캐릭터를 겹쳐 보며 맞춤
public class PlayerExecution : MonoBehaviour
{
    static readonly int LocomotionHash = Animator.StringToHash("Locomotion");
    static readonly int[] ExecutionHashes =
    {
        Animator.StringToHash("Execution_1"), Animator.StringToHash("Execution_2"), Animator.StringToHash("Execution_3")
    };

    // 모션별 짝 맞춤. distance = 앞, side = 오른쪽(+)·왼쪽(-), yaw = 적이 나를 정면으로 보는 각도에서 더 돌림
    [Serializable]
    public class MotionSetup
    {
        public float distance = 1.2f;
        public float side;
        public float yaw;
        [Range(0f, 1f)] public float killTime = 0.55f;
        [Range(0f, 1f)] public float endTime = 0.97f;
    }

    [SerializeField] Animator animator;
    [SerializeField] PlayerCombat combat;
    [SerializeField] PlayerLocomotion locomotion;
    [SerializeField] PlayerLockOn lockOn;
    [SerializeField] PlayerSkill skill;
    [SerializeField] PlayerHealth health;
    [SerializeField] PlayerDodge dodge;
    [SerializeField] PlayerParry parry;
    [SerializeField] PlayerChargeAttack charge;
    [SerializeField] ThirdPersonCamera followCamera;

    [Header("Find")]
    [SerializeField] float range = 2.8f;
    [SerializeField] float coneAngle = 110f; // 정면 기준 (락온 대상은 각도 무시)

    [Header("Motion")]
    [SerializeField] MotionSetup[] motions = { new MotionSetup(), new MotionSetup(), new MotionSetup() };
    [SerializeField] float playbackSpeed = 0.8f; // 짝 모션 재생 속도 (1보다 작으면 묵직하게)
    [SerializeField] float alignTime = 0.12f;    // 적이 자리로 미끄러지는 시간

    [Header("Kill Moment")]
    [SerializeField] float freezeTime = 0.09f;   // 칼이 꽂히는 순간 거의 정지 (실제 시간)
    [SerializeField] float slowScale = 0.3f;
    [SerializeField] float slowTime = 0.55f;

    [Header("Camera")]
    [SerializeField] bool cinematicCamera = true;
    [SerializeField] float cameraSideAngle = 70f; // 플레이어 정면 기준 옆으로 돈 각도 (+ 오른쪽)
    [SerializeField] float cameraPitch = 8f;
    [SerializeField] float cameraDistance = 3.4f;
    [SerializeField] bool logExecution = true;

    readonly Collider[] buffer = new Collider[64];

    EnemySwordsman victim;
    int playing = -1;
    bool killed;
    float startTime;
    float freezeUntil = -1f, slowUntil = -1f;
    float savedTimeScale = 1f;

    public bool IsActive => playing >= 0;
    public EnemySwordsman Candidate { get; private set; }
    public event Action<EnemySwordsman> Executed; // 타격 순간 (뇌기·HUD 연결용)

    public int MotionCount => ExecutionHashes.Length;
    public MotionSetup GetMotion(int index) => motions != null && index >= 0 && index < motions.Length ? motions[index] : null;

    // 플레이어가 이 자리에 이 방향으로 서 있을 때 index번 모션의 적 자리 (튜너 창도 같이 씀)
    public void GetVictimPose(Vector3 playerPos, Quaternion playerRot, int index, out Vector3 pos, out Quaternion rot)
    {
        MotionSetup m = GetMotion(index) ?? new MotionSetup();
        Vector3 forward = playerRot * Vector3.forward;
        Vector3 right = playerRot * Vector3.right;
        pos = playerPos + forward * m.distance + right * m.side;
        rot = Quaternion.LookRotation(-forward, Vector3.up) * Quaternion.Euler(0f, m.yaw, 0f);
    }

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (combat == null) combat = GetComponent<PlayerCombat>();
        if (locomotion == null) locomotion = GetComponent<PlayerLocomotion>();
        if (lockOn == null) lockOn = GetComponent<PlayerLockOn>();
        if (skill == null) skill = GetComponent<PlayerSkill>();
        if (health == null) health = GetComponent<PlayerHealth>();
        if (dodge == null) dodge = GetComponent<PlayerDodge>();
        if (parry == null) parry = GetComponent<PlayerParry>();
        if (charge == null) charge = GetComponent<PlayerChargeAttack>();
        if (followCamera == null) followCamera = FindAnyObjectByType<ThirdPersonCamera>();
    }

    void OnDisable()
    {
        if (IsActive) End();
        RestoreTime();
    }

    void Update()
    {
        if (GamePause.IsPaused) return; // [변경] 일시정지 중엔 입력·진행을 멈춤
        UpdateTime();

        if (!IsActive)
        {
            Candidate = CanExecuteNow() ? FindCandidate() : null; // 표시 조건 = 실행 조건
            if (Candidate != null && Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame) TryExecute();
            return;
        }
        UpdatePlaying();
    }

    // 죽지 않았고 땅에 있고 스킬 중이 아니면 처형 가능 (공격·회피·패링·차지·피격 중이어도 끊고 들어감)
    bool CanExecuteNow()
    {
        if (health != null && (health.IsDead || health.IsGrabbed)) return false; // 보스에게 잡힌 동안은 처형 불가
        if (skill != null && skill.IsActive) return false;
        return locomotion == null || locomotion.IsGrounded;
    }

    public bool TryExecute()
    {
        if (IsActive || Candidate == null) return false;

        int index = PickMotion(Candidate);
        if (index < 0) return false;
        Begin(Candidate, index);
        return true;
    }

    void Begin(EnemySwordsman enemy, int index)
    {
        victim = enemy;
        playing = index;
        killed = false;
        startTime = Time.time;
        Candidate = null;

        // 진행 중이던 행동을 조용히 끊음 (각자 끝날 때 로코모션으로 되돌리거나 잠금을 풀지 않도록)
        if (dodge != null && dodge.IsDodging) dodge.Interrupt();
        if (parry != null) parry.Interrupt();
        if (charge != null) charge.Cancel();
        if (health != null) health.ClearStun();
        if (combat != null)
        {
            combat.CancelAttack();
            combat.InputLocked = true;
        }
        if (locomotion != null) locomotion.MovementLocked = true;
        if (skill != null) skill.enabled = false;

        // 핵심: 나는 적을 향하고, 적은 모션별 자리로 미끄러지며 같은 프레임에 짝 모션 시작
        Vector3 dir = enemy.transform.position - transform.position;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward;
        transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        GetVictimPose(transform.position, transform.rotation, index, out Vector3 spot, out Quaternion spotRot);
        spot.y = enemy.transform.position.y;
        enemy.BeginExecuted(spot, spotRot, index, playbackSpeed, alignTime);

        animator.speed = playbackSpeed;
        animator.CrossFadeInFixedTime(ExecutionHashes[index], 0.05f, 0, 0f);

        if (cinematicCamera && followCamera != null)
        {
            Vector3 focus = Vector3.Lerp(transform.position, spot, 0.5f) + Vector3.up * 1.1f;
            followCamera.BeginCinematic(focus, transform.eulerAngles.y + cameraSideAngle, cameraPitch, cameraDistance);
        }
        if (logExecution) Debug.Log($"[Execution] 처형 {index + 1} · {enemy.name}");
    }

    void UpdatePlaying()
    {
        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
        bool inMotion = info.shortNameHash == ExecutionHashes[playing];
        if (!inMotion)
        {
            // 전환 직후 몇 프레임은 이전 상태일 수 있음. 오래 다른 상태면 끊긴 것으로 보고 정리
            if (Time.time - startTime > 0.5f && !animator.IsInTransition(0)) End();
            return;
        }

        MotionSetup m = GetMotion(playing) ?? new MotionSetup();
        if (!killed && info.normalizedTime >= m.killTime) Kill();
        if (info.normalizedTime >= m.endTime) End();
    }

    void Kill()
    {
        killed = true;
        if (victim == null || !victim.CanBeTargeted) return;

        Vector3 dir = victim.transform.position - transform.position;
        dir.y = 0f;
        victim.ReceiveDamage(new DamageInfo
        {
            Attacker = gameObject,
            Damage = 999f,
            InstantKill = true,
            Weight = AttackWeight.Execution,
            HitPoint = victim.transform.position + Vector3.up * 1.2f,
            Direction = dir.normalized
        });

        if (followCamera != null) followCamera.Shake(0.2f, 0.25f);
        // 정지 → 슬로 → 원래 속도 (실제 시간 기준)
        if (freezeUntil < 0f && slowUntil < 0f) savedTimeScale = Time.timeScale > 0.01f ? Time.timeScale : 1f;
        Time.timeScale = 0.02f;
        freezeUntil = Time.unscaledTime + freezeTime;
        slowUntil = freezeUntil + slowTime;
        Executed?.Invoke(victim);
    }

    void UpdateTime()
    {
        if (slowUntil < 0f) return;
        if (freezeUntil > 0f && Time.unscaledTime >= freezeUntil)
        {
            freezeUntil = -1f;
            Time.timeScale = slowScale;
        }
        if (Time.unscaledTime >= slowUntil) RestoreTime();
    }

    void End()
    {
        if (!killed) Kill(); // 모션이 끊겨도 처형은 성립
        playing = -1;
        victim = null;

        animator.speed = 1f;
        if (followCamera != null) followCamera.EndCinematic();
        if (combat != null) combat.InputLocked = false;
        if (locomotion != null) locomotion.MovementLocked = false;
        if (skill != null) skill.enabled = true;
        animator.CrossFadeInFixedTime(LocomotionHash, 0.3f, 0, 0f);
    }

    void RestoreTime()
    {
        if (slowUntil < 0f && freezeUntil < 0f) return;
        freezeUntil = slowUntil = -1f;
        Time.timeScale = savedTimeScale;
    }

    // 락온 대상이 처형 가능하고 가까우면 우선, 아니면 정면 원뿔 안에서 가장 가까운 처형 가능 적
    EnemySwordsman FindCandidate()
    {
        if (lockOn != null && lockOn.IsLocked && lockOn.Target.TryGetComponent(out EnemySwordsman locked)
            && locked.IsExecutable && FlatDistance(locked.transform) <= range && PickMotion(locked) >= 0)
            return locked;

        EnemySwordsman best = null;
        float bestDist = float.MaxValue;
        int count = Physics.OverlapSphereNonAlloc(transform.position, range, buffer, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var enemy = buffer[i].GetComponentInParent<EnemySwordsman>();
            if (enemy == null || !enemy.IsExecutable || PickMotion(enemy) < 0) continue; // 짝 모션이 없으면 표시도 안 함

            Vector3 to = enemy.transform.position - transform.position;
            to.y = 0f;
            if (Vector3.Angle(transform.forward, to) > coneAngle * 0.5f) continue;
            float d = to.magnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = enemy;
            }
        }
        return best;
    }

    // 플레이어·적 둘 다 짝 모션이 있는 번호 중 무작위
    int PickMotion(EnemySwordsman enemy)
    {
        int start = UnityEngine.Random.Range(0, ExecutionHashes.Length);
        for (int k = 0; k < ExecutionHashes.Length; k++)
        {
            int i = (start + k) % ExecutionHashes.Length;
            if (animator.HasState(0, ExecutionHashes[i]) && enemy.HasExecutedMotion(i)) return i;
        }
        return -1;
    }

    float FlatDistance(Transform t)
    {
        Vector3 d = t.position - transform.position;
        d.y = 0f;
        return d.magnitude;
    }
}
