using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace MidnightMouse
{
    /// <summary>기존 SampleScene의 아트와 연결을 유지하고 M1 지도 배치만 갱신한다.</summary>
    public static class M1LayoutSetup
    {
        private static readonly Vector3[] Route = {
            new Vector3(-8f, 0f, -4.5f), new Vector3(-3f, 0f, -4.5f), new Vector3(.8f, 0f, -4.5f),
            new Vector3(.8f, 0f, 1f), new Vector3(-5f, 0f, 2.8f), new Vector3(-8f, 0f, 2.8f) };

        /// <summary>열려 있는 평가 씬에만 배치를 적용하고 저장한다.</summary>
        [MenuItem("MidnightMouse/M1 지도 적용")]
        public static void Apply()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != "Assets/Scenes/SampleScene.unity") throw new InvalidOperationException("편집 모드의 SampleScene에서 실행해야 합니다.");
            ApplyToScene(scene);
            Validate();
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("M1 지도 저장 실패");
        }

        /// <summary>새 씬 생성과 기존 씬 갱신에 같은 지도 좌표를 적용한다.</summary>
        public static void ApplyToScene(Scene scene)
        {
            Transform environment = GameObject.Find("Environment_NightLivingRoom").transform;
            Transform route = GameObject.Find("Vacuum_Route").transform;
            if (route.childCount != Route.Length) throw new InvalidOperationException("청소기 경로는 6개 지점이어야 합니다.");
            for (int i = 0; i < Route.Length; i++) route.GetChild(i).position = Route[i];
            GameObject.Find("Trap_Vacuum").transform.position = Route[0];
            Transform patrol = GameObject.Find("Patrol_And_Cheese_Points").transform;
            foreach (Transform point in patrol)
                if (Vector3.Distance(point.position, new Vector3(-10f, 0f, -3f)) < .3f) point.position = new Vector3(-11.8f, 0f, -3f);
            Transform root = GameObject.Find("M1_Layout")?.transform ?? new GameObject("M1_Layout").transform;
            M1Layout layout = root.GetComponent<M1Layout>() ?? root.gameObject.AddComponent<M1Layout>();
            var serialized = new SerializedObject(layout);
            serialized.FindProperty("_vacuumRoute").objectReferenceValue = route;
            serialized.FindProperty("_patrolPoints").objectReferenceValue = GameObject.Find("Patrol_And_Cheese_Points").transform;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Material wood = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Assessment/Environment/Materials/WoodDark.mat");
            Material dark = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Assessment/Environment/Materials/Dark.mat");
            Box(root, "TV_Cabinet", new Vector3(-10.2f, .6f, -4.5f), new Vector3(1.2f, 1.2f, 3f), wood, true);
            Box(root, "TV_Screen", new Vector3(-10.2f, 1.55f, -4.5f), new Vector3(.15f, 1f, 2.1f), dark, false);
            Box(root, "PetDoor_LivingRoom", new Vector3(-12.96f, .6f, 0f), new Vector3(.1f, 1.2f, 1.5f), dark, false);
            Box(root, "PetDoor_Kitchen", new Vector3(1.7f, .6f, 9.96f), new Vector3(1.5f, 1.2f, .1f), dark, false);
            Box(root, "MouseHole_02", new Vector3(0f, .15f, -6.4f), new Vector3(.65f, .3f, .1f), dark, false);
            Point(root, "PetDoor_LivingRoom_Entry", new Vector3(-12f, 0f, 0f));
            Point(root, "PetDoor_Kitchen_Entry", new Vector3(1.7f, 0f, 9.1f));
            Point(root, "Sleep_01_Reserved", new Vector3(-10.8f, 0f, -8f));
            Point(root, "Sleep_02_Reserved", new Vector3(8f, 0f, -5.5f));
            Point(root, "Ball_Start_Reserved", new Vector3(0f, 0f, -5.7f));
            Point(root, "Cup_B_Center_Reserved", new Vector3(7f, 0f, 5.5f));
            Point(root, "Cup_B_Switch_Reserved", new Vector3(1f, 0f, 5.5f));
            foreach (string name in new[] { "RightWall", "RightSkirting", "FrontWall" })
                if (environment.Find(name)?.GetComponent<Renderer>() is Renderer renderer) renderer.enabled = false;
            EditorSceneManager.MarkSceneDirty(scene);
        }

        /// <summary>저장된 이동 지도를 임시 등록하여 실패한 검사 번호를 저장 전에 알린다.</summary>
        [MenuItem("MidnightMouse/M1 지도 검사")]
        public static void Validate()
        {
            M1Layout layout = UnityEngine.Object.FindFirstObjectByType<M1Layout>();
            var navigation = new SerializedObject(UnityEngine.Object.FindFirstObjectByType<LevelNavigation>());
            var data = (NavMeshData)navigation.FindProperty("_data").objectReferenceValue;
            NavMeshDataInstance instance = NavMesh.AddNavMeshData(data);
            try
            {
                var errors = layout.ValidateLayout(UnityEngine.Object.FindFirstObjectByType<GameController>());
                if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            }
            finally { instance.Remove(); }
        }

        /// <summary>예약 지점은 게임 기능 없는 빈 오브젝트로 유지한다.</summary>
        private static Transform Point(Transform root, string name, Vector3 position)
        {
            Transform point = root.Find(name) ?? new GameObject(name).transform;
            point.SetParent(root, false);
            point.position = position;
            return point;
        }

        /// <summary>도형 가구에는 충돌과 길찾기 구멍을 함께 두고 벽의 문 표시는 장식으로 둔다.</summary>
        private static void Box(Transform root, string name, Vector3 position, Vector3 size, Material material, bool obstacle)
        {
            Transform item = root.Find(name);
            if (item == null)
            {
                item = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
                item.name = name;
                item.SetParent(root, false);
                if (!obstacle) UnityEngine.Object.DestroyImmediate(item.GetComponent<BoxCollider>());
            }
            item.position = position;
            item.localScale = size;
            item.GetComponent<Renderer>().sharedMaterial = material;
            if (!obstacle) return;
            NavMeshObstacle nav = item.GetComponent<NavMeshObstacle>() ?? item.gameObject.AddComponent<NavMeshObstacle>();
            nav.shape = NavMeshObstacleShape.Box;
            nav.size = Vector3.one;
            nav.carving = true;
            nav.carveOnlyStationary = true;
        }
    }
}
