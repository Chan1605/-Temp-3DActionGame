using System;
using UnityEngine;

// 간파(오공식): 콤보 중 △ 분기 강공격을 휘두르는 초반 window초 안에 적 공격이 들어오면 맞받아 뚫는다
// 패링(PlayerParry)과 달리 전용 모션 없이 △가 그대로 이어지고, 실패해도 그냥 강공격
// 판정은 "적 공격이 실제로 몸에 닿는 순간"(PlayerHealth.ReceiveDamage)에서 함 → 연출이 부딪히는 프레임에 터짐
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

    AttackData windowAttack;
    float windowEnd;

    public bool IsWindowOpen => windowAttack != null && combat != null && combat.CurrentAttack == windowAttack && Time.time <= windowEnd;
    // PlayerChargeAttack이 우클릭을 누른 순간 물어봄: 공격 중이면 패링 대신 △를 바로 예약
    public bool HandlesPress => isActiveAndEnabled && combat != null && combat.IsAttacking && (!comboOnly || combat.CurrentAttack.GetNext(AttackInput.Heavy) != null);

    public event Action<GameObject, Vector3> Clashed; // 연출용 (적, 접촉 지점)
    public event Action<GameObject> Countered;        // 표식·스파크용

    void Awake()
    {
        if (combat == null) combat = GetComponent<PlayerCombat>();
        if (afterimage == null) afterimage = GetComponent<PlayerAfterimage>();
    }

    void OnEnable()
    {
        if (combat != null) combat.AttackStarted += OnAttackStarted;
    }

    void OnDisable()
    {
        if (combat != null) combat.AttackStarted -= OnAttackStarted;
        windowAttack = null;
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

        if (logKanpa) Debug.Log($"[Kanpa] 간파 성공 · {enemy.name} (△ 시작 후 {window - (windowEnd - Time.time):F2}초)");
        return true;
    }

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
