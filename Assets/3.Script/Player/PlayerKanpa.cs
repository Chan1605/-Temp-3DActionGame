using System;
using UnityEngine;

// 간파(오공식): 콤보 중 △ 분기 강공격을 휘두르는 초반 window초 안에 적 공격이 들어오면 맞받아 뚫는다
// 패링(PlayerParry)과 달리 전용 모션 없이 △가 그대로 이어지고, 실패해도 그냥 강공격
// 판정은 "적 공격이 실제로 몸에 닿는 순간"(PlayerHealth.ReceiveDamage)에서 함 → 연출이 부딪히는 프레임에 터짐
// 뇌락(雷落): 간파 성공 뒤 followWindow(실제 시간) 안에 우클릭 → 뛰어올라 내려찍기. 착지 순간 주변 경직 + 뇌인 +2 + 낙뢰
public class PlayerKanpa : MonoBehaviour
{
    [SerializeField] PlayerCombat combat;
    [SerializeField] PlayerAfterimage afterimage;

    [Header("Judge")]
    [SerializeField] float window = 0.3f;    // △ 시작부터 간파가 되는 시간(초)
    [SerializeField] bool comboOnly = true;  // true면 □ 뒤 △ 분기만, false면 대기 중 △ 첫 타도 포함

    [Header("Counter")]
    [SerializeField] float counterDamage = 40f;
    [SerializeField] float counterPostureDamage = 60f;
    [SerializeField] bool logKanpa = true;

    // 뇌락: 판정·내딛기·히트박스는 일반 공격 흐름(PlayerCombat)을 그대로 타고, 착지(판정 시작) 순간 범위 효과만 여기서
    [Header("Follow-up (뇌락)")]
    [SerializeField] AttackData smashAttack;
    [SerializeField] float followWindow = 0.8f;
    [SerializeField] float smashRadius = 3f;
    [SerializeField] float smashSplashDamage = 15f;  // 주변 적 (대상은 칼에 맞아 smashAttack 피해를 따로 받음)
    [SerializeField] float smashPostureDamage = 40f;

    AttackData windowAttack;
    float windowEnd;
    float followUntil = -1f; // 실제 시간
    bool smashing;

    public bool IsWindowOpen => windowAttack != null && combat != null && combat.CurrentAttack == windowAttack && Time.time <= windowEnd;
    // PlayerChargeAttack이 우클릭을 누른 순간 물어봄: 공격 중이면 패링 대신 △를 바로 예약
    public bool HandlesPress => isActiveAndEnabled && combat != null && combat.IsAttacking && combat.CurrentAttack != null && (!comboOnly || combat.CurrentAttack.GetNext(AttackInput.Heavy) != null); // [변경] 내려찍기 중엔 CurrentAttack이 비어 있음

    public event Action<GameObject, Vector3> Clashed; // 연출용 (적, 접촉 지점)
    public event Action<GameObject> Countered;        // 표식·스파크용

    // 뇌락 이벤트: 입력 창 열림(남은 시간) / 창 닫힘(사용 여부) / 착지(위치)
    public event Action<float> FollowReady;
    public event Action<bool> FollowClosed;
    public event Action<Vector3> SmashLanded;
    public bool IsFollowOpen => followUntil > 0f && Time.unscaledTime <= followUntil;
    public bool IsInvulnerable => smashing; // 뛰어올라 착지할 때까지 무적

    void Awake()
    {
        if (combat == null) combat = GetComponent<PlayerCombat>();
        if (afterimage == null) afterimage = GetComponent<PlayerAfterimage>();
    }

    void OnEnable()
    {
        if (combat != null)
        {
            combat.AttackStarted += OnAttackStarted;
            combat.SwingStarted += OnSwingStarted;
        }
    }

    // 창이 시간 초과로 닫히면 알림 (HUD 프롬프트 · 칼 번개 정리)
    void Update()
    {
        if (followUntil > 0f && Time.unscaledTime > followUntil) CloseFollow(false);
        if (smashing && (combat == null || combat.CurrentAttack != smashAttack)) smashing = false; // 회피·피격 등으로 끊기면 무적 해제
    }

    void OnDisable()
    {
        if (combat != null)
        {
            combat.AttackStarted -= OnAttackStarted;
            combat.SwingStarted -= OnSwingStarted;
        }
        windowAttack = null;
        followUntil = -1f;
        smashing = false;
    }

