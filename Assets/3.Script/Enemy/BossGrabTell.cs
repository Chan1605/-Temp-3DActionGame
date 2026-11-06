using UnityEngine;

// 보스 잡기 예고: 양손에 붉은 빛 구슬 + 붉은 점광원. 예고(BossGrabWindup) 동안 커지며 맥박치고, 달려드는 순간 가장 밝음
// 보스의 GrabTell01(0~1)만 읽어 스스로 켜고 끔 → 경직·사망·재시작으로 예고가 끊겨도 따로 정리할 필요 없음
public class BossGrabTell : MonoBehaviour
{
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly Color OrbColor = new Color(3f, 0.18f, 0.08f); // HDR → 블룸으로 번짐

    EnemySwordsman owner;
    Transform leftHand, rightHand;
    readonly Transform[] orbs = new Transform[2];
    Material material;
    Light glow;
    float size;

    public static BossGrabTell Create(EnemySwordsman owner, Animator animator)
    {
        if (animator == null || !animator.isHuman) return null;
        var go = new GameObject("BossGrabTell");
        var tell = go.AddComponent<BossGrabTell>();
        tell.owner = owner;
        tell.leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        tell.rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        tell.size = 0.22f * owner.transform.lossyScale.x;
        tell.material = new Material(BossTelegraph.RuntimeMaterial()) { name = "BossGrabTell" };

        for (int i = 0; i < 2; i++)
        {
            GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(orb.GetComponent<Collider>());
            orb.transform.SetParent(go.transform, false);
            var r = orb.GetComponent<Renderer>();
            r.sharedMaterial = tell.material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            tell.orbs[i] = orb.transform;
        }

        tell.glow = new GameObject("Glow").AddComponent<Light>();
        tell.glow.transform.SetParent(go.transform, false);
        tell.glow.type = LightType.Point;
        tell.glow.color = new Color(1f, 0.15f, 0.08f);
        tell.glow.shadows = LightShadows.None;
        tell.SetVisible(false);
        return tell;
    }

    void LateUpdate()
    {
        if (owner == null)
        {
            Destroy(gameObject);
            return;
        }

        float k = owner.GrabTell01;
        if (k <= 0f || leftHand == null || rightHand == null)
        {
            SetVisible(false);
            return;
        }
        SetVisible(true);

        // 시작부터 바로 보이게 0.6배에서 시작해 커지고, 빠르게 맥박침
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 20f);
        float s = size * Mathf.Lerp(0.6f, 1.25f, k) * (0.85f + 0.3f * pulse);
        orbs[0].position = leftHand.position;
        orbs[1].position = rightHand.position;
        orbs[0].localScale = orbs[1].localScale = Vector3.one * s;

        Color c = OrbColor * Mathf.Lerp(0.7f, 1.3f, k);
        c.a = Mathf.Lerp(0.55f, 0.95f, k);
        material.SetColor(BaseColorId, c);

        glow.transform.position = (leftHand.position + rightHand.position) * 0.5f;
        glow.range = 3.5f * owner.transform.lossyScale.x;
        glow.intensity = Mathf.Lerp(2f, 7f, k) * (0.8f + 0.4f * pulse);
    }

    void SetVisible(bool on)
    {
        foreach (Transform orb in orbs)
            if (orb != null && orb.gameObject.activeSelf != on) orb.gameObject.SetActive(on);
        if (glow != null) glow.enabled = on;
    }

    void OnDestroy()
    {
        if (material != null) Destroy(material);
    }
}
