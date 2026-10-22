#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace MidnightMouse
{
    /// <summary>명시적인 명령줄 옵션으로만 실행하는 제출 영상용 실제 Player 자동 입력 기록기.</summary>
    public sealed class AssessmentCapture : MonoBehaviour
    {
        private GameController _game;
        private TrapController[] _traps;
        private CheesePickup[] _pickups;
        private Camera _camera;
        private NavMeshPath _path;
        private string _output;
        private int _frame;
        private const int CaptureRate = 24;
        private const int CaptureSeconds = 108;
        private int _nextDecision;
        private int _trapIndex;
        private float _resultHold;
        private Vector3 _target;
        private Vector3[] _corners = new Vector3[32];
        private int _cornerCount, _cornerIndex;
        private bool _pausedForDemo;
        private Vector3 _lastPosition;
        private float _stuckTime;
        private RenderTexture _recordTarget;
        private Texture2D _recordPixels;
        private RenderPipeline.StandardRequest _renderRequest;
        private bool _menuSmoke;
        private bool _reloadCheck;
        private const string VolumeKey = "MidnightMouse.Settings.Volume.v1";
        private const string ScreenKey = "MidnightMouse.Settings.FullScreen.v1";

        /// <summary>사용자가 일반 실행했을 때는 기록기가 생성되지 않는다.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--assessment-capture");
            bool menuSmoke = false;
            if (index < 0)
            {
                index = Array.IndexOf(args, "--assessment-menu-smoke");
                menuSmoke = index >= 0;
            }
            if (index < 0 || index + 1 >= args.Length) return;
            string output = Path.GetFullPath(args[index + 1]);
            bool reloadCheck = menuSmoke && Array.IndexOf(args, "--verify-settings-reload") >= 0;
            // 1) 기존 파일이 있는 위치를 덮어쓰지 않아 과거 시연 증거를 보존한다.
            if (!reloadCheck && Directory.Exists(output) && Directory.GetFiles(output).Length > 0)
            {
                Debug.LogError("AssessmentCapture: 비어 있는 출력 폴더가 필요합니다.");
                return;
            }
            Directory.CreateDirectory(output);
            Application.runInBackground = true;
            var host = new GameObject("AssessmentCapture");
            var capture = host.AddComponent<AssessmentCapture>();
            capture._output = output;
            capture._menuSmoke = menuSmoke;
            capture._reloadCheck = reloadCheck;
        }

        /// <summary>실제 게임과 동일한 프레임 진행을 일정 간격의 영상 프레임으로 남긴다.</summary>
        private IEnumerator Start()
        {
            yield return null;
            _game = FindFirstObjectByType<GameController>();
            _camera = Camera.main;
            _traps = FindObjectsByType<TrapController>(FindObjectsSortMode.None);
            _pickups = FindObjectsByType<CheesePickup>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            _path = new NavMeshPath();
            if (_game == null || _camera == null)
            {
                Debug.LogError("AssessmentCapture: 게임과 카메라를 찾지 못했습니다.");
                yield break;
            }
            // 숨겨진 창의 검은 back buffer 대신 동일 카메라와 UI를 명시적으로 렌더링한다.
            _recordTarget = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32);
            _recordTarget.Create();
            _recordPixels = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            _renderRequest = new RenderPipeline.StandardRequest { destination = _recordTarget };
            var canvas = FindFirstObjectByType<GameHud>().GetComponentInChildren<Canvas>(true);
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _camera;
            canvas.planeDistance = 1f;
            if (_menuSmoke)
            {
                yield return CheckMenusInPlayer();
                yield break;
            }
            Time.captureFramerate = CaptureRate;
            var endOfFrame = new WaitForEndOfFrame();
            int total = CaptureRate * CaptureSeconds;
            for (_frame = 0; _frame < total; _frame++)
            {
                Drive();
                yield return endOfFrame;
                ReadRenderedFrame();
                File.WriteAllBytes(Path.Combine(_output, $"frame_{_frame:00000}.jpg"), _recordPixels.EncodeToJPG(91));
            }
            Time.captureFramerate = 0;
            _game.Player.ClearDemoInput();
            File.WriteAllText(Path.Combine(_output, "capture.json"),
                $"{{\"frames\":{total},\"fps\":{CaptureRate},\"duration\":{CaptureSeconds},\"automatic_input\":true,\"phase\":\"{_game.Phase}\",\"elapsed\":{_game.Elapsed.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"cheese\":{_game.CheeseCollected},\"banished\":{_game.CatsBanished}}}");
            yield return RecordResultFixtures();
            Application.Quit();
        }

        /// <summary>영상과 별개로 규칙 입력에 따른 두 결과 UI를 기록한다. 실제 300초 생존 증거는 아니다.</summary>
        private IEnumerator RecordResultFixtures()
        {
            // 개발 빌드의 화면 검사에만 사용하며 게임 수치와 제출 영상 프레임은 바꾸지 않는다.
            var field = typeof(GameController).GetField("_session", BindingFlags.NonPublic | BindingFlags.Instance);
            _game.StartRun(); _game.ConfirmStart();
            var session = (GameSession)field.GetValue(_game);
            session.Tick(_game.Duration);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return new WaitForEndOfFrame();
            SaveResult("result_won.png", GamePhase.Won);
            _game.RestartRun(); _game.ConfirmStart();
            session = (GameSession)field.GetValue(_game);
            while (session.Hearts > 0)
            {
                session.TryDamage();
                session.Tick(_game.Rules.Invulnerability + 0.01f);
            }
            yield return new WaitForSecondsRealtime(0.3f);
            yield return new WaitForEndOfFrame();
            SaveResult("result_lost.png", GamePhase.Lost);
            File.WriteAllText(Path.Combine(_output, "result-fixtures.json"),
                "{\"source\":\"same Windows Player\",\"method\":\"GameSession rule fixture via reflection\",\"natural_survival\":false,\"included_in_video\":false,\"won\":\"Tick(Duration)\",\"lost\":\"TryDamage with invulnerability expiration\"}");
        }

        /// <summary>기대한 결과 상태를 먼저 확인하고 실제 Player 화면을 저장한다.</summary>
        private void SaveResult(string name, GamePhase expected)
        {
            if (_game.Phase != expected) throw new InvalidOperationException("결과 화면 상태가 일치하지 않습니다: " + expected);
            ReadRenderedFrame();
            File.WriteAllBytes(Path.Combine(_output, name), _recordPixels.EncodeToPNG());
        }

        /// <summary>게임 카메라와 연결된 Canvas를 같은 해상도로 기록한다.</summary>
        private void ReadRenderedFrame()
        {
            RenderPipeline.SubmitRenderRequest(_camera, _renderRequest);
            var previous = RenderTexture.active;
            RenderTexture.active = _recordTarget;
            _recordPixels.ReadPixels(new Rect(0, 0, _recordTarget.width, _recordTarget.height), 0, 0, false);
            _recordPixels.Apply(false);
            RenderTexture.active = previous;
        }

        /// <summary>기록기 전용 GPU·CPU 이미지 버퍼를 해제한다.</summary>
        private void OnDestroy()
        {
            if (_recordTarget != null) { _recordTarget.Release(); Destroy(_recordTarget); }
            if (_recordPixels != null) Destroy(_recordPixels);
        }

        /// <summary>이동 입력과 실제 상호작용 API만 사용하며 체력·시간·위치를 강제로 바꾸지 않는다.</summary>
        private void Drive()
        {
            if (_frame == CaptureRate * 2) ClickMenuButton("TitleSettingsButton");
            if (_frame == CaptureRate * 5) ClickMenuButton("CloseSettingsButton");
            if (_frame == CaptureRate * 6) ClickMenuButton("TitleQuitButton");
            if (_frame == CaptureRate * 8) ClickMenuButton("CancelQuitButton");
            if (_frame < CaptureRate * 9) return;
            if (_game.Phase == GamePhase.Title)
            {
                _game.StartRun(); _game.ConfirmStart();
                _lastPosition = _game.Player.transform.position;
                _nextDecision = 0;
            }
            if (_game.Phase == GamePhase.Upgrade)
            {
                _resultHold += 1f / CaptureRate;
                if (_resultHold > 2.2f)
                {
                    _game.SelectUpgrade(_game.Hearts < _game.MaxHearts ? 2 : 1);
                    _resultHold = 0f;
                }
                return;
            }
            if (_game.Phase == GamePhase.Lost || _game.Phase == GamePhase.Won)
            {
                _resultHold += 1f / CaptureRate;
                if (_resultHold > 4.5f)
                {
                    _game.RestartRun(); _game.ConfirmStart();
                    _resultHold = 0f;
                    _nextDecision = 0;
                }
                return;
            }
            if (_frame == CaptureRate * 52)
            {
                _game.TogglePause();
                _pausedForDemo = true;
            }
            if (_pausedForDemo && _frame == CaptureRate * 55)
            {
                _game.TogglePause();
                _pausedForDemo = false;
            }
            if (_game.Phase != GamePhase.Playing) return;
            foreach (var trap in _traps)
                if (trap.IsPressing) { _game.Player.SetDemoInput(Vector2.zero, false, false, false); return; }
            Vector3 position = _game.Player.transform.position;
            if ((position - _lastPosition).sqrMagnitude < 0.001f) _stuckTime += 1f / CaptureRate;
            else _stuckTime = 0f;
            _lastPosition = position;
            if (_frame >= _nextDecision || _stuckTime > 1.5f)
            {
                ChooseTarget(position);
                _nextDecision = _frame + CaptureRate * 4;
                _stuckTime = 0f;
            }
            if (_cornerIndex < _cornerCount)
            {
                Vector3 corner = _corners[_cornerIndex]; corner.y = position.y;
                if ((corner - position).sqrMagnitude < 0.5f) _cornerIndex++;
            }
            Vector3 destination = _cornerIndex < _cornerCount ? _corners[_cornerIndex] : _target;
            Vector3 direction = destination - position; direction.y = 0f;
            if (direction.sqrMagnitude < 0.4f)
            {
                if (_game.TryUseNearestTrap())
                {
                    _game.Player.SetDemoInput(Vector2.zero, false, false, false);
                    _nextDecision = 0;
                    return;
                }
                _nextDecision = 0;
            }
            Vector3 forward = _camera.transform.forward; forward.y = 0; forward.Normalize();
            Vector3 right = _camera.transform.right; right.y = 0; right.Normalize();
            direction.Normalize();
            var move = new Vector2(Vector3.Dot(direction, right), Vector3.Dot(direction, forward));
            bool sneak = _frame < CaptureRate * 11;
            _game.Player.SetDemoInput(move, sneak, !sneak, _frame % (CaptureRate * 7) == 0);
            if (_frame % (CaptureRate * 6) == 0 && _traps.Length > 0)
                _game.TryThrowBait(_traps[_trapIndex % _traps.Length].EffectPosition);
        }

        /// <summary>빌드된 Player에서 설정 적용·재실행 복원·실제 종료 버튼을 검사한다.</summary>
        private IEnumerator CheckMenusInPlayer()
        {
            // 시연의 안내·별 기록은 사용자 진행 파일과 분리한다.
            typeof(GameController).GetField("_campaignStore", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_game, new CampaignStore(Path.Combine(_output, "campaign-fixture.json")));
            typeof(GameController).GetField("<Progress>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_game, new CampaignProgress());
            string backup = Path.Combine(_output, "original-settings.json");
            if (!_reloadCheck)
            {
                File.WriteAllText(backup, JsonUtility.ToJson(new SavedPreferences
                {
                    hadVolume = PlayerPrefs.HasKey(VolumeKey), volume = PlayerPrefs.GetFloat(VolumeKey),
                    hadScreen = PlayerPrefs.HasKey(ScreenKey), screen = PlayerPrefs.GetInt(ScreenKey)
                }, true));
            }
            else
            {
                Require(File.Exists(backup), "설정 복구용 원본 기록");
                Require(Mathf.Abs(GameSettings.Volume - 0.37f) < 0.001f, "새 Player의 음량 복원");
                Require(!GameSettings.FullScreen && Screen.fullScreenMode == FullScreenMode.Windowed, "새 Player의 창 모드 복원");
            }
            yield return new WaitForSecondsRealtime(0.3f);
            SaveMenuFrame(_reloadCheck ? "title-reloaded.png" : "title.png");
            ClickMenuButton("TitleSettingsButton");
            var slider = GameObject.Find("VolumeSlider").GetComponent<UnityEngine.UI.Slider>();
            if (_reloadCheck) Require(Mathf.Abs(slider.value - 37f) < 0.01f, "복원된 음량 UI");
            slider.value = 37f;
            Require(Mathf.Abs(AudioListener.volume - 0.37f) < 0.001f, "실제 전체 음량 적용");
            ClickMenuButton("FullScreenButton");
            yield return new WaitForSecondsRealtime(0.7f);
            Require(Screen.fullScreenMode == FullScreenMode.FullScreenWindow, "실제 전체 화면 전환");
            SaveMenuFrame(_reloadCheck ? "settings-fullscreen-reloaded.png" : "settings-fullscreen.png");
            ClickMenuButton("WindowedButton");
            yield return new WaitForSecondsRealtime(0.7f);
            Require(Screen.fullScreenMode == FullScreenMode.Windowed, "실제 창 모드 전환");
            SaveMenuFrame(_reloadCheck ? "settings-windowed-reloaded.png" : "settings-windowed.png");
            ClickMenuButton("CloseSettingsButton");
            Require(Mathf.Abs(PlayerPrefs.GetFloat(VolumeKey) - 0.37f) < 0.001f && PlayerPrefs.GetInt(ScreenKey) == 0, "닫을 때 설정 저장");
            ClickMenuButton("StartButton");
            yield return new WaitForSecondsRealtime(0.2f);
            SaveMenuFrame("night-selection.png");
            ClickMenuButton("NightButton1");
            yield return new WaitForSecondsRealtime(0.2f);
            SaveMenuFrame("intro.png");
            var keyboard = Keyboard.current;
            bool addedKeyboard = keyboard == null;
            if (addedKeyboard) keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return new WaitForSecondsRealtime(0.2f);
            if (addedKeyboard) InputSystem.RemoveDevice(keyboard);
            Require(_game.Phase == GamePhase.Playing, "밤 선택과 인트로 뒤 실제 시작");
            SaveMenuFrame("night-one-hud.png");
            ClickMenuButton("PauseButton");
            yield return new WaitForSecondsRealtime(0.2f);
            Require(_game.Phase == GamePhase.Paused, "실제 Player 일시정지");
            SaveMenuFrame(_reloadCheck ? "pause-reloaded.png" : "pause.png");
            float pausedTime = _game.Elapsed;
            ClickMenuButton("PauseSettingsButton");
            yield return new WaitForSecondsRealtime(0.2f);
            ClickMenuButton("CloseSettingsButton");
            Require(_game.Phase == GamePhase.Paused && _game.Elapsed == pausedTime, "설정 중 시간 정지");
            ClickMenuButton("PauseQuitButton");
            ClickMenuButton("CancelQuitButton");
            Require(_game.Phase == GamePhase.Paused, "종료 취소 후 일시정지 유지");
            ClickMenuButton("PauseQuitButton");
            SaveMenuFrame(_reloadCheck ? "quit-reloaded.png" : "quit.png");
            string suffix = _reloadCheck ? "reload" : "first";
            File.WriteAllText(Path.Combine(_output, "menu-" + suffix + ".json"),
                "{\"checks_passed\":true,\"volume\":0.37,\"fullscreen_and_windowed\":true,\"pause_preserved\":true,\"quit_button_invoked\":true,\"restart_persistence\":" + (_reloadCheck ? "true" : "false") + "}");
            Application.quitting += () => File.WriteAllText(Path.Combine(_output, "quit-" + suffix + ".json"), "{\"application_quitting_event\":true}");
            ClickMenuButton("ConfirmQuitButton");
            // 두 번째 실행 확인 뒤 원래 두 키를 복구한다. 사용자 설정 전체는 지우지 않는다.
            if (_reloadCheck) RestorePreferences();
        }

        /// <summary>검사 전에 보존한 두 설정 키만 원래 값이나 미설정 상태로 돌린다.</summary>
        private void RestorePreferences()
        {
            string path = Path.Combine(_output, "original-settings.json");
            if (!File.Exists(path)) return;
            var saved = JsonUtility.FromJson<SavedPreferences>(File.ReadAllText(path));
            if (saved.hadVolume) PlayerPrefs.SetFloat(VolumeKey, saved.volume); else PlayerPrefs.DeleteKey(VolumeKey);
            if (saved.hadScreen) PlayerPrefs.SetInt(ScreenKey, saved.screen); else PlayerPrefs.DeleteKey(ScreenKey);
            PlayerPrefs.Save();
        }

        /// <summary>검사 실패를 기록하고 해당 테스트 Player만 실패 코드로 종료한다.</summary>
        private void Require(bool condition, string description)
        {
            if (condition) return;
            var close = GameObject.Find("CloseSettingsButton");
            if (close != null) close.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            RestorePreferences();
            Debug.LogError("메뉴 Player 검사 실패: " + description);
            Application.Quit(1);
            throw new InvalidOperationException(description);
        }

        /// <summary>화면에서 활성화된 실제 버튼의 연결을 통해 시연 입력을 보낸다.</summary>
        private void ClickMenuButton(string name)
        {
            var target = GameObject.Find(name);
            Require(target != null, "표시된 버튼 " + name);
            var button = target.GetComponent<UnityEngine.UI.Button>();
            Require(button != null && button.IsInteractable(), "조작 가능한 버튼 " + name);
            button.onClick.Invoke();
        }

        /// <summary>화면 비율을 유지한 실제 메뉴 렌더를 기록한다.</summary>
        private void SaveMenuFrame(string name)
        {
            Canvas.ForceUpdateCanvases();
            ReadRenderedFrame();
            File.WriteAllBytes(Path.Combine(_output, name), _recordPixels.EncodeToPNG());
        }

        [Serializable]
        private struct SavedPreferences
        {
            public bool hadVolume, hadScreen;
            public float volume;
            public int screen;
        }

        /// <summary>함정과 치즈를 번갈아 방문하고 NavMesh 경로를 이동 입력으로 따른다.</summary>
        private void ChooseTarget(Vector3 position)
        {
            bool seekCheese = (_frame / (CaptureRate * 4)) % 3 != 0;
            float nearest = float.MaxValue;
            bool found = false;
            if (seekCheese)
            {
                foreach (var pickup in _pickups)
                {
                    if (!pickup.gameObject.activeInHierarchy) continue;
                    float distance = (pickup.transform.position - position).sqrMagnitude;
                    if (distance >= nearest || distance < 0.35f) continue;
                    nearest = distance; _target = pickup.transform.position; found = true;
                }
            }
            if (!found && _traps.Length > 0)
            {
                _trapIndex = (_trapIndex + 1) % _traps.Length;
                _target = _traps[_trapIndex].transform.position;
            }
            _cornerCount = _cornerIndex = 0;
            if (NavMesh.SamplePosition(position, out var start, 3f, NavMesh.AllAreas)
                && NavMesh.SamplePosition(_target, out var end, 2f, NavMesh.AllAreas)
                && NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, _path))
            {
                _cornerCount = _path.GetCornersNonAlloc(_corners);
                _cornerIndex = _cornerCount > 1 ? 1 : 0;
            }
        }
    }
}
#endif