    void OnAttackStarted(AttackData attack, AttackInput input)
    {
        bool open = input == AttackInput.Heavy && (!comboOnly || combat.CurrentIsBranch);
        windowAttack = open ? attack : null;
        windowEnd = Time.time + window;
    }

    // 핵심: 창이 열려 있고 막을 수 있는 공격이면 적을 경직시키고 true → PlayerHealth는 피해를 무시
    // 적 잔상이 공격 자세로 남도록 연출 호출은 경직 직후 같은 프레임(애니메이션 갱신 전)에 함
    public bool TryCounter(DamageInfo info)
    {
        if (!IsWindowOpen || !info.Parryable || info.Attacker == null) return false;

        IAttackThreat threat = info.Attacker.GetComponentInParent<IAttackThreat>();
        if (threat == null) return false;
        if (threat.ReceiveParry(gameObject) != HitResult.Countered) return false;

        GameObject enemy = ((Component)threat).gameObject;
        Vector3 point = Vector3.Lerp(transform.position, enemy.transform.position, 0.5f) + Vector3.up * 1.2f;

        Clashed?.Invoke(enemy, point);
        if (afterimage != null) afterimage.Spawn();
        Countered?.Invoke(enemy);
        ApplyCounter(enemy);
        if (enemy.TryGetComponent(out EnemySwordsman swordsman)) swordsman.CloseExecution(); // 간파 뒤는 처형 대신 뇌락

        // 뇌락 입력 창 열기
        if (smashAttack != null)
        {
            followUntil = Time.unscaledTime + followWindow;
            FollowReady?.Invoke(followWindow);
        }

        if (logKanpa) Debug.Log($"[Kanpa] 간파 성공 · {enemy.name} (△ 시작 후 {window - (windowEnd - Time.time):F2}초)");
        return true;
    }

    // PlayerChargeAttack이 우클릭을 누른 순간 가장 먼저 물어봄. true면 이번 입력은 뇌락으로 소비
    public bool TryFollowUp()
    {
        if (!IsFollowOpen || smashAttack == null || combat == null) return false;

        CloseFollow(true);
        smashing = true;
        combat.StartChargedAttack(smashAttack, 1f, 0); // 진행 중인 △를 끊고 내려찍기 (차지 단계 0 = 일반 피해)
        if (logKanpa) Debug.Log("[Kanpa] 뇌락!");
        return true;
    }

    void CloseFollow(bool used)
    {
        followUntil = -1f;
        FollowClosed?.Invoke(used);
    }

    // 핵심: 내려찍기 판정이 켜지는 순간 = 착지. 주변 적을 경직시키고 표식·연출 신호
    void OnSwingStarted(AttackData attack)
    {
        if (attack != smashAttack || smashAttack == null) return;
        smashing = false;

        Vector3 center = transform.position + transform.forward * 1f;
        foreach (Collider col in Physics.OverlapSphere(center, smashRadius, ~0, QueryTriggerInteraction.Ignore))
        {
            if (col.transform.IsChildOf(transform)) continue;
            var target = col.GetComponentInParent<EnemySwordsman>();
            if (target == null || !target.CanBeTargeted || !hitSet.Add(target)) continue;

            Vector3 dir = target.transform.position - transform.position;
            dir.y = 0f;
            target.ReceiveDamage(new DamageInfo
            {
                Attacker = gameObject,
                Damage = smashSplashDamage,
                PostureDamage = smashPostureDamage,
                Parryable = false,
                Weight = AttackWeight.Heavy,
                BreakArmor = true,
                HitPoint = target.transform.position + Vector3.up,
                Direction = dir.normalized
            });
        }
        hitSet.Clear();

        SmashLanded?.Invoke(center);
    }

    readonly System.Collections.Generic.HashSet<EnemySwordsman> hitSet = new System.Collections.Generic.HashSet<EnemySwordsman>();

    void ApplyCounter(GameObject enemy)
    {
        var damageable = enemy.GetComponentInParent<IDamageable>();
        if (damageable == null) return;

        Vector3 dir = enemy.transform.position - transform.position;
        dir.y = 0f;

        damageable.ReceiveDamage(new DamageInfo
        {
            Attacker = gameObject,
            Damage = counterDamage,
            PostureDamage = counterPostureDamage,
            Parryable = false,
            Weight = AttackWeight.Parry,
            HitPoint = enemy.transform.position + Vector3.up,
            Direction = dir.normalized
        });
    }
}
