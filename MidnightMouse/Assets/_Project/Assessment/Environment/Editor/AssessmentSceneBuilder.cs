using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MidnightMouse
{
    /// <summary>Editor 메뉴에서 기존 아트를 재사용하는 평가용 밤의 거실 씬과 프리팹을 만듭니다.</summary>
    public static class AssessmentSceneBuilder
    {
        private const string Root = "Assets/_Project/Assessment/Environment";
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string BackupPath = Root + "/Scenes/OriginalPreview.unity";
        private const string MousePath = "Assets/_Project/Art/Model/Mouse/Mouse_텍스처정리.fbx";
        private const string MugPath = "Assets/_Project/Art/Model/MugCup/MugCup.fbx";
        private const string VacuumPath = "Assets/_Project/Art/Model/VacuumRobot/VacuumBot.fbx";
        private const string FontPath = "Assets/_Project/Assessment/UI/NotoSansKR-Regular.otf";

        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        private static readonly List<NavMeshBuildSource> Sources = new List<NavMeshBuildSource>();
        private static Mesh _cheeseMesh;
        private static Mesh _earMesh;
        private static int _agentTypeId;

        /// <summary>필수 에셋을 확인하고 원본 씬을 보존한 다음 평가용 게임 씬을 저장합니다.</summary>
        [MenuItem("MidnightMouse/평가용 게임 씬 생성")]
        public static void Build()
        {
            RequireAsset<GameObject>(MousePath);
            RequireAsset<GameObject>(MugPath);
            RequireAsset<GameObject>(VacuumPath);
            Font font = RequireAsset<Font>(FontPath);
            if (Shader.Find("Universal Render Pipeline/Lit") == null || Shader.Find("Universal Render Pipeline/Unlit") == null || NavMesh.GetSettingsCount() == 0)
            {
                throw new InvalidOperationException("URP Lit/Unlit 셰이더 또는 기본 NavMesh 에이전트 설정이 없습니다.");
            }

            EnsureFolder(Root + "/Scenes");
            EnsureFolder(Root + "/Materials");
            EnsureFolder(Root + "/Meshes");
            EnsureFolder(Root + "/Prefabs");
            EnsureFolder(Root + "/Resources");
            PreserveOriginalScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Sources.Clear();
            CreateMaterials();
            CreateMeshes();

            Transform environment = new GameObject("Environment_NightLivingRoom").transform;
            CreateRoom(environment);
            CreateFurniture(environment);
            Camera worldCamera = CreateLightingAndCamera();
            NavMeshData navigationData = BakeNavigation();
            LevelNavigation navigation = new GameObject("Navigation").AddComponent<LevelNavigation>();
            SetReference(navigation, "_data", navigationData);

            GameObject playerPrefab = CreatePlayerPrefab();
            GameObject catPrefab = CreateCatPrefab();
            GameObject cupPrefab = CreateTrapPrefab(TrapKind.Cup, MugPath, 1.2f);
            GameObject vacuumPrefab = CreateTrapPrefab(TrapKind.Vacuum, VacuumPath, 1.1f);
            GameObject cheesePrefab = CreateCheesePrefab();

            GameController game = new GameObject("GameController").AddComponent<GameController>();
            AssessmentRules rules = AssetDatabase.LoadAssetAtPath<AssessmentRules>(Root + "/AssessmentRules.asset");
            if (rules == null)
            {
                rules = ScriptableObject.CreateInstance<AssessmentRules>();
                AssetDatabase.CreateAsset(rules, Root + "/AssessmentRules.asset");
            }
            SetReference(game, "_rules", rules);

            MouseController player = InstantiatePrefab(playerPrefab, "MousePlayer", new Vector3(0f, 0f, -5.8f))
                .GetComponent<MouseController>();
            Transform actors = new GameObject("Cats_Pool").transform;
            Vector3[] catStarts =
            {
                new Vector3(-11.4f, 0f, -7.4f), new Vector3(11.1f, 0f, -7.5f),
                new Vector3(-11.4f, 0f, 2.5f), new Vector3(11.2f, 0f, 3.9f),
                new Vector3(-2.9f, 0f, 8.1f), new Vector3(2.5f, 0f, 7f), new Vector3(.7f, 0f, -8.6f)
            };
            CatController[] cats = new CatController[catStarts.Length];
            for (int i = 0; i < cats.Length; i++)
            {
                GameObject cat = InstantiatePrefab(catPrefab, "Cat_" + (i + 1).ToString("00"), catStarts[i], actors);
                cats[i] = cat.GetComponent<CatController>();
            }

            TrapController cup = InstantiatePrefab(cupPrefab, "Trap_Cup", new Vector3(-3f, 0f, -.5f))
                .GetComponent<TrapController>();
            cup.transform.Find("Visual").localPosition = new Vector3(8.2f, 1.4f, 1f);
            cup.transform.Find("EffectCenter").localPosition = new Vector3(6f, .05f, 0f);
            TrapController vacuum = InstantiatePrefab(vacuumPrefab, "Trap_Vacuum", new Vector3(-8f, 0f, -4.5f))
                .GetComponent<TrapController>();
            Transform routeRoot = new GameObject("Vacuum_Route").transform;
            Transform[] route = CreatePoints(routeRoot, new[]
            {
                new Vector3(-8f, 0f, -4.5f), new Vector3(-3f, 0f, -4.5f),
                new Vector3(.8f, 0f, -4.5f), new Vector3(.8f, 0f, 1f),
                new Vector3(-5f, 0f, 2.8f), new Vector3(-8f, 0f, 2.8f)
            });
            SetReferences(vacuum, "_route", route);

            Transform pickupRoot = new GameObject("Cheese_Pool").transform;
            CheesePickup[] pickups = new CheesePickup[24];
            for (int i = 0; i < pickups.Length; i++)
            {
                GameObject cheese = InstantiatePrefab(cheesePrefab, "Cheese_" + (i + 1).ToString("00"), Vector3.zero, pickupRoot);
                pickups[i] = cheese.GetComponent<CheesePickup>();
                cheese.SetActive(false);
            }

            Transform[] patrolPoints = CreatePoints(new GameObject("Patrol_And_Cheese_Points").transform, new[]
            {
                new Vector3(-10f, 0f, -7f), new Vector3(-7f, 0f, -7f), new Vector3(-3f, 0f, -7f),
                new Vector3(2f, 0f, -7f), new Vector3(7f, 0f, -7f), new Vector3(10f, 0f, -7f),
                new Vector3(-10f, 0f, -3f), new Vector3(-7f, 0f, -3f), new Vector3(-3f, 0f, -3f),
                new Vector3(1f, 0f, -3f), new Vector3(6f, 0f, -4f), new Vector3(10f, 0f, -3f),
                new Vector3(-10f, 0f, 1f), new Vector3(-5f, 0f, 1f), new Vector3(-1f, 0f, 1f),
                new Vector3(2f, 0f, 2f), new Vector3(8.7f, 0f, 3.5f), new Vector3(11f, 0f, 2f),
                new Vector3(-3f, 0f, 5f), new Vector3(1f, 0f, 5f), new Vector3(4f, 0f, 5f),
                new Vector3(7f, 0f, 5f), new Vector3(-2f, 0f, 8f), new Vector3(1f, 0f, 8f)
            });

            ValidateNavigation(navigationData, player.transform, cats, patrolPoints, route, cup.transform.Find("EffectCenter"));
            SetReference(game, "_player", player);
            SetReferences(game, "_cats", cats);
            SetReferences(game, "_traps", new[] { cup, vacuum });
            SetReferences(game, "_pickups", pickups);
            SetReferences(game, "_patrolPoints", patrolPoints);
            SetReference(game, "_worldCamera", worldCamera);
            GameHud hud = new GameObject("GameHud").AddComponent<GameHud>();
            SetReference(hud, "_game", game);
            SetReference(hud, "_font", font);

            AssessmentArtSetup.ApplyToScene(scene);
            M1LayoutSetup.ApplyToScene(scene);
            M1LayoutSetup.Validate();
            ValidateScene();
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException("평가용 씬 저장에 실패했습니다: " + ScenePath);
            }
            Debug.Log("평가용 씬 저장 완료: " + ScenePath + " / 원본 보존: " + BackupPath);
        }

        /// <summary>대상 외의 미저장 씬을 보호하고 최초 원본 사본만 생성합니다.</summary>
        private static void PreserveOriginalScene()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene loaded = SceneManager.GetSceneAt(i);
                if (loaded.isDirty && loaded.path != ScenePath)
                {
                    throw new InvalidOperationException("다른 씬의 미저장 변경을 먼저 보존해야 합니다: " + loaded.name);
                }
            }
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(BackupPath) != null)
            {
                return;
            }
            Scene original = SceneManager.GetSceneByPath(ScenePath);
            bool saved = original.IsValid() && original.isLoaded
                ? EditorSceneManager.SaveScene(original, BackupPath, true)
                : AssetDatabase.CopyAsset(ScenePath, BackupPath);
            if (!saved)
            {
                throw new InvalidOperationException("원본 씬 백업에 실패해 생성을 중단했습니다: " + BackupPath);
            }
        }

        /// <summary>밤의 청록색과 따뜻한 나무색 팔레트를 저장합니다.</summary>
        private static void CreateMaterials()
        {
            Materials.Clear();
            MaterialAsset("Wood", new Color(.48f, .29f, .17f));
            MaterialAsset("WoodLight", new Color(.56f, .36f, .22f));
            MaterialAsset("WoodDark", new Color(.39f, .23f, .15f));
            MaterialAsset("WoodHoney", new Color(.62f, .42f, .27f));
            MaterialAsset("Wall", new Color(.13f, .27f, .29f));
            MaterialAsset("Trim", new Color(.58f, .48f, .32f));
            MaterialAsset("Cream", new Color(.86f, .79f, .63f));
            MaterialAsset("Sage", new Color(.46f, .59f, .53f));
            MaterialAsset("Sofa", new Color(.29f, .47f, .47f));
            MaterialAsset("Rug", new Color(.65f, .48f, .31f));
            MaterialAsset("RugInner", new Color(.81f, .68f, .47f));
            MaterialAsset("Dark", new Color(.055f, .09f, .105f));
            MaterialAsset("Brass", new Color(.74f, .51f, .20f), .45f);
            MaterialAsset("CatFur", new Color(.71f, .40f, .22f));
            MaterialAsset("CatStripe", new Color(.33f, .18f, .12f));
            MaterialAsset("Pink", new Color(.77f, .41f, .40f));
            MaterialAsset("Cheese", new Color(1f, .73f, .25f), 0f, new Color(.22f, .12f, .015f));
            MaterialAsset("CheeseHole", new Color(.66f, .35f, .07f));
            MaterialAsset("Window", new Color(.07f, .28f, .36f), .15f, new Color(.04f, .16f, .25f));
            MaterialAsset("Moon", new Color(.78f, .92f, .83f), 0f, new Color(1.3f, 1.7f, 1.5f));
            MaterialAsset("Lamp", new Color(.97f, .78f, .46f), 0f, new Color(.6f, .3f, .08f));
            MaterialAsset("Mint", new Color(.37f, .92f, .77f), 0f, new Color(.08f, .35f, .23f));

            // 런타임에서 이름으로 찾는 선 셰이더가 Player 빌드에서 제거되지 않도록 에셋에 연결합니다.
            string feedbackPath = Root + "/Resources/AssessmentFeedbackLine.mat";
            Material feedback = AssetDatabase.LoadAssetAtPath<Material>(feedbackPath);
            if (feedback == null)
            {
                feedback = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "AssessmentFeedbackLine" };
                AssetDatabase.CreateAsset(feedback, feedbackPath);
            }
            feedback.shader = Shader.Find("Universal Render Pipeline/Unlit");
            feedback.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(feedback);
        }

        /// <summary>생성한 머티리얼을 같은 GUID로 갱신합니다.</summary>
        private static void MaterialAsset(string name, Color color, float metallic = 0f, Color emission = default)
        {
            string path = Root + "/Materials/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", metallic > 0f ? .5f : .22f);
            material.SetColor("_EmissionColor", emission);
            if (emission.maxColorComponent > 0f) material.EnableKeyword("_EMISSION");
            else material.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(material);
            Materials.Add(name, material);
        }

        /// <summary>치즈 쐐기와 고양이 귀에 사용할 삼각기둥 메시를 저장합니다.</summary>
        private static void CreateMeshes()
        {
            _cheeseMesh = PrismMesh("CheeseWedge", new Vector3(-.35f, 0f, -.25f), new Vector3(.35f, 0f, -.25f),
                new Vector3(0f, 0f, .4f), Vector3.up * .28f);
            _earMesh = PrismMesh("CatEar", new Vector3(-.5f, 0f, 0f), new Vector3(.5f, 0f, 0f),
                new Vector3(0f, 1f, 0f), Vector3.forward * .55f);
        }

        /// <summary>면마다 정점을 분리해 삼각기둥의 모서리를 선명하게 만듭니다.</summary>
        private static Mesh PrismMesh(string name, Vector3 a, Vector3 b, Vector3 c, Vector3 depth)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), depth) < 0f)
            {
                Vector3 previousB = b;
                b = c;
                c = previousB;
            }
            Vector3[] corners = { a, b, c, a + depth, b + depth, c + depth };
            int[] order = { 0, 2, 1, 3, 4, 5, 0, 1, 4, 0, 4, 3, 1, 2, 5, 1, 5, 4, 2, 0, 3, 2, 3, 5 };
            Vector3[] vertices = new Vector3[order.Length];
            int[] triangles = new int[order.Length];
            for (int i = 0; i < order.Length; i++) { vertices[i] = corners[order[i]]; triangles[i] = i; }
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(Root + "/Meshes/" + name + ".asset");
            bool isNew = mesh == null;
            if (isNew) mesh = new Mesh { name = name };
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            for (int i = 0; i < vertices.Length; i += 3)
            {
                Vector3 normal = Vector3.Cross(vertices[i + 1] - vertices[i], vertices[i + 2] - vertices[i]);
                Vector3 faceCenter = (vertices[i] + vertices[i + 1] + vertices[i + 2]) / 3f;
                if (Vector3.Dot(normal, faceCenter - mesh.bounds.center) <= 0f)
                {
                    throw new InvalidOperationException("삼각기둥의 면 방향이 뒤집혔습니다: " + name);
                }
            }
            if (isNew) AssetDatabase.CreateAsset(mesh, Root + "/Meshes/" + name + ".asset");
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        /// <summary>충돌 바닥, 판재, 낮은 전면 벽과 달빛 창을 배치합니다.</summary>
        private static void CreateRoom(Transform parent)
        {
            Box("Backdrop", parent, new Vector3(0f, -.5f, 0f), new Vector3(70f, .2f, 60f), "Dark");
            GameObject floor = Box("Floor", parent, new Vector3(0f, -.16f, 0f), new Vector3(26f, .3f, 20f), "WoodDark", true, 0);
            floor.isStatic = true;
            string[] woods = { "Wood", "WoodLight", "WoodHoney", "Wood", "WoodDark" };
            for (int row = 0; row < 18; row++)
            {
                for (int col = 0; col < 6; col++)
                {
                    float x = -10.82f + col * 4.33f;
                    Box("Plank_" + row + "_" + col, parent, new Vector3(x, -.005f, -9.44f + row * 1.11f),
                        new Vector3(4.30f, .025f, 1.085f), woods[(row * 3 + col) % woods.Length]);
                }
            }
            Box("BackWall", parent, new Vector3(0f, 1.8f, 10.15f), new Vector3(26.6f, 3.6f, .3f), "Wall", true);
            Box("LeftWall", parent, new Vector3(-13.15f, .65f, 0f), new Vector3(.3f, 1.3f, 20f), "Wall", true);
            Box("RightWall", parent, new Vector3(13.15f, .65f, 0f), new Vector3(.3f, 1.3f, 20f), "Wall", true);
            Box("FrontWall", parent, new Vector3(0f, -.03f, -10.15f), new Vector3(26.6f, .22f, .3f), "Trim");
            GameObject frontCollider = new GameObject("FrontBoundary");
            frontCollider.transform.SetParent(parent, false);
            frontCollider.transform.localPosition = new Vector3(0f, 1f, -10.15f);
            BoxCollider boundary = frontCollider.AddComponent<BoxCollider>();
            boundary.size = new Vector3(26.6f, 2f, .3f);
            AddNavBox(boundary, 1);
            Box("BackSkirting", parent, new Vector3(0f, .16f, 9.96f), new Vector3(26f, .32f, .09f), "Trim");
            Box("LeftSkirting", parent, new Vector3(-12.96f, .16f, 0f), new Vector3(.09f, .32f, 20f), "Trim");
            Box("RightSkirting", parent, new Vector3(12.96f, .16f, 0f), new Vector3(.09f, .32f, 20f), "Trim");
            Window(parent, -7f, true);
            Window(parent, -.7f, false);
            Box("RugBorder", parent, new Vector3(-5.6f, .025f, .3f), new Vector3(8f, .025f, 6.1f), "Rug");
            Box("RugInside", parent, new Vector3(-5.6f, .041f, .3f), new Vector3(7.65f, .015f, 5.75f), "RugInner");
            for (int i = 0; i < 5; i++)
            {
                Box("RugStripe", parent, new Vector3(-5.6f, .052f, -2.2f + i * 1.25f), new Vector3(7.5f, .008f, .025f), "Rug");
            }
            Box("KitchenRunner", parent, new Vector3(7f, .025f, 5.8f), new Vector3(7.2f, .025f, 1.2f), "Sofa");
            Box("MouseDoormat", parent, new Vector3(-10.8f, .025f, -8f), new Vector3(2.1f, .03f, 1.2f), "Sage");
            MeshObject("DoormatCheese", _cheeseMesh, parent, new Vector3(-10.8f, .05f, -8f), new Vector3(.85f, .08f, .85f), "Cream");
        }

        /// <summary>창틀과 초승달 실루엣을 벽 앞에 배치합니다.</summary>
        private static void Window(Transform parent, float x, bool moon)
        {
            Box("WindowFrame", parent, new Vector3(x, 2.15f, 9.94f), new Vector3(4.65f, 2.35f, .18f), "Cream");
            Box("NightGlass", parent, new Vector3(x, 2.15f, 9.82f), new Vector3(4.4f, 2.1f, .06f), "Window");
            Box("WindowMullion", parent, new Vector3(x, 2.15f, 9.73f), new Vector3(.07f, 2.13f, .08f), "Trim");
            Box("WindowCrossbar", parent, new Vector3(x, 2.05f, 9.72f), new Vector3(4.4f, .07f, .08f), "Trim");
            Box("WindowSill", parent, new Vector3(x, .96f, 9.66f), new Vector3(4.85f, .13f, .48f), "WoodHoney");
            if (moon)
            {
                Sphere("Moon", parent, new Vector3(x - 1.25f, 2.72f, 9.75f), new Vector3(.7f, .7f, .035f), "Moon");
                Sphere("MoonCutout", parent, new Vector3(x - 1.05f, 2.83f, 9.71f), new Vector3(.64f, .64f, .035f), "Window");
            }
            for (int i = 0; i < 9; i++)
            {
                Sphere("Star", parent, new Vector3(x - 1.9f + i * .45f, 2.27f + (i % 3) * .27f, 9.75f), Vector3.one * .035f, "Moon");
            }
        }

        /// <summary>가구를 벽 쪽에 모으고 주 이동 통로를 남깁니다.</summary>
        private static void CreateFurniture(Transform parent)
        {
            Transform sofa = Empty("Sofa", parent, new Vector3(-7.4f, 0f, 5.4f));
            Box("SofaBody", sofa, new Vector3(0f, .47f, 0f), new Vector3(5.8f, .75f, 2f), "Sofa", true);
            Box("Backrest", sofa, new Vector3(0f, 1.12f, .78f), new Vector3(5.8f, 1.1f, .45f), "Sofa");
            for (int i = -1; i <= 1; i++)
            {
                Box("SeatCushion", sofa, new Vector3(i * 1.75f, .91f, -.14f), new Vector3(1.68f, .22f, 1.5f), "Sage");
            }
            for (int side = -1; side <= 1; side += 2)
            {
                Box("Armrest", sofa, new Vector3(side * 2.66f, .96f, 0f), new Vector3(.48f, .58f, 2.1f), "Sofa");
                GameObject pillow = Box("Pillow", sofa, new Vector3(side * 1.85f, 1.32f, .42f), new Vector3(.8f, .76f, .28f), side < 0 ? "Cream" : "Rug");
                pillow.transform.localRotation = Quaternion.Euler(12f, 0f, side * 12f);
            }
            Transform table = Empty("DiningTable", parent, new Vector3(6f, 0f, .6f));
            BoxCollider tableObstacle = table.gameObject.AddComponent<BoxCollider>();
            tableObstacle.center = new Vector3(0f, .68f, 0f);
            tableObstacle.size = new Vector3(3.65f, 1.35f, 2.6f);
            AddNavBox(tableObstacle, 1);
            Box("Tabletop", table, new Vector3(0f, 1.3f, 0f), new Vector3(3.9f, .2f, 2.8f), "WoodHoney");
            for (int x = -1; x <= 1; x += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Box("TableLeg", table, new Vector3(x * 1.55f, .6f, z * .95f), new Vector3(.18f, 1.2f, .18f), "WoodDark");
                }
            }
            Box("TableRunner", table, new Vector3(.65f, 1.412f, 0f), new Vector3(.78f, .016f, 2.75f), "Sage");
            Cylinder("FruitBowl", table, new Vector3(.65f, 1.48f, .45f), new Vector3(.7f, .07f, .7f), "Cream");
            Sphere("FruitPear", table, new Vector3(.55f, 1.66f, .43f), new Vector3(.25f, .34f, .25f), "Sage");
            Sphere("FruitOrange", table, new Vector3(.82f, 1.64f, .48f), Vector3.one * .3f, "CatFur");
            Box("Book", table, new Vector3(-.85f, 1.48f, .8f), new Vector3(.7f, .13f, .6f), "Cream");

            Transform kitchen = Empty("Kitchen", parent, new Vector3(7.2f, 0f, 8.5f));
            Box("CabinetBody", kitchen, new Vector3(0f, .75f, 0f), new Vector3(8.6f, 1.5f, 2f), "Sage", true);
            Box("Countertop", kitchen, new Vector3(0f, 1.57f, -.05f), new Vector3(8.85f, .18f, 2.2f), "Cream");
            for (int i = 0; i < 5; i++)
            {
                float x = -3.42f + i * 1.7f;
                Box("CabinetDoor", kitchen, new Vector3(x, .8f, -1.025f), new Vector3(1.59f, 1.25f, .06f), "Sofa");
                Box("DoorHandle", kitchen, new Vector3(x + .49f, 1.12f, -1.08f), new Vector3(.3f, .06f, .06f), "Brass");
            }
            Box("Cooktop", kitchen, new Vector3(1.7f, 1.68f, -.15f), new Vector3(1.75f, .06f, 1.45f), "Dark");
            for (int i = 0; i < 4; i++)
            {
                Cylinder("Burner", kitchen, new Vector3(1.3f + (i % 2) * .8f, 1.72f, -.53f + (i / 2) * .75f), new Vector3(.46f, .01f, .46f), "Brass");
            }
            Box("SinkRim", kitchen, new Vector3(-1.6f, 1.68f, -.15f), new Vector3(1.7f, .06f, 1.35f), "Brass");
            Box("Sink", kitchen, new Vector3(-1.6f, 1.72f, -.15f), new Vector3(1.46f, .02f, 1.11f), "Dark");
            Cylinder("Faucet", kitchen, new Vector3(-1.6f, 1.96f, .57f), new Vector3(.09f, .28f, .09f), "Brass");
            Box("FaucetSpout", kitchen, new Vector3(-1.6f, 2.19f, .34f), new Vector3(.09f, .09f, .48f), "Brass");
            Box("Fridge", parent, new Vector3(11.3f, 1.45f, 5.95f), new Vector3(1.95f, 2.9f, 1.8f), "Cream", true);
            Box("FridgeDivide", parent, new Vector3(11.3f, 1.95f, 5.035f), new Vector3(1.85f, .04f, .03f), "Trim");
            Box("FridgeHandle", parent, new Vector3(10.65f, 1.47f, 4.98f), new Vector3(.07f, .58f, .07f), "Brass");
            Box("FridgeNote", parent, new Vector3(11.5f, 2.27f, 5.015f), new Vector3(.42f, .48f, .02f), "Lamp");
            FloorLamp(parent, new Vector3(-11.4f, 0f, 5.1f));
            Plant(parent, new Vector3(-11.2f, 0f, 8.2f));
            Plant(parent, new Vector3(11.8f, 0f, -8.4f));
            Box("Sideboard", parent, new Vector3(-5.2f, .55f, 8.65f), new Vector3(2.5f, 1.1f, 1.25f), "Wood", true);
            for (int i = 0; i < 6; i++)
            {
                GameObject book = Box("ShelfBook", parent, new Vector3(-6f + i * .26f, 1.37f, 8.65f),
                    new Vector3(.2f, .46f + (i % 2) * .13f, .6f), i % 3 == 0 ? "Cream" : i % 3 == 1 ? "Sage" : "CatFur");
                book.transform.localRotation = Quaternion.Euler(0f, 0f, (i % 2) * 6f);
            }
        }

        /// <summary>가구 가장자리의 조명 기구와 따뜻한 빛을 배치합니다.</summary>
        private static void FloorLamp(Transform parent, Vector3 position)
        {
            Transform lamp = Empty("FloorLamp", parent, position);
            Cylinder("LampBase", lamp, new Vector3(0f, .05f, 0f), new Vector3(.65f, .05f, .65f), "Brass");
            Cylinder("LampStem", lamp, new Vector3(0f, 1.15f, 0f), new Vector3(.07f, 1.15f, .07f), "Brass");
            Cylinder("LampShade", lamp, new Vector3(0f, 2.35f, 0f), new Vector3(.9f, .3f, .9f), "Lamp");
            PointLight("LampGlow", lamp, new Vector3(0f, 2.2f, -.15f), new Color(1f, .68f, .34f), 6f, 8f);
            BoxCollider obstacle = lamp.gameObject.AddComponent<BoxCollider>();
            obstacle.center = new Vector3(0f, 1f, 0f);
            obstacle.size = new Vector3(.65f, 2f, .65f);
            AddNavBox(obstacle, 1);
        }

        /// <summary>단순한 화분과 넓은 잎으로 방 가장자리를 장식합니다.</summary>
        private static void Plant(Transform parent, Vector3 position)
        {
            Transform plant = Empty("Plant", parent, position);
            Cylinder("Pot", plant, new Vector3(0f, .32f, 0f), new Vector3(.75f, .32f, .75f), "Rug");
            Cylinder("Soil", plant, new Vector3(0f, .65f, 0f), new Vector3(.64f, .015f, .64f), "WoodDark");
            for (int i = 0; i < 6; i++)
            {
                float angle = i * 60f * Mathf.Deg2Rad;
                GameObject leaf = Sphere("Leaf", plant, new Vector3(Mathf.Sin(angle) * .27f, 1.05f + (i % 2) * .16f, Mathf.Cos(angle) * .27f),
                    new Vector3(.32f, .84f, .18f), i % 2 == 0 ? "Sage" : "Sofa");
                leaf.transform.localRotation = Quaternion.Euler(25f, i * 60f, 20f);
            }
            BoxCollider obstacle = plant.gameObject.AddComponent<BoxCollider>();
            obstacle.center = Vector3.up * .4f;
            obstacle.size = new Vector3(.75f, .8f, .75f);
            AddNavBox(obstacle, 1);
        }

        /// <summary>방 전체가 보이는 직교 카메라와 달빛·실내 조명을 만듭니다.</summary>
        private static Camera CreateLightingAndCamera()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.32f, .40f, .43f);
            RenderSettings.fog = false;
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 11.2f;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 90f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.025f, .055f, .07f);
            camera.transform.position = new Vector3(0f, 22f, -18f);
            camera.transform.LookAt(new Vector3(0f, 0f, 1f));
            cameraObject.AddComponent<AudioListener>();
            UniversalAdditionalCameraData cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            Light moonlight = new GameObject("Moonlight").AddComponent<Light>();
            moonlight.type = LightType.Directional;
            moonlight.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            moonlight.color = new Color(.68f, .85f, 1f);
            moonlight.intensity = 1.15f;
            moonlight.shadows = LightShadows.Soft;
            moonlight.shadowBias = .06f;
            moonlight.shadowNormalBias = .25f;
            RenderSettings.sun = moonlight;
            PointLight("KitchenWarmth", null, new Vector3(6f, 3.7f, 5.5f), new Color(1f, .76f, .45f), 8f, 11f);
            PointLight("WindowFill", null, new Vector3(-4f, 3f, 7.5f), new Color(.4f, .72f, 1f), 3f, 9f);

            string profilePath = Root + "/NightVolume.asset";
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, profilePath);
            }
            EnsureVolumeEffect<Bloom>(profile).intensity.Override(.23f);
            EnsureVolumeEffect<Bloom>(profile).threshold.Override(1.1f);
            EnsureVolumeEffect<ColorAdjustments>(profile).postExposure.Override(.25f);
            EnsureVolumeEffect<ColorAdjustments>(profile).contrast.Override(8f);
            EnsureVolumeEffect<ColorAdjustments>(profile).saturation.Override(-4f);
            EnsureVolumeEffect<Vignette>(profile).intensity.Override(.14f);
            EnsureVolumeEffect<Tonemapping>(profile).mode.Override(TonemappingMode.Neutral);
            EditorUtility.SetDirty(profile);
            Volume volume = new GameObject("Night Post Processing").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
            return camera;
        }

        /// <summary>볼륨 효과를 에셋의 하위 오브젝트로 보존합니다.</summary>
        private static T EnsureVolumeEffect<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet(out T component))
            {
                component = profile.Add<T>(true);
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            EditorUtility.SetDirty(component);
            return component;
        }

        /// <summary>가구 그림자를 위한 불필요한 추가 그림자 지도를 만들지 않는 점광원입니다.</summary>
        private static void PointLight(string name, Transform parent, Vector3 position, Color color, float intensity, float range)
        {
            Transform lightTransform = Empty(name, parent, position);
            Light light = lightTransform.gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
        }

        /// <summary>사용자 쥐 모델을 재사용하는 플레이어 프리팹을 만듭니다.</summary>
        private static GameObject CreatePlayerPrefab()
        {
            GameObject root = new GameObject("MousePlayer");
            Transform visual = Empty("Visual", root.transform, Vector3.zero);
            NormalizeModel(MousePath, visual, .75f, false);
            CharacterController controller = root.AddComponent<CharacterController>();
            controller.height = .72f;
            controller.radius = .25f;
            controller.center = new Vector3(0f, .36f, 0f);
            controller.stepOffset = .14f;
            controller.skinWidth = .025f;
            controller.minMoveDistance = 0f;
            MouseController player = root.AddComponent<MouseController>();
            SetReference(player, "_visual", visual);
            Ring("PlayerMarker", root.transform, .43f, "Mint");
            return SavePrefab(root, "MousePlayer");
        }

        /// <summary>귀·수염·눈·꼬리로 구별되는 임시 고양이 모델과 AI 프리팹을 만듭니다.</summary>
        private static GameObject CreateCatPrefab()
        {
            GameObject root = new GameObject("CuriousCat");
            Transform visual = Empty("Visual", root.transform, Vector3.zero);
            Sphere("Body", visual, new Vector3(0f, .53f, -.06f), new Vector3(.68f, .7f, 1f), "CatFur");
            Sphere("Head", visual, new Vector3(0f, .91f, .46f), new Vector3(.74f, .65f, .64f), "CatFur");
            Sphere("Muzzle", visual, new Vector3(0f, .81f, .75f), new Vector3(.46f, .25f, .12f), "Cream");
            Sphere("Nose", visual, new Vector3(0f, .9f, .819f), new Vector3(.085f, .065f, .04f), "Pink");
            for (int side = -1; side <= 1; side += 2)
            {
                MeshObject("Ear", _earMesh, visual, new Vector3(side * .23f, 1.12f, .44f), new Vector3(.29f, .37f, .28f), "CatFur");
                MeshObject("InnerEar", _earMesh, visual, new Vector3(side * .23f, 1.15f, .603f), new Vector3(.18f, .25f, .025f), "Pink");
                Sphere("Eye", visual, new Vector3(side * .18f, .995f, .736f), new Vector3(.18f, .17f, .065f), "Cream");
                Sphere("Pupil", visual, new Vector3(side * .18f, .995f, .774f), new Vector3(.05f, .125f, .022f), "Dark");
                Sphere("EyeShine", visual, new Vector3(side * .18f - .015f, 1.03f, .787f), Vector3.one * .025f, "Moon");
                for (int leg = -1; leg <= 1; leg += 2)
                {
                    Sphere("Paw", visual, new Vector3(side * .23f, .16f, leg * .29f), new Vector3(.24f, .31f, .31f), "Cream");
                }
                for (int whisker = 0; whisker < 2; whisker++)
                {
                    Segment("Whisker", visual, new Vector3(side * .12f, .81f, .818f),
                        new Vector3(side * .43f, .81f + (whisker == 0 ? .07f : -.025f), .8f), .009f, "Dark");
                }
            }
            Vector3[] tail = { new Vector3(0f, .53f, -.48f), new Vector3(0f, .73f, -.8f), new Vector3(.07f, 1f, -.93f), new Vector3(.27f, 1.16f, -.85f) };
            for (int i = 0; i < tail.Length - 1; i++) Segment("Tail", visual, tail[i], tail[i + 1], .14f, i == 2 ? "CatStripe" : "CatFur");
            for (int i = 0; i < 3; i++) Sphere("BackStripe", visual, new Vector3(0f, .866f, -.29f + i * .2f), new Vector3(.43f, .035f, .085f), "CatStripe");
            Sphere("Collar", visual, new Vector3(0f, .66f, .32f), new Vector3(.59f, .16f, .5f), "Sofa");
            Sphere("Bell", visual, new Vector3(0f, .62f, .59f), Vector3.one * .14f, "Brass");
            NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
            // 씬의 NavMesh 등록 뒤 SetAvailable(true)에서 활성화합니다.
            agent.enabled = false;
            agent.agentTypeID = _agentTypeId;
            agent.radius = .38f;
            agent.height = 1f;
            agent.baseOffset = 0f;
            agent.angularSpeed = 540f;
            agent.acceleration = 18f;
            agent.stoppingDistance = .2f;
            CatController cat = root.AddComponent<CatController>();
            SetReference(cat, "_visual", visual);
            return SavePrefab(root, "CuriousCat");
        }

        /// <summary>기존 컵·청소기 FBX를 원본 변경 없이 함정 프리팹에 넣습니다.</summary>
        private static GameObject CreateTrapPrefab(TrapKind kind, string path, float size)
        {
            GameObject root = new GameObject(kind + "Trap");
            Transform visual = Empty("Visual", root.transform, Vector3.zero);
            NormalizeModel(path, visual, size, kind == TrapKind.Vacuum);
            Transform effect = Empty("EffectCenter", root.transform, new Vector3(0f, .05f, 0f));
            TrapController trap = root.AddComponent<TrapController>();
            SerializedObject serialized = new SerializedObject(trap);
            RequiredProperty(serialized, "_kind").enumValueIndex = (int)kind;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            SetReference(trap, "_visual", visual);
            SetReference(trap, "_effectCenter", effect);
            Ring("InteractionMarker", root.transform, .75f, kind == TrapKind.Cup ? "Cheese" : "Mint");
            return SavePrefab(root, kind + "Trap");
        }

        /// <summary>거리 기반 수집에 쓰이는 작은 치즈 프리팹을 만듭니다.</summary>
        private static GameObject CreateCheesePrefab()
        {
            GameObject root = new GameObject("CheesePickup");
            MeshObject("Wedge", _cheeseMesh, root.transform, Vector3.zero, Vector3.one, "Cheese");
            Sphere("TopHoleA", root.transform, new Vector3(-.13f, .281f, -.08f), new Vector3(.12f, .008f, .12f), "CheeseHole");
            Sphere("TopHoleB", root.transform, new Vector3(.15f, .281f, -.13f), new Vector3(.09f, .008f, .09f), "CheeseHole");
            Sphere("TopHoleC", root.transform, new Vector3(0f, .281f, .21f), new Vector3(.07f, .008f, .07f), "CheeseHole");
            Sphere("FrontHole", root.transform, new Vector3(.12f, .13f, -.251f), new Vector3(.09f, .09f, .008f), "CheeseHole");
            root.transform.localScale = Vector3.one * .8f;
            root.AddComponent<CheesePickup>();
            return SavePrefab(root, "CheesePickup");
        }

        /// <summary>인스턴스의 렌더링 경계로 크기와 바닥 피벗을 정규화합니다.</summary>
        private static void NormalizeModel(string path, Transform parent, float targetSize, bool fitWidth)
        {
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(RequireAsset<GameObject>(path));
            model.name = "UserModel";
            model.transform.SetParent(parent, false);
            model.transform.localPosition = Vector3.zero;
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("모델에 Renderer가 없습니다: " + path);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            float dimension = fitWidth ? Mathf.Max(bounds.size.x, bounds.size.z) : bounds.size.y;
            if (!float.IsFinite(dimension) || dimension < .0001f) throw new InvalidOperationException("모델 경계가 올바르지 않습니다: " + path);
            model.transform.localScale *= targetSize / dimension;
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            model.transform.position += parent.position - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            foreach (Camera camera in model.GetComponentsInChildren<Camera>(true)) Object.DestroyImmediate(camera.gameObject);
            foreach (Light light in model.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(light.gameObject);
        }

        /// <summary>명시적으로 모은 바닥과 가구만으로 고양이 이동 지도를 만듭니다.</summary>
        private static NavMeshData BakeNavigation()
        {
            NavMeshBuildSettings settings = NavMesh.GetSettingsByIndex(0);
            _agentTypeId = settings.agentTypeID;
            settings.agentRadius = .5f;
            settings.agentHeight = 1f;
            settings.agentClimb = .2f;
            settings.agentSlope = 40f;
            settings.overrideVoxelSize = true;
            settings.voxelSize = .1f;
            Bounds bounds = new Bounds(new Vector3(0f, 2f, 0f), new Vector3(30f, 7f, 24f));
            string path = Root + "/LivingRoomNavMesh.asset";
            NavMeshData data = AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
            if (data == null)
            {
                data = NavMeshBuilder.BuildNavMeshData(settings, Sources, bounds, Vector3.zero, Quaternion.identity);
                if (data == null) throw new InvalidOperationException("고양이 이동 지도 생성에 실패했습니다.");
                data.name = "LivingRoomNavMesh";
                AssetDatabase.CreateAsset(data, path);
            }
            else if (!NavMeshBuilder.UpdateNavMeshData(data, settings, Sources, bounds))
            {
                throw new InvalidOperationException("기존 이동 지도 갱신에 실패했습니다.");
            }
            EditorUtility.SetDirty(data);
            return data;
        }

        /// <summary>모든 시작점·순찰점·청소기 경로가 바닥 위에서 이어지는지 확인합니다.</summary>
        private static void ValidateNavigation(NavMeshData data, Transform player, CatController[] cats, Transform[] points, Transform[] route, Transform cupEffectCenter)
        {
            NavMeshDataInstance instance = NavMesh.AddNavMeshData(data);
            try
            {
                List<Transform> all = new List<Transform> { player };
                foreach (CatController cat in cats) all.Add(cat.transform);
                all.AddRange(points);
                all.AddRange(route);
                all.Add(cupEffectCenter);
                NavMeshPath path = new NavMeshPath();
                foreach (Transform target in all)
                {
                    if (!NavMesh.SamplePosition(target.position, out NavMeshHit hit, .45f, NavMesh.AllAreas))
                    {
                        throw new InvalidOperationException("NavMesh 위에 없는 시작/경로 지점: " + target.name + " " + target.position);
                    }
                    if (target != cupEffectCenter) target.position = hit.position;
                    if (!NavMesh.CalculatePath(player.position, hit.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                    {
                        throw new InvalidOperationException("플레이어에게 이어지지 않는 이동 지점: " + target.name);
                    }
                }
            }
            finally
            {
                instance.Remove();
            }
        }

        /// <summary>현재 씬의 필수 연결과 풀 수량, 누락된 스크립트를 검사합니다.</summary>
        [MenuItem("MidnightMouse/평가용 씬 연결 확인")]
        public static void ValidateScene()
        {
            GameController[] games = Object.FindObjectsByType<GameController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (games.Length != 1) throw new InvalidOperationException("GameController는 씬에 정확히 1개여야 합니다.");
            SerializedObject game = new SerializedObject(games[0]);
            foreach (string field in new[] { "_rules", "_player", "_worldCamera" })
            {
                if (RequiredProperty(game, field).objectReferenceValue == null) throw new InvalidOperationException("GameController 참조 누락: " + field);
            }
            foreach (string field in new[] { "_cats", "_traps", "_pickups", "_patrolPoints" })
            {
                SerializedProperty array = RequiredProperty(game, field);
                int minimum = field == "_cats" ? 7 : field == "_traps" ? 2 : 24;
                if (array.arraySize < minimum) throw new InvalidOperationException("풀/경로 수량 부족: " + field);
                for (int i = 0; i < array.arraySize; i++)
                {
                    if (array.GetArrayElementAtIndex(i).objectReferenceValue == null) throw new InvalidOperationException("풀/경로 참조 누락: " + field + "[" + i + "]");
                }
            }
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
                {
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(item.gameObject) != 0)
                    {
                        throw new InvalidOperationException("Missing Script: " + item.name);
                    }
                }
            }
        }

        /// <summary>생성한 프리팹을 저장하고 임시 씬 오브젝트를 제거합니다.</summary>
        private static GameObject SavePrefab(GameObject root, string name)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/" + name + ".prefab");
            Object.DestroyImmediate(root);
            if (prefab == null) throw new InvalidOperationException("프리팹 저장 실패: " + name);
            return prefab;
        }

        /// <summary>프리팹 연결을 유지한 채 씬의 지정 위치에 배치합니다.</summary>
        private static GameObject InstantiatePrefab(GameObject prefab, string name, Vector3 position, Transform parent = null)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = name;
            instance.transform.SetParent(parent, false);
            instance.transform.position = position;
            return instance;
        }

        /// <summary>공유되는 순찰점 또는 청소기 경로를 만듭니다.</summary>
        private static Transform[] CreatePoints(Transform parent, Vector3[] positions)
        {
            Transform[] points = new Transform[positions.Length];
            for (int i = 0; i < positions.Length; i++) points[i] = Empty("Point_" + (i + 1).ToString("00"), parent, positions[i]);
            return points;
        }

        /// <summary>직렬화 참조 이름이 변경되면 조용히 누락하는 대신 즉시 알립니다.</summary>
        private static SerializedProperty RequiredProperty(SerializedObject target, string name)
        {
            SerializedProperty property = target.FindProperty(name);
            if (property == null) throw new InvalidOperationException(target.targetObject.GetType().Name + "에 직렬화 필드가 없습니다: " + name);
            return property;
        }

        /// <summary>씬과 프리팹의 단일 참조를 설정합니다.</summary>
        private static void SetReference(Object target, string name, Object value)
        {
            SerializedObject serialized = new SerializedObject(target);
            RequiredProperty(serialized, name).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>씬과 프리팹의 참조 배열을 설정합니다.</summary>
        private static void SetReferences<T>(Object target, string name, T[] values) where T : Object
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = RequiredProperty(serialized, name);
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>필수 원본이 없으면 기존 씬을 건드리기 전에 중단합니다.</summary>
        private static T RequireAsset<T>(string path) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new InvalidOperationException("필수 에셋을 찾을 수 없습니다: " + path);
            return asset;
        }

        /// <summary>생성 전용 하위 폴더를 기존 폴더와 GUID를 보존하며 만듭니다.</summary>
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int separator = path.LastIndexOf('/');
            string parent = path.Substring(0, separator);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(separator + 1));
        }

        /// <summary>주어진 부모 아래 빈 배치 기준점을 만듭니다.</summary>
        private static Transform Empty(string name, Transform parent, Vector3 position)
        {
            Transform transform = new GameObject(name).transform;
            transform.SetParent(parent, false);
            transform.localPosition = position;
            return transform;
        }

        /// <summary>장식용 상자 또는 이동을 막는 가구 상자를 만듭니다.</summary>
        private static GameObject Box(string name, Transform parent, Vector3 position, Vector3 scale, string material, bool collision = false, int area = 1)
        {
            GameObject item = Primitive(name, PrimitiveType.Cube, parent, position, scale, material, collision);
            if (collision) AddNavBox(item.GetComponent<BoxCollider>(), area);
            return item;
        }

        /// <summary>충돌이 없는 장식용 타원체를 만듭니다.</summary>
        private static GameObject Sphere(string name, Transform parent, Vector3 position, Vector3 scale, string material)
        {
            return Primitive(name, PrimitiveType.Sphere, parent, position, scale, material);
        }

        /// <summary>충돌이 없는 원통 장식을 만듭니다.</summary>
        private static GameObject Cylinder(string name, Transform parent, Vector3 position, Vector3 scale, string material)
        {
            return Primitive(name, PrimitiveType.Cylinder, parent, position, scale, material);
        }

        /// <summary>공통 기본 도형을 만들고 사용하지 않는 Collider를 제거합니다.</summary>
        private static GameObject Primitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, string material, bool collision = false)
        {
            GameObject item = GameObject.CreatePrimitive(type);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = position;
            item.transform.localScale = scale;
            item.GetComponent<Renderer>().sharedMaterial = Materials[material];
            if (!collision) Object.DestroyImmediate(item.GetComponent<Collider>());
            return item;
        }

        /// <summary>저장된 절차 메시를 장식 오브젝트로 배치합니다.</summary>
        private static GameObject MeshObject(string name, Mesh mesh, Transform parent, Vector3 position, Vector3 scale, string material)
        {
            Transform item = Empty(name, parent, position);
            item.localScale = scale;
            item.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            item.gameObject.AddComponent<MeshRenderer>().sharedMaterial = Materials[material];
            return item.gameObject;
        }

        /// <summary>두 끝점을 잇는 가는 원통으로 수염과 꼬리를 만듭니다.</summary>
        private static void Segment(string name, Transform parent, Vector3 start, Vector3 end, float thickness, string material)
        {
            GameObject item = Cylinder(name, parent, (start + end) * .5f, new Vector3(thickness, (end - start).magnitude * .5f, thickness), material);
            item.transform.localRotation = Quaternion.FromToRotation(Vector3.up, end - start);
        }

        /// <summary>지면의 얇은 색 고리로 플레이어와 상호작용 위치를 구별합니다.</summary>
        private static void Ring(string name, Transform parent, float radius, string material)
        {
            LineRenderer ring = Empty(name, parent, new Vector3(0f, .04f, 0f)).gameObject.AddComponent<LineRenderer>();
            ring.sharedMaterial = Materials[material];
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.positionCount = 48;
            ring.widthMultiplier = .035f;
            ring.generateLightingData = true;
            ring.shadowCastingMode = ShadowCastingMode.Off;
            ring.receiveShadows = false;
            for (int i = 0; i < ring.positionCount; i++)
            {
                float angle = i * Mathf.PI * 2f / ring.positionCount;
                ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }
        }

        /// <summary>가구 윗면을 걷지 않도록 상자 전체를 비보행 영역으로 제공합니다.</summary>
        private static void AddNavBox(BoxCollider collider, int area)
        {
            Sources.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box,
                transform = collider.transform.localToWorldMatrix * Matrix4x4.Translate(collider.center),
                size = collider.size,
                area = area
            });
        }
    }
}
