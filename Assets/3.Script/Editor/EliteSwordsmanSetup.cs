using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// 정예 검병: 검병 프리팹의 Variant. 3연타(콤보 세트 2의 1·2타 + 세트 3 마무리) · 짧은 예고 · 체력 2배 · 붉은 몸 · 조금 더 큼
// 구역 2 마지막 웨이브에 1명 배치. 다시 실행해도 수치는 처음 만들 때만 넣어 튜닝 값을 보존
public static class EliteSwordsmanSetup
{
    const string AttackRoot = "Assets/ThirdParty/SwordAnimationPack/Animation/Humanoid/02_Attack/";
    const string Clip1 = AttackRoot + "02_Combo_Attack_02/Combo_Attack_02_01.anim";
    const string Clip2 = AttackRoot + "02_Combo_Attack_02/Combo_Attack_02_02.anim";
    const string Clip3 = AttackRoot + "03_Combo_Attack_03/Combo_Attack_03_04.anim";

    const string EnemyController = "Assets/5.Animation/Enemy/Swordsman.controller";
    const string DataFolder = "Assets/11.Data/Enemy/";
    const string BaseDataPath = DataFolder + "Swordsman.asset";
    const string EliteDataPath = DataFolder + "Swordsman_Elite.asset";
    const string BasePrefabPath = "Assets/2.Model/Prefabs/Swordsman.prefab";
    const string ElitePrefabPath = "Assets/2.Model/Prefabs/Swordsman_Elite.prefab";

    static readonly Color EliteTint = new Color(0.42f, 0.07f, 0.06f);
    const float EliteScale = 1.12f;

