using UnityEngine;

// 경직 없이 맞을 때(아머 중 · 보스)의 피격 반응: 상체(가슴 뼈)가 맞은 방향으로 살짝 젖혀졌다 돌아오고,
// 이 적만 아주 짧게 멈춤(개별 히트스톱). 애니메이션 클립 없이 LateUpdate에서 뼈를 돌려서 처리
public partial class EnemySwordsman
{
    [Header("Flinch")]
    [SerializeField] float flinchAngle = 9f;
    [SerializeField] float flinchTime = 0.18f;
    [SerializeField] float flinchFreeze = 0.04f; // 실제 시간. 강공격은 1.6배
    [SerializeField] float heavyFlinchScale = 1.6f;

    Transform flinchBone;
    Vector3 flinchAxis;
    float flinchLeft, flinchScale = 1f, freezeUntil = -1f;

    void Flinch(Vector3 hitDirection, bool heavy)
    {
        if (animator == null) return;
        if (flinchBone == null && animator.isHuman)
        {
            flinchBone = animator.GetBoneTransform(HumanBodyBones.Chest);
            if (flinchBone == null) flinchBone = animator.GetBoneTransform(HumanBodyBones.Spine);
        }

        Vector3 dir = Flat(hitDirection);
        if (dir.sqrMagnitude < 0.01f) dir = -transform.forward;
        flinchAxis = Vector3.Cross(Vector3.up, dir.normalized); // 맞은 방향 쪽으로 젖혀지는 회전축
        flinchScale = heavy ? heavyFlinchScale : 1f;
        flinchLeft = flinchTime;
        freezeUntil = Time.unscaledTime + flinchFreeze * flinchScale;
    }

    bool UpdateFreeze()
    {
        if (Time.unscaledTime >= freezeUntil || state == State.Dead) return false;
        if (animator != null) animator.speed = 0f;
        return true;
    }

    // 핵심: 애니메이터가 포즈를 만든 뒤 가슴 뼈에 회전을 덧붙임 → 어떤 동작 중이든 같은 반응
    void LateUpdate()
    {
        if (flinchLeft <= 0f || flinchBone == null) return;
        flinchLeft -= Time.deltaTime;
        float k = Mathf.Clamp01(flinchLeft / flinchTime);
        flinchBone.rotation = Quaternion.AngleAxis(flinchAngle * flinchScale * k * k, flinchAxis) * flinchBone.rotation;
    }
}
