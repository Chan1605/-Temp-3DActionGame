using System.Collections.Generic;
using UnityEngine;

// 칼 표시: 전투 자세 = 손에 든 칼, 평상시 = 허리 칼집 속 칼 (Samurai 모델의 weapon/katana, 골반에 붙은 스킨 메시)
// 애니메이션 팩 클립은 평상시에도 weapon_r로 칼을 허리 쪽에 옮기는데, 그 값이 예전 모델의 손 뼈 기준이라 새 모델에선 엉뚱한 곳에 보임
// → 평상시엔 손의 칼을 그리지 않고 칼집 속 칼을 보여줌
// [변경] 기본은 납도 모션을 클립 그대로 끝까지 보여준 뒤(swapAt) 바꿈 → Animation Picker 미리보기와 같은 모습
//        pullIntoSheath를 켜면 insertStart~swapAt 구간에서 손의 칼을 칼집 속 칼 자리로 끌어당김 (시도안)
// renderer.forceRenderingOff만 써서, 일섬 중 몸 숨기기(enabled)·칼 종류 전환(active)과 겹치지 않음
// PlayerStance가 실행 중 자동으로 붙임. 칼집 속 칼이 없는 모델이면 아무것도 하지 않음
public class SheathedSwordVisual : MonoBehaviour
{
    [SerializeField] string handSwordRoot = "weapon_r";
    [SerializeField] string sheathedSwordPath = "weapon/katana";
    [SerializeField, Range(0f, 1f)] float swapAt = 0.92f;      // [변경] 납도 모션이 끝나는 지점(PlayerStance.sheatheEnd)에서 칼집 속 칼로 바꿈. 새 이름이라 저장된 0.53 대신 바로 적용
    [SerializeField] bool pullIntoSheath = false;               // [변경] 끌어당기기는 끔 (미리보기와 달라 보였음)
    [SerializeField, Range(0f, 1f)] float insertStart = 0.36f; // 끌어당기기를 켰을 때 시작 지점

    class HandSword
    {
        public Transform Root;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public float Length; // 날밑(원점)부터 칼끝까지, 월드 기준 (처음 측정)
    }

    PlayerStance stance;
    readonly List<Renderer> handRenderers = new List<Renderer>();
    readonly List<HandSword> handSwords = new List<HandSword>();
    Renderer sheathed;
    Transform sheathBone;           // 칼집 속 칼이 붙은 뼈 (골반)
    Vector3 sheathGuardLocal;       // 그 뼈 기준 날밑 위치·칼날 방향
    Vector3 sheathDirLocal;
    float sheathLength;
    bool canInsert;
    int shownInHand = -1;

    void Awake()
    {
        stance = GetComponent<PlayerStance>();
        Collect();
    }

    void Collect()
    {
        Animator animator = GetComponentInChildren<Animator>(true);
        if (animator == null) return;
        Transform model = animator.transform;

        Transform sheathedTransform = model.Find(sheathedSwordPath);
        if (sheathedTransform == null) return;
        sheathedTransform.gameObject.SetActive(true); // 스킨 셋업에서 꺼 둔 칼집 속 칼
        sheathed = sheathedTransform.GetComponent<Renderer>();

        Transform hand = FindDeep(model, handSwordRoot);
        if (hand == null) return;
        foreach (Renderer r in hand.GetComponentsInChildren<Renderer>(true))
        {
            if (r is TrailRenderer || r is ParticleSystemRenderer) continue;
            if (r.GetComponentInParent<WeaponHitbox>(true) != null) continue; // 판정 쪽은 건드리지 않음
            handRenderers.Add(r);
        }

        // 손의 칼 = 렌더러를 가진 칼 오브젝트(ZenitsuKatana·SamuraiKatana). 원점이 날밑, +Y가 칼날
        foreach (string name in new[] { "ZenitsuKatana", "SamuraiKatana" })
        {
            Transform root = FindDeep(hand, name);
            if (root == null) continue;
            handSwords.Add(new HandSword
            {
                Root = root,
                Position = root.localPosition,
                Rotation = root.localRotation,
                Scale = root.localScale,
                Length = MeasureLength(root)
            });
        }

        canInsert = MeasureSheath(sheathedTransform.GetComponent<SkinnedMeshRenderer>());
    }

