using System.Collections.Generic;
using UnityEngine;

// 일정 간격으로 플레이어의 현재 포즈를 메시로 구워(BakeMesh) 반투명 복사본을 남기고 서서히 지운다
public class PlayerAfterimage : MonoBehaviour
{
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    class Ghost
    {
        public GameObject Root;
        public Transform[] Parts;
        public MeshFilter[] Filters;
        public MeshRenderer[] Renderers;
        public float Age;
        public bool Alive;
    }

    [SerializeField] Material material;
    [SerializeField] Color color = new Color(0.45f, 0.75f, 1f, 0.6f);
    [SerializeField] float interval = 0.05f;
    [SerializeField] float lifetime = 0.35f;
    [SerializeField] int poolSize = 16;
    [SerializeField] Transform sourceRoot;

    readonly List<Ghost> pool = new List<Ghost>();
    SkinnedMeshRenderer[] skinned;
    MeshFilter[] statics;
    MaterialPropertyBlock block;

    float emitUntil;
    float emitTimer;
    int next;

    public bool IsEmitting => Time.unscaledTime < emitUntil;

    void Awake()
    {
        if (sourceRoot == null)
        {
            Animator animator = GetComponentInChildren<Animator>();
            sourceRoot = animator != null ? animator.transform : transform;
        }

        skinned = sourceRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        statics = CollectStaticMeshes();
        block = new MaterialPropertyBlock();

        for (int i = 0; i < poolSize; i++) pool.Add(CreateGhost(i));
    }

    void OnDestroy()
    {
        foreach (Ghost g in pool)
        {
            if (g.Root == null) continue;
            for (int i = 0; i < skinned.Length; i++)
                if (g.Filters[i] != null) Destroy(g.Filters[i].sharedMesh);
            Destroy(g.Root);
        }
    }

    // 실제 시간 기준이라 적 슬로·히트스톱 중에도 일정하게 남고 사라짐
    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        if (IsEmitting)
        {
            emitTimer -= dt;
            if (emitTimer <= 0f)
            {
                Spawn();
                emitTimer = interval;
            }
        }

        foreach (Ghost g in pool)
        {
            if (!g.Alive) continue;

            g.Age += dt;
            if (g.Age >= lifetime)
            {
                g.Alive = false;
                g.Root.SetActive(false);
                continue;
            }

            float k = 1f - g.Age / lifetime;
            SetAlpha(g, color.a * k * k);
        }
    }

    public void Emit(float duration)
    {
        if (!IsEmitting) emitTimer = 0f;
        emitUntil = Mathf.Max(emitUntil, Time.unscaledTime + duration);
    }

    public void Spawn()
    {
        if (material == null || pool.Count == 0) return;

        Ghost g = pool[next];
        next = (next + 1) % pool.Count;

        for (int i = 0; i < skinned.Length; i++)
        {
            SkinnedMeshRenderer src = skinned[i];
            bool visible = src != null && src.enabled && src.gameObject.activeInHierarchy;
            g.Parts[i].gameObject.SetActive(visible);
            if (!visible) continue;

            src.BakeMesh(g.Filters[i].sharedMesh, true);
            g.Parts[i].SetPositionAndRotation(src.transform.position, src.transform.rotation);
        }

        for (int i = 0; i < statics.Length; i++)
        {
            int p = skinned.Length + i;
            MeshFilter src = statics[i];
            MeshRenderer srcRenderer = src != null ? src.GetComponent<MeshRenderer>() : null;
            bool visible = srcRenderer != null && srcRenderer.enabled && src.gameObject.activeInHierarchy;
            g.Parts[p].gameObject.SetActive(visible);
            if (!visible) continue;

            g.Parts[p].SetPositionAndRotation(src.transform.position, src.transform.rotation);
            g.Parts[p].localScale = src.transform.lossyScale;
        }

        g.Age = 0f;
        g.Alive = true;
        g.Root.SetActive(true);
        SetAlpha(g, color.a);
    }

    Ghost CreateGhost(int index)
    {
        int count = skinned.Length + statics.Length;
        var g = new Ghost
        {
            Root = new GameObject($"Afterimage_{index}"),
            Parts = new Transform[count],
            Filters = new MeshFilter[count],
            Renderers = new MeshRenderer[count]
        };

        for (int i = 0; i < count; i++)
        {
            var part = new GameObject("Part");
            part.transform.SetParent(g.Root.transform, false);

            MeshFilter mf = part.AddComponent<MeshFilter>();
            mf.sharedMesh = i < skinned.Length ? new Mesh() : statics[i - skinned.Length].sharedMesh;

            MeshRenderer mr = part.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            g.Parts[i] = part.transform;
            g.Filters[i] = mf;
            g.Renderers[i] = mr;
        }

        g.Root.SetActive(false);
        return g;
    }

    // 무기처럼 스킨이 없는 메시도 잔상에 포함 (트레일·파티클 제외)
    MeshFilter[] CollectStaticMeshes()
    {
        var list = new List<MeshFilter>();
        foreach (MeshFilter mf in sourceRoot.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            if (mf.GetComponent<MeshRenderer>() == null) continue;
            list.Add(mf);
        }
        return list.ToArray();
    }

    void SetAlpha(Ghost g, float alpha)
    {
        Color c = color;
        c.a = alpha;
        block.SetColor(BaseColorId, c);
        foreach (MeshRenderer r in g.Renderers) r.SetPropertyBlock(block);
    }
}
