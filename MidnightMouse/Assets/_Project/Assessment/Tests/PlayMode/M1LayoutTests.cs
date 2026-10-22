using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MidnightMouse.Tests
{
    /// <summary>저장된 M1 씬에서 실제 보행면과 충돌체로 지도 조건을 검증한다.</summary>
    public sealed class M1LayoutTests
    {
        private M1Layout _layout;
        private GameController _game;

        /// <summary>저장된 씬을 읽고 가구의 길찾기 구멍이 반영될 때까지 기다린다.</summary>
        [UnitySetUp]
        public IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/SampleScene.unity");
            _layout = Object.FindFirstObjectByType<M1Layout>();
            _game = Object.FindFirstObjectByType<GameController>();
            _game.enabled = false;
            yield return new WaitForSecondsRealtime(.7f);
            yield return null;
        }

        /// <summary>각 검사의 수정과 임시 개체를 다음 검사에 남기지 않는다.</summary>
        [UnityTearDown]
        public IEnumerator Unload()
        {
            var scene = _game.gameObject.scene;
            SceneManager.SetActiveScene(SceneManager.CreateScene("M1LayoutCleanup"));
            yield return SceneManager.UnloadSceneAsync(scene);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>예약 수면·공·컵 B를 포함한 조건을 검사하고 TV장이 실제 보행면을 막는지 확인한다.</summary>
        [Test]
        public void SavedScenePassesLayoutAndCabinetBlocksNavigation()
        {
            Assert.That(_layout, Is.Not.Null);
            Assert.That(_layout.ValidateLayout(_game), Is.Empty);
            Assert.That(_layout.PetDoors.Count, Is.EqualTo(2));
            Assert.That(_layout.PetDoors[0], Is.EqualTo(_layout.transform.Find("PetDoor_LivingRoom_Entry").position));
            Assert.That(_layout.PetDoors[1], Is.EqualTo(_layout.transform.Find("PetDoor_Kitchen_Entry").position));
            Assert.That(NavMesh.SamplePosition(new Vector3(-10.2f, 0f, -4.5f), out _, .1f, NavMesh.AllAreas), Is.False, "TV장은 고양이도 통과할 수 없음");
        }

        /// <summary>경로점이 보행면에 있어도 두 지점 사이 가구를 통과하는 직선은 거부한다.</summary>
        [Test]
        public void ObstacleBetweenValidRoutePointsFails()
        {
            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blocker.transform.SetParent(_layout.transform);
            blocker.transform.position = new Vector3(-5f, .7f, -4.5f);
            Assert.That(_layout.ValidateLayout(_game).Any(error => error.Contains("검사 5: 청소기 직선 장애물")), Is.True);
        }

        /// <summary>바닥 밖 경로점을 가까운 다른 보행면으로 보정하여 합격시키지 않는다.</summary>
        [Test]
        public void RoutePointOutsideFloorFails()
        {
            _layout.VacuumRoute.GetChild(2).position = new Vector3(40f, 0f, 0f);
            Assert.That(_layout.ValidateLayout(_game).Any(error => error.Contains("검사 6: 보행 불가 청소기")), Is.True);
        }
    }
}
