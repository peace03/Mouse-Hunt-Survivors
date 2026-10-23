using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace MidnightMouse
{
    /// <summary>게임 루트에 붙여 세션 상태를 한글 Canvas 화면과 버튼으로 보여준다.</summary>
    public sealed partial class GameHud : MonoBehaviour
    {
        [SerializeField, Tooltip("화면에서 관찰하고 버튼으로 제어할 게임 세션.")]
        private GameController _game;
        [SerializeField, Tooltip("배포 가능한 한글 원본 글꼴. Noto Sans KR을 사용한다.")]
        private Font _font;

        private readonly Color _ink = new Color(0.045f, 0.085f, 0.13f, 0.96f);
        private readonly Color _cream = new Color(0.97f, 0.94f, 0.85f);
        private readonly Color _muted = new Color(0.66f, 0.76f, 0.79f);
        private readonly Color _gold = new Color(1f, 0.79f, 0.28f);
        private TMP_FontAsset _fontAsset;
        private Sprite _flatSprite;
        private RectTransform _canvas;
        private GameObject _title, _hud, _pause, _upgrade, _result, _help, _settings, _quit;
        private TMP_Text _hearts, _clock, _phaseLabel, _score, _level, _hint, _cooldowns, _dawnBonus, _debugNotice;
        private TMP_Text _resultTitle, _resultBody, _defeatAdvice, _catHelp;
        private UnityEngine.UI.Image _experience, _dawn;
        private readonly TMP_Text[] _upgradeTitles = new TMP_Text[3];
        private readonly TMP_Text[] _upgradeBodies = new TMP_Text[3];
        private readonly UnityEngine.UI.Button[] _upgradeButtons = new UnityEngine.UI.Button[3];
        private UnityEngine.UI.Button _startButton, _continueButton, _retryButton;
        private GamePhase _shownPhase = (GamePhase)(-1);
        private int _shownLevel = -1;
        private float _upgradeUnlockTime;
        private UnityEngine.UI.Slider _volumeSlider;
        private TMP_Text _volumeValue, _displayModeValue;
        private UnityEngine.UI.Button _cancelQuitButton;
        private GameObject _overlayReturnFocus;

        private bool HasOverlay => (_help != null && _help.activeSelf)
            || (_settings != null && _settings.activeSelf) || (_quit != null && _quit.activeSelf)
            || HasCampaignOverlay;

        /// <summary>필수 참조를 확인하고 게임 상태와 독립적인 표시물을 한 번만 만든다.</summary>
        private void Start()
        {
            if (_game == null || _font == null)
            {
                Debug.LogError("GameHud: GameController 또는 한글 Font 연결이 없습니다.", this);
                enabled = false;
                return;
            }
            // 1) 동적 글꼴은 실행 중에만 만들므로 원본 폰트 에셋은 바뀌지 않는다.
            _fontAsset = TMP_FontAsset.CreateFontAsset(_font, 64, 6,
                UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 2048, 2048);
            _fontAsset.name = "MidnightMouse Korean Runtime Font";
            _flatSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
            BuildCanvas();
            BuildHud();
            BuildTitle();
            BuildPause();
            BuildUpgrades();
            BuildResult();
            BuildHelp();
            GameSettings.Load();
            BuildSettings();
            BuildQuit();
            BuildCampaign();
            Refresh();
            StartCoroutine(RefreshLoop());
        }

        /// <summary>메뉴와 성장 화면의 키보드 입력만 처리한다. 게임 키는 플레이어가 담당한다.</summary>
        private void Update()
        {
            var keyboard = Keyboard.current;
            if (_game != null && HandleCampaignInput(keyboard)) return;
            if (keyboard == null || _game == null) return;
            if (_game.Phase == GamePhase.Ready || _game.StartedThisFrame) return;
            // Esc는 가장 위의 창 하나만 닫아 설정 취소와 게임 재개가 겹치지 않게 한다.
            if (keyboard[_game.PauseKey].wasPressedThisFrame)
            {
                if (HasOverlay) CloseOverlay();
                else _game.TogglePause();
                return;
            }
            if (HasOverlay) return;
            if (_game.Phase == GamePhase.Title && keyboard.enterKey.wasPressedThisFrame &&
                (EventSystem.current == null || EventSystem.current.currentSelectedGameObject == _startButton.gameObject))
                StartFromTitle();
            if (_game.Phase != GamePhase.Upgrade || Time.unscaledTime < _upgradeUnlockTime) return;
            if (keyboard.digit1Key.wasPressedThisFrame) ChooseUpgrade(0);
            else if (keyboard.digit2Key.wasPressedThisFrame) ChooseUpgrade(1);
            else if (keyboard.digit3Key.wasPressedThisFrame) ChooseUpgrade(2);
        }

        /// <summary>표시는 게임 정지 중에도 갱신하고, 문자열 갱신을 초당 10회로 제한한다.</summary>
        private IEnumerator RefreshLoop()
        {
            var wait = new WaitForSecondsRealtime(0.1f);
            while (true)
            {
                yield return wait;
                Refresh();
            }
        }

        /// <summary>현재 세션의 값과 화면 전환을 한 곳에서 반영한다.</summary>
        private void Refresh()
        {
            if (_game == null) return;
            var phase = _game.Phase;
            if (_shownPhase != phase || (phase == GamePhase.Upgrade && _shownLevel != _game.Level))
            {
                if (_shownPhase == GamePhase.Ready && phase == GamePhase.Playing) ShowNightBanner();
                _shownPhase = phase;
                _shownLevel = _game.Level;
                CloseCampaignOverlay();
                _title.SetActive(phase == GamePhase.Title);
                _hud.SetActive(phase != GamePhase.Title);
                _pause.SetActive(phase == GamePhase.Paused);
                _upgrade.SetActive(phase == GamePhase.Upgrade);
                _result.SetActive(phase == GamePhase.Won || phase == GamePhase.Lost);
                CloseOverlay();
                if (phase == GamePhase.Title) Focus(_startButton);
                if (phase == GamePhase.Paused) Focus(_continueButton);
                if (phase == GamePhase.Won || phase == GamePhase.Lost) Focus(_retryButton);
                if (phase == GamePhase.Upgrade)
                {
                    _upgradeUnlockTime = Time.unscaledTime + 0.5f;
                    for (int i = 0; i < 3; i++)
                    {
                        _upgradeTitles[i].text = _game.GetUpgradeTitle(i);
                        _upgradeBodies[i].text = _game.GetUpgradeDescription(i);
                    }
                    Focus(_upgradeButtons[0]);
                }
            }
            // 2) 상태가 멈춰 있을 때도 UI 입력은 유지하고, 실제 규칙은 GameController가 판정한다.
            _hearts.text = new string('♥', Mathf.Max(0, _game.Hearts))
                + "<color=#647781>" + new string('♡', Mathf.Max(0, _game.MaxHearts - _game.Hearts)) + "</color>";
            float progress = Mathf.Clamp01(_game.Elapsed / Mathf.Max(1f, _game.Duration));
            int minutes = Mathf.FloorToInt(progress * 360f);
            _clock.SetText("{0:00}:{1:00}", (float)(minutes / 60), (float)(minutes % 60));
            _dawnBonus.text = Time.unscaledTime < _game.DawnNoticeUntil ? $"아침 +{_game.LastDawnAdvance:0.#}초" : "";
            _debugNotice.text = Time.unscaledTime < _game.DebugNoticeUntil ? _game.DebugNotice : "";
            int stage = _game.Stage;
            _phaseLabel.text = _game.CurrentNight != null ? _game.CurrentNight.StageLine(_game.Elapsed) : stage switch
            {
                1 => "고요한 밤 · 작은 발소리부터",
                2 => "깨어나는 집 · 귀를 기울여요",
                3 => "깊은 밤 · 소리로 길을 만들어요",
                4 => "새벽 문턱 · 조금만 더 버텨요",
                _ => "다가오는 아침 · 끝까지 조심해요"
            };
            _score.SetText("치즈  {0}   /   쫓아낸 고양이  {1}", (float)_game.CheeseCollected, (float)_game.CatsBanished);
            _level.SetText("Lv. {0}    {1} / {2}", _game.Level, _game.Experience, _game.ExperienceToNext);
            _experience.fillAmount = Mathf.Clamp01((float)_game.Experience / Mathf.Max(1, _game.ExperienceToNext));
            _dawn.fillAmount = progress;
            _hint.text = _game.CurrentHint;
            _cooldowns.text = "SPACE  대시 " + (_game.DashCooldown01 <= 0f ? "준비" : "충전 중")
                + "     /     클릭  미끼 " + (_game.BaitCooldown01 <= 0f ? "준비" : "충전 중");
            for (int i = 0; i < 3; i++) _upgradeButtons[i].interactable = Time.unscaledTime >= _upgradeUnlockTime;
            RefreshCampaign();
            if (phase != GamePhase.Won && phase != GamePhase.Lost) return;
            if (!_game.IsCampaignResultReady || _renderedResultPhase == phase) return;
            _renderedResultPhase = phase;
            bool won = phase == GamePhase.Won;
            _resultTitle.text = won ? "아침이 왔어요." : "오늘 밤은 여기까지.";
            _resultTitle.color = won ? _gold : _cream;
            _defeatAdvice.gameObject.SetActive(!won && !string.IsNullOrEmpty(_game.DefeatCause));
            _defeatAdvice.text = won ? "" : $"{_game.DefeatCause}\n{_game.DefeatTip}";
            _resultBody.text = (won ? "모찌가 무사히 밤을 넘겼습니다.\n" : "모찌가 쥐구멍으로 몸을 숨겼습니다.\n")
                + $"\n생존  {Mathf.FloorToInt(_game.RealElapsed / 60f):00}:{Mathf.FloorToInt(_game.RealElapsed % 60f):00}"
                + $"     치즈  {_game.CheeseCollected}\n쫓아낸 고양이  {_game.CatsBanished}     도달 레벨  {_game.Level}";
        }

        /// <summary>Canvas와 Input System UI 입력을 한 번만 만든다.</summary>
        private void BuildCanvas()
        {
            var root = new GameObject("GameCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            root.transform.SetParent(transform, false);
            _canvas = root.GetComponent<RectTransform>();
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            root.GetComponent<Canvas>().sortingOrder = 20;
            var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;
            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
        }

        /// <summary>중앙 플레이 영역을 가리지 않는 HUD를 만든다.</summary>
        private void BuildHud()
        {
            _hud = Layer("Hud", false);
            var left = Panel(_hud.transform, "HealthPanel", 26, 24, 286, 106, _ink);
            Label(left, "PlayerLabel", 20, 10, 250, 25, "모찌의 작은 밤", 16, _muted);
            _hearts = Label(left, "Hearts", 18, 36, 255, 54, "", 36, new Color(1f, 0.57f, 0.45f));
            var clock = CenterPanel(_hud.transform, "ClockPanel", 0, 366, 370, 126, _ink);
            Anchor(clock, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));
            clock.anchoredPosition = new Vector2(0, -87);
            Label(clock, "ClockCaption", 10, 8, 350, 24, "아침 6시까지", 16, _muted, TextAlignmentOptions.Center);
            _clock = Label(clock, "Clock", 10, 29, 350, 51, "00:00", 42, _cream, TextAlignmentOptions.Center);
            _phaseLabel = Label(clock, "Stage", 10, 85, 350, 25, "", 15, _gold, TextAlignmentOptions.Center);
            var dawn = Panel(clock, "DawnTrack", 18, 119, 334, 3, new Color(0.22f, 0.3f, 0.34f));
            _dawn = Fill(dawn, "DawnProgress", _gold);
            _dawnBonus = Label(clock, "DawnBonus", 10, 133, 350, 28, "", 22, _gold, TextAlignmentOptions.Center);
            _debugNotice = Label(clock, "DebugNotice", -100, 165, 570, 28, "", 18, _cream, TextAlignmentOptions.Center);
            var right = Panel(_hud.transform, "ScorePanel", -26, 24, 355, 80, _ink);
            Anchor(right, new Vector2(1, 1), new Vector2(1, 1));
            _score = Label(right, "Score", 18, 15, 325, 30, "", 17, _cream);
            Button(right, "PauseButton", 206, 46, 128, 25, "Esc  잠깐 쉬기", _game.TogglePause, false, 14);
            var bottom = CenterPanel(_hud.transform, "ProgressPanel", 0, -356, 760, 128, _ink);
            Anchor(bottom, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f));
            bottom.anchoredPosition = new Vector2(0, 94);
            _hint = Label(bottom, "InteractionHint", 18, 9, 724, 29, "", 19, _gold, TextAlignmentOptions.Center);
            _cooldowns = Label(bottom, "Cooldowns", 18, 42, 724, 25, "", 16, _cream, TextAlignmentOptions.Center);
            var track = Panel(bottom, "ExperienceTrack", 20, 80, 720, 7, new Color(0.22f, 0.3f, 0.34f));
            _experience = Fill(track, "Experience", new Color(0.34f, 0.81f, 0.72f));
            _level = Label(bottom, "ExperienceLabel", 20, 94, 720, 24, "", 15, _muted, TextAlignmentOptions.Center);
            var controls = CenterPanel(_hud.transform, "ControlStrip", 0, -435, 1160, 24, Color.clear);
            Anchor(controls, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f));
            controls.anchoredPosition = new Vector2(0, 15);
            Label(controls, "Controls", 0, 0, 1160, 24,
                "WASD 이동    ·    Ctrl 살금살금    ·    Shift 달리기    ·    Space 대시    ·    E 함정    ·    클릭 미끼", 16, _cream, TextAlignmentOptions.Center);
        }

        /// <summary>실제 게임 배경을 남긴 시작 화면을 만든다.</summary>
        private void BuildTitle()
        {
            _title = Layer("TitleScreen", false);
            var card = CenterPanel(_title.transform, "TitleCard", -438, 0, 600, 800, _ink);
            Label(card, "Edition", 42, 36, 510, 28, "MIDNIGHT MOUSE  /  ONE NIGHT", 17, _gold);
            Label(card, "Title", 36, 91, 530, 105, "한밤의 쥐", 72, _cream);
            Label(card, "Subtitle", 42, 207, 510, 40, "잠든 집을 지나, 아침까지.", 27, _cream);
            Panel(card, "Rule", 42, 273, 508, 2, new Color(0.35f, 0.45f, 0.47f));
            Label(card, "Story", 42, 302, 510, 106,
                "작은 쥐 모찌의 밤 나들이.\n소리로 고양이를 유인하고, 집 안의 물건으로\n길을 열어 겨울을 위한 치즈를 모으세요.", 23, _muted);
            Label(card, "Goal", 42, 438, 510, 72,
                "목표  ·  아침 6시까지 살아남기\n고양이를 쫓아낼수록 아침이 빨리 와요.", 21, _cream);
            _startButton = Button(card, "StartButton", 42, 544, 508, 67, "밤 고르기    ENTER", StartFromTitle, true, 24);
            Button(card, "HelpButton", 42, 627, 508, 44, "조작과 생존 요령", () => OpenHelp("HelpButton"), false, 20);
            Button(card, "TitleSettingsButton", 42, 685, 246, 44, "설정", () => OpenSettings("TitleSettingsButton"), false, 20);
            Button(card, "TitleQuitButton", 304, 685, 246, 44, "게임 종료", () => OpenQuit("TitleQuitButton"), false, 20);
            Label(card, "Credit", 42, 755, 510, 22, "윤평화  ·  Unity 프로토타입", 14, _muted);
        }

        /// <summary>일시정지 상태에서 계속하기·설정·제목 복귀·종료를 제공한다.</summary>
        private void BuildPause()
        {
            _pause = Layer("PauseScreen", true);
            var card = CenterPanel(_pause.transform, "PauseCard", 0, 0, 610, 640, _ink);
            Label(card, "PauseTitle", 36, 40, 538, 70, "잠깐 숨 고르기", 40, _cream, TextAlignmentOptions.Center);
            Label(card, "PauseBody", 36, 129, 538, 48, "집도, 고양이도 잠시 멈췄어요.", 23, _muted, TextAlignmentOptions.Center);
            _continueButton = Button(card, "ContinueButton", 60, 222, 490, 62, "다시 움직이기    ESC", _game.TogglePause, true, 23);
            Button(card, "PauseHelpButton", 60, 307, 490, 52, "조작과 생존 요령", () => OpenHelp("PauseHelpButton"), false, 21);
            Button(card, "PauseSettingsButton", 60, 377, 490, 52, "설정", () => OpenSettings("PauseSettingsButton"), false, 21);
            Button(card, "TitleButton", 60, 447, 490, 52, "제목으로", _game.ReturnToTitle, false, 21);
            Button(card, "PauseQuitButton", 60, 517, 490, 52, "게임 종료", () => OpenQuit("PauseQuitButton"), false, 21);
        }

        /// <summary>세 개의 성장 카드를 만들고 오입력 방지 시간을 적용한다.</summary>
        private void BuildUpgrades()
        {
            _upgrade = Layer("UpgradeScreen", true);
            var group = CenterPanel(_upgrade.transform, "UpgradeCards", 0, -20, 1170, 465, Color.clear);
            Label(group, "UpgradeCaption", 0, -115, 1170, 36, "작은 치즈가 큰 힘이 됩니다", 21, _gold, TextAlignmentOptions.Center);
            Label(group, "UpgradeTitle", 0, -73, 1170, 58, "이번 밤의 성장", 44, _cream, TextAlignmentOptions.Center);
            for (int i = 0; i < 3; i++)
            {
                int choice = i;
                var card = Panel(group, "UpgradeCard" + (i + 1), i * 398, 35, 374, 377, _ink);
                Label(card, "ChoiceNumber", 28, 25, 318, 49, "0" + (i + 1), 32, _gold);
                _upgradeTitles[i] = Label(card, "UpgradeName", 28, 103, 318, 50, "", 28, _cream);
                _upgradeBodies[i] = Label(card, "UpgradeDescription", 28, 178, 318, 98, "", 21, _muted);
                _upgradeButtons[i] = Button(card, "ChooseButton", 28, 301, 318, 49, "선택하기    " + (i + 1),
                    () => ChooseUpgrade(choice), true, 20);
            }
            Label(group, "UpgradeHint", 0, 435, 1170, 30, "시간이 멈췄습니다. 카드 클릭 또는 1 · 2 · 3으로 선택하세요.", 19, _cream, TextAlignmentOptions.Center);
        }

        /// <summary>승패 후 수치와 다시 하기를 표시한다.</summary>
        private void BuildResult()
        {
            _result = Layer("ResultScreen", true);
            var card = CenterPanel(_result.transform, "ResultCard", 0, 0, 730, 760, _ink);
            Label(card, "ResultCaption", 42, 36, 646, 28, "모찌의 밤 나들이 기록", 20, _muted, TextAlignmentOptions.Center);
            _resultTitle = Label(card, "ResultTitle", 34, 101, 662, 75, "", 44, _gold, TextAlignmentOptions.Center);
            _resultBody = Label(card, "ResultBody", 42, 190, 646, 125, "", 21, _cream, TextAlignmentOptions.Center);
            _campaignResult = Label(card, "CampaignResult", 42, 330, 646, 105, "", 21, _gold, TextAlignmentOptions.Center);
            _defeatAdvice = Label(card, "DefeatAdvice", 42, 442, 646, 95, "", 21, _cream, TextAlignmentOptions.Center);
            _retryButton = Button(card, "RetryButton", 80, 560, 570, 52, "같은 밤 다시 하기", RetryCampaign, true, 24);
            Button(card, "ChooseNightButton", 80, 630, 570, 45, "밤 고르기", ReturnToNightSelection, false, 20);
            Button(card, "ReturnTitleButton", 80, 690, 570, 40, "제목으로", _game.ReturnToTitle, false, 20);
        }

        /// <summary>게임 규칙과 조작을 읽을 수 있는 도움말을 만든다.</summary>
        private void BuildHelp()
        {
            _help = Layer("HelpScreen", true);
            var card = CenterPanel(_help.transform, "HelpCard", 0, 0, 1280, 790, _ink);
            Label(card, "HelpTitle", 45, 35, 1190, 60, "작은 쥐의 생존 요령", 38, _cream);
            Label(card, "HelpBody", 45, 127, 510, 550,
                "01   움직임이 소리가 됩니다\nWASD 이동 · Ctrl 조용히 · Shift 빠르게 · Space 대시\n대시는 출발점에 소리 잔상을 남기고 잠시 조용해져요.\n\n"
                + "02   고양이를 원하는 자리로\n클릭: 미끼 던지기. 먹는 고양이에게도 잡혀요.\n스위치에서 E: 0.5초 동안 멈추세요. 움직이면 취소돼요.\n가까운 공에서 E: 커서 쪽으로 차요. 멈춘 자리에서 다시 차세요.\n\n"
                + "03   치즈를 모으며 아침을 당겨요\n치즈로 레벨을 올리고 성장 카드 한 장을 고르세요.\n고양이를 쫓아내면 아침이 빨라져요. Esc로 잠시 멈춥니다.", 20, _cream);
            Label(card, "CatHelpTitle", 585, 127, 650, 35, "만난 고양이", 25, _gold);
            _catHelp = Label(card, "CatHelp", 585, 170, 650, 520, "", 20, _cream);
            Button(card, "CloseHelpButton", 45, 700, 1190, 58, "알겠어요    ESC", CloseOverlay, true, 23);
        }

        /// <summary>도움말을 열 때 저장된 종류만 모아 보여 주고 원래 메뉴로 돌아갈 위치를 유지한다.</summary>
        private void OpenHelp(string sourceButton)
        {
            if (!OpenOverlay(_help, sourceButton)) return;
            _catHelp.text = "";
            foreach (CatKind kind in System.Enum.GetValues(typeof(CatKind)))
                if (_game.Progress.HasEncountered(kind))
                    _catHelp.text += (string.IsNullOrEmpty(_catHelp.text) ? "" : "\n\n") + CatLearning.Summary(kind);
            if (string.IsNullOrEmpty(_catHelp.text)) _catHelp.text = "밤을 시작하면 만난 고양이의 생존 요령이 여기에 남아요.";
        }

        /// <summary>음량 슬라이더와 두 화면 모드를 기존 메뉴 스타일로 배치한다.</summary>
        private void BuildSettings()
        {
            _settings = Layer("SettingsScreen", true);
            var card = CenterPanel(_settings.transform, "SettingsCard", 0, 0, 740, 570, _ink);
            Label(card, "SettingsTitle", 52, 34, 636, 62, "편안한 밤을 위한 설정", 36, _cream);
            Label(card, "VolumeLabel", 52, 129, 410, 35, "전체 음량", 24, _cream);
            _volumeValue = Label(card, "VolumeValue", 552, 129, 136, 35, "", 24, _gold, TextAlignmentOptions.Right);
            var sliderRect = Panel(card, "VolumeSlider", 52, 183, 636, 48, Color.clear);
            sliderRect.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
            var track = Panel(sliderRect, "VolumeTrack", 12, 20, 612, 8, new Color(0.22f, 0.3f, 0.34f));
            var fill = Panel(track, "VolumeFill", 0, 0, 612, 8, _gold);
            fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one;
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            var handleArea = Panel(sliderRect, "VolumeHandleArea", 12, 0, 612, 48, Color.clear);
            var handle = Panel(handleArea, "VolumeHandle", 0, 0, 24, 36, _cream);
            handle.anchorMin = Vector2.zero; handle.anchorMax = Vector2.up;
            handle.pivot = new Vector2(0.5f, 0.5f);
            handle.sizeDelta = new Vector2(24, -12); handle.anchoredPosition = Vector2.zero;
            _volumeSlider = sliderRect.gameObject.AddComponent<UnityEngine.UI.Slider>();
            _volumeSlider.fillRect = fill;
            _volumeSlider.handleRect = handle;
            _volumeSlider.targetGraphic = handle.GetComponent<UnityEngine.UI.Image>();
            _volumeSlider.minValue = 0f; _volumeSlider.maxValue = 100f; _volumeSlider.wholeNumbers = true;
            _volumeSlider.SetValueWithoutNotify(Mathf.Round(GameSettings.Volume * 100f));
            _volumeSlider.onValueChanged.AddListener(value =>
            {
                GameSettings.SetVolume(value / 100f);
                _volumeValue.SetText("{0:0}%", value);
            });
            Label(card, "DisplayLabel", 52, 273, 280, 35, "화면 모드", 24, _cream);
            _displayModeValue = Label(card, "DisplayModeValue", 332, 273, 356, 35, "", 22, _gold, TextAlignmentOptions.Right);
            Button(card, "WindowedButton", 52, 326, 310, 56, "창 모드", () => SetDisplayMode(false), false, 22);
            Button(card, "FullScreenButton", 378, 326, 310, 56, "전체 화면", () => SetDisplayMode(true), false, 22);
            Label(card, "SettingsHint", 52, 407, 636, 55,
                "변경 즉시 적용되며, 닫을 때 저장됩니다.\n음량: 마우스로 조절하거나 선택 후 ← → 키", 18, _muted);
            Button(card, "CloseSettingsButton", 52, 481, 636, 55, "저장하고 돌아가기    ESC", CloseOverlay, true, 22);
            _settings.SetActive(false);
        }

        /// <summary>실수로 진행 중인 판을 닫지 않도록 취소를 기본 선택으로 둔다.</summary>
        private void BuildQuit()
        {
            _quit = Layer("QuitScreen", true);
            var card = CenterPanel(_quit.transform, "QuitCard", 0, 0, 700, 375, _ink);
            Label(card, "QuitTitle", 45, 39, 610, 65, "게임을 종료할까요?", 38, _cream, TextAlignmentOptions.Center);
            Label(card, "QuitBody", 45, 132, 610, 70,
                "현재 밤의 진행은 저장되지 않습니다.\n음량과 화면 설정은 유지됩니다.", 23, _muted, TextAlignmentOptions.Center);
            _cancelQuitButton = Button(card, "CancelQuitButton", 45, 258, 295, 62, "계속 머무르기    ESC", CloseOverlay, true, 22);
            Button(card, "ConfirmQuitButton", 360, 258, 295, 62, "게임 종료", QuitGame, false, 22);
            _quit.SetActive(false);
        }

        /// <summary>제목의 시작 버튼과 Enter가 같은 프레임에 들어와도 한 번만 시작한다.</summary>
        private void StartFromTitle()
        {
            if (!HasOverlay && _game.Phase == GamePhase.Title) OpenNightSelection();
        }

        /// <summary>현재 저장 값을 표시하고 음량 슬라이더에 키보드 초점을 준다.</summary>
        private void OpenSettings(string sourceButton)
        {
            if (!OpenOverlay(_settings, sourceButton)) return;
            _volumeSlider.SetValueWithoutNotify(Mathf.Round(GameSettings.Volume * 100f));
            _volumeValue.SetText("{0:0}%", _volumeSlider.value);
            _displayModeValue.text = GameSettings.FullScreen ? "전체 화면 · 테두리 없음" : "창 모드";
            EventSystem.current?.SetSelectedGameObject(_volumeSlider.gameObject);
        }

        /// <summary>선택한 화면 모드를 적용하고 현재 선택을 글로 표시한다.</summary>
        private void SetDisplayMode(bool fullScreen)
        {
            GameSettings.SetFullScreen(fullScreen);
            _displayModeValue.text = fullScreen ? "전체 화면 · 테두리 없음" : "창 모드";
        }

        /// <summary>종료 확인을 열되 기본 키보드 선택으로 취소를 지정한다.</summary>
        private void OpenQuit(string sourceButton)
        {
            if (OpenOverlay(_quit, sourceButton)) Focus(_cancelQuitButton);
        }

        /// <summary>모달 뒤 메뉴의 포인터·키보드 선택을 차단하고 복귀 위치를 보관한다.</summary>
        private bool OpenOverlay(GameObject overlay, string sourceButton)
        {
            if (HasOverlay || (_game.Phase != GamePhase.Title && _game.Phase != GamePhase.Paused)) return false;
            _overlayReturnFocus = GameObject.Find(sourceButton);
            _title.GetComponent<CanvasGroup>().interactable = false;
            _pause.GetComponent<CanvasGroup>().interactable = false;
            _hud.GetComponent<CanvasGroup>().interactable = false;
            overlay.SetActive(true);
            var first = overlay.GetComponentInChildren<UnityEngine.UI.Button>();
            if (first != null) Focus(first);
            return true;
        }

        /// <summary>위 창만 닫고 기존 메뉴의 조작과 키보드 초점을 복원한다.</summary>
        private void CloseOverlay()
        {
            if (_settings != null && _settings.activeSelf) GameSettings.Save();
            if (_settings != null) _settings.SetActive(false);
            if (_quit != null) _quit.SetActive(false);
            if (_help != null) _help.SetActive(false);
            _title.GetComponent<CanvasGroup>().interactable = true;
            _pause.GetComponent<CanvasGroup>().interactable = true;
            _hud.GetComponent<CanvasGroup>().interactable = true;
            if (_overlayReturnFocus != null && _overlayReturnFocus.activeInHierarchy)
                EventSystem.current?.SetSelectedGameObject(_overlayReturnFocus);
            _overlayReturnFocus = null;
        }

        /// <summary>설정을 저장하고 Player에서는 앱을, Editor에서는 Play Mode만 종료한다.</summary>
        private void QuitGame()
        {
            GameSettings.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(0);
#endif
        }

        /// <summary>설정 창을 연 채 운영체제에서 종료한 경우에도 변경 값을 저장한다.</summary>
        private void OnApplicationQuit()
        {
            if (_settings != null && _settings.activeSelf) GameSettings.Save();
        }

        /// <summary>성장 화면이 뜬 직후 눌려 있던 클릭이 선택으로 이어지는 것을 막는다.</summary>
        private void ChooseUpgrade(int index)
        {
            if (_game.Phase == GamePhase.Upgrade && _shownPhase == GamePhase.Upgrade &&
                _shownLevel == _game.Level && Time.unscaledTime >= _upgradeUnlockTime)
                _game.SelectUpgrade(index);
        }

        /// <summary>현재 화면의 기본 버튼을 키보드 탐색 시작점으로 지정한다.</summary>
        private static void Focus(UnityEngine.UI.Button button)
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(button.gameObject);
        }

        /// <summary>화면 전체를 채우는 표시 계층을 만든다.</summary>
        private GameObject Layer(string name, bool dim)
        {
            var rect = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
            rect.SetParent(_canvas, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            if (dim)
            {
                var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
                image.color = new Color(0.015f, 0.035f, 0.06f, 0.74f);
                image.raycastTarget = true;
            }
            return rect.gameObject;
        }

        /// <summary>왼쪽 위를 기준으로 고정 크기 패널을 배치한다.</summary>
        private static RectTransform Panel(Transform parent, string name, float x, float y, float width, float height, Color color)
        {
            var rect = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Anchor(rect, new Vector2(0, 1), new Vector2(0, 1));
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
            var image = rect.GetComponent<UnityEngine.UI.Image>();
            image.color = color; image.raycastTarget = false;
            return rect;
        }

        /// <summary>화면 중앙을 기준으로 메뉴나 HUD 패널을 배치한다.</summary>
        private static RectTransform CenterPanel(Transform parent, string name, float x, float y, float width, float height, Color color)
        {
            var rect = Panel(parent, name, 0, 0, width, height, color);
            Anchor(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            rect.anchoredPosition = new Vector2(x, y);
            return rect;
        }

        /// <summary>앵커와 피벗을 동시에 설정해 다른 화면 비율에서도 기준점을 유지한다.</summary>
        private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 pivot)
        {
            rect.anchorMin = rect.anchorMax = anchor; rect.pivot = pivot;
        }

        /// <summary>임포트한 한글 글꼴로 텍스트를 만든다.</summary>
        private TMP_Text Label(Transform parent, string name, float x, float y, float width, float height,
            string text, float size, Color color, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
        {
            var rect = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Anchor(rect, new Vector2(0, 1), new Vector2(0, 1));
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
            var label = rect.GetComponent<TextMeshProUGUI>();
            label.font = _fontAsset; label.fontSize = size; label.color = color;
            label.alignment = alignment; label.text = text; label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;
            return label;
        }

        /// <summary>배경과 연결된 동작을 가진 버튼을 만든다.</summary>
        private UnityEngine.UI.Button Button(Transform parent, string name, float x, float y, float width, float height,
            string text, UnityEngine.Events.UnityAction action, bool primary, int fontSize)
        {
            var rect = Panel(parent, name, x, y, width, height, primary ? _gold : new Color(0.15f, 0.23f, 0.28f));
            var image = rect.GetComponent<UnityEngine.UI.Image>(); image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image; button.onClick.AddListener(action);
            var colors = button.colors;
            colors.highlightedColor = new Color(1f, 0.92f, 0.72f);
            colors.selectedColor = new Color(1f, 0.92f, 0.72f);
            colors.pressedColor = new Color(0.75f, 0.72f, 0.61f);
            button.colors = colors;
            Label(rect, "Label", 8, 0, width - 16, height, text, fontSize, primary ? _ink : _cream, TextAlignmentOptions.Center);
            return button;
        }

        /// <summary>가로 방향 채우기로 진행률을 표시하는 이미지를 만든다.</summary>
        private UnityEngine.UI.Image Fill(RectTransform parent, string name, Color color)
        {
            var rect = Panel(parent, name, 0, 0, parent.sizeDelta.x, parent.sizeDelta.y, color);
            var image = rect.GetComponent<UnityEngine.UI.Image>();
            image.sprite = _flatSprite;
            image.type = UnityEngine.UI.Image.Type.Filled;
            image.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            return image;
        }

        /// <summary>런타임에 생성한 글꼴과 아틀라스만 해제한다.</summary>
        private void OnDestroy()
        {
            if (_flatSprite != null) Destroy(_flatSprite);
            if (_fontAsset == null) return;
            foreach (var atlas in _fontAsset.atlasTextures) if (atlas != null) Destroy(atlas);
            if (_fontAsset.material != null) Destroy(_fontAsset.material);
            Destroy(_fontAsset);
        }
    }
}
