using UnityEngine;

public struct DamageInfo
{
    public GameObject Attacker;
    public float Damage;
    public float PostureDamage;
    public bool Parryable;
    public bool InstantKill;
    public bool ForceKill; // [변경] 궁극기: 즉사 저항(정예 · 방패병 · 돌격병)도 무시
    public bool Critical; // 치명타 여부 (데미지 숫자 표시용)
    public bool BreakArmor; // 휘두르는 중 아머도 무시하고 경직 (뇌락 착지 충격)
    public bool BreakGuard; // 방패병 가드를 한 번에 깸 (차지 강타 2단계 이상)
    public bool KnockDown; // 맞은 쪽을 넘어뜨림 (돌격병 내려베기 → 플레이어 넉다운·기상)
    public float Launch; // 0보다 크면 이 높이(m)로 띄움 (띄우기)
    public bool Slam;    // 내려찍기: 떠 있으면 바닥으로 내리꽂고, 서 있으면 넘어뜨림
    public AttackWeight Weight;
    public Vector3 HitPoint;
    public Vector3 Direction;
}
