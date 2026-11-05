using UnityEngine;

public struct DamageInfo
{
    public GameObject Attacker;
    public float Damage;
    public float PostureDamage;
    public bool Parryable;
    public bool InstantKill;
    public bool Critical; // 치명타 여부 (데미지 숫자 표시용)
    public bool BreakArmor; // 휘두르는 중 아머도 무시하고 경직 (뇌락 착지 충격)
    public bool BreakGuard; // 방패병 가드를 한 번에 깸 (차지 강타 2단계 이상)
    public bool KnockDown; // [변경] 맞은 쪽을 넘어뜨림 (돌격병 내려베기 → 플레이어 넉다운·기상)
    public AttackWeight Weight;
    public Vector3 HitPoint;
    public Vector3 Direction;
}
