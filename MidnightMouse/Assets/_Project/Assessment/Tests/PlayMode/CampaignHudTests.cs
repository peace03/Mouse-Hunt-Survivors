using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MidnightMouse.Tests
{
    /// <summary>실제 씬에서 캠페인 메뉴·인트로·부탁·결과 표시를 검증한다.</summary>
    public sealed class CampaignHudTests
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private InputTestFixture _input;
        private Keyboard _keyboard;
        private GameController _game;
        private string _directory, _path;
        private CampaignStore _store;

        /// <summary>입력 장치와 저장 경로를 테스트 전용으로 교체한다.</summary>
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return UnloadScene();
            _input = new InputTestFixture();
            _input.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            yield return SceneManager.LoadSceneAsync(ScenePath);
            yield return null;
            _game = Object.FindFirstObjectByType<GameController>();
            _directory = Path.Combine(Path.GetTempPath(), "MidnightMouse-HudTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _path = Path.Combine(_directory, "campaign.json");
            _store = new CampaignStore(_path);
            typeof(GameController).GetField("_campaignStore", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_game, _store);
            typeof(GameController).GetField("<Progress>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_game, new CampaignProgress());
        }

        /// <summary>씬을 먼저 내려 입력 참조를 끊고 이번 테스트 파일만 제거한다.</summary>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return UnloadScene();
            if (_keyboard != null) InputSystem.RemoveDevice(_keyboard);
            _input?.TearDown();
            if (_directory != null && Directory.Exists(_directory)) Directory.Delete(_directory, true);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>현재 시험 씬의 UI 입력을 완전히 종료한다.</summary>
        private static IEnumerator UnloadScene()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded) yield break;
            SceneManager.SetActiveScene(SceneManager.CreateScene("CampaignHudCleanup"));
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        /// <summary>입력 이벤트를 한 프레임 누른 뒤 놓아 실제 UI 경로로 전달한다.</summary>
        private IEnumerator Press(Key key)
        {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key));
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
        }

        /// <summary>첫 키는 시작에만 쓰며 대시·함정·일시정지·디버그로 중복 전달하지 않는다.</summary>
        [UnityTest]
        public IEnumerator ReadyWaitsAndConsumesFirstKey()
        {
            foreach (var key in new[] { Key.Space, Key.E, Key.Escape, Key.F1 })
            {
                _game.StartRun();
                Vector3 playerStart = _game.Player.transform.position;
                Vector3 catStart = _game.Cats[0].transform.position;
                yield return new WaitForSecondsRealtime(.2f);
                Assert.That(_game.Phase, Is.EqualTo(GamePhase.Ready));
                Assert.That(_game.Elapsed, Is.Zero);
                Assert.That(_game.RealElapsed, Is.Zero);
                Assert.That(GameObject.Find("ReadyText").GetComponent<TMP_Text>().text, Does.Contain("아무 키"));
                Assert.That(Vector3.Distance(playerStart, _game.Player.transform.position), Is.LessThan(.001f));
                Assert.That(Vector3.Distance(catStart, _game.Cats[0].transform.position), Is.LessThan(.001f));
                Assert.That(_game.Cats[0].ApplyTrapHit(1, 1, Vector3.zero), Is.False);
                yield return Press(key);
                Assert.That(_game.Phase, Is.EqualTo(GamePhase.Playing), key.ToString());
                Assert.That(_game.Stage, Is.EqualTo(1));
                Assert.That(_game.DashCooldown01, Is.Zero);
                foreach (var trap in _game.Traps) Assert.That(trap.IsPressing || trap.IsActive, Is.False);
            }
        }

        /// <summary>재시작 전에 잡고 있던 키와 마우스 클릭은 시작하지 않으며 새 키만 시작한다.</summary>
        [UnityTest]
        public IEnumerator RestartRequiresFreshKeyboardPress()
        {
            var mouse = InputSystem.AddDevice<Mouse>();
            _game.StartRun();
            yield return new WaitForSecondsRealtime(.15f);
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left));
            yield return null;
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Ready));
            InputSystem.QueueStateEvent(mouse, new MouseState());
            yield return Press(Key.Enter);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.W));
            yield return null;
            _game.RestartRun();
            Vector3 start = _game.Player.transform.position;
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Ready));
            Assert.That(_game.Elapsed, Is.Zero);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.W));
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Playing));
            Assert.That(Vector3.Distance(start, _game.Player.transform.position), Is.GreaterThan(.1f));
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            InputSystem.RemoveDevice(mouse);
        }

        /// <summary>활성 화면의 버튼 동작을 호출한다.</summary>
        private static void Click(string name) => GameObject.Find(name).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();

        /// <summary>7개 밤의 잠금과 인트로 세 장의 시간 정지·한 번 표시를 확인한다.</summary>
        [UnityTest]
        public IEnumerator IntroHasThreePagesWithoutClockAndDoesNotRepeat()
        {
            yield return Press(Key.Enter);
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Title));
            Assert.That(GameObject.Find("NightSelection"), Is.Not.Null);
            for (int i = 2; i <= 7; i++)
                Assert.That(GameObject.Find("NightButton" + i).GetComponent<UnityEngine.UI.Button>().interactable, Is.False);
            Click("NightButton1");
            yield return null;
            Assert.That(GameObject.Find("IntroText").GetComponent<TMP_Text>().text, Does.Contain("집사가 잠들면"));
            yield return Press(Key.Space);
            Assert.That(GameObject.Find("IntroText").GetComponent<TMP_Text>().text, Does.Contain("벽 속에"));
            yield return Press(Key.Space);
            Assert.That(GameObject.Find("IntroText").GetComponent<TMP_Text>().text, Does.Contain("아침 6시"));
            Assert.That(_game.Elapsed, Is.Zero);
            Assert.That(_game.RealElapsed, Is.Zero);
            yield return Press(Key.Enter);
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Ready));
            Assert.That(_game.Elapsed, Is.Zero);
            yield return Press(Key.Enter);
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Playing));
            Assert.That(_game.Progress.introSeen, Is.True);
            Assert.That(GameObject.Find("NightIntroduction"), Is.Not.Null);
            _game.ReturnToTitle();
            yield return new WaitForSecondsRealtime(0.15f);
            Click("StartButton");
            yield return null;
            Click("NightButton1");
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Ready));
            Assert.That(_game.Elapsed, Is.Zero);
            yield return Press(Key.Enter);
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Playing));
            Assert.That(GameObject.Find("IntroScreen"), Is.Null);
            Assert.That(new CampaignStore(_path).Load().introSeen, Is.True);
        }

        /// <summary>Esc 건너뛰기도 인트로를 본 것으로 저장하고 뒤 메뉴를 함께 닫지 않는다.</summary>
        [UnityTest]
        public IEnumerator EscapeSkipsIntroAndStartsExactlyOneNight()
        {
            yield return Press(Key.Enter);
            Click("NightButton1");
            yield return null;
            yield return Press(Key.Escape);
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Ready));
            Assert.That(_game.Elapsed, Is.Zero);
            yield return Press(Key.Enter);
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Playing));
            Assert.That(_game.Progress.introSeen, Is.True);
            Assert.That(_game.CurrentNight.Order, Is.EqualTo(1));
            Assert.That(GameObject.Find("NightSelection"), Is.Null);
            Assert.That(GameObject.Find("IntroScreen"), Is.Null);
        }

        /// <summary>두 번째 밤의 해금·부탁·결과 원기록·저장 실패 표시를 연결한다.</summary>
        [UnityTest]
        public IEnumerator SecondNightShowsRequestResultsAndPreservedSaveError()
        {
            _game.Progress.RecordResult(_game.Nights[0].Id, true, false, 1, 200f);
            yield return Press(Key.Enter);
            Assert.That(GameObject.Find("NightButton2").GetComponent<UnityEngine.UI.Button>().interactable, Is.True);
            Click("NightButton2");
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(_game.CurrentNight.Order, Is.EqualTo(2));
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Ready));
            yield return Press(Key.Enter);
            Assert.That(GameObject.Find("RequestProgress").GetComponent<TMP_Text>().text, Does.Contain(_game.CurrentNight.RequestName));
            _game.RecordTrapHit(_game.CurrentNight.RequestTrap, _game.CurrentNight.RequestHits);
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(GameObject.Find("RequestProgress").GetComponent<TMP_Text>().text, Does.Contain("완료"));
            var session = (GameSession)typeof(GameController).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_game);
            session.Tick(_game.Duration);
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Won));
            Assert.That(GameObject.Find("CampaignResult").GetComponent<TMP_Text>().text, Does.Contain("부탁 ★"));
            File.WriteAllText(_path, "damaged campaign");
            _store.Load();
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(GameObject.Find("SaveErrorText").GetComponent<TMP_Text>().text, Does.Contain("기존 파일을 보존"));
            Click("ChooseNightButton");
            yield return null;
            Assert.That(GameObject.Find("NightSelection"), Is.Not.Null);
            Assert.That(File.ReadAllText(_path), Is.EqualTo("damaged campaign"));
        }
    }
}