    void LateUpdate()
    {
        if (sheathed == null || stance == null) return;

        // 지난 프레임에 끌어당긴 칼을 원래 자리로 (로컬값만 되돌림)
        foreach (HandSword s in handSwords)
        {
            s.Root.localPosition = s.Position;
            s.Root.localRotation = s.Rotation;
            s.Root.localScale = s.Scale;
        }

        // 핵심: 전투 자세이거나, 납도 모션이 아직 칼집에 닿기 전이면 손에 든 칼
        float progress = stance.SheatheProgress;
        bool sheathing = progress >= 0f;
        bool inHand = stance.InCombatStance || (sheathing && progress < swapAt); // [변경]

        if (pullIntoSheath && inHand && sheathing && canInsert && progress > insertStart) // [변경]
        {
            float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(insertStart, swapAt, progress));
            foreach (HandSword s in handSwords)
                if (s.Root.gameObject.activeInHierarchy) PullToSheath(s, t);
        }

        int state = inHand ? 1 : 0;
        if (state == shownInHand) return;
        shownInHand = state;

        foreach (Renderer r in handRenderers)
            if (r != null) r.forceRenderingOff = !inHand;
        sheathed.forceRenderingOff = inHand;
    }

    // 날밑을 칼집 속 칼의 날밑으로, 칼날 방향을 칼집 방향으로, 길이를 칼집 속 칼 길이로 t만큼 맞춤
    void PullToSheath(HandSword s, float t)
    {
        Vector3 guard = sheathBone.TransformPoint(sheathGuardLocal);
        Vector3 dir = sheathBone.TransformDirection(sheathDirLocal).normalized;

        Quaternion targetRotation = Quaternion.FromToRotation(s.Root.up, dir) * s.Root.rotation;
        s.Root.SetPositionAndRotation(Vector3.Lerp(s.Root.position, guard, t), Quaternion.Slerp(s.Root.rotation, targetRotation, t));
        if (s.Length > 0.01f) s.Root.localScale = s.Scale * Mathf.Lerp(1f, sheathLength / s.Length, t);
    }

    static float MeasureLength(Transform root)
    {
        float length = 0f;
        foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            Bounds b = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                length = Mathf.Max(length, Vector3.Dot(mf.transform.TransformPoint(corner) - root.position, root.up));
            }
        }
        return length;
    }

    // 칼집 속 칼을 한 번 구워서(BakeMesh) 가장 긴 축 = 칼 축, 가장 굵은 곳 = 날밑, 날밑에서 긴 쪽 = 칼날로 보고 골반 기준으로 저장
    bool MeasureSheath(SkinnedMeshRenderer smr)
    {
        if (smr == null) return false;
        sheathBone = smr.rootBone != null ? smr.rootBone : smr.transform;

        var baked = new Mesh();
        smr.BakeMesh(baked, true);
        Vector3[] verts = baked.vertices;
        Destroy(baked);
        if (verts.Length < 10) return false;

        Vector3 mean = Vector3.zero;
        for (int i = 0; i < verts.Length; i++)
        {
            verts[i] = smr.transform.TransformPoint(verts[i]);
            mean += verts[i];
        }
        mean /= verts.Length;

        float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        foreach (Vector3 v in verts)
        {
            Vector3 d = v - mean;
            xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z;
            yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z;
        }
        Vector3 axis = new Vector3(1f, 1f, 1f).normalized;
        for (int i = 0; i < 32; i++)
            axis = new Vector3(xx * axis.x + xy * axis.y + xz * axis.z, xy * axis.x + yy * axis.y + yz * axis.z, xz * axis.x + yz * axis.y + zz * axis.z).normalized;

        float tMin = float.MaxValue, tMax = float.MinValue;
        foreach (Vector3 v in verts)
        {
            float t = Vector3.Dot(v - mean, axis);
            tMin = Mathf.Min(tMin, t);
            tMax = Mathf.Max(tMax, t);
        }

        const int Bins = 40;
        var radius = new float[Bins];
        float span = Mathf.Max(tMax - tMin, 0.0001f);
        foreach (Vector3 v in verts)
        {
            Vector3 d = v - mean;
            float t = Vector3.Dot(d, axis);
            int bin = Mathf.Clamp((int)((t - tMin) / span * Bins), 0, Bins - 1);
            radius[bin] = Mathf.Max(radius[bin], (d - axis * t).magnitude);
        }
        int guardBin = 0;
        for (int i = 1; i < Bins; i++) if (radius[i] > radius[guardBin]) guardBin = i;
        float tGuard = tMin + (guardBin + 0.5f) * span / Bins;

        bool positive = tMax - tGuard >= tGuard - tMin;
        Vector3 dir = positive ? axis : -axis;
        sheathLength = positive ? tMax - tGuard : tGuard - tMin;
        sheathGuardLocal = sheathBone.InverseTransformPoint(mean + axis * tGuard);
        sheathDirLocal = sheathBone.InverseTransformDirection(dir);
        return sheathLength > 0.05f;
    }

    static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            Transform found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
