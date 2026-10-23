using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MidnightMouse.Tests
{
    /// <summary>4일째 실제 씬에서 공의 입력·충돌·소음·위치 유지와 부탁 연결을 확인한다.</summary>
    public sealed class ToyBallGameplayTests
    {
        private GameController _game;
        private ToyBall _ball;
        private Vector3 _start;
        private InputTestFixture _input;
        private Keyboard _keyboard;
        private Mouse _mouse;

        /// <summary>독립 입력과 진행 상태에서 넷째 밤을 열고 시간은 시험에서 진행한다.</summary>
        [UnitySetUp]
        public IEnumerator Load()
        {
            _input = new InputTestFixture(); _input.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/SampleScene.unity");
            yield return null;
            _game = Object.FindFirstObjectByType<GameController>();
            _game.enabled = false;
            typeof(GameController).GetField("<Progress>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_game, new CampaignProgress());
            for (int i = 0; i < 3; i++) _game.Progress.RecordResult(_game.Nights[i].Id, true, false, 0, 200);
            Assert.That(_game.BeginNight(3), Is.True);
            _ball = _game.Ball;
            _start = _ball.transform.position;
            MovePlayer(_start + Vector3.back * .6f);
            Physics.SyncTransforms();
        }

        /// <summary>씬과 시험 입력을 복원하고 예외 로그를 검사한다.</summary>
        [UnityTearDown]
        public IEnumerator Unload()
        {
            var scene = _game.gameObject.scene;
            SceneManager.SetActiveScene(SceneManager.CreateScene("ToyBallCleanup"));
            yield return SceneManager.UnloadSceneAsync(scene);
            _input.TearDown();
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>CharacterController를 잠시 끄고 시험 위치로 옮긴다.</summary>
        private void MovePlayer(Vector3 point)
        {
            var body = _game.Player.GetComponent<CharacterController>();
            body.enabled = false; _game.Player.transform.position = point; body.enabled = true;
        }

        /// <summary>시험의 실제 시간은 중앙 세션과 공에 함께 전달한다.</summary>
        private void Tick(float delta)
        {
            ((GameSession)Field(_game, "_session")).Tick(delta);
            _ball.Tick(delta);
        }

        /// <summary>내부 시간과 표시를 읽되 제품 API는 늘리지 않는다.</summary>
        private static object Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

        /// <summary>준비 상태에서는 차지 못하며 첫 세 밤에는 공이 나타나지 않는다.</summary>
        [Test]
        public void BallStartsOnlyOnFourthNightAndWaitsForStartInput()
        {
            Assert.That(_ball.gameObject.activeSelf, Is.True);
            Assert.That(_game.TryInteract(_start + Vector3.forward), Is.False);
            Tick(20f);
            Assert.That(_ball.transform.position, Is.EqualTo(_start));
            _game.ReturnToTitle();
            Assert.That(_game.BeginNight(2), Is.True);
            Assert.That(_ball.gameObject.activeSelf, Is.False);
        }

        /// <summary>목적지에서 멈추고 최대 6m와 일정 속도, 구르는 중 재입력 제한을 지킨다.</summary>
        [Test]
        public void CursorDistanceSpeedAndRollingLockAreRespected()
        {
            _game.ConfirmStart();
            Vector3 end = _start + Vector3.forward * 1.5f;
            Vector3 direction = Vector3.zero;
            var layout = Object.FindFirstObjectByType<M1Layout>();
            for (int i = 0; i < 8; i++)
            {
                Vector3 candidate = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
                Vector3 far = _start + candidate * 6f;
                if (NavMesh.Raycast(_start, far, out _, NavMesh.AllAreas) || !layout.IsClearSegment(_start, far, .3f)) continue;
                direction = candidate;
                break;
            }
            Assert.That(direction.sqrMagnitude, Is.GreaterThan(.9f), "최대 거리 시험에 필요한 열린 직선 통로");
            Assert.That(Vector3.Distance(_ball.LandingPoint(_start + direction * 20f), _start + direction * 6f), Is.LessThan(.001f));
            Assert.That(_game.TryInteract(end), Is.True);
            Assert.That(_game.TryInteract(end), Is.False);
            Tick(.25f);
            Assert.That(Vector3.Distance(_start, _ball.transform.position), Is.EqualTo(.75f).Within(.001f));
            Tick(.25f);
            Assert.That(_ball.IsRolling, Is.False);
            Assert.That(Vector3.Distance(end, _ball.transform.position), Is.LessThan(.001f));
            MovePlayer(end);
            Assert.That(_game.TryInteract(end + Vector3.forward), Is.True, "정지 뒤 쿨타임 없음");
        }

        /// <summary>가구 충돌은 실제 도착과 예고선에 같게 반영되고 이동 개체는 무시한다.</summary>
        [Test]
        public void FurnitureClipsPreviewAndMovementButActorsDoNot()
        {
            _game.ConfirmStart();
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "시험 가구";
            wall.transform.SetParent(GameObject.Find("Environment_NightLivingRoom").transform);
            wall.transform.position = _start + Vector3.forward * 2f + Vector3.up * .5f;
            wall.transform.localScale = new Vector3(1f, 1f, .2f);
            Physics.SyncTransforms();
            Vector3 end = _ball.LandingPoint(_start + Vector3.forward * 5f);
            Assert.That(Vector3.Distance(end, _start), Is.InRange(1.65f, 1.75f));
            _ball.ShowAim(true, _start + Vector3.forward * 5f);
            Assert.That(((LineRenderer)Field(_ball, "_line")).GetPosition(1), Is.EqualTo(end + Vector3.up * .06f));
            Assert.That(_ball.TryKick(_start + Vector3.forward * 5f), Is.True);
            Tick(2f);
            Assert.That(Vector3.Distance(end, _ball.transform.position), Is.LessThan(.001f));
            Assert.That(_ball.IsRolling, Is.False);
            MovePlayer(end + Vector3.back * .6f);
            Assert.That(_ball.TryKick(_start), Is.True, "가구 앞에 멈춘 공을 되찰 수 있음");
            Tick(2f);
            Assert.That(Vector3.Distance(_start, _ball.transform.position), Is.LessThan(.001f));
            wall.transform.SetParent(_game.transform);
            _ball.ResetRun(true);
            Physics.SyncTransforms();
            Assert.That(_ball.LandingPoint(_start + Vector3.forward * 5f), Is.EqualTo(_start + Vector3.forward * 5f));
        }

        /// <summary>한 번의 E는 가장 가까운 대상만 사용하며 공을 찰 수 없어도 뒤의 함정을 누르지 않는다.</summary>
        [Test]
        public void EChoosesOneClosestTargetAndRejectsInvalidAim()
        {
            _game.ConfirmStart();
            var trap = _game.Traps[0];
            _ball.transform.position = trap.transform.position + Vector3.back * .8f;
            MovePlayer(_ball.transform.position);
            Assert.That(_game.TryInteract(new Vector3(float.NaN, 0, 0)), Is.False);
            Assert.That(_game.TryInteract(null), Is.False);
            Assert.That(trap.IsPressing, Is.False);
            MovePlayer(trap.transform.position);
            Assert.That(_game.TryInteract(_ball.transform.position + Vector3.back), Is.True);
            Assert.That(trap.IsPressing, Is.True);
            Assert.That(_ball.IsRolling, Is.False);
            MovePlayer(_start + Vector3.left * 5f);
            Assert.That(_ball.TryKick(_start), Is.False);
        }

        /// <summary>실제 커서와 E 입력을 중앙 갱신에서 읽어 공을 움직인다.</summary>
        [UnityTest]
        public IEnumerator KeyboardAndCursorKickThroughGameUpdate()
        {
            _game.ConfirmStart();
            foreach (var cat in _game.Cats) cat.SetAvailable(false);
            Vector3 screen = _game.WorldCamera.WorldToScreenPoint(_start + Vector3.forward * 2f);
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = new Vector2(screen.x, screen.y) });
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.E));
            _game.enabled = true;
            yield return null;
            yield return null;
            _game.enabled = false;
            Assert.That(_ball.IsRolling, Is.True);
            Assert.That(_ball.transform.position.z, Is.GreaterThan(_start.z));
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
        }

        /// <summary>보행 경계 밖으로 빠지지 않고 모찌가 도달해 다시 찰 수 있다.</summary>
        [Test]
        public void BoundaryKeepsBallReachableForRekick()
        {
            _game.ConfirmStart();
            Assert.That(_ball.TryKick(_start + Vector3.back * 6f), Is.True);
            Tick(3f);
            Vector3 stopped = _ball.transform.position;
            Assert.That(_ball.IsRolling, Is.False);
            Assert.That(Vector3.Distance(_start, stopped), Is.InRange(.5f, 5.9f));
            Assert.That(NavMesh.SamplePosition(stopped, out var hit, .1f, NavMesh.AllAreas), Is.True);
            var path = new NavMeshPath();
            Assert.That(NavMesh.CalculatePath(_start, hit.position, NavMesh.AllAreas, path), Is.True);
            Assert.That(path.status, Is.EqualTo(NavMeshPathStatus.PathComplete));
            MovePlayer(stopped + Vector3.forward * .6f);
            Assert.That(_ball.TryKick(_start), Is.True);
            Tick(3f);
            Assert.That(Vector3.Distance(_start, _ball.transform.position), Is.LessThan(.001f));
        }

        /// <summary>방치·일시정지·아침 당김 뒤에도 그 자리에서 재차기하며 재시작만 위치를 초기화한다.</summary>
        [Test]
        public void StoppedBallStaysUntilRekickedOrRestarted()
        {
            _game.ConfirmStart();
            Assert.That(_ball.TryKick(_start + Vector3.forward * 1.5f), Is.True);
            Tick(.5f);
            Vector3 stopped = _ball.transform.position;
            _game.TogglePause(); Tick(30f);
            Assert.That(_ball.transform.position, Is.EqualTo(stopped));
            _game.TogglePause();
            ((GameSession)Field(_game, "_session")).AdvanceClock(30f);
            Tick(60f);
            Assert.That(_ball.transform.position, Is.EqualTo(stopped));
            MovePlayer(stopped + Vector3.back * .6f);
            Assert.That(_game.TryInteract(_start), Is.True, "방치한 도착 위치에서 E로 다시 차기");
            Tick(.2f);
            _game.RestartRun();
            Assert.That(_ball.transform.position, Is.EqualTo(_start));
            Assert.That(_ball.IsRolling, Is.False);
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Ready));
        }

        /// <summary>굴림에는 반응하지 않고 정지 소리는 카드·전역 배율 밖에서 한 번만 들린다.</summary>
        [Test]
        public void RollingIsIgnoredAndStopWakesOnceWithoutMouseNoiseScaling()
        {
            _game.ConfirmStart();
            for (int i = 0; i < 10; i++) _game.Player.UpgradeNoise();
            CatController lazy = _game.Cats[1], curious = _game.Cats[0];
            Vector3 end = _start + Vector3.forward * 1.5f;
            Assert.That(curious.GetComponent<NavMeshAgent>().Warp(end + Vector3.left * 3f), Is.True);
            Assert.That(lazy.GetComponent<NavMeshAgent>().Warp(end + Vector3.left * 7f), Is.True);
            Assert.That(_ball.TryKick(end), Is.True);
            Tick(.25f);
            Assert.That(lazy.State, Is.EqualTo(CatState.Sleeping));
            Assert.That(curious.State, Is.EqualTo(CatState.Patrol));
            Tick(.25f);
            Assert.That(lazy.State, Is.EqualTo(CatState.WakeRush));
            Assert.That(curious.State, Is.EqualTo(CatState.Investigate));
            Assert.That(Field(lazy, "_target"), Is.EqualTo(end));
            lazy.Configure(lazy.Definition, end + Vector3.left * 7f, end + Vector3.left * 7f, true);
            lazy.SetAvailable(true);
            Tick(.1f);
            Assert.That(lazy.State, Is.EqualTo(CatState.Sleeping), "정지 소리는 반복하지 않음");
            Tick(12f);
            Assert.That(lazy.State, Is.EqualTo(CatState.Sleeping), "방치한 공은 추가 소리를 내지 않음");
        }

        /// <summary>공 기상 뒤 제한 시간의 함정 타격만 한 번 기록한다.</summary>
        [TestCase(9.9f, true)]
        [TestCase(10.1f, false)]
        public void BallWakeRequestChecksRealWindowAndSingleHit(float wait, bool succeeds)
        {
            _game.ConfirmStart();
            var cat = _game.Cats[1];
            _game.EmitNoise(cat.transform.position, 9f, NoiseKind.Loud, fromBall: true);
            Assert.That(cat.State, Is.EqualTo(CatState.WakeRush));
            Tick(wait);
            Assert.That(cat.ApplyTrapHit(1, .2f, Vector3.zero, switchPosition: _start), Is.True);
            Assert.That(_game.Events.RequestCompleted, Is.EqualTo(succeeds));
            cat.ApplyTrapHit(1, .2f, Vector3.zero, switchPosition: _start);
            Assert.That(_game.Events.RequestBestHits, Is.EqualTo(succeeds ? 1 : 0));
        }

        /// <summary>공 외의 기상과 일반 함정 집계로 공 부탁을 달성할 수 없다.</summary>
        [Test]
        public void OtherWakeSourcesDoNotCompleteBallRequest()
        {
            _game.ConfirmStart();
            var cat = _game.Cats[1];
            _game.EmitNoise(cat.transform.position, 9f, NoiseKind.Loud);
            cat.ApplyTrapHit(1, .2f, Vector3.zero, switchPosition: _start);
            _game.RecordTrapHit(TrapKind.Cup, 7);
            _game.RecordBaitSleepHit();
            Assert.That(_game.Events.RequestCompleted, Is.False);
        }
    }
}
