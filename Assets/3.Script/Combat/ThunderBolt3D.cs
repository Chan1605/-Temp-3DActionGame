using System.Collections.Generic;
using UnityEngine;

// 입체 낙뢰: 지그재그 경로를 따라 실제 굵기가 있는 관(튜브) 메시로 그림 → 어느 각도에서 봐도 납작하지 않음
//  가는 흰 심 + 넓고 옅은 푸른 빛 껍질 + 곁가지 2~3개, 짧게 2~3번 모양을 바꾸며 깜빡이다 사라짐, 바닥에 순간 불빛
//  카메라 가까이 지나가는 낙뢰는 빛 껍질을 끄고 흐리게 (화면을 하얗게 덮지 않게)
// 사용: ThunderBolt3D.Strike(하늘, 바닥, 굵기 배율, 곁가지 수). 오브젝트는 재사용(풀)
// [변경] 긴 낙뢰(뇌신강림)가 매끈한 네온 막대처럼 보여 이질적 → 더 많이 꺾고, 빛 껍질을 가늘고 옅게, 긴 줄기엔 곁가지를 더
public class ThunderBolt3D : MonoBehaviour
{
    const int Sides = 6;
    const float Life = 0.26f;
    const float CoreRadius = 0.045f;
    const float GlowRadius = 0.14f; // [변경] 0.32 → 0.14 (굵은 막대 느낌 제거)
    const float NearCamera = 6f;

    static readonly Color CoreColor = new Color(2.4f, 2.7f, 4f, 1f);    // HDR → 블룸으로 번짐
    static readonly Color GlowColor = new Color(0.55f, 0.7f, 1.5f, 0.13f); // [변경] 옅게
    static Material coreMaterial, glowMaterial;
    static readonly Stack<ThunderBolt3D> pool = new Stack<ThunderBolt3D>();

    MeshRenderer coreRenderer, glowRenderer;
    Mesh coreMesh, glowMesh;
    MaterialPropertyBlock block;
    Light flash;

    Vector3 from, to;
    float thickness, age, nextReshape, nearFade;
    int branches;
    readonly List<Vector3> path = new List<Vector3>();
    readonly List<Vector3> verts = new List<Vector3>();
    readonly List<int> tris = new List<int>();
    readonly List<Color> colors = new List<Color>();

