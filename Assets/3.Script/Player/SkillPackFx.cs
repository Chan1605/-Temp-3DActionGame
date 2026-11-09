using UnityEngine;

// 벽력일섬 팩 이펙트 (Projectiles 팩 프리팹)
// [변경] 발도 자세 차징(LightningExplosionCharge)은 삭제 (발밑에 보라색 띠로 크게 퍼져 어색함). 자세 연출은 흑백·PlayerVfx 번개로 충분
//  - 돌진: 출발 지점 섬광(Muzzle) + 몸 대신 노란 검기(YellowSwordBeam)가 돌진 경로를 따라감
//  - 일괄 베기 순간: 베인 적마다 검기 타격(YellowSwordBeam_Hit)
// 기존 번개(PlayerVfx)는 그대로 두고 위에 얹음. 프리팹을 비우면 그 연출만 꺼짐
// 연결은 Tools/Setup/Skill Pack Fx
public class SkillPackFx : MonoBehaviour
{
    [SerializeField] PlayerSkill skill;

    [Header("Dash")]
    [SerializeField] GameObject dashMuzzle;
    [SerializeField] GameObject dashBeam;
    [SerializeField] float muzzleScale = 0.7f;
    [SerializeField] float beamScale = 1f;
    [SerializeField] float beamHeight = 1f;

    [Header("Cut")]
    [SerializeField] GameObject cutHit;
    [SerializeField] float cutHitScale = 0.6f;
    [SerializeField] float cutHitHeight = 1f;

    [SerializeField] float lifetime = 3f; // 멈춘 뒤 남은 입자가 사라질 때까지 기다렸다 삭제

    GameObject beamFx;

    void Awake()
    {
        if (skill == null) skill = GetComponent<PlayerSkill>();
    }

    void OnEnable()
    {
        if (skill == null) return;
        skill.Dashed += OnDashed;
        skill.TargetCut += OnTargetCut;
    }

    void OnDisable()
    {
        if (skill != null)
        {
            skill.Dashed -= OnDashed;
            skill.TargetCut -= OnTargetCut;
        }
        Release(ref beamFx);
    }

    void LateUpdate()
    {
        if (skill == null) return; // [변경] 발도 자세 차징 처리 삭제

        // 핵심: 돌진 중(몸이 숨겨진 동안) 검기가 플레이어 자리를 따라가고, 돌진이 끝나면 그 자리에서 꼬리만 남기고 사라짐
        if (beamFx == null) return;
        if (skill.IsInvulnerable) beamFx.transform.position = transform.position + Vector3.up * beamHeight;
        else Release(ref beamFx);
    }

    void OnDashed(Vector3 start, Vector3 end)
    {
        Vector3 dir = end - start;
        dir.y = 0f;
        Quaternion rotation = dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir.normalized, Vector3.up) : transform.rotation;

        if (dashMuzzle != null) Destroy(Spawn(dashMuzzle, start + Vector3.up * beamHeight, rotation, muzzleScale), lifetime);

        Release(ref beamFx); // 연쇄 돌진: 이전 검기는 그 자리에 남김
        if (dashBeam != null) beamFx = Spawn(dashBeam, start + Vector3.up * beamHeight, rotation, beamScale);
    }

    void OnTargetCut(Vector3 position)
    {
        if (cutHit == null) return;
        Vector3 toTarget = position - transform.position;
        toTarget.y = 0f;
        Quaternion rotation = toTarget.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toTarget.normalized, Vector3.up) : transform.rotation;
        Destroy(Spawn(cutHit, position + Vector3.up * cutHitHeight, rotation, cutHitScale), lifetime);
    }

    // 팩 파티클은 Local 스케일 모드라 루트 크기를 바꿔도 안 줄어듦 → 계층 스케일로 바꾼 뒤 크기 조절 (HitFeedback과 같은 방식)
    static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, float scale)
    {
        GameObject fx = Instantiate(prefab, position, rotation);
        if (!Mathf.Approximately(scale, 1f))
        {
            foreach (ParticleSystem ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            fx.transform.localScale *= scale;
        }
        return fx;
    }

    // 새 입자만 멈추고, 이미 나온 입자는 끝까지 보여준 뒤 삭제
    void Release(ref GameObject fx)
    {
        if (fx == null) return;
        fx.transform.SetParent(null, true);
        foreach (ParticleSystem ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        foreach (TrailRenderer trail in fx.GetComponentsInChildren<TrailRenderer>(true))
            trail.emitting = false;
        Destroy(fx, lifetime);
        fx = null;
    }
}
