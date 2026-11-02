using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class HitFeedbackSetup
{
    const string FxRoot = "Assets/ThirdParty/MasterStylizedProjectiles/Projectiles/";
    const string LightFx = FxRoot + "CyanBlueBullet/Prefabs/CyanBlueHit.prefab";
    const string HeavyFx = FxRoot + "LightningExplosion/Prefabs/LightningExplosionHit.prefab";
    const string ParryFx = FxRoot + "EnergyExplosion/Prefabs/EnergyExplosionHit.prefab";
    const string PlayerHitFx = FxRoot + "Shoot_Red/Prefabs/Par_RedShoot_Hit.prefab";
    const string BoltPrefab = "Assets/ThirdParty/ProceduralLightning/Prefab/LightningBoltPrefabHDR.prefab"; // [변경]

    const string PlayerAttackFolder = "Assets/11.Data/Attack";
    const float HeavyDamage = 18f;

    [MenuItem("Tools/Setup/Hit Feedback")]
    static void Run()
    {
        var combat = Object.FindAnyObjectByType<PlayerCombat>();
        if (combat == null)
        {
            Debug.LogError("[HitFeedbackSetup] 씬에 PlayerCombat이 없습니다.");
            return;
        }

        int heavyCount = AssignWeights();

        GameObject player = combat.gameObject;
        HitFeedback feedback = player.GetComponent<HitFeedback>();
        if (feedback == null) feedback = Undo.AddComponent<HitFeedback>(player);

        var so = new SerializedObject(feedback);
        so.FindProperty("weapon").objectReferenceValue = player.GetComponentInChildren<WeaponHitbox>(true);
        so.FindProperty("parry").objectReferenceValue = player.GetComponent<PlayerParry>();
        so.FindProperty("health").objectReferenceValue = player.GetComponent<PlayerHealth>();
        so.FindProperty("skill").objectReferenceValue = player.GetComponent<PlayerSkill>();
        so.FindProperty("followCamera").objectReferenceValue = Object.FindAnyObjectByType<ThirdPersonCamera>();
        so.FindProperty("playerHitEffect").objectReferenceValue = Load(PlayerHitFx);
        so.FindProperty("boltPrefab").objectReferenceValue = Load(BoltPrefab); // [변경]

        // 무게별 이펙트 연결 (목록 순서: Light, Heavy, Parry)
        SerializedProperty list = so.FindProperty("settings");
        for (int i = 0; i < list.arraySize; i++)
        {
            SerializedProperty item = list.GetArrayElementAtIndex(i);
            var weight = (AttackWeight)item.FindPropertyRelative("weight").enumValueIndex;
            string path = weight == AttackWeight.Light ? LightFx : weight == AttackWeight.Heavy ? HeavyFx : weight == AttackWeight.Parry ? ParryFx : null;
            if (path != null) item.FindPropertyRelative("effect").objectReferenceValue = Load(path);

            // [변경] 기존 목록 원소에는 새 필드 기본값이 안 들어갈 수 있어 무게별 스파크 값을 직접 지정
            (int count, float radius, float streak) spark =
                weight == AttackWeight.Heavy ? (6, 0.8f, 1.4f) :
                weight == AttackWeight.Parry ? (8, 1f, 0f) : (3, 0.45f, 0f);
            item.FindPropertyRelative("sparkCount").intValue = spark.count;
            item.FindPropertyRelative("sparkRadius").floatValue = spark.radius;
            item.FindPropertyRelative("streakLength").floatValue = spark.streak;
        }
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(player.scene);
        Selection.activeGameObject = player;
        Debug.Log($"[HitFeedbackSetup] 완료 · 플레이어 공격 중 강공격 {heavyCount}개 지정, 무게별 히트스톱·이펙트·번개 스파크 연결"); // [변경]
    }

    // 기획서 기준: □ 1~3타는 약, △와 마무리(피해 18 이상)는 강
    static int AssignWeights()
    {
        int heavy = 0;
        string[] guids = AssetDatabase.FindAssets("t:AttackData", new[] { PlayerAttackFolder });
        foreach (AttackData data in guids.Select(g => AssetDatabase.LoadAssetAtPath<AttackData>(AssetDatabase.GUIDToAssetPath(g))))
        {
            if (data == null) continue;
            bool isHeavy = data.Damage >= HeavyDamage;
            var so = new SerializedObject(data);
            so.FindProperty("weight").enumValueIndex = (int)(isHeavy ? AttackWeight.Heavy : AttackWeight.Light);
            so.ApplyModifiedProperties();
            if (isHeavy) heavy++;
        }
        AssetDatabase.SaveAssets();
        return heavy;
    }

    static GameObject Load(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) Debug.LogWarning($"[HitFeedbackSetup] 이펙트를 찾지 못했습니다: {path}");
        return prefab;
    }
}
