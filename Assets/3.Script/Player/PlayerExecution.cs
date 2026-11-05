using System;
using UnityEngine;

// 처형: 처형 가능한 적(가드 붕괴 · 돌격 뒤 탈진 · 패링 경직)이 가까이 앞에 있을 때 □를 누르면
// 적을 내 앞 정해진 자리로 끌어와 짝 모션(Execution_N ↔ Executed_N)을 재생하고, 타격 순간 즉사 + 연출
// 처형 중에는 무적이고 다른 행동(회피·패링·차지·스킬)이 막힘
public class PlayerExecution : MonoBehaviour
{
    static readonly int LocomotionHash = Animator.StringToHash("Locomotion");
    static readonly int[] ExecutionHashes =
    {
        Animator.StringToHash("Execution_1"), Animator.StringToHash("Execution_2"), Animator.StringToHash("Execution_3")
    };

    [SerializeField] Animator animator;
    [SerializeField] PlayerCombat combat;
    [SerializeField] PlayerLocomotion locomotion;
    [SerializeField] PlayerLockOn lockOn;
    [SerializeField] PlayerSkill skill;
    [SerializeField] ThirdPersonCamera followCamera;

    [Header("Find")]
    [SerializeField] float range = 2.8f;
    [SerializeField] float coneAngle = 110f; // 정면 기준 (락온 대상은 각도 무시)

    [Header("Play")]
    [SerializeField] float alignDistance = 1.2f; // 짝 모션이 맞물리는 거리. 칼이 허공을 베거나 몸이 겹치면 조절
    [SerializeField, Range(0f, 1f)] float killTime = 0.55f; // 플레이어 모션 정규화 시간
    [SerializeField, Range(0f, 1f)] float endTime = 0.92f;
    [SerializeField] float slowScale = 0.25f;  // 타격 순간 잠깐 느리게
    [SerializeField] float slowTime = 0.25f;   // 실제 시간
    [SerializeField] bool logExecution = true;

    readonly Collider[] buffer = new Collider[64];

    EnemySwordsman victim;
    int playing = -1;
    bool killed;
    float startTime;
    float slowUntil = -1f;
    float savedTimeScale = 1f;

    public bool IsActive => playing >= 0;
    public EnemySwordsman Candidate { get; private set; }
    public event Action<EnemySwordsman> Executed; // 타격 순간 (뇌기·HUD 연결용)

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (combat == null) combat = GetComponent<PlayerCombat>();
        if (locomotion == null) locomotion = GetComponent<PlayerLocomotion>();
        if (lockOn == null) lockOn = GetComponent<PlayerLockOn>();
        if (skill == null) skill = GetComponent<PlayerSkill>();
        if (followCamera == null) followCamera = FindAnyObjectByType<ThirdPersonCamera>();
    }

    void OnDisable()
    {
        if (IsActive) End();
        RestoreTime();
    }

    void Update()
    {
        if (slowUntil > 0f && Time.unscaledTime >= slowUntil) RestoreTime();

        if (!IsActive)
        {
            Candidate = FindCandidate();
            return;
        }
        UpdatePlaying();
    }

    // PlayerCombat이 □를 누른 순간 먼저 물어봄. true면 이번 입력은 처형으로 소비
    public bool TryExecute()
    {
        if (IsActive || Candidate == null || (locomotion != null && !locomotion.IsGrounded)) return false;

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

        if (combat != null)
        {
            combat.CancelAttack();
            combat.InputLocked = true;
        }
        if (locomotion != null) locomotion.MovementLocked = true;
        if (skill != null) skill.enabled = false;

        // 핵심: 나는 적 쪽을 보고, 적은 내 앞 alignDistance에서 나를 보게 맞춘 뒤 두 모션을 같은 프레임에 시작
        Vector3 dir = enemy.transform.position - transform.position;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward;
        transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        Vector3 spot = transform.position + dir * alignDistance;
        spot.y = enemy.transform.position.y;
        enemy.BeginExecuted(spot, Quaternion.LookRotation(-dir, Vector3.up), index);
        animator.CrossFadeInFixedTime(ExecutionHashes[index], 0.05f, 0, 0f);

        if (followCamera != null) followCamera.Punch(0.18f, 0.25f, 0.6f, 0.5f, 0f);
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

        if (!killed && info.normalizedTime >= killTime) Kill();
        if (info.normalizedTime >= endTime) End();
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

        if (followCamera != null) followCamera.Shake(0.18f, 0.2f);
        savedTimeScale = Time.timeScale > 0.01f ? Time.timeScale : 1f;
        Time.timeScale = slowScale;
        slowUntil = Time.unscaledTime + slowTime;
        Executed?.Invoke(victim);
    }

    void End()
    {
        if (!killed) Kill(); // 모션이 끊겨도 처형은 성립
        playing = -1;
        victim = null;
        RestoreTime();

        if (combat != null) combat.InputLocked = false;
        if (locomotion != null) locomotion.MovementLocked = false;
        if (skill != null) skill.enabled = true;
        animator.CrossFadeInFixedTime(LocomotionHash, 0.25f, 0, 0f);
    }

    void RestoreTime()
    {
        if (slowUntil < 0f) return;
        slowUntil = -1f;
        Time.timeScale = savedTimeScale;
    }

    // 락온 대상이 처형 가능하고 가까우면 우선, 아니면 정면 원뿔 안에서 가장 가까운 처형 가능 적
    EnemySwordsman FindCandidate()
    {
        if (lockOn != null && lockOn.IsLocked && lockOn.Target.TryGetComponent(out EnemySwordsman locked)
            && locked.IsExecutable && FlatDistance(locked.transform) <= range)
            return locked;

        EnemySwordsman best = null;
        float bestDist = float.MaxValue;
        int count = Physics.OverlapSphereNonAlloc(transform.position, range, buffer, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var enemy = buffer[i].GetComponentInParent<EnemySwordsman>();
            if (enemy == null || !enemy.IsExecutable) continue;

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
