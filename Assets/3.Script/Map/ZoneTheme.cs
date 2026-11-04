using UnityEngine;

// 구역 하나의 배치 규칙. ZoneGenerator가 이 값으로 지형·바위·나무·문을 만든다. 값만 바꾸고 다시 생성하면 됨
[CreateAssetMenu(menuName = "NoeGeom/Zone Theme", fileName = "ZoneTheme")]
public class ZoneTheme : ScriptableObject
{
    public string zoneName = "Zone_1_Sanmun";
    public int seed = 1;
    // 구역 중심 위치. Generate All Zones가 구역 순서대로 북쪽(+Z)에 이어 붙이며 자동으로 채움
    public Vector3 origin;
    // [변경] HUD 구역 배너 문구 (예: "구역 1" / "산문")
    public string displayNumber = "";
    public string displayName = "";

    [Header("Arena")]
    public float combatRadius = 16f;   // 이 안은 장애물 없이 비워 둠
    public float wallRadius = 19f;     // 바위 벽과 투명 벽 위치
    public float gateHalfWidth = 3.5f; // 남(입구)·북(출구) 문 폭의 절반

    [Header("Terrain")]
    public float terrainSize = 140f;
    public float hillHeight = 10f;
    public float hillNoise = 4f;
    public float hillWidth = 22f;
    public TerrainLayer groundLayer;
    public TerrainLayer arenaLayer;
    public TerrainLayer roadLayer;

    // 크기는 프리팹 원본 크기와 상관없이 이 범위로 맞춤 (바위는 가장 긴 변, 나무는 높이 기준)
    [Header("Wall Rocks")]
    public GameObject[] wallRocks;
    public Vector2 wallRockSize = new Vector2(4.5f, 7f);
    public float wallRockSpacing = 0.65f;

    [Header("Inner Rocks")]
    public GameObject[] innerRocks;
    public int innerRockCount = 8;
    public Vector2 innerRockSize = new Vector2(0.8f, 2f);

    [Header("Trees")]
    public GameObject[] trees;
    public Vector2 treeHeight = new Vector2(9f, 16f);
    public float treeMinDistance = 4.5f;
    public int treeMaxCount = 220;

    [Header("Ground Cover")]
    public GameObject[] arenaCover;
    public int arenaCoverCount = 40;
    public GameObject[] outerCover;
    public int outerCoverCount = 80;

    [Header("Path")]
    public GameObject[] pathStones;
    public float pathStoneSize = 1.6f;
    public float pathStoneSpacing = 2.4f;

    [Header("Atmosphere")]
    public Material skybox;
    public bool fog = true;
    public Color fogColor = new Color(0.72f, 0.8f, 0.82f);
    public float fogDensity = 0.008f;
    public Color sunColor = new Color(1f, 0.94f, 0.82f);
    public Vector3 sunEuler = new Vector3(45f, -35f, 0f);
    public GameObject[] groundParticles;
    public int groundParticleCount = 6;
    public GameObject[] overheadParticles;

    [Header("Gate")]
    public Color gateColor = new Color(0.72f, 0.12f, 0.1f);
    public Color barrierColor = new Color(0.45f, 0.75f, 1f, 0.35f);
}
