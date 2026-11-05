using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 처형 맞춤 창: 씬의 플레이어와 적 하나를 처형 자리로 세우고 두 짝 모션을 같은 시간으로 겹쳐 재생
// 거리·옆·각도를 바꾸면 바로 보이고, 칼이 꽂히는 프레임에서 "타격 = 지금"을 누르면 Kill Time이 기록됨 (PlayerExecution에 저장)
// 창을 닫거나 미리보기를 끄면 적 위치와 두 캐릭터의 포즈가 원래대로 돌아감
public class ExecutionTunerWindow : EditorWindow
{
    PlayerExecution execution;
    EnemySwordsman enemy;
    int index;
    float time;
    float speed = 1f;
    bool playing;
    double lastTick;

    bool posed;
    Vector3 savedPos;
    Quaternion savedRot;

    [MenuItem("Tools/Animation/Execution Tuner")]
    static void Open() => GetWindow<ExecutionTunerWindow>("처형 맞춤");

    void OnEnable()
    {
        EditorApplication.update += Tick;
        if (execution == null) execution = FindAnyObjectByType<PlayerExecution>();
        if (enemy == null) enemy = FindAnyObjectByType<EnemySwordsman>();
    }

    void OnDisable()
    {
        EditorApplication.update -= Tick;
        StopPreview();
    }

