using UnityEngine;

public struct DamageInfo
{
    public GameObject Attacker;
    public float Damage;
    public float PostureDamage;
    public bool Parryable;
    public bool InstantKill;
    public Vector3 HitPoint;
    public Vector3 Direction;
}