    [MenuItem("Tools/Setup/Elite Swordsman")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[EliteSetup] 플레이 모드가 아닐 때 실행하세요.");
            return;
        }

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(EnemyController);
        var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefabPath);
        if (controller == null || basePrefab == null || AssetDatabase.LoadAssetAtPath<EnemyData>(BaseDataPath) == null)
        {
            Debug.LogError("[EliteSetup] Swordsman.controller · Swordsman 프리팹 · Swordsman 데이터 중 하나가 없습니다. Swordsman Enemy 셋업을 먼저 실행하세요.");
            return;
        }

        // 1) 3연타 공격 데이터 (상태는 검병 컨트롤러 공용, 3타만 새 상태)
        AnimationClip clip3 = AssetDatabase.LoadAssetAtPath<AnimationClip>(Clip3);
        if (clip3 == null)
        {
            Debug.LogError($"[EliteSetup] {Clip3}를 찾지 못했습니다.");
            return;
        }
        SetLoopOff(clip3);
        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        AnimatorState state3 = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == "Attack_3") ?? sm.AddState("Attack_3");
        state3.motion = clip3;
        EditorUtility.SetDirty(controller);

        AttackData a3 = GetOrCreateAttack("Elite_Attack_3", "Attack_3", Clip3, 22f, null, true);
        AttackData a2 = GetOrCreateAttack("Elite_Attack_2", "Attack_2", Clip2, 16f, a3, false);
        AttackData a1 = GetOrCreateAttack("Elite_Attack_1", "Attack_1", Clip1, 14f, a2, false);

        // 2) 정예 데이터: 검병 데이터를 복사한 뒤 값만 바꿈
        EnemyData data = AssetDatabase.LoadAssetAtPath<EnemyData>(EliteDataPath);
        bool created = data == null;
        if (created)
        {
            AssetDatabase.CopyAsset(BaseDataPath, EliteDataPath);
            data = AssetDatabase.LoadAssetAtPath<EnemyData>(EliteDataPath);
        }
        var dataSo = new SerializedObject(data);
        dataSo.FindProperty("firstAttack").objectReferenceValue = a1;
        if (created)
        {
            dataSo.FindProperty("maxHealth").floatValue = 120f;
            dataSo.FindProperty("windupTime").floatValue = 0.3f;
            dataSo.FindProperty("attackCooldown").floatValue = 1.0f;
            dataSo.FindProperty("runSpeed").floatValue = 4.6f;
            dataSo.FindProperty("parryStunTime").floatValue = 1.0f;
            dataSo.FindProperty("xpReward").intValue = 25;
        }
        dataSo.ApplyModifiedPropertiesWithoutUndo();

        // 3) 프리팹 Variant: 검병 프리팹을 바꾸면 정예도 따라감 (데이터 · 색 · 크기만 덮어씀)
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
        instance.name = "Swordsman_Elite";
        instance.transform.localScale = Vector3.one * EliteScale;
        var enemy = instance.GetComponent<EnemySwordsman>();
        var enemySo = new SerializedObject(enemy);
        enemySo.FindProperty("data").objectReferenceValue = data;
        enemySo.FindProperty("bodyTint").colorValue = EliteTint;
        enemySo.ApplyModifiedPropertiesWithoutUndo();
        GameObject elitePrefab = PrefabUtility.SaveAsPrefabAsset(instance, ElitePrefabPath);
        Object.DestroyImmediate(instance);

        AssetDatabase.SaveAssets();

        // 4) 구역 2 마지막 웨이브에 정예 1명
        AssignToZone2(elitePrefab);

        // 새 3타 클립 정리 + 판정 구간 측정
        PlayerClipFixer.Fix(EnemyController);
        EditorApplication.ExecuteMenuItem("Tools/Setup/Fit Attack Hit Windows");

        Debug.Log("[EliteSetup] 완료 · 정예 검병(3연타 · 예고 0.3초 · 체력 120 · 경험치 25) · 구역 2 마지막 웨이브에 1명");
    }

    static AttackData GetOrCreateAttack(string assetName, string stateName, string clipPath, float damage, AttackData next, bool heavy)
    {
        string path = DataFolder + assetName + ".asset";
        var data = AssetDatabase.LoadAssetAtPath<AttackData>(path);
        bool created = data == null;
        if (created)
        {
            data = ScriptableObject.CreateInstance<AttackData>();
            AssetDatabase.CreateAsset(data, path);
        }

        var so = new SerializedObject(data);
        so.FindProperty("clip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        so.FindProperty("stateName").stringValue = stateName;
        so.FindProperty("nextLight").objectReferenceValue = next; // 적은 Light 연결만 사용
        if (created)
        {
            so.FindProperty("damage").floatValue = damage;
            so.FindProperty("postureDamage").floatValue = damage * 0.5f;
            so.FindProperty("stepDistance").floatValue = heavy ? 0.8f : 0.4f;
            if (heavy) so.FindProperty("weight").enumValueIndex = 1;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        return data;
    }

    static void AssignToZone2(GameObject elitePrefab)
    {
        ZoneArena zone = Object.FindObjectsByType<ZoneArena>(FindObjectsSortMode.None)
            .FirstOrDefault(z => z.gameObject.name.Contains("Zone_2") || z.transform.root.name.Contains("Zone_2"));
        if (zone == null)
        {
            Debug.LogWarning("[EliteSetup] 씬에서 구역 2(ZoneArena)를 찾지 못해 웨이브 배치를 건너뜁니다. 인스펙터에서 Elite Prefab을 직접 넣으세요.");
            return;
        }

        var so = new SerializedObject(zone);
        SerializedProperty waves = so.FindProperty("waves");
        if (waves == null || waves.arraySize == 0)
        {
            Debug.LogWarning("[EliteSetup] 구역 2 웨이브가 비어 있습니다.");
            return;
        }

        SerializedProperty last = waves.GetArrayElementAtIndex(waves.arraySize - 1);
        last.FindPropertyRelative("elitePrefab").objectReferenceValue = elitePrefab;
        if (last.FindPropertyRelative("eliteCount").intValue == 0) last.FindPropertyRelative("eliteCount").intValue = 1;
        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(zone.gameObject.scene);
    }

    static void SetLoopOff(AnimationClip clip)
    {
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        if (!settings.loopTime) return;
        settings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
    }
}