    void OnGUI()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorGUILayout.HelpBox("플레이 모드가 아닐 때 사용하세요.", MessageType.Info);
            return;
        }

        execution = (PlayerExecution)EditorGUILayout.ObjectField("플레이어", execution, typeof(PlayerExecution), true);
        EditorGUILayout.BeginHorizontal();
        var newEnemy = (EnemySwordsman)EditorGUILayout.ObjectField("적 (아무나)", enemy, typeof(EnemySwordsman), true);
        if (GUILayout.Button("선택한 적", GUILayout.Width(70f)) && Selection.activeGameObject != null)
        {
            var picked = Selection.activeGameObject.GetComponentInParent<EnemySwordsman>();
            if (picked != null) newEnemy = picked;
        }
        EditorGUILayout.EndHorizontal();
        if (newEnemy != enemy)
        {
            StopPreview();
            enemy = newEnemy;
        }
        if (execution == null || enemy == null)
        {
            EditorGUILayout.HelpBox("씬에 PlayerExecution(Player Execution 셋업)과 적이 하나 있어야 합니다. 수련 검병이나 Add Guardian (Test)로 둔 적을 쓰면 편합니다.", MessageType.Warning);
            return;
        }

        int newIndex = EditorGUILayout.Popup("처형 모션", index, new[] { "처형 1", "처형 2", "처형 3" });
        if (newIndex != index)
        {
            index = newIndex;
            time = 0f;
        }

        AnimationClip playerClip = StateClip(execution.GetComponentInChildren<Animator>(), $"Execution_{index + 1}");
        AnimationClip enemyClip = StateClip(enemy.GetComponentInChildren<Animator>(), $"Executed_{index + 1}");
        if (playerClip == null || enemyClip == null)
        {
            EditorGUILayout.HelpBox($"Execution_{index + 1} / Executed_{index + 1} 상태에 클립이 없습니다. (비워 두면 이 번호는 게임에서 안 나옴)", MessageType.Info);
            return;
        }

        // 수치: PlayerExecution.motions[index]
        var so = new SerializedObject(execution);
        SerializedProperty motions = so.FindProperty("motions");
        while (motions.arraySize <= index) motions.InsertArrayElementAtIndex(motions.arraySize);
        SerializedProperty m = motions.GetArrayElementAtIndex(index);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("적 자리", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        Slider(m, "distance", "앞 거리", 0.3f, 3f);
        Slider(m, "side", "옆 (+오른쪽)", -1.5f, 1.5f);
        Slider(m, "yaw", "적 방향 (도)", -180f, 180f);
        Slider(m, "killTime", "타격 시점", 0f, 1f);
        Slider(m, "endTime", "끝 시점", 0f, 1f);
        if (EditorGUI.EndChangeCheck())
        {
            so.ApplyModifiedProperties();
            Sample(playerClip, enemyClip);
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("재생", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        float k = playerClip.length > 0f ? time / playerClip.length : 0f;
        time = EditorGUILayout.Slider($"시간  ({k:0.00})", time, 0f, playerClip.length);
        if (EditorGUI.EndChangeCheck())
        {
            playing = false;
            Sample(playerClip, enemyClip);
        }
        speed = EditorGUILayout.Slider("속도", speed, 0.1f, 1.5f);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(playing ? "일시정지" : "재생"))
        {
            playing = !playing;
            lastTick = EditorApplication.timeSinceStartup;
            Sample(playerClip, enemyClip);
        }
        if (GUILayout.Button("타격 = 지금")) SetNormalized(so, m, "killTime", k, playerClip, enemyClip);
        if (GUILayout.Button("끝 = 지금")) SetNormalized(so, m, "endTime", k, playerClip, enemyClip);
        if (GUILayout.Button("타격 지점으로")) JumpTo(m.FindPropertyRelative("killTime").floatValue * playerClip.length, playerClip, enemyClip);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("씬 뷰에서 보기"))
        {
            Selection.activeGameObject = execution.gameObject;
            SceneView.FrameLastActiveSceneView();
        }
        if (GUILayout.Button("미리보기 끄기 (원래대로)")) StopPreview();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.HelpBox("칼이 몸에 닿는 프레임에서 멈추고 '타격 = 지금' → 칼이 허공이면 거리·옆을, 몸이 엉뚱한 쪽을 보면 각도를 조절하세요. 플레이어가 바라보는 방향 기준입니다.", MessageType.None);

        if (playing) Repaint();
    }

    void Tick()
    {
        if (!playing || execution == null || enemy == null) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            StopPreview();
            return;
        }

        AnimationClip playerClip = StateClip(execution.GetComponentInChildren<Animator>(), $"Execution_{index + 1}");
        AnimationClip enemyClip = StateClip(enemy.GetComponentInChildren<Animator>(), $"Executed_{index + 1}");
        if (playerClip == null || enemyClip == null) return;

        double now = EditorApplication.timeSinceStartup;
        time += (float)(now - lastTick) * speed;
        lastTick = now;
        if (time > playerClip.length) time = 0f;
        Sample(playerClip, enemyClip);
        Repaint();
    }

    // 핵심: 적을 게임과 같은 계산(GetVictimPose)으로 세우고, 두 클립을 같은 시간으로 샘플
    void Sample(AnimationClip playerClip, AnimationClip enemyClip)
    {
        if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();
        if (!posed)
        {
            savedPos = enemy.transform.position;
            savedRot = enemy.transform.rotation;
            posed = true;
        }

        Transform p = execution.transform;
        execution.GetVictimPose(p.position, p.rotation, index, out Vector3 pos, out Quaternion rot);
        pos.y = p.position.y;
        enemy.transform.SetPositionAndRotation(pos, rot);

        AnimationMode.BeginSampling();
        AnimationMode.SampleAnimationClip(execution.GetComponentInChildren<Animator>().gameObject, playerClip, time);
        AnimationMode.SampleAnimationClip(enemy.GetComponentInChildren<Animator>().gameObject, enemyClip, Mathf.Min(time, enemyClip.length));
        AnimationMode.EndSampling();
        SceneView.RepaintAll();
    }

    void StopPreview()
    {
        playing = false;
        if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
        if (posed && enemy != null) enemy.transform.SetPositionAndRotation(savedPos, savedRot);
        posed = false;
        SceneView.RepaintAll();
    }

    void SetNormalized(SerializedObject so, SerializedProperty m, string field, float value, AnimationClip playerClip, AnimationClip enemyClip)
    {
        m.FindPropertyRelative(field).floatValue = Mathf.Clamp01(value);
        so.ApplyModifiedProperties();
        Sample(playerClip, enemyClip);
    }

    void JumpTo(float t, AnimationClip playerClip, AnimationClip enemyClip)
    {
        playing = false;
        time = t;
        Sample(playerClip, enemyClip);
    }

    static void Slider(SerializedProperty m, string field, string label, float min, float max)
    {
        SerializedProperty p = m.FindPropertyRelative(field);
        p.floatValue = EditorGUILayout.Slider(label, p.floatValue, min, max);
    }

    static AnimationClip StateClip(Animator animator, string stateName)
    {
        if (animator == null || !(animator.runtimeAnimatorController is AnimatorController controller)) return null;
        foreach (ChildAnimatorState s in controller.layers[0].stateMachine.states)
            if (s.state.name == stateName) return s.state.motion as AnimationClip;
        return null;
    }
}
