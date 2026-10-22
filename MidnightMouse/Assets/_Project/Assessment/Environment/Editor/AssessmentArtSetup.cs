using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MidnightMouse
{
    /// <summary>기존 충돌체와 게임 연결을 유지하며 임포트한 모델을 평가 씬에 배치한다.</summary>
    public static class AssessmentArtSetup
    {
        private const string Art = "Assets/_Project/Art/ThirdParty/";
        private const string Furniture = Art + "Furniture/dlgames/Prefabs/";
        private const string CatPrefab = "Assets/_Project/Assessment/Environment/Prefabs/CuriousCat.prefab";

        /// <summary>현재 평가 씬에만 모델을 적용하고 저장한다.</summary>
        [MenuItem("MidnightMouse/받은 리소스 적용")]
        public static void Apply()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != "Assets/Scenes/SampleScene.unity")
                throw new InvalidOperationException("편집 모드의 SampleScene에서 실행해야 합니다.");
            ApplyToScene(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("씬 저장에 실패했습니다.");
        }

        /// <summary>재생성한 씬과 기존 씬에 동일한 모델 배치를 적용한다.</summary>
        public static void ApplyToScene(Scene scene)
        {
            Transform environment = scene.GetRootGameObjects().Single(o => o.name == "Environment_NightLivingRoom").transform;
            string[] required = { Art + "Cat/PolyOne/Prefab/SM_CartoonAnimal_Cat.prefab", Art + "Fan/Solodream/Mesh/Table_fan.fbx",
                Art + "Rug/ithappy/Prefabs/Props/Rug_01.prefab", Furniture + "Sofas/Sofa01.prefab", Furniture + "Tables/Table01.prefab",
                Furniture + "Chairs/Chair01.prefab", Furniture + "Kitchen/Refrigerator01.prefab", Furniture + "Kitchen/CabinetA01.prefab",
                Furniture + "Kitchen/CabinetA_Sink.prefab" };
            foreach (string path in required)
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) throw new InvalidOperationException("모델 누락: " + path);

            // 기존 도형은 충돌·길찾기 자료로 유지하고, 교체 대상의 렌더러만 숨긴다.
            Transform sofa = environment.Find("Sofa");
            Hide(sofa, "SofaBody", "Backrest", "SeatCushion", "Armrest", "Pillow");
            Place(required[3], sofa, "ImportedSofa", Vector3.zero, new Vector3(5.8f, 1.65f, 2.1f), 180f);
            Transform table = environment.Find("DiningTable");
            Hide(table, "Tabletop", "TableLeg");
            Place(required[4], table, "ImportedTable", Vector3.zero, new Vector3(3.9f, 1.4f, 2.8f));
            // 등받이는 상판 밖에 두고 좌판 앞부분만 식탁 아래에 넣는다.
            ChairObstacle(Place(required[5], table, "ImportedChairFront", new Vector3(0f, 0f, -1.65f), new Vector3(.85f, 1.8f, .9f)));
            ChairObstacle(Place(required[5], table, "ImportedChairBack", new Vector3(0f, 0f, 1.65f), new Vector3(.85f, 1.8f, .9f), 180f));

            Transform kitchen = environment.Find("Kitchen");
            Hide(kitchen, "CabinetBody", "Countertop", "CabinetDoor", "DoorHandle", "SinkRim", "Sink", "Faucet", "FaucetSpout");
            Place(required[8], kitchen, "ImportedSink", new Vector3(-2.58f, 0f, 0f), new Vector3(3.44f, 1.66f, 2f));
            for (int i = 0; i < 3; i++)
                Place(required[7], kitchen, "ImportedCabinet" + i, new Vector3(i * 1.72f, 0f, 0f), new Vector3(1.72f, 1.66f, 2f));
            Hide(environment, "Fridge", "FridgeDivide", "FridgeHandle", "FridgeNote", "RugBorder", "RugInside", "RugStripe");
            Place(required[6], environment, "ImportedFridge", new Vector3(11.3f, 0f, 5.95f), new Vector3(1.95f, 2.9f, 1.8f));
            // 바닥 범위 선(높이 .045m)의 아래쪽까지 러그가 가리지 않게 한다.
            Place(required[2], environment, "ImportedRug", new Vector3(-5.6f, .005f, .3f), new Vector3(8f, .01f, 6.1f), 90f);
            // 선풍기 함정이 아직 없는 단계이므로 조리대 위에 모델만 배치한다.
            Place(required[1], kitchen, "ImportedFan", new Vector3(3.45f, 1.66f, .1f), new Vector3(.7f, .8f, .45f));
            ApplyRoomDetails(environment, table, kitchen);

            GameObject cat = PrefabUtility.LoadPrefabContents(CatPrefab);
            try
            {
                Transform visual = cat.transform.Find("Visual");
                if (visual.Find("ImportedCat") == null)
                {
                    for (int i = visual.childCount - 1; i >= 0; i--) Object.DestroyImmediate(visual.GetChild(i).gameObject);
                }
                GameObject model = Place(required[0], visual, "ImportedCat", Vector3.zero, new Vector3(.68f, 1.15f, 1.42f));
                Animator animator = model.GetComponent<Animator>();
                if (animator == null) throw new InvalidOperationException("고양이 Animator가 없습니다.");
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.runtimeAnimatorController = LoopingCatAnimations();
                PrefabUtility.SaveAsPrefabAsset(cat, CatPrefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(cat); }
            EditorSceneManager.MarkSceneDirty(scene);
        }

        /// <summary>의자의 실제 크기에 충돌체와 길찾기 구멍을 맞춘다.</summary>
        private static void ChairObstacle(GameObject chair)
        {
            Bounds bounds = LocalBounds(chair.transform, chair.GetComponentsInChildren<Renderer>(true));
            var collider = chair.GetComponent<BoxCollider>();
            if (collider == null) collider = chair.AddComponent<BoxCollider>();
            collider.center = bounds.center;
            collider.size = bounds.size;
            collider.enabled = true;
            var obstacle = chair.GetComponent<NavMeshObstacle>();
            if (obstacle == null) obstacle = chair.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = bounds.center;
            obstacle.size = bounds.size;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;
        }

        /// <summary>받은 소품과 저대비 재질을 배치하고 기존 창문·수전을 다듬는다.</summary>
        private static void ApplyRoomDetails(Transform environment, Transform table, Transform kitchen)
        {
            Material wood = Surface("FloorWood", "WoodFloor007/WoodFloor007_2K-PNG", new Color(.78f, .68f, .55f), new Vector2(6f, 5f), .18f);
            Material wallpaper = Surface("Wallpaper", "Wallpaper001A/Wallpaper001A_2K-PNG", new Color(.36f, .53f, .52f), new Vector2(6f, 1f), .12f);
            Material linen = Surface("TableLinen", "Fabric030/Fabric030_1K-PNG", new Color(.42f, .66f, .62f), new Vector2(1f, 3f), .2f);
            foreach (Transform child in environment)
            {
                if (child.name.StartsWith("Plank_")) child.GetComponent<Renderer>().enabled = false;
                if (child.name == "Floor") child.GetComponent<Renderer>().sharedMaterial = wood;
                if (child.name == "BackWall" || child.name == "LeftWall" || child.name == "RightWall") child.GetComponent<Renderer>().sharedMaterial = wallpaper;
                if (child.name == "Plant")
                {
                    Hide(child, "Pot", "Soil", "Leaf");
                    Place(Art + "Kenney/Furniture/pottedPlant.fbx", child, "ImportedPlant", Vector3.zero, new Vector3(.85f, 1.65f, .85f));
                }
            }
            table.Find("TableRunner").GetComponent<Renderer>().sharedMaterial = linen;
            Hide(table, "FruitBowl", "FruitPear", "FruitOrange", "Book");
            Place(Art + "Kenney/Food/bowl.fbx", table, "ImportedFruitBowl", new Vector3(.65f, 1.43f, .4f), new Vector3(.75f, .23f, .75f));
            Place(Art + "Kenney/Food/apple.fbx", table, "ImportedApple", new Vector3(.48f, 1.52f, .36f), Vector3.one * .28f);
            Place(Art + "Kenney/Food/orange.fbx", table, "ImportedOrange", new Vector3(.8f, 1.52f, .35f), Vector3.one * .27f);
            Place(Art + "Kenney/Food/pear.fbx", table, "ImportedPear", new Vector3(.65f, 1.52f, .57f), new Vector3(.25f, .37f, .25f));
            Place(Art + "Kenney/Furniture/books.fbx", table, "ImportedBooks", new Vector3(-1.2f, 1.41f, -.65f), new Vector3(.65f, .32f, .45f), 90f);
            Hide(environment, "Sideboard", "ShelfBook");
            Place(Furniture + "Drawers/Drawer03.prefab", environment, "ImportedSideboard", new Vector3(-5.2f, 0f, 8.65f), new Vector3(2.5f, 1.1f, 1.25f));
            Place(Art + "Kenney/Furniture/books.fbx", environment, "ImportedShelfBooks", new Vector3(-5.7f, 1.1f, 8.65f), new Vector3(.95f, .45f, .5f));

            // 싱크 모델에는 수전이 없어 기존 수전을 싱크 중심으로 옮겨 재사용한다.
            foreach (string name in new[] { "Faucet", "FaucetSpout" })
            {
                Transform faucet = kitchen.Find(name);
                Vector3 position = faucet.localPosition;
                position.x = -2.58f;
                faucet.localPosition = position;
                faucet.GetComponent<Renderer>().enabled = true;
            }
            Material cream = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Assessment/Environment/Materials/Cream.mat");
            Material brass = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Assessment/Environment/Materials/Brass.mat");
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -7f : -.7f;
                DetailBox(environment, "WindowApron" + i, new Vector3(x, .8f, 9.73f), new Vector3(4.6f, .18f, .12f), cream);
                DetailBox(environment, "WindowTopTrim" + i, new Vector3(x, 3.37f, 9.77f), new Vector3(4.85f, .14f, .24f), cream);
                DetailBox(environment, "WindowHandle" + i, new Vector3(x + .17f, 1.9f, 9.64f), new Vector3(.055f, .26f, .07f), brass);
            }
        }

        /// <summary>색상과 약한 노멀 질감만 쓰는 게임 소유 URP 재질을 만든다.</summary>
        private static Material Surface(string name, string source, Color color, Vector2 tiling, float normalStrength)
        {
            string prefix = Art + "ambientCG/" + source;
            var normalImporter = AssetImporter.GetAtPath(prefix + "_NormalGL.png") as TextureImporter;
            if (normalImporter == null) throw new InvalidOperationException("텍스처 누락: " + prefix);
            if (normalImporter.textureType != TextureImporterType.NormalMap)
            {
                normalImporter.textureType = TextureImporterType.NormalMap;
                normalImporter.SaveAndReimport();
            }
            string path = "Assets/_Project/Assessment/Environment/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(prefix + "_Color.png"));
            material.SetColor("_BaseColor", color);
            material.SetTextureScale("_BaseMap", tiling);
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(prefix + "_NormalGL.png"));
            material.SetFloat("_BumpScale", normalStrength);
            material.SetFloat("_Smoothness", .12f);
            material.EnableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        /// <summary>창틀 장식을 중복 없이 만들며 이동을 막는 충돌체는 추가하지 않는다.</summary>
        private static void DetailBox(Transform parent, string name, Vector3 position, Vector3 size, Material material)
        {
            Transform existing = parent.Find(name);
            GameObject box = existing != null ? existing.gameObject : GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = position;
            box.transform.localScale = size;
            box.GetComponent<Renderer>().sharedMaterial = material;
            var collider = box.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);
        }

        /// <summary>원본을 보존하고 게임에서 반복 재생할 클립과 교체 컨트롤러만 저장한다.</summary>
        private static RuntimeAnimatorController LoopingCatAnimations()
        {
            const string folder = "Assets/_Project/Assessment/Environment/Animations";
            const string source = Art + "Cat/PolyOne/Animation/Cat/";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/_Project/Assessment/Environment", "Animations");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(folder + "/CatLocomotion.overrideController");
            if (controller == null)
            {
                controller = new AnimatorOverrideController();
                AssetDatabase.CreateAsset(controller, folder + "/CatLocomotion.overrideController");
            }
            controller.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(source + "Controller_CartoonAnimal_Cat.controller");
            string[] names = { "A_CartoonAnimal-Cat_Idle", "A_CartoonAnimal-Cat__Run" };
            foreach (string name in names)
            {
                AnimationClip original = AssetDatabase.LoadAssetAtPath<AnimationClip>(source + name + ".anim");
                if (original == null) throw new InvalidOperationException("고양이 동작 누락: " + name);
                string path = folder + "/" + (name.EndsWith("Idle") ? "CatIdle" : "CatRun") + ".anim";
                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null)
                {
                    clip = Object.Instantiate(original);
                    AssetDatabase.CreateAsset(clip, path);
                }
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = true;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                AssetDatabase.SaveAssetIfDirty(clip);
                controller[original.name] = clip;
            }
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
            return controller;
        }

        /// <summary>기존 충돌체를 보존하면서 이름으로 지정한 도형의 표시만 끈다.</summary>
        private static void Hide(Transform parent, params string[] names)
        {
            foreach (Transform child in parent)
                if (names.Contains(child.name))
                    foreach (Renderer renderer in child.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        }

        /// <summary>원본 프리팹을 연결한 채 기준 경계와 바닥 높이에 맞추며 중복 배치를 막는다.</summary>
        private static GameObject Place(string path, Transform parent, string name, Vector3 position, Vector3 size, float yaw = 0f)
        {
            Transform existing = parent.Find(name);
            GameObject model = existing != null ? existing.gameObject : (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            model.name = name;
            model.transform.SetParent(parent, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            model.transform.localScale = Vector3.one;
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = LocalBounds(parent, renderers);
            if (bounds.size.x < .0001f || bounds.size.y < .0001f || bounds.size.z < .0001f)
                throw new InvalidOperationException("모델 경계가 잘못되었습니다: " + path);
            // 회전 뒤의 가로·세로 크기에 맞추기 위해 원본 모델 위에 축별 스케일을 적용한다.
            Vector3 factor = new Vector3(size.x / bounds.size.x, size.y / bounds.size.y, size.z / bounds.size.z);
            model.transform.localScale = Mathf.RoundToInt(yaw) % 180 == 0 ? factor : new Vector3(factor.z, factor.y, factor.x);
            bounds = LocalBounds(parent, renderers);
            model.transform.localPosition = position - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            return model;
        }

        /// <summary>부모의 이동·회전에 영향을 받지 않는 렌더링 경계를 계산한다.</summary>
        private static Bounds LocalBounds(Transform parent, Renderer[] renderers)
        {
            if (renderers.Length == 0) throw new InvalidOperationException("모델에 Renderer가 없습니다.");
            Bounds bounds = new Bounds(parent.InverseTransformPoint(renderers[0].bounds.center), Vector3.zero);
            foreach (Renderer renderer in renderers)
            {
                Bounds world = renderer.bounds;
                for (int i = 0; i < 8; i++)
                    bounds.Encapsulate(parent.InverseTransformPoint(world.center + Vector3.Scale(world.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            }
            return bounds;
        }
    }
}
