using System.Collections.Generic;
using UnityEngine;

// 공격 차례(토큰) + 맴돌기: 동시에 공격하는 적을 MaxAttackers명으로 제한
// 차례가 없는 적은 공격 거리 바깥 원(사거리 + circleGap)에서 플레이어를 바라본 채 옆걸음으로 돌고, 차례가 비면 들어옴
// → 다수전에서 패링·간파를 "한 명씩" 읽을 수 있게. 보스는 토큰을 쓰지 않음
public partial class EnemySwordsman
{
    static readonly int StrafeHash = Animator.StringToHash("Strafe");
    static readonly int StrafeXHash = Animator.StringToHash("StrafeX");
    static readonly int StrafeZHash = Animator.StringToHash("StrafeZ");
    static readonly List<EnemySwordsman> tokenHolders = new List<EnemySwordsman>();
    static readonly List<EnemySwordsman> alive = new List<EnemySwordsman>();
    public static int MaxAttackers = 2;
    public static IReadOnlyList<EnemySwordsman> Alive => alive; // [변경] HUD·일섬이 흐트러진 적을 찾을 때 (물리 검색 없이)

    [Header("Attack Token")]
    [SerializeField] bool useAttackToken = true;
    [SerializeField] float circleGap = 2f;       // 사거리 바깥으로 이만큼 떨어진 원을 돎
    [SerializeField] float strafeSpeed = 1.6f;
    [SerializeField] Vector2 strafeFlipTime = new Vector2(1.8f, 3.5f);
    [SerializeField] float separationRadius = 1.6f;

    bool hasToken, strafeAnim;
    float strafeSign = 1f, strafeFlipAt;
    Vector3 strafeVelocity;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetTokenStatics()
    {
        tokenHolders.Clear();
        alive.Clear();
        MaxAttackers = 2;
    }

    bool UsesToken => useAttackToken && !IsBoss;

    void OnEnable()
    {
        if (!alive.Contains(this)) alive.Add(this);
    }

    void TokensOnDisable()
    {
        alive.Remove(this);
        ReleaseToken();
    }

    bool CanTakeToken()
    {
        if (!UsesToken || hasToken) return true;
        tokenHolders.RemoveAll(e => e == null || !e.isActiveAndEnabled || e.state == State.Dead);
        return tokenHolders.Count < MaxAttackers;
    }

    bool TryTakeToken()
    {
        if (!CanTakeToken()) return false;
        if (UsesToken && !hasToken)
        {
            tokenHolders.Add(this);
            hasToken = true;
        }
        return true;
    }

    void ReleaseToken()
    {
        if (!hasToken) return;
        tokenHolders.Remove(this);
        hasToken = false;
    }

    // 공격 관련 상태가 아니면 차례 반납, 추적이 아니면 옆걸음 애니메이션 표시 해제
    void OnTokenStateChanged(State next)
    {
        bool attacking = next == State.Windup || next == State.Attack || next == State.ChargeWindup || next == State.Charge;
        if (!attacking) ReleaseToken();
        if (next != State.Chase) strafeAnim = false;
        strafeVelocity = Vector3.zero;
    }

    // 핵심: 차례가 없고 원 안쪽까지 왔으면 원을 따라 돌며 반지름을 맞춤. true면 이번 프레임 이동은 여기서 결정
    bool UpdateCircling(float dist, float dt)
    {
        strafeVelocity = Vector3.zero;
        float ring = data.AttackRange + circleGap;
        if (!UsesToken || hasToken || CanTakeToken() || dist > ring + 1.5f || IsGuardian && GuardReady)
        {
            if (strafeAnim)
            {
                strafeAnim = false;
                PlayMoveState(0.2f);
            }
            return false;
        }

        if (localTime >= strafeFlipAt)
        {
            strafeSign = Random.value < 0.5f ? -1f : 1f;
            strafeFlipAt = localTime + Random.Range(strafeFlipTime.x, strafeFlipTime.y);
        }

        Vector3 toTarget = Flat(target.position - transform.position);
        Vector3 inward = toTarget.sqrMagnitude > 0.01f ? toTarget.normalized : transform.forward;
        Vector3 tangent = Vector3.Cross(Vector3.up, inward) * strafeSign;
        Vector3 v = tangent * strafeSpeed + inward * (Mathf.Clamp(dist - ring, -1f, 1f) * strafeSpeed);
        v += Separation() * strafeSpeed;
        strafeVelocity = Vector3.ClampMagnitude(v, strafeSpeed * 1.3f);
        moveSpeed = 0f;

        if (animator != null && animator.HasState(0, StrafeHash))
        {
            if (!strafeAnim)
            {
                strafeAnim = true;
                animator.CrossFadeInFixedTime(StrafeHash, 0.25f, 0, 0f);
            }
            Vector3 local = transform.InverseTransformDirection(strafeVelocity) / Mathf.Max(strafeSpeed, 0.01f);
            animator.SetFloat(StrafeXHash, local.x, 0.15f, dt);
            animator.SetFloat(StrafeZHash, local.z, 0.15f, dt);
        }
        return true;
    }

    // 가까운 아군에게서 밀려나는 방향 (겹쳐 서지 않게). 부딪히면 도는 방향도 바꿈
    Vector3 Separation()
    {
        Vector3 push = Vector3.zero;
        foreach (EnemySwordsman other in alive)
        {
            if (other == this || other == null || other.state == State.Dead) continue;
            Vector3 away = Flat(transform.position - other.transform.position);
            float d = away.magnitude;
            if (d < 0.01f || d > separationRadius) continue;
            push += away / d * (1f - d / separationRadius);
            if (Vector3.Dot(away, Vector3.Cross(Vector3.up, Flat(target.position - transform.position)) * strafeSign) < 0f)
                strafeFlipAt = Mathf.Min(strafeFlipAt, localTime); // 도는 쪽에 아군이 막고 있으면 곧 방향 전환
        }
        return Vector3.ClampMagnitude(push, 1f);
    }
}
