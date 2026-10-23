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

        /// <summary>텍스트의 실제 줄바꿈 결과가 할당한 영역 안에 들어오는지 확인한다.</summary>
        private static void AssertTextFits(string name)
        {
            var label = GameObject.Find(name).GetComponent<TMP_Text>();
            label.ForceMeshUpdate();
            Assert.That(label.preferredHeight, Is.LessThanOrEqualTo(label.rectTransform.rect.height + 1f), name);
            Assert.That(label.textBounds.size.x, Is.LessThanOrEqualTo(label.rectTransform.rect.width + 1f), name);
        }

        /// <summary>준비 중에는 종류를 미리 공개하지 않고 시작 뒤에만 도움말과 저장을 갱신한다.</summary>
        [UnityTest]
        public IEnumerator EncounterHelpWaitsForStartPersistsAndClosesBackToPause()
        {
            Click("HelpButton");
            Assert.That(GameObject.Find("CatHelp").GetComponent<TMP_Text>().text, Does.Not.Contain("게으름냥"));
            Click("CloseHelpButton");
            for (int i = 0; i < 2; i++) _game.Progress.RecordResult(_game.Nights[i].Id, true, false, 0, 200f);
            Assert.That(_game.BeginNight(2), Is.True);
            Assert.That(_game.Progress.HasEncountered(CatKind.Lazy), Is.False);
            _game.ConfirmStart();
            yield return new WaitForSecondsRealtime(.15f);
            yield return Press(Key.Escape);
            yield return new WaitForSecondsRealtime(.15f);
            Click("PauseHelpButton");
            string help = GameObject.Find("CatHelp").GetComponent<TMP_Text>().text;
            Assert.That(help, Does.Contain("호기심냥").And.Contain("게으름냥").And.Not.Contain("사냥꾼냥"));
            var loaded = new CampaignStore(_path).Load();
            Assert.That(loaded.HasEncountered(CatKind.Curious) && loaded.HasEncountered(CatKind.Lazy), Is.True);
            Assert.That(loaded.HasEncountered(CatKind.Hunter), Is.False);
            AssertTextFits("HelpBody");
            AssertTextFits("CatHelp");
            yield return Press(Key.Escape);
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Paused));
            Assert.That(GameObject.Find("HelpScreen"), Is.Null);
            Assert.That(UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.name, Is.EqualTo("PauseHelpButton"));
            // 이미 알려진 세 종류를 표시해도 좁은 화면에 맞춘 고정 영역을 넘지 않는다.
            _game.Progress.RecordEncounter(CatKind.Hunter);
            Click("PauseHelpButton");
            AssertTextFits("CatHelp");
        }

        /// <summary>마지막 실제 고양이 접촉을 기록하고 종료 후 피격·재시작·승리와 섞지 않는다.</summary>
        [UnityTest]
        public IEnumerator FatalCatContactShowsAdviceAndRestartClearsIt()
        {
            _game.enabled = false;
            for (int i = 0; i < 2; i++) _game.Progress.RecordResult(_game.Nights[i].Id, true, false, 0, 200f);
            Assert.That(_game.BeginNight(2), Is.True);
            _game.ConfirmStart();
            var session = (GameSession)typeof(GameController).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_game);
            while (_game.Hearts > 1)
            {
                Assert.That(_game.TryDamagePlayer(Vector3.zero), Is.True);
                Assert.That(_game.TryDamagePlayer(Vector3.zero), Is.False);
                Assert.That(_game.DefeatCause, Is.Empty);
                session.Tick(_game.Rules.Invulnerability + .01f);
            }
            CatController lazy = null;
            foreach (var cat in _game.Cats)
                if (cat.IsAvailable && cat.Definition.Kind == CatKind.Lazy) { lazy = cat; break; }
            Assert.That(lazy, Is.Not.Null);
            lazy.ReceiveNoise(lazy.transform.position + Vector3.right * 2f, 40f, NoiseKind.Loud, false);
            Assert.That(lazy.State, Is.EqualTo(CatState.WakeRush));
            var controller = _game.Player.GetComponent<CharacterController>();
            controller.enabled = false;
            _game.Player.transform.position = lazy.transform.position;
            controller.enabled = true;
            lazy.Tick(.01f);
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Lost));
            Assert.That(_game.DefeatCause, Does.Contain("게으름냥").And.Contain("기상 질주"));
            string cause = _game.DefeatCause;
            Assert.That(_game.TryDamagePlayer(Vector3.zero), Is.False);
            Assert.That(_game.DefeatCause, Is.EqualTo(cause));
            yield return new WaitForSecondsRealtime(.15f);
            Assert.That(GameObject.Find("DefeatAdvice").GetComponent<TMP_Text>().text, Does.Contain(cause));
            AssertTextFits("DefeatAdvice");
            Click("RetryButton");
            Assert.That(_game.DefeatCause, Is.Empty);
            _game.ConfirmStart();
            yield return new WaitForSecondsRealtime(.15f);
            session.AdvanceClock(_game.Duration);
            _game.enabled = true;
            yield return new WaitForSecondsRealtime(.15f);
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Won));
            Assert.That(GameObject.Find("DefeatAdvice"), Is.Null);
        }

        /// <summary>준비 시간을 오래 보내도 시작 후 배너가 보이고 표시 중 게임 시계가 흐른다.</summary>
        [UnityTest]
        public IEnumerator IntroductionGetsFullDurationAfterReadyWait()
        {
            var hud = Object.FindFirstObjectByType<GameHud>();
            typeof(GameHud).GetField("_nightBannerSeconds", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(hud, .6f);
            Assert.That(_game.BeginNight(0), Is.True);
            yield return new WaitForSecondsRealtime(.8f);
            Assert.That(_game.Elapsed, Is.Zero);
            Assert.That(GameObject.Find("NightIntroduction"), Is.Not.Null);
            _game.ConfirmStart();
            yield return new WaitForSecondsRealtime(.25f);
            Assert.That(GameObject.Find("NightIntroduction"), Is.Not.Null);
            Assert.That(_game.RealElapsed, Is.GreaterThan(0f));
            yield return new WaitForSecondsRealtime(.6f);
            Assert.That(GameObject.Find("NightIntroduction"), Is.Null);
        }

        /// <summary>4일째는 생존 기록으로 해금되고 실제 메뉴 제출로 시작하며 미구현 밤만 준비 중이다.</summary>
        [UnityTest]
        public IEnumerator FourthNightUnlocksAndStartsThroughMenu()
        {
            yield return Press(Key.Enter);
            var fourth = GameObject.Find("NightButton4").GetComponent<UnityEngine.UI.Button>();
            Assert.That(fourth.interactable, Is.False);
            Assert.That(fourth.GetComponentInChildren<TMP_Text>().text, Is.EqualTo("앞선 밤 생존 후 열림"));
            Assert.That(GameObject.Find("NightCard4").GetComponentsInChildren<TMP_Text>()[0].text, Does.Contain("장난감 공"));
            Click("NightButton4");
            yield return null;
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Title));
            for (int i = 5; i <= 7; i++)
            {
                var later = GameObject.Find("NightButton" + i).GetComponent<UnityEngine.UI.Button>();
                Assert.That(later.interactable, Is.False);
                Assert.That(later.GetComponentInChildren<TMP_Text>().text, Is.EqualTo("준비 중"));
            }
            for (int i = 0; i < 3; i++) _game.Progress.RecordResult(_game.Nights[i].Id, true, false, 0, 200f);
            yield return Press(Key.Escape);
            yield return Press(Key.Enter);
            Assert.That(fourth.interactable, Is.True);
            Assert.That(fourth.GetComponentInChildren<TMP_Text>().text, Is.EqualTo("이 밤 시작하기"));
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(fourth.gameObject);
            yield return Press(Key.Enter);
            yield return new WaitForSecondsRealtime(.15f);
            Assert.That(_game.CurrentNight.Order, Is.EqualTo(4));
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Ready));
            Assert.That(_game.Elapsed, Is.Zero);
            Assert.That(_game.Ball.gameObject.activeSelf, Is.True);
            string request = GameObject.Find("RequestProgress").GetComponent<TMP_Text>().text;
            Assert.That(request, Does.Contain("깨워서 한 방"));
            Assert.That(request, Does.Contain("공으로 깨운 뒤 10초 안에 타격"));
            yield return Press(Key.Enter);
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Playing));
            _game.RecordBallWakeHit();
            yield return new WaitForSecondsRealtime(.15f);
            Assert.That(GameObject.Find("RequestProgress").GetComponent<TMP_Text>().text, Does.Contain("완료"));
        }

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
