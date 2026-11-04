using UnityEngine;

public struct DamageInfo
{
    public GameObject Attacker;
    public float Damage;
    public float PostureDamage;
    public bool Parryable;
    public bool InstantKill;
    public bool Critical; // 치명타 여부 (데미지 숫자 표시용)
    public bool BreakArmor; // [변경] 휘두르는 중 아머도 무시하고 경직 (뇌락 착지 충격)
    public AttackWeight Weight;
    public Vector3 HitPoint;
    public Vector3 Direction;
}
