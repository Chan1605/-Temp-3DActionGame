using UnityEngine;

// 보스 검기: 일직선으로 날아가며 닿은 플레이어에게 한 번 피해 (패링 불가, 회피 무적이면 통과)
// 적 시간(CombatTime)을 따라 느려짐 → 저스트 회피 슬로 중에도 맞춰 느려짐
public class BossSwordWave : MonoBehaviour
{
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    readonly Collider[] buffer = new Collider[16];
    Transform owner;
    DamageInfo info;
    float speed, range, halfWidth, traveled;
    bool touched, done;
    GameObject hitVfx;
    Material ownMaterial;

    public static void Fire(GameObject owner, Vector3 position, Vector3 direction, float speed, float range, float width,
        DamageInfo info, GameObject visualPrefab, GameObject hitVfx, Material fallbackMaterial)
    {
        var go = new GameObject("BossSwordWave");
        go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction, Vector3.up));
        var wave = go.AddComponent<BossSwordWave>();
        wave.owner = owner != null ? owner.transform : null;
        wave.info = info;
        wave.speed = speed;
        wave.range = range;
        wave.halfWidth = width * 0.5f;
        wave.hitVfx = hitVfx;

        if (visualPrefab != null)
        {
            Instantiate(visualPrefab, go.transform, false);
            return;
        }

        // 기본 모양: 가로로 넓은 얇은 붉은 판
        GameObject blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(blade.GetComponent<Collider>());
        blade.transform.SetParent(go.transform, false);
        blade.transform.localScale = new Vector3(width, 0.08f, 0.35f);
        var r = blade.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        wave.ownMaterial = new Material(fallbackMaterial != null ? fallbackMaterial : BossTelegraph.RuntimeMaterial());
        wave.ownMaterial.SetColor(BaseColorId, new Color(1f, 0.2f, 0.15f, 0.8f));
        r.sharedMaterial = wave.ownMaterial;
    }

    void Update()
    {
        if (done) return;

        float step = speed * Time.deltaTime * CombatTime.EnemyScale;
        if (!touched) CheckHit(step);

        transform.position += transform.forward * step;
        traveled += step;
        if (traveled >= range) Finish();
    }

    // 이번 프레임에 지나갈 구간 전체를 상자로 검사 (빠른 속도에도 건너뛰지 않게)
    void CheckHit(float step)
    {
        Vector3 center = transform.position + transform.forward * (step * 0.5f);
        Vector3 half = new Vector3(halfWidth, 1f, step * 0.5f + 0.4f);
        int count = Physics.OverlapBoxNonAlloc(center, half, buffer, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider col = buffer[i];
            if (owner != null && col.transform.IsChildOf(owner)) continue;
            var player = col.GetComponentInParent<PlayerHealth>();
            if (player == null) continue;

            touched = true; // 피했든 맞았든 한 번만
            DamageInfo hit = info;
            hit.HitPoint = col.ClosestPoint(center);
            hit.Direction = transform.forward;
            if (player.ReceiveDamage(hit) == HitResult.Hit)
            {
                if (hitVfx != null) Destroy(Instantiate(hitVfx, hit.HitPoint, transform.rotation), 2f);
                Finish();
            }
            return;
        }
    }

    // 파티클은 방출만 멈추고 남은 입자가 사라진 뒤 삭제
    void Finish()
    {
        done = true;
        foreach (ParticleSystem ps in GetComponentsInChildren<ParticleSystem>())
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
            if (!(r is ParticleSystemRenderer)) r.enabled = false;
        Destroy(gameObject, 1.2f);
    }

    void OnDestroy()
    {
        if (ownMaterial != null) Destroy(ownMaterial);
    }
}
