using DG.Tweening;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class TrainingDummy : MonoBehaviour, IDamageable
{
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    [SerializeField] Renderer bodyRenderer;
    [SerializeField] Color hitColor = new Color(1f, 0.25f, 0.2f);
    [SerializeField] float flashTime = 0.1f;
    [SerializeField] float comboResetTime = 1.5f;
    [SerializeField] float respawnTime = 2f;

    [Header("Debug")]
    [SerializeField] int hitCount;
    [SerializeField] float totalDamage;

    MaterialPropertyBlock block;
    Color baseColor = Color.white;
    float lastHitTime = float.NegativeInfinity;
    Tween flashTween;
    Sequence downSequence;

    Vector3 homePosition;
    Quaternion homeRotation;
    float halfHeight;
    bool isDown;

    public bool CanBeTargeted => !isDown;

    void Awake()
    {
        if (bodyRenderer == null) bodyRenderer = GetComponentInChildren<Renderer>();
        block = new MaterialPropertyBlock();

        Material mat = bodyRenderer != null ? bodyRenderer.sharedMaterial : null;
        if (mat != null && mat.HasProperty(BaseColorId)) baseColor = mat.GetColor(BaseColorId);

        homePosition = transform.position;
        homeRotation = transform.rotation;
        halfHeight = GetComponent<Collider>().bounds.extents.y;
    }

    void OnDestroy()
    {
        flashTween?.Kill();
        downSequence?.Kill();
        transform.DOKill();
    }

    public HitResult ReceiveDamage(DamageInfo info)
    {
        if (isDown) return HitResult.Ignored;

        if (Time.time - lastHitTime > comboResetTime)
        {
            hitCount = 0;
            totalDamage = 0f;
        }

        lastHitTime = Time.time;
        hitCount++;
        totalDamage += info.Damage;

        Flash();

        if (info.InstantKill)
        {
            Debug.Log($"[Dummy] {name} 즉사");
            KnockDown(info.Direction);
            return HitResult.Hit;
        }

        Debug.Log($"[Dummy] {hitCount}타 · 피해 {info.Damage} (누적 {totalDamage})");
        transform.DOKill(true);
        transform.DOPunchPosition(info.Direction * 0.15f, 0.2f, 8, 0.5f);
        transform.DOPunchScale(new Vector3(0.12f, -0.08f, 0.12f), 0.2f, 8, 0.5f);

        return HitResult.Hit;
    }

    // 맞은 방향으로 쓰러졌다가 일정 시간 뒤 제자리로 복귀
    void KnockDown(Vector3 hitDirection)
    {
        isDown = true;
        transform.DOKill(true);

        Vector3 dir = hitDirection.sqrMagnitude > 0.0001f ? hitDirection : transform.forward;
        Vector3 axis = Vector3.Cross(Vector3.up, dir).normalized;
        Quaternion tilt = Quaternion.AngleAxis(85f, axis);
        Quaternion fallen = tilt * homeRotation;

        // 중심이 아니라 발밑을 축으로 넘어지도록 위치도 함께 이동
        Vector3 foot = homePosition - Vector3.up * halfHeight;
        Vector3 fallenPos = foot + tilt * (Vector3.up * halfHeight);

        downSequence?.Kill();
        downSequence = DOTween.Sequence()
            .Append(transform.DORotateQuaternion(fallen, 0.25f).SetEase(Ease.InQuad))
            .Join(transform.DOMove(fallenPos, 0.25f).SetEase(Ease.InQuad))
            .AppendInterval(respawnTime)
            .Append(transform.DORotateQuaternion(homeRotation, 0.3f).SetEase(Ease.OutBack))
            .Join(transform.DOMove(homePosition, 0.3f))
            .OnComplete(() => isDown = false);
    }

    void Flash()
    {
        if (bodyRenderer == null) return;

        SetColor(hitColor);
        flashTween?.Kill();
        flashTween = DOVirtual.DelayedCall(flashTime, () => SetColor(baseColor), false);
    }

    void SetColor(Color color)
    {
        bodyRenderer.GetPropertyBlock(block);
        block.SetColor(BaseColorId, color);
        bodyRenderer.SetPropertyBlock(block);
    }
}
