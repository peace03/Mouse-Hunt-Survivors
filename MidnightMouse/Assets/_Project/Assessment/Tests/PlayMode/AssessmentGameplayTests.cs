using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MidnightMouse.Tests
{
    /// <summary>실제 제출 씬의 입력·UI·충돌·함정 연결을 PlayMode에서 확인한다.</summary>
    public sealed class AssessmentGameplayTests
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private InputTestFixture _input;
        private GameController _game;
        private Keyboard _keyboard;
        private string _campaignTestPath;

        /// <summary>각 시나리오는 새 씬과 독립 입력 장치에서 시작한다.</summary>
        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return UnloadScene();
            // UnitySetUp이 일반 SetUp보다 먼저 실행되므로 상속 대신 수명 순서를 직접 보장한다.
            _input = new InputTestFixture();
            _input.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            yield return SceneManager.LoadSceneAsync(ScenePath);
            yield return null;
            _game = Object.FindFirstObjectByType<GameController>();
            Assert.That(_game, Is.Not.Null, "제출 씬의 GameController 연결");
            _campaignTestPath = System.IO.Path.Combine(Application.temporaryCachePath, "CampaignTests", System.Guid.NewGuid().ToString("N") + ".json");
            typeof(GameController).GetField("_campaignStore", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_game, new CampaignStore(_campaignTestPath));
            typeof(GameController).GetField("<Progress>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_game, new CampaignProgress());
            Assert.That(Keyboard.current, Is.SameAs(_keyboard), "씬과 시험이 같은 입력 장치를 사용");
            Assert.That(InputSystem.GetDeviceById(_keyboard.deviceId), Is.SameAs(_keyboard));
        }

        /// <summary>UI 입력을 먼저 해제하고 장치와 입력 시스템을 역순으로 복원한다.</summary>
        [UnityTearDown]
        public IEnumerator CleanupScene()
        {
            yield return UnloadScene();
            if (_keyboard != null) InputSystem.RemoveDevice(_keyboard);
            _keyboard = null;
            _input?.TearDown();
            _input = null;
            foreach (string suffix in new[] { "", ".bak", ".tmp" }) if (System.IO.File.Exists(_campaignTestPath + suffix)) System.IO.File.Delete(_campaignTestPath + suffix);
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>이전 UI가 다른 입력 manager의 장치 상태를 참조하지 않도록 씬을 완전히 내린다.</summary>
        private static IEnumerator UnloadScene()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded) yield break;
            SceneManager.SetActiveScene(SceneManager.CreateScene("AssessmentTestCleanup"));
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        /// <summary>시작 화면에서 Enter를 누르면 실제 입력 경로를 통해 게임이 시작된다.</summary>
        [UnityTest]
        public IEnumerator EnterStartsGameAndHudShowsHearts()
        {
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Title));
            Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Enter));
            yield return null;
            Assert.That(_keyboard.enterKey.isPressed, Is.True, "PlayerLoop가 Enter 누름을 처리");
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Title));
            GameObject.Find("NightButton1").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Escape));
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Ready));
            Assert.That(_game.Elapsed, Is.Zero);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Enter));
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Playing));
            Assert.That(_game.Hearts, Is.EqualTo(3));
            Assert.That(GameObject.Find("Hearts").GetComponent<TMPro.TMP_Text>().text, Does.Contain("♥♥♥"));
            Assert.That(_game.Elapsed, Is.GreaterThan(0f));
        }

        /// <summary>W 입력으로 이동하고 일시정지 중에는 이동과 시계가 함께 멈춘다.</summary>
        [UnityTest]
        public IEnumerator KeyboardMovementAndPauseFreezeTogether()
        {
            _game.StartRun(); _game.ConfirmStart();
            Vector3 start = _game.Player.transform.position;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.W));
            yield return new WaitForSeconds(0.25f);
            Assert.That(_keyboard.wKey.isPressed, Is.True, "이동 프레임 동안 W 누름을 유지");
            Assert.That(Vector3.Distance(start, _game.Player.transform.position), Is.GreaterThan(0.3f));
            _game.TogglePause();
            Vector3 pausedPosition = _game.Player.transform.position;
            float pausedTime = _game.Elapsed;
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.That(_game.Elapsed, Is.EqualTo(pausedTime));
            Assert.That(Vector3.Distance(pausedPosition, _game.Player.transform.position), Is.LessThan(0.01f));
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            _game.TogglePause();
        }

        /// <summary>치즈 성장 중 시간 정지와 카드 선택, 재시작 초기화를 함께 확인한다.</summary>
        [UnityTest]
        public IEnumerator CheeseUpgradeAndRestartResetState()
        {
            _game.StartRun(); _game.ConfirmStart();
            UnityEngine.UI.Button heartChoice = null;
            foreach (var button in Object.FindFirstObjectByType<GameHud>().GetComponentsInChildren<UnityEngine.UI.Button>(true))
                if (button.name == "ChooseButton" && button.transform.parent.name == "UpgradeCard3") heartChoice = button;
            Assert.That(heartChoice, Is.Not.Null, "실제 하트 성장 카드의 선택 버튼");
            _game.CollectCheese(4);
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Upgrade));
            Assert.That(_game.Level, Is.EqualTo(2));
            // 화면 갱신 전 들어온 UI 이벤트도 같은 선택 경로에서 거절해야 한다.
            heartChoice.onClick.Invoke();
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Upgrade));
            Assert.That(_game.MaxHearts, Is.EqualTo(3));
            float elapsed = _game.Elapsed;
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.That(_game.Elapsed, Is.EqualTo(elapsed));
            Assert.That(GameObject.Find("UpgradeScreen"), Is.Not.Null);
            heartChoice.onClick.Invoke();
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Upgrade), "표시 후 0.5초 전의 선택을 차단");
            Assert.That(_game.MaxHearts, Is.EqualTo(3));
            yield return new WaitForSecondsRealtime(0.55f);
            Assert.That(heartChoice.interactable, Is.True);
            heartChoice.onClick.Invoke();
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Playing));
            Assert.That(_game.MaxHearts, Is.EqualTo(4));
            _game.RestartRun(); _game.ConfirmStart();
            Assert.That(_game.Level, Is.EqualTo(1));
            Assert.That(_game.CheeseCollected, Is.Zero);
            Assert.That(_game.Hearts, Is.EqualTo(3));
            Assert.That(_game.Elapsed, Is.Zero);
        }

        /// <summary>미끼의 중복 사용을 막고 일시정지 중 대기 시간이 줄지 않는지 확인한다.</summary>
        [UnityTest]
        public IEnumerator BaitCooldownCannotBeBypassedByPause()
        {
            _game.StartRun(); _game.ConfirmStart();
            Vector3 aim = _game.Player.transform.position + Vector3.forward * 3f;
            Assert.That(_game.TryThrowBait(aim), Is.True);
            Assert.That(_game.TryThrowBait(aim), Is.False);
            _game.TogglePause();
            float cooldown = _game.BaitCooldown01;
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.That(_game.BaitCooldown01, Is.EqualTo(cooldown));
            _game.TogglePause();
            yield return new WaitForSeconds(0.65f);
            Assert.That(_game.BaitCooldown01, Is.LessThan(cooldown));
        }

        /// <summary>컵의 실제 낙하가 범위 안 고양이에게 피해를 주며 재사용을 막는지 확인한다.</summary>
        [UnityTest]
        public IEnumerator CupDropsOnCatAndRejectsRepeatedActivation()
        {
            _game.StartRun(); _game.ConfirmStart();
            TrapController cup = null;
            foreach (var trap in Object.FindObjectsByType<TrapController>(FindObjectsSortMode.None))
                if (trap.Kind == TrapKind.Cup) cup = trap;
            Assert.That(cup, Is.Not.Null);
            CatController cat = null;
            foreach (var candidate in Object.FindObjectsByType<CatController>(FindObjectsSortMode.None))
                if (candidate.IsAvailable) { cat = candidate; break; }
            Assert.That(cat, Is.Not.Null);
            var controller = _game.Player.GetComponent<CharacterController>();
            controller.enabled = false;
            _game.Player.transform.position = cup.transform.position;
            controller.enabled = true;
            Assert.That(NavMesh.SamplePosition(cup.EffectPosition, out var hit, 2f, NavMesh.AllAreas), Is.True);
            cat.GetComponent<NavMeshAgent>().Warp(hit.position);
            cat.ApplyTrapHit(1, 2f, Vector3.zero);
            int before = cat.Health;
            Assert.That(_game.TryUseNearestTrap(), Is.True);
            Assert.That(_game.TryUseNearestTrap(), Is.False);
            yield return new WaitForSeconds(1.2f);
            Assert.That(cat.Health, Is.EqualTo(before - 2));
            Assert.That(cup.CooldownRemaining, Is.GreaterThan(18f));
        }

        /// <summary>휴지 상태에 둔 고양이도 소음 이벤트를 받아 조사 대상을 갱신한다.</summary>
        [UnityTest]
        public IEnumerator NoiseChangesCatState()
        {
            _game.StartRun(); _game.ConfirmStart();
            CatController cat = null;
            foreach (var candidate in Object.FindObjectsByType<CatController>(FindObjectsSortMode.None))
                if (candidate.IsAvailable) { cat = candidate; break; }
            Assert.That(cat, Is.Not.Null);
            _game.EmitNoise(cat.transform.position + Vector3.forward, 5f);
            Assert.That(cat.State.ToString(), Is.EqualTo("Investigate"));
            yield return null;
        }

        /// <summary>실제 청소기 경로·단일 타격·일시정지·재시작 연결을 확인한다.</summary>
        [UnityTest]
        public IEnumerator VacuumMovesHitsEachCatOnceAndResetsAfterPause()
        {
            _game.StartRun(); _game.ConfirmStart();
            TrapController vacuum = null;
            foreach (var trap in _game.Traps)
                if (trap.Kind == TrapKind.Vacuum) vacuum = trap;
            Assert.That(vacuum, Is.Not.Null, "제출 씬의 청소기 연결");
            Assert.That(_game.Cats.Count, Is.GreaterThan(0));
            CatController cat = _game.Cats[0];
            for (int index = 1; index < _game.Cats.Count; index++) _game.Cats[index].SetAvailable(false);
            Vector3 playerStart = _game.Player.transform.position;
            Vector3 vacuumStart = vacuum.EffectPosition;
            int startingHealth = cat.Health;
            _game.enabled = false;

            // 고양이 위치만 시험용으로 배치하고 타격은 실제 청소기 Tick에서 발생시킨다.
            MovePlayerFixture(vacuum.transform.position);
            MoveCatToVacuumFixture(cat, vacuum);
            Assert.That(_game.TryUseNearestTrap(), Is.True);
            Assert.That(_game.TryUseNearestTrap(), Is.False);
            Assert.That(vacuum.IsPressing, Is.True);
            vacuum.Tick(0.5f);
            Assert.That(vacuum.IsActive, Is.True);
            MovePlayerFixture(playerStart);
            vacuum.Tick(1.2f);
            MoveCatToVacuumFixture(cat, vacuum);
            vacuum.Tick(0.02f);
            Assert.That(cat.Health, Is.EqualTo(startingHealth - 2), "청소기의 실제 접촉 피해");
            Assert.That(cat.CanReceiveHit, Is.True, "퇴장 무적이 중복 타격 실패를 가리지 않음");

            for (int repeat = 0; repeat < 3; repeat++)
            {
                // 밀려난 고양이를 다시 닿게 해 같은 작동의 중복 타격 방지를 검증한다.
                MoveCatToVacuumFixture(cat, vacuum);
                vacuum.Tick(0.02f);
                Assert.That(cat.Health, Is.EqualTo(startingHealth - 2), "같은 고양이는 한 작동에 한 번만 타격");
            }
            vacuum.Tick(0.3f);
            Assert.That(Vector3.Distance(vacuumStart, vacuum.EffectPosition), Is.GreaterThan(0.3f), "충전독에서 실제 경로 이동");
            Assert.That(vacuum.CooldownRemaining, Is.GreaterThan(0f).And.LessThan(25f));
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Playing));

            _game.TogglePause();
            Vector3 pausedVacuum = vacuum.EffectPosition;
            float pausedCooldown = vacuum.CooldownRemaining;
            float pausedElapsed = _game.Elapsed;
            vacuum.Tick(10f);
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.That(Vector3.Distance(pausedVacuum, vacuum.EffectPosition), Is.LessThan(0.001f));
            Assert.That(vacuum.CooldownRemaining, Is.EqualTo(pausedCooldown));
            Assert.That(_game.Elapsed, Is.EqualTo(pausedElapsed));

            _game.RestartRun(); _game.ConfirmStart();
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Playing));
            Assert.That(vacuum.IsActive, Is.False);
            Assert.That(vacuum.IsReady, Is.True);
            Assert.That(vacuum.CooldownRemaining, Is.Zero);
            Assert.That(Vector3.Distance(vacuumStart, vacuum.EffectPosition), Is.LessThan(0.001f));
            Assert.That(cat.Health, Is.EqualTo(startingHealth));
            Assert.That(_game.Elapsed, Is.Zero);

            MovePlayerFixture(vacuum.transform.position);
            MoveCatToVacuumFixture(cat, vacuum);
            Assert.That(_game.TryUseNearestTrap(), Is.True);
            vacuum.Tick(0.5f);
            MovePlayerFixture(playerStart);
            vacuum.Tick(1.2f);
            MoveCatToVacuumFixture(cat, vacuum);
            vacuum.Tick(0.02f);
            Assert.That(cat.Health, Is.EqualTo(startingHealth - 2), "재시작하면 이전 작동의 타격 기록도 초기화");
        }

        /// <summary>시간·피해를 명시적으로 설정한 시험용 세션에서 승패 결과 화면과 재시작 버튼을 검증한다.</summary>
        [UnityTest]
        public IEnumerator TerminalSessionFixturesShowCorrectResultAndRetryWorks()
        {
            _game.StartRun(); _game.ConfirmStart();
            FieldInfo field = typeof(GameController).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "UI 상태를 준비할 시험용 세션 접근");
            var session = (GameSession)field.GetValue(_game);

            // 실제 300초 생존 플레이가 아닌 결과 UI 연결 시험용 상태다.
            session.Tick(300f);
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Won));
            yield return new WaitForSecondsRealtime(0.2f);
            GameObject result = GameObject.Find("ResultScreen");
            Assert.That(result, Is.Not.Null);
            Assert.That(result.activeInHierarchy, Is.True);
            Assert.That(GameObject.Find("ResultTitle").GetComponent<TMPro.TMP_Text>().text, Does.Contain("아침이 왔어요"));
            Assert.That(GameObject.Find("ResultBody").GetComponent<TMPro.TMP_Text>().text, Does.Contain("05:00"));
            var retry = GameObject.Find("RetryButton").GetComponent<UnityEngine.UI.Button>();
            Assert.That(retry.interactable, Is.True);
            retry.onClick.Invoke();
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Ready));
            Assert.That(_game.Elapsed, Is.Zero);
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.That(result.activeInHierarchy, Is.False);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Enter));
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Playing));

            // 무적 종료만 순수 세션에서 진행하고 세 번의 실제 피해 규칙으로 패배를 만든다.
            Assert.That(session.TryDamage(), Is.True);
            session.Tick(1.5f);
            Assert.That(session.TryDamage(), Is.True);
            session.Tick(1.5f);
            Assert.That(session.TryDamage(), Is.True);
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Lost));
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.That(result.activeInHierarchy, Is.True);
            Assert.That(_game.Hearts, Is.Zero);
            Assert.That(GameObject.Find("ResultTitle").GetComponent<TMPro.TMP_Text>().text, Does.Contain("오늘 밤은 여기까지"));
            Assert.That(GameObject.Find("ResultBody").GetComponent<TMPro.TMP_Text>().text, Does.Contain("쥐구멍으로 몸을 숨겼습니다"));
            Assert.That(GameObject.Find("UpgradeScreen"), Is.Null);
            retry.onClick.Invoke();
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Ready));
            Assert.That(_game.Hearts, Is.EqualTo(3));
            Assert.That(_game.Elapsed, Is.Zero);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Enter));
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Playing));
        }

        /// <summary>제목의 설정·종료 창이 Enter와 Esc를 먼저 받아 뒤쪽 게임을 시작하지 않는지 확인한다.</summary>
        [UnityTest]
        public IEnumerator MenuOverlaysConsumeEnterAndEscape()
        {
            MenuButton("TitleSettingsButton").onClick.Invoke();
            Assert.That(GameObject.Find("SettingsScreen"), Is.Not.Null);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Enter));
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Title));
            Assert.That(GameObject.Find("SettingsScreen"), Is.Not.Null, "음량 선택 중 Enter로 게임을 시작하지 않음");
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Escape));
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
            Assert.That(GameObject.Find("SettingsScreen"), Is.Null);
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Title));
            Assert.That(EventSystem.current.currentSelectedGameObject.name, Is.EqualTo("TitleSettingsButton"));

            MenuButton("TitleQuitButton").onClick.Invoke();
            Assert.That(GameObject.Find("QuitScreen"), Is.Not.Null);
            Assert.That(EventSystem.current.currentSelectedGameObject.name, Is.EqualTo("CancelQuitButton"));
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Enter));
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
            Assert.That(GameObject.Find("QuitScreen"), Is.Null, "종료 확인의 기본 선택은 취소");
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Title));
            MenuButton("TitleQuitButton").onClick.Invoke();
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Escape));
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
            Assert.That(GameObject.Find("QuitScreen"), Is.Null);
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Title));
        }

        /// <summary>일시정지의 설정·종료 취소가 시간을 재개하지 않으며 설정이 새 씬에서도 복원되는지 확인한다.</summary>
        [UnityTest]
        public IEnumerator SettingsPersistAndModalEscapeKeepsGamePaused()
        {
            const string volumeKey = "MidnightMouse.Settings.Volume.v1";
            const string screenKey = "MidnightMouse.Settings.FullScreen.v1";
            bool hadVolume = PlayerPrefs.HasKey(volumeKey), hadScreen = PlayerPrefs.HasKey(screenKey);
            float savedVolume = PlayerPrefs.GetFloat(volumeKey), originalVolume = AudioListener.volume;
            int savedScreen = PlayerPrefs.GetInt(screenKey);
            FullScreenMode originalMode = Screen.fullScreenMode;
            try
            {
                _game.StartRun(); _game.ConfirmStart();
                _game.TogglePause();
                yield return new WaitForSecondsRealtime(0.2f);
                MenuButton("PauseSettingsButton").onClick.Invoke();
                Assert.That(GameObject.Find("PauseButton").GetComponent<UnityEngine.UI.Button>().IsInteractable(), Is.False,
                    "설정 뒤 HUD의 일시정지 버튼도 키보드 탐색 대상에서 제외");
                var volume = GameObject.Find("VolumeSlider").GetComponent<UnityEngine.UI.Slider>();
                volume.value = 23f;
                MenuButton("WindowedButton").onClick.Invoke();
                Assert.That(AudioListener.volume, Is.EqualTo(0.23f).Within(0.001f));
                float pausedTime = _game.Elapsed;
                Vector3 pausedPosition = _game.Player.transform.position;
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.W, Key.Escape));
                yield return null;
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                yield return new WaitForSecondsRealtime(0.2f);
                Assert.That(GameObject.Find("SettingsScreen"), Is.Null);
                Assert.That(_game.Phase, Is.EqualTo(GamePhase.Paused));
                Assert.That(_game.Elapsed, Is.EqualTo(pausedTime));
                Assert.That(Vector3.Distance(pausedPosition, _game.Player.transform.position), Is.LessThan(0.001f));
                Assert.That(PlayerPrefs.GetFloat(volumeKey), Is.EqualTo(0.23f).Within(0.001f));
                Assert.That(PlayerPrefs.GetInt(screenKey), Is.Zero);

                MenuButton("PauseQuitButton").onClick.Invoke();
                MenuButton("CancelQuitButton").onClick.Invoke();
                Assert.That(_game.Phase, Is.EqualTo(GamePhase.Paused));
                Assert.That(_game.Elapsed, Is.EqualTo(pausedTime));
                MenuButton("ContinueButton").onClick.Invoke();
                yield return null;
                Assert.That(_game.Phase, Is.EqualTo(GamePhase.Playing));

                AudioListener.volume = 1f;
                yield return SceneManager.LoadSceneAsync(ScenePath);
                yield return null;
                _game = Object.FindFirstObjectByType<GameController>();
                Assert.That(AudioListener.volume, Is.EqualTo(0.23f).Within(0.001f));
                MenuButton("TitleSettingsButton").onClick.Invoke();
                Assert.That(GameObject.Find("VolumeSlider").GetComponent<UnityEngine.UI.Slider>().value, Is.EqualTo(23f));
                Assert.That(GameObject.Find("DisplayModeValue").GetComponent<TMPro.TMP_Text>().text, Does.Contain("창 모드"));
                MenuButton("CloseSettingsButton").onClick.Invoke();
                PlayerPrefs.SetFloat(volumeKey, float.NaN);
                yield return SceneManager.LoadSceneAsync(ScenePath);
                yield return null;
                _game = Object.FindFirstObjectByType<GameController>();
                Assert.That(AudioListener.volume, Is.EqualTo(1f), "손상된 저장 음량에는 기본값 적용");
            }
            finally
            {
                // 게임 설정 두 키만 되돌려 다른 사용자 저장 데이터를 건드리지 않는다.
                if (hadVolume) PlayerPrefs.SetFloat(volumeKey, savedVolume); else PlayerPrefs.DeleteKey(volumeKey);
                if (hadScreen) PlayerPrefs.SetInt(screenKey, savedScreen); else PlayerPrefs.DeleteKey(screenKey);
                PlayerPrefs.Save();
                AudioListener.volume = originalVolume;
                Screen.fullScreenMode = originalMode;
            }
        }

        /// <summary>화면 갱신 전에 도착한 이전 메뉴 클릭이 진행 중 게임 위에 창을 열지 않는지 확인한다.</summary>
        [UnityTest]
        public IEnumerator StaleTitleCallbacksCannotOpenMenusDuringPlay()
        {
            var settings = MenuButton("TitleSettingsButton");
            var quit = MenuButton("TitleQuitButton");
            var help = MenuButton("HelpButton");
            _game.StartRun(); _game.ConfirmStart();
            settings.onClick.Invoke();
            quit.onClick.Invoke();
            help.onClick.Invoke();
            Assert.That(GameObject.Find("SettingsScreen"), Is.Null);
            Assert.That(GameObject.Find("QuitScreen"), Is.Null);
            Assert.That(GameObject.Find("HelpScreen"), Is.Null);
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Playing));
            yield return null;
        }

        /// <summary>현재 표시된 실제 메뉴 버튼을 통해 연결된 UI 동작을 검사한다.</summary>
        private static UnityEngine.UI.Button MenuButton(string name)
        {
            GameObject target = GameObject.Find(name);
            Assert.That(target, Is.Not.Null, "표시된 메뉴 버튼: " + name);
            var button = target.GetComponent<UnityEngine.UI.Button>();
            Assert.That(button, Is.Not.Null);
            Assert.That(button.IsInteractable(), Is.True);
            return button;
        }

        /// <summary>충돌 이동을 시험하는 대신 상호작용 시작 위치를 준비한다.</summary>
        private void MovePlayerFixture(Vector3 position)
        {
            var controller = _game.Player.GetComponent<CharacterController>();
            controller.enabled = false;
            _game.Player.transform.position = position;
            controller.enabled = true;
        }

        /// <summary>실제 NavMesh 위의 청소기 접촉 위치를 준비한다.</summary>
        private static void MoveCatToVacuumFixture(CatController cat, TrapController vacuum)
        {
            Assert.That(NavMesh.SamplePosition(vacuum.EffectPosition, out var hit, 0.5f, NavMesh.AllAreas), Is.True);
            Assert.That(cat.GetComponent<NavMeshAgent>().Warp(hit.position), Is.True);
            Vector3 difference = hit.position - vacuum.EffectPosition;
            difference.y = 0f;
            Assert.That(difference.magnitude, Is.LessThan(0.6f), "실제 청소기 타격 범위 안의 fixture");
        }
    }
}
