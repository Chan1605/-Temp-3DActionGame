public interface IDamageable
{
    bool CanBeTargeted { get; }
    HitResult ReceiveDamage(DamageInfo info);
}
