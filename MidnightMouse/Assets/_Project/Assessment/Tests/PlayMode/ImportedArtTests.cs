using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace MidnightMouse.Tests
{
    /// <summary>적용한 모델의 씬 연결과 고양이 동작·정지·풀 재사용을 확인한다.</summary>
    public sealed class ImportedArtTests
    {
        private GameController _game;

        /// <summary>저장된 씬을 로드하고 게임을 시작한다.</summary>
        [UnitySetUp]
        public IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/SampleScene.unity");
            yield return null;
            _game = Object.FindFirstObjectByType<GameController>();
            _game.StartRun(); _game.ConfirmStart();
            _game.enabled = false;
        }

        /// <summary>검사한 씬과 런타임 표시를 함께 정리한다.</summary>
        [UnityTearDown]
        public IEnumerator Unload()
        {
            var scene = _game.gameObject.scene;
            SceneManager.SetActiveScene(SceneManager.CreateScene("ImportedArtCleanup"));
            yield return SceneManager.UnloadSceneAsync(scene);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>모델 교체 뒤에도 식탁 충돌 영역과 전체 고양이 연결이 유지된다.</summary>
        [Test]
        public void ImportedModelsKeepGameplayColliders()
        {
            var environment = GameObject.Find("Environment_NightLivingRoom").transform;
            var table = environment.Find("DiningTable");
            Assert.That(table.GetComponent<BoxCollider>().size, Is.EqualTo(new Vector3(3.65f, 1.35f, 2.6f)));
            foreach (string path in new[] { "Sofa/ImportedSofa", "DiningTable/ImportedTable", "DiningTable/ImportedChairFront",
                "DiningTable/ImportedChairBack", "Kitchen/ImportedSink", "Kitchen/ImportedFan", "ImportedRug", "ImportedFridge" })
            {
                var model = environment.Find(path);
                Assert.That(model, Is.Not.Null, path);
                Assert.That(model.GetComponentsInChildren<Renderer>().Length, Is.GreaterThan(0), path);
                foreach (var collider in model.GetComponentsInChildren<Collider>(true))
                    Assert.That(collider.enabled, Is.EqualTo(path.Contains("ImportedChair") && collider.gameObject == model.gameObject), path);
            }
            foreach (var cat in _game.Cats)
            {
                var animator = cat.GetComponentInChildren<Animator>(true);
                Assert.That(animator, Is.Not.Null);
                Assert.That(animator.applyRootMotion, Is.False);
            }
        }

        /// <summary>재배치한 의자에 충돌하고 고양이의 이동 지도가 의자를 우회하는지 확인한다.</summary>
        [UnityTest]
        public IEnumerator ChairsBlockMovementWithoutClosingRoomRoutes()
        {
            yield return new WaitForSecondsRealtime(.7f);
            yield return null;
            var table = GameObject.Find("Environment_NightLivingRoom/DiningTable").transform;
            foreach (string name in new[] { "ImportedChairFront", "ImportedChairBack" })
            {
                var chair = table.Find(name);
                var collider = chair.GetComponent<BoxCollider>();
                Assert.That(collider.enabled, Is.True);
                Assert.That(collider.Raycast(new Ray(collider.bounds.center + Vector3.up * 3f, Vector3.down), out _, 5f), Is.True);
                Vector3 floorPoint = new Vector3(collider.bounds.center.x, 0f, collider.bounds.center.z);
                Assert.That(NavMesh.SamplePosition(floorPoint, out _, .1f, NavMesh.AllAreas), Is.False, "의자 안으로 고양이 경로가 생성되면 안 됨");
                Assert.That(Vector3.Dot(chair.forward, (table.position - chair.position).normalized), Is.GreaterThan(.9f));
            }
            Assert.That(NavMesh.SamplePosition(new Vector3(3f, 0f, -3f), out var start, 1f, NavMesh.AllAreas), Is.True);
            Assert.That(NavMesh.SamplePosition(new Vector3(9f, 0f, 4f), out var end, 1f, NavMesh.AllAreas), Is.True);
            var path = new NavMeshPath();
            Assert.That(NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path), Is.True);
            Assert.That(path.status, Is.EqualTo(NavMeshPathStatus.PathComplete));
        }

        /// <summary>러그 표면이 소음·함정 범위 선의 두께까지 가리지 않는지 확인한다.</summary>
        [Test]
        public void RugStaysBelowNoiseAndTrapIndicators()
        {
            var rug = GameObject.Find("Environment_NightLivingRoom/ImportedRug");
            float rugTop = float.NegativeInfinity;
            foreach (var renderer in rug.GetComponentsInChildren<Renderer>())
                rugTop = Mathf.Max(rugTop, renderer.bounds.max.y);
            Assert.That(rugTop, Is.GreaterThan(0f), "러그가 바닥 안에 묻히면 안 됨");
            var lines = Object.FindObjectsByType<LineRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (string name in new[] { "소음 범위", "E 상호작용 범위", "함정 타격 범위" })
            {
                int found = 0;
                foreach (var line in lines)
                {
                    if (line.name != name) continue;
                    found++;
                    float center = line.transform.TransformPoint(line.GetPosition(0)).y;
                    Assert.That(rugTop, Is.LessThan(center - line.widthMultiplier * .5f), name);
                }
                Assert.That(found, Is.GreaterThan(0), name);
            }
        }

        /// <summary>실제 뼈 동작이 재생되고 일시정지 및 풀 재사용 시 안전하게 복구된다.</summary>
        [UnityTest]
        public IEnumerator CatBonesAnimateAndRespectPauseAndReuse()
        {
            var cat = _game.Cats[0];
            cat.SetAvailable(true);
            var animator = cat.GetComponentInChildren<Animator>();
            var bone = animator.transform.Find("Root/Spine/Spine1/Spine2/RightShoulder/RightArm/RightForeArm");
            Assert.That(bone, Is.Not.Null);
            animator.Play("Base Layer.A_CartoonAnimal-Cat__Run", 0, 0f);
            animator.Update(0f);
            Quaternion first = bone.localRotation;
            animator.Update(.12f);
            Assert.That(Quaternion.Angle(first, bone.localRotation), Is.GreaterThan(1f), "달리기 뼈가 실제로 움직여야 함");
            Assert.That(animator.GetCurrentAnimatorClipInfo(0)[0].clip.isLooping, Is.True, "교체된 클립이 반복 설정이어야 함");
            animator.Update(.5f);
            first = bone.localRotation;
            animator.Update(.1f);
            Assert.That(Quaternion.Angle(first, bone.localRotation), Is.GreaterThan(1f), "첫 주기 이후에도 뼈가 움직여야 함");
            _game.TogglePause();
            Assert.That(animator.speed, Is.Zero);
            first = bone.localRotation;
            yield return new WaitForSecondsRealtime(.1f);
            Assert.That(Quaternion.Angle(first, bone.localRotation), Is.LessThan(.01f), "정지 중 동작도 멈춰야 함");
            _game.TogglePause();
            Assert.That(animator.speed, Is.GreaterThan(0f));
            cat.SetAvailable(false);
            cat.SetAvailable(true);
            animator.Update(0f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.A_CartoonAnimal-Cat_Idle"), Is.True);
        }
    }
}