    public static void Strike(Vector3 sky, Vector3 ground, float thickness = 1f, int branches = 2)
    {
        ThunderBolt3D bolt = pool.Count > 0 ? pool.Pop() : Create();
        if (bolt == null) bolt = Create();
        bolt.Begin(sky, ground, thickness, branches);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetPool() => pool.Clear();

    static ThunderBolt3D Create()
    {
        if (coreMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default"); // 기본 포함 셰이더 (빌드에서도 있음)
            coreMaterial = new Material(shader) { color = Color.white };
            glowMaterial = new Material(shader) { color = Color.white };
        }
        var go = new GameObject("ThunderBolt3D");
        DontDestroyOnLoad(go);
        var bolt = go.AddComponent<ThunderBolt3D>();
        bolt.Build();
        return bolt;
    }

    void Build()
    {
        block = new MaterialPropertyBlock();
        coreMesh = new Mesh { name = "BoltCore" };
        glowMesh = new Mesh { name = "BoltGlow" };
        coreMesh.MarkDynamic();
        glowMesh.MarkDynamic();
        coreRenderer = Part("Core", coreMesh, coreMaterial);
        glowRenderer = Part("Glow", glowMesh, glowMaterial);

        var lightGo = new GameObject("Flash");
        lightGo.transform.SetParent(transform, false);
        flash = lightGo.AddComponent<Light>();
        flash.type = LightType.Point;
        flash.color = new Color(0.65f, 0.75f, 1f);
        flash.shadows = LightShadows.None;
        gameObject.SetActive(false);
    }

    MeshRenderer Part(string name, Mesh mesh, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }

    void Begin(Vector3 sky, Vector3 ground, float scale, int branchCount)
    {
        from = sky;
        to = ground;
        thickness = Mathf.Max(0.2f, scale);
        branches = branchCount;
        age = 0f;
        nextReshape = 0f;
        transform.position = Vector3.zero;
        flash.transform.position = ground + Vector3.up * 0.8f;
        flash.range = 10f * thickness;

        // 카메라가 낙뢰 줄기 가까이 있으면 흐리게 (0 = 바로 앞, 1 = 충분히 멂)
        Camera cam = Camera.main;
        nearFade = 1f;
        if (cam != null)
        {
            float d = DistanceToSegment(cam.transform.position, sky, ground);
            nearFade = Mathf.Clamp01((d - 1.5f) / (NearCamera - 1.5f));
        }
        gameObject.SetActive(true);
        Update();
    }

    void Update()
    {
        age += Time.unscaledDeltaTime;
        if (age >= Life)
        {
            gameObject.SetActive(false);
            pool.Push(this);
            return;
        }
        // 핵심: 짧게 몇 번 모양을 새로 뽑아 깜빡이는 느낌
        if (age >= nextReshape)
        {
            nextReshape += Life / 3f;
            Rebuild();
        }

        float k = age / Life;
        float flicker = Mathf.Lerp(1f, 0.35f, k) * (Random.value < 0.25f ? 0.55f : 1f);
        float core = flicker * Mathf.Lerp(0.35f, 1f, nearFade);
        block.SetColor("_Color", new Color(CoreColor.r * core, CoreColor.g * core, CoreColor.b * core, 1f));
        coreRenderer.SetPropertyBlock(block);
        float glow = flicker * nearFade;
        block.SetColor("_Color", new Color(GlowColor.r, GlowColor.g, GlowColor.b, GlowColor.a * glow));
        glowRenderer.SetPropertyBlock(block);
        glowRenderer.enabled = glow > 0.02f;
        flash.intensity = 7f * (1f - k) * (1f - k) * thickness;
    }

    void Rebuild()
    {
        verts.Clear();
        tris.Clear();
        MakePath(from, to, 0.17f); // [변경] 0.11 → 0.17 더 꺾이게
        AddTube(path, CoreRadius * thickness, 0.7f);
        List<Vector3> main = new List<Vector3>(path);
        int count = branches + Mathf.FloorToInt((to - from).magnitude / 12f); // [변경] 길수록 곁가지 추가
        for (int b = 0; b < count; b++)
        {
            int at = Random.Range(main.Count / 5, main.Count * 3 / 4);
            Vector3 start = main[at];
            Vector3 down = (to - from).normalized;
            Vector3 side = Vector3.Cross(down, Random.onUnitSphere).normalized;
            float len = (to - from).magnitude * Random.Range(0.12f, 0.25f);
            Vector3 end = start + (down * 0.6f + side * 0.8f).normalized * len;
            MakePath(start, end, 0.22f);
            AddTube(path, CoreRadius * thickness * 0.6f, 0.15f); // 끝으로 갈수록 가늘게
        }
        Apply(coreMesh);

        // 빛 껍질은 주 줄기만, 같은 모양으로 굵게
        verts.Clear();
        tris.Clear();
        AddTube(main, GlowRadius * thickness, 0.4f);
        Apply(glowMesh);
    }

    // 중간점을 옆으로 밀어내며 반으로 쪼개는 방식 (번개 특유의 꺾임)
    void MakePath(Vector3 a, Vector3 b, float roughness)
    {
        path.Clear();
        path.Add(a);
        path.Add(b);
        float offset = (b - a).magnitude * roughness;
        for (int gen = 0; gen < 6; gen++) // [변경] 5 → 6세대 (잔 꺾임)
        {
            for (int i = path.Count - 1; i > 0; i--)
            {
                Vector3 mid = (path[i - 1] + path[i]) * 0.5f;
                Vector3 dir = (path[i] - path[i - 1]).normalized;
                Vector3 push = Vector3.ProjectOnPlane(Random.insideUnitSphere, dir) * offset;
                path.Insert(i, mid + push);
            }
            offset *= 0.5f;
        }
    }

    void AddTube(List<Vector3> points, float radius, float endScale)
    {
        int start = verts.Count;
        Vector3 prevNormal = Vector3.zero;
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 tangent = i < points.Count - 1 ? points[i + 1] - points[i] : points[i] - points[i - 1];
            tangent.Normalize();
            Vector3 normal = Vector3.Cross(tangent, Mathf.Abs(tangent.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            if (i > 0 && Vector3.Dot(normal, prevNormal) < 0f) normal = -normal;
            prevNormal = normal;
            Vector3 binormal = Vector3.Cross(tangent, normal);
            float r = radius * Mathf.Lerp(1f, endScale, i / (float)(points.Count - 1));
            for (int s = 0; s < Sides; s++)
            {
                float a = s * Mathf.PI * 2f / Sides;
                verts.Add(points[i] + (normal * Mathf.Cos(a) + binormal * Mathf.Sin(a)) * r);
            }
        }
        for (int i = 0; i < points.Count - 1; i++)
            for (int s = 0; s < Sides; s++)
            {
                int a = start + i * Sides + s;
                int b = start + i * Sides + (s + 1) % Sides;
                int c = a + Sides, d = b + Sides;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }
    }

    void Apply(Mesh mesh)
    {
        mesh.Clear();
        colors.Clear();
        for (int i = 0; i < verts.Count; i++) colors.Add(Color.white); // 정점 색이 없으면 셰이더에 따라 검게 나올 수 있음
        mesh.SetVertices(verts);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
    }

    static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
        return Vector3.Distance(p, a + ab * t);
    }
}
