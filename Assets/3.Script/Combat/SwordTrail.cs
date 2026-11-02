using System.Collections.Generic;
using UnityEngine;

// 칼날의 손잡이 쪽·끝 쪽 두 점을 매 프레임 기록해 리본 메시로 잇는 검 궤적.
// 월드 공간에 따로 그려서 칼이 움직여도 지나간 자리가 남는다.
public class SwordTrail : MonoBehaviour
{
    struct Sample
    {
        public Vector3 Base;
        public Vector3 Tip;
        public float Time;
    }

    [SerializeField] BoxCollider blade;
    [SerializeField] Transform handReference;
    [SerializeField] Material material;
    // [변경] 칼끝 쪽 35%만 남기고 짧게 → 판처럼 보이지 않고 검광처럼 보이게. 밝기는 머티리얼 HDR 색으로 올림
    [SerializeField] Color color = new Color(0.35f, 0.65f, 1f, 0.9f);
    [SerializeField] float lifetime = 0.1f;
    [SerializeField, Range(0f, 0.9f)] float baseRatio = 0.65f;
    [SerializeField] int subdivisions = 4;
    [SerializeField] int maxSamples = 40;

    readonly List<Sample> samples = new List<Sample>();
    readonly List<Vector3> vertices = new List<Vector3>();
    readonly List<Color> colors = new List<Color>();
    readonly List<int> triangles = new List<int>();

    Mesh mesh;
    GameObject meshObject;
    bool emitting;
    float clock;

    public bool Emitting => emitting;

    void Awake()
    {
        if (blade == null) blade = GetComponent<BoxCollider>();
        if (handReference == null && blade != null) handReference = blade.transform.parent;

        mesh = new Mesh { name = "SwordTrail" };
        mesh.MarkDynamic();

        meshObject = new GameObject($"{name}_SwordTrail");
        meshObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = meshObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    void OnDestroy()
    {
        if (meshObject != null) Destroy(meshObject);
        if (mesh != null) Destroy(mesh);
    }

    public void SetEmitting(bool value)
    {
        emitting = value;
    }

    // 히트스톱(timeScale)이 걸리면 궤적도 같이 멈춰서 타격 순간이 남아 보이게 게임 시간 기준
    void LateUpdate()
    {
        if (blade == null || material == null) return;

        clock += Time.deltaTime;
        if (emitting) AddSample();

        while (samples.Count > 0 && clock - samples[0].Time > lifetime) samples.RemoveAt(0);
        BuildMesh();
    }

    void AddSample()
    {
        GetBladeEnds(out Vector3 hilt, out Vector3 tip);
        var s = new Sample { Base = Vector3.Lerp(hilt, tip, baseRatio), Tip = tip, Time = clock };

        // 같은 프레임에 두 번 들어오지 않게
        if (samples.Count > 0 && Mathf.Approximately(samples[samples.Count - 1].Time, clock)) samples[samples.Count - 1] = s;
        else samples.Add(s);

        if (samples.Count > maxSamples) samples.RemoveAt(0);
    }

    // 박스의 가장 긴 축 양 끝 중 손에서 먼 쪽을 칼끝으로 본다
    void GetBladeEnds(out Vector3 hilt, out Vector3 tip)
    {
        Vector3 size = blade.size;
        int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
        Vector3 half = Vector3.zero;
        half[axis] = size[axis] * 0.5f;

        Transform t = blade.transform;
        Vector3 a = t.TransformPoint(blade.center + half);
        Vector3 b = t.TransformPoint(blade.center - half);

        Vector3 hand = handReference != null ? handReference.position : t.position;
        bool aIsTip = (a - hand).sqrMagnitude >= (b - hand).sqrMagnitude;
        tip = aIsTip ? a : b;
        hilt = aIsTip ? b : a;
    }

    // 프레임 사이가 각져 보이지 않게 Catmull-Rom으로 샘플 사이를 보간
    void BuildMesh()
    {
        mesh.Clear();
        if (samples.Count < 2) return;

        vertices.Clear();
        colors.Clear();
        triangles.Clear();

        int steps = Mathf.Max(1, subdivisions);
        for (int i = 0; i < samples.Count - 1; i++)
        {
            Sample p0 = samples[Mathf.Max(0, i - 1)];
            Sample p1 = samples[i];
            Sample p2 = samples[i + 1];
            Sample p3 = samples[Mathf.Min(samples.Count - 1, i + 2)];

            int last = i == samples.Count - 2 ? steps : steps - 1;
            for (int k = 0; k <= last; k++)
            {
                float u = k / (float)steps;
                Vector3 basePos = CatmullRom(p0.Base, p1.Base, p2.Base, p3.Base, u);
                Vector3 tipPos = CatmullRom(p0.Tip, p1.Tip, p2.Tip, p3.Tip, u);
                float time = Mathf.Lerp(p1.Time, p2.Time, u);

                float life = 1f - Mathf.Clamp01((clock - time) / lifetime);
                float alpha = color.a * life * life;

                vertices.Add(basePos);
                vertices.Add(tipPos);
                colors.Add(new Color(color.r, color.g, color.b, alpha * 0.15f));
                colors.Add(new Color(color.r, color.g, color.b, alpha));
            }
        }

        for (int v = 0; v + 3 < vertices.Count; v += 2)
        {
            triangles.Add(v); triangles.Add(v + 1); triangles.Add(v + 2);
            triangles.Add(v + 2); triangles.Add(v + 1); triangles.Add(v + 3);
        }

        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
    }
}
