using UnityEngine;

public struct DamageInfo
{
    public GameObject Attacker;
    public float Damage;
    public float PostureDamage;
    public bool Parryable;
    public bool InstantKill;
    public bool Critical; // [변경] 치명타 여부 (데미지 숫자 표시용)
    public AttackWeight Weight;
    public Vector3 HitPoint;
    public Vector3 Direction;
}
