using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class KatanaEquipSetup
{
    const string Folder = "Assets/2.Model/Weapon/ZenitsuKatana";
    const string ModelPath = Folder + "/ZenitsuKatana.fbx";
    const string MaterialFolder = Folder + "/Materials";
    const string LitShader = "Universal Render Pipeline/Lit";

    const string WeaponBoneName = "weapon_r";
    const string OldSwordName = "sword";
    const string SocketName = "WeaponSocket";
    const string BladeName = "Blade";

    static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int MetallicId = Shader.PropertyToID("_Metallic");
    static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

    // FBX 머티리얼 이름, 텍스처(없으면 null), 금속도, 매끄러움, 색
    static readonly (string Name, string Texture, float Metallic, float Smoothness, Color Color)[] MaterialDefs =
    {
        ("Katana_Blade", "Katana_Blade.png", 1f, 0.45f, Color.white),
        ("Katana_Metal", null, 1f, 0.6f, new Color(0.75f, 0.72f, 0.65f)),
        ("Katana_Guard", "Katana_Guard.png", 0f, 0.4f, Color.white),
        ("Katana_Handle", "Katana_Handle.png", 0f, 0.3f, Color.white),
        ("Katana_Wrap", "Katana_Wrap.png", 0f, 0.2f, Color.white),
    };

    [MenuItem("Tools/Setup/Equip Katana")]
    static void Equip()
    {
        if (!SetupImporter()) return;

        // 재임포트 이후에 로드해야 리맵된 머티리얼이 반영된 모델을 받음
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            Debug.LogError($"[Katana] {ModelPath} 이 없습니다.");
            return;
        }

        var locomotion = Object.FindAnyObjectByType<PlayerLocomotion>();
        if (locomotion == null)
        {
            Debug.LogError("[Katana] Player가 없습니다. Tools/Setup/Player Locomotion을 먼저 실행하세요.");
            return;
        }

        Transform player = locomotion.transform;
        Transform bone = FindChild(player, WeaponBoneName);
        if (bone == null)
        {
            Debug.LogError($"[Katana] '{WeaponBoneName}' 본을 찾지 못했습니다.");
            return;
        }

        Transform oldSword = FindChild(bone, OldSwordName);
        Transform socket = GetOrCreateSocket(bone, oldSword);

        // 이미 장착돼 있으면 사용자가 조절한 위치·회전·스케일을 그대로 둠
        Transform katana = socket.Find(model.name);
        if (katana == null)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, socket);
            Undo.RegisterCreatedObjectUndo(go, "Equip Katana");
            katana = go.transform;
            katana.localPosition = Vector3.zero;
            katana.localRotation = Quaternion.identity;
            katana.localScale = Vector3.one;
        }

        if (oldSword != null && oldSword.TryGetComponent(out MeshRenderer oldRenderer))
        {
            Undo.RecordObject(oldRenderer, "Hide Old Sword");
            oldRenderer.enabled = false;
        }

        AlignHitbox(player, katana);

        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        Selection.activeTransform = socket;
        Debug.Log("[Katana] 장착 완료 · 날 방향이 반대면 Tools/Setup/Flip Katana Edge");
    }

    [MenuItem("Tools/Setup/Flip Katana Edge")]
    static void FlipEdge()
    {
        var locomotion = Object.FindAnyObjectByType<PlayerLocomotion>();
        Transform socket = locomotion != null ? FindChild(locomotion.transform, SocketName) : null;
        if (socket == null)
        {
            Debug.LogError("[Katana] WeaponSocket이 없습니다. Equip Katana를 먼저 실행하세요.");
            return;
        }

        // 소켓 +Y(칼끝 방향)를 축으로 180도 → 날과 등의 방향만 바뀜
        Undo.RecordObject(socket, "Flip Katana Edge");
        socket.localRotation *= Quaternion.AngleAxis(180f, Vector3.up);

        Transform katana = socket.childCount > 0 ? socket.GetChild(0) : null;
        if (katana != null) AlignHitbox(locomotion.transform, katana);

        EditorSceneManager.MarkSceneDirty(socket.gameObject.scene);
    }

    [MenuItem("Tools/Setup/Remove Old Sword")]
    static void RemoveOldSword()
    {
        var locomotion = Object.FindAnyObjectByType<PlayerLocomotion>();
        Transform player = locomotion != null ? locomotion.transform : null;
        Transform bone = player != null ? FindChild(player, WeaponBoneName) : null;
        Transform socket = player != null ? FindChild(player, SocketName) : null;
        if (bone == null || socket == null)
        {
            Debug.LogError("[Katana] weapon_r 또는 WeaponSocket이 없습니다. Equip Katana를 먼저 실행하세요.");
            return;
        }

        Transform oldSword = FindChild(bone, OldSwordName);
        if (oldSword == null)
        {
            Debug.Log("[Katana] 기존 검이 이미 없습니다.");
            return;
        }

        // 히트박스를 sword 밖(weapon_r 바로 아래)으로 옮겨야 sword를 지워도 남음
        foreach (WeaponHitbox hitbox in oldSword.GetComponentsInChildren<WeaponHitbox>(true))
            Undo.SetTransformParent(hitbox.transform, bone, "Move Hitbox Out Of Old Sword");

        Transform katana = socket.childCount > 0 ? socket.GetChild(0) : null;
        if (katana != null) AlignHitbox(player, katana);

        // 프리팹 인스턴스의 자식은 스크립트로 지울 수 없는 경우가 있어 비활성화까지만 처리
        Undo.RecordObject(oldSword.gameObject, "Disable Old Sword");
        oldSword.gameObject.SetActive(false);

        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        Selection.activeTransform = oldSword;
        Debug.Log("[Katana] 히트박스를 weapon_r 아래로 옮기고 기존 sword를 비활성화했습니다. 이제 Hierarchy에서 Delete 해도 안전합니다.");
    }

    static bool SetupImporter()
    {
        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        Shader lit = Shader.Find(LitShader);
        if (importer == null || lit == null)
        {
            Debug.LogError($"[Katana] {ModelPath} 임포터 또는 URP Lit 셰이더를 찾지 못했습니다.");
            return false;
        }

        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder(Folder, "Materials");

        foreach (var def in MaterialDefs)
        {
            string path = $"{MaterialFolder}/{def.Name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(lit);
                AssetDatabase.CreateAsset(mat, path);

                if (def.Texture != null)
                    mat.SetTexture(BaseMapId, AssetDatabase.LoadAssetAtPath<Texture2D>($"{Folder}/{def.Texture}"));
                mat.SetColor(BaseColorId, def.Color);
                mat.SetFloat(MetallicId, def.Metallic);
                mat.SetFloat(SmoothnessId, def.Smoothness);
                EditorUtility.SetDirty(mat);
            }

            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), def.Name), mat);
        }

        importer.animationType = ModelImporterAnimationType.None;
        importer.importAnimation = false;
        importer.SaveAndReimport();
        AssetDatabase.SaveAssets();
        return true;
    }

    // 기존 sword 메시에서 칼끝 방향(손에서 먼 쪽)과 날 폭 방향을 읽어 소켓 회전을 맞춤
    // 카타나 모델은 +Y = 칼끝, +Z = 날 방향으로 변환되어 있음
    static Transform GetOrCreateSocket(Transform bone, Transform oldSword)
    {
        // 소켓은 처음 만들 때만 정렬 → 다시 실행해도 수동 조정(회전·스케일)이 유지됨
        Transform socket = bone.Find(SocketName);
        if (socket != null) return socket;

        var go = new GameObject(SocketName);
        Undo.RegisterCreatedObjectUndo(go, "Create Weapon Socket");
        socket = go.transform;
        socket.SetParent(bone, false);

        socket.localPosition = Vector3.zero;
        socket.localRotation = oldSword != null ? RotationFromOldSword(bone, oldSword) : Quaternion.identity;

        float boneScale = Mathf.Max(bone.lossyScale.x, 0.0001f);
        socket.localScale = Vector3.one / boneScale;
        return socket;
    }

    static Quaternion RotationFromOldSword(Transform bone, Transform sword)
    {
        if (!sword.TryGetComponent(out MeshFilter mf) || mf.sharedMesh == null) return Quaternion.identity;

        Bounds b = mf.sharedMesh.bounds;
        Vector3 size = b.size;
        int longAxis = size.x >= size.y && size.x >= size.z ? 0 : (size.y >= size.z ? 1 : 2);
        int sideAxis = longAxis == 0 ? (size.y >= size.z ? 1 : 2)
                     : longAxis == 1 ? (size.x >= size.z ? 0 : 2)
                     : (size.x >= size.y ? 0 : 1);

        Vector3 grip = sword.InverseTransformPoint(bone.position);
        float toMin = Mathf.Abs(grip[longAxis] - b.min[longAxis]);
        float toMax = Mathf.Abs(b.max[longAxis] - grip[longAxis]);
        float sideSign = b.center[sideAxis] >= grip[sideAxis] ? 1f : -1f;

        Vector3 bladeLocal = Axis(longAxis) * (toMax >= toMin ? 1f : -1f);
        Vector3 sideLocal = Axis(sideAxis) * sideSign;

        Vector3 bladeDir = bone.InverseTransformDirection(sword.TransformDirection(bladeLocal)).normalized;
        Vector3 edgeDir = Vector3.ProjectOnPlane(bone.InverseTransformDirection(sword.TransformDirection(sideLocal)), bladeDir).normalized;
        return Quaternion.LookRotation(edgeDir, bladeDir);
    }

    // 히트박스는 기존 위치(sword 아래)에 두고, 월드 기준으로 카타나 날에 맞춤
    static void AlignHitbox(Transform player, Transform katana)
    {
        WeaponHitbox hitbox = player.GetComponentInChildren<WeaponHitbox>(true);
        Transform blade = FindChild(katana, BladeName);
        if (hitbox == null || blade == null || !blade.TryGetComponent(out MeshFilter mf) || !hitbox.TryGetComponent(out BoxCollider box))
        {
            Debug.LogWarning("[Katana] 히트박스를 맞추지 못했습니다. Combat Test 셋업 후 Equip Katana를 다시 실행하세요.");
            return;
        }

        Transform t = hitbox.transform;
        Undo.RecordObject(t, "Align Hitbox");
        Undo.RecordObject(box, "Align Hitbox");

        // 비균일 스케일은 Transform 대신 박스 크기에 반영 (부모와 축이 달라도 왜곡되지 않게)
        t.SetPositionAndRotation(blade.position, blade.rotation);
        float parentScale = t.parent != null ? Mathf.Max(t.parent.lossyScale.x, 0.0001f) : 1f;
        t.localScale = Vector3.one / parentScale;

        Vector3 bladeScale = blade.lossyScale;
        Bounds b = mf.sharedMesh.bounds;
        box.center = Vector3.Scale(b.center, bladeScale);
        box.size = Vector3.Max(Vector3.Scale(b.size, bladeScale), Vector3.one * 0.04f);
    }

    static Vector3 Axis(int index) => index == 0 ? Vector3.right : index == 1 ? Vector3.up : Vector3.forward;

    static Transform FindChild(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }
}
