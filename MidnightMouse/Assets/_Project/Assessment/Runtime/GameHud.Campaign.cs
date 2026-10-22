using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace MidnightMouse
{
    /// <summary>기존 HUD의 밤 선택·첫 인트로·부탁·원기록 표시를 연결한다.</summary>
    public sealed partial class GameHud
    {
        [SerializeField, Min(1f), Tooltip("밤 시작 안내 표시 시간(초). 높이면 새 요소를 오래 읽음. 기본 3, 권장 2~5.")]
        private float _nightBannerSeconds = 3f;
        private GameObject _nightSelection, _introScreen, _nightBanner, _saveErrorPanel, _readyPrompt;
        private TMP_Text _introText, _introPageText, _nightBannerText, _requestText, _firstGuideText, _campaignResult, _saveErrorText;
        private readonly UnityEngine.UI.Button[] _nightButtons = new UnityEngine.UI.Button[7];
        private readonly TMP_Text[] _nightRecords = new TMP_Text[7];
        private readonly int[] _nightIndices = new int[7];
        private readonly string[] _introPages =
        {
            "집사가 잠들면, 이 집은 고양이들 차지가 된다.",
            "벽 속에 사는 쥐 모찌는 가족과 겨울을 날 치즈가 필요하다.",
            "아침 6시, 알람이 울릴 때까지 버텨라!"
        };
        private int _introPage, _selectedNight, _campaignInputFrame = -1, _shownRequestHits = -1;
        private bool _pendingNightStart;
        private float _nightBannerUntil;
        private NightDefinition _shownCampaignNight;
        private GamePhase _renderedResultPhase = (GamePhase)(-1);
        private bool HasCampaignOverlay => _pendingNightStart || (_nightSelection != null && _nightSelection.activeSelf)
            || (_introScreen != null && _introScreen.activeSelf);

        /// <summary>기존 Canvas 규격과 버튼 생성기를 사용해 캠페인 화면을 한 번 만든다.</summary>
        private void BuildCampaign()
        {
            _nightSelection = Layer("NightSelection", true);
            var card = CenterPanel(_nightSelection.transform, "NightSelectionCard", 0, 0, 1260, 720, _ink);
            Label(card, "NightSelectionTitle", 30, 25, 1200, 50, "첫눈 전 일주일의 밤", 34, _cream, TextAlignmentOptions.Center);
            for (int i = 0; i < 7; i++)
            {
                int slot = i;
                var night = Panel(card, "NightCard" + (i + 1), 30 + i % 4 * 300, 100 + i / 4 * 255, 280, 230,
                    new Color(0.10f, 0.17f, 0.22f));
                _nightRecords[i] = Label(night, "NightRecord", 18, 20, 244, 130, "", 20, _cream);
                _nightButtons[i] = Button(night, "NightButton" + (i + 1), 18, 165, 244, 46, "", () => SelectNight(slot), true, 20);
            }
            Button(card, "CloseNightSelection", 430, 645, 400, 45, "제목으로    ESC", CloseCampaignOverlay, false, 20);
            _nightSelection.SetActive(false);
            _introScreen = Layer("IntroScreen", true);
            var intro = CenterPanel(_introScreen.transform, "IntroCard", 0, 0, 1000, 420, new Color(0.169f, 0.227f, 0.404f));
            _introPageText = Label(intro, "IntroPage", 50, 35, 900, 36, "", 23, _gold, TextAlignmentOptions.Center);
            _introText = Label(intro, "IntroText", 65, 120, 870, 165, "", 34, _cream, TextAlignmentOptions.Center);
            Label(intro, "IntroInput", 50, 340, 900, 35, "아무 키 또는 클릭으로 다음 · ESC 건너뛰기", 20, _muted, TextAlignmentOptions.Center);
            _introScreen.SetActive(false);
            var banner = CenterPanel(_hud.transform, "NightIntroduction", 0, 160, 850, 125, _ink);
            _nightBanner = banner.gameObject;
            _nightBannerText = Label(banner, "NightIntroductionText", 24, 15, 802, 100, "", 22, _cream, TextAlignmentOptions.Center);
            _nightBanner.SetActive(false);
            var ready = CenterPanel(_hud.transform, "ReadyPrompt", 0, -150, 720, 110, _ink);
            _readyPrompt = ready.gameObject;
            Label(ready, "ReadyText", 20, 18, 680, 74, "아무 키나 누르면 밤이 시작됩니다\n준비하는 동안 시간은 흐르지 않습니다", 25, _cream, TextAlignmentOptions.Center);
            _readyPrompt.SetActive(false);
            var request = Panel(_hud.transform, "RequestPanel", 26, 150, 420, 95, _ink);
            _requestText = Label(request, "RequestProgress", 15, 12, 390, 72, "", 18, _cream);
            var guide = CenterPanel(_hud.transform, "FirstGuidePanel", 0, -250, 1120, 45, Color.clear);
            _firstGuideText = Label(guide, "FirstGuideText", 0, 0, 1120, 45, "", 23, _gold, TextAlignmentOptions.Center);
            var errorLayer = Layer("SaveErrorLayer", false);
            var error = CenterPanel(errorLayer.transform, "SaveErrorPanel", 0, 0, 1120, 65, _ink);
            Anchor(error, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            error.anchoredPosition = Vector2.zero;
            _saveErrorPanel = error.gameObject;
            _saveErrorText = Label(error, "SaveErrorText", 15, 8, 1090, 52, "", 16, new Color(1f, 0.6f, 0.45f), TextAlignmentOptions.Center);
            _saveErrorPanel.SetActive(false);
            _phaseLabel.enableAutoSizing = true;
            _phaseLabel.fontSizeMin = 11;
            _phaseLabel.fontSizeMax = 15;
        }

        /// <summary>선택 화면을 열 때만 원기록과 현재 해금 조건을 읽는다.</summary>
        private void OpenNightSelection()
        {
            if (_game.Phase != GamePhase.Title || HasOverlay || _campaignInputFrame == Time.frameCount) return;
            _campaignInputFrame = Time.frameCount;
            _title.GetComponent<CanvasGroup>().interactable = false;
            _nightSelection.SetActive(true);
            for (int slot = 0; slot < 7; slot++)
            {
                _nightIndices[slot] = -1;
                for (int index = 0; index < _game.Nights.Length; index++)
                    if (_game.Nights[index].Order == slot + 1) { _nightIndices[slot] = index; break; }
                int selected = _nightIndices[slot];
                var night = selected >= 0 ? _game.Nights[selected] : null;
                bool available = night != null && night.Order <= 3;
                bool unlocked = available && _game.IsNightUnlocked(selected);
                _nightButtons[slot].interactable = unlocked;
                _nightButtons[slot].GetComponentInChildren<TMP_Text>().text = !available ? "준비 중" : unlocked ? "이 밤 시작하기" : "앞선 밤 생존 후 열림";
                _nightRecords[slot].text = available ? night.Title + "\n\n" + RecordText(night)
                    : $"{slot + 1}일째 밤\n\n준비 중";
            }
            Focus(_nightButtons[0]);
        }

        /// <summary>첫날의 최초 선택만 인트로를 거치고 잠긴 밤과 중복 입력을 거부한다.</summary>
        private void SelectNight(int slot)
        {
            if (!_nightSelection.activeSelf || _pendingNightStart || _campaignInputFrame == Time.frameCount) return;
            int index = _nightIndices[slot];
            if (!_nightButtons[slot].interactable || !_game.IsNightUnlocked(index)) return;
            _selectedNight = index;
            _campaignInputFrame = Time.frameCount;
            if (_game.Nights[index].Order == 1 && !_game.Progress.introSeen)
            {
                _nightSelection.SetActive(false);
                _introScreen.SetActive(true);
                EventSystem.current?.SetSelectedGameObject(null);
                _introPage = 0;
                ShowIntroPage();
            }
            else StartCoroutine(BeginSelectedNight());
        }

        /// <summary>인트로를 넘긴 키가 메뉴 제출이나 게임 입력으로 중복 처리되지 않게 소비한다.</summary>
        private bool HandleCampaignInput(Keyboard keyboard)
        {
            if (!HasCampaignOverlay) return false;
            if (_pendingNightStart || _campaignInputFrame == Time.frameCount) return true;
            if (_introScreen.activeSelf)
            {
                bool skip = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
                bool advance = keyboard != null && keyboard.anyKey.wasPressedThisFrame;
                advance |= Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
                if (skip || advance)
                {
                    _campaignInputFrame = Time.frameCount;
                    if (skip || ++_introPage >= _introPages.Length)
                    {
                        _game.Progress.introSeen = true;
                        _game.SaveProgress();
                        StartCoroutine(BeginSelectedNight());
                    }
                    else ShowIntroPage();
                }
            }
            else if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) CloseCampaignOverlay();
            return true;
        }

        /// <summary>새 카드로 넘어갔을 때만 문구와 페이지 번호를 만든다.</summary>
        private void ShowIntroPage()
        {
            _introText.text = _introPages[_introPage];
            _introPageText.text = $"모찌의 첫 밤  {_introPage + 1} / {_introPages.Length}";
        }

        /// <summary>선택 클릭이 첫 프레임 미끼 던지기로 이어지지 않도록 한 프레임 뒤 시작한다.</summary>
        private IEnumerator BeginSelectedNight()
        {
            _pendingNightStart = true;
            EventSystem.current?.SetSelectedGameObject(null);
            yield return null;
            bool started = _game.BeginNight(_selectedNight);
            _pendingNightStart = false;
            CloseCampaignOverlay();
            if (started) ShowNightBanner();
            Refresh();
        }

        /// <summary>캠페인 창만 닫고 제목의 클릭과 키보드 초점을 복구한다.</summary>
        private void CloseCampaignOverlay()
        {
            if (_pendingNightStart) return;
            if (HasCampaignOverlay) _campaignInputFrame = Time.frameCount;
            if (_nightSelection != null) _nightSelection.SetActive(false);
            if (_introScreen != null) _introScreen.SetActive(false);
            if (_title != null) _title.GetComponent<CanvasGroup>().interactable = true;
            if (_game.Phase == GamePhase.Title && _startButton != null) Focus(_startButton);
        }

        /// <summary>결과에서 제목 상태를 거쳐 밤 선택 화면으로 이동한다.</summary>
        private void ReturnToNightSelection()
        {
            if (_game.Phase != GamePhase.Won && _game.Phase != GamePhase.Lost) return;
            _game.ReturnToTitle();
            Refresh();
            OpenNightSelection();
        }

        /// <summary>같은 밤을 다시 시작하면서 밤 시작 문구도 다시 표시한다.</summary>
        private void RetryCampaign()
        {
            if (_game.Phase != GamePhase.Won && _game.Phase != GamePhase.Lost) return;
            _game.RestartRun();
            ShowNightBanner();
            Refresh();
        }

        /// <summary>밤 시작 때만 날짜와 새 요소 문구를 구성한다.</summary>
        private void ShowNightBanner()
        {
            if (_game.CurrentNight == null) return;
            var night = _game.CurrentNight;
            _nightBannerText.text = $"{night.Order}일째 밤 · 첫눈까지 {8 - night.Order}일\n\n{night.Introduction}";
            _nightBannerUntil = Time.unscaledTime + _nightBannerSeconds;
        }

        /// <summary>부탁 변화와 결과 진입 때만 새 문자열을 만들고 저장 오류를 계속 표시한다.</summary>
        private void RefreshCampaign()
        {
            var night = _game.CurrentNight;
            if (_shownCampaignNight != night) { _shownCampaignNight = night; _shownRequestHits = -1; }
            _requestText.transform.parent.gameObject.SetActive(night != null);
            if (night != null && _shownRequestHits != _game.Events.RequestBestHits)
            {
                _shownRequestHits = _game.Events.RequestBestHits;
                _requestText.text = $"부탁 · {night.RequestName}  {(_game.Events.RequestCompleted ? "완료 ✓" : "0 / 1")}\n"
                    + $"{(night.LullabyRequest ? "미끼로 재운 뒤 타격" : "한 번에 맞힌 고양이")}  {Mathf.Min(_shownRequestHits, night.RequestHits)} / {night.RequestHits}";
                _requestText.color = _game.Events.RequestCompleted ? _gold : _cream;
            }
            _firstGuideText.text = Time.unscaledTime < _game.GuideUntil ? _game.FirstGuide : "";
            _readyPrompt.SetActive(_game.Phase == GamePhase.Ready);
            _nightBanner.SetActive(night != null && (_game.Phase == GamePhase.Ready ||
                (_game.Phase == GamePhase.Playing && Time.unscaledTime < _nightBannerUntil)));
            _saveErrorPanel.SetActive(!string.IsNullOrEmpty(_game.SaveError) &&
                (_game.Phase == GamePhase.Title || _game.Phase == GamePhase.Won || _game.Phase == GamePhase.Lost));
            _saveErrorText.text = _game.SaveError ?? "";
            if (_game.Phase != GamePhase.Won && _game.Phase != GamePhase.Lost) _renderedResultPhase = (GamePhase)(-1);
            else if (_renderedResultPhase != _game.Phase && _game.IsCampaignResultReady)
                _campaignResult.text = night != null ? RecordText(night) : "";
        }

        /// <summary>현재 기준의 조건별 별과 저장된 최단 기록을 표시한다.</summary>
        private string RecordText(NightDefinition night)
        {
            var record = _game.Progress.Find(night.Id);
            if (record == null) return "생존 ☆ · 부탁 ☆ · 퇴장 ☆\n생존 기록 없음";
            string stars = $"생존 {(record.survived ? "★" : "☆")} · 부탁 {(record.survived && record.requestCompleted ? "★" : "☆")} · 퇴장 {(record.survived && record.mostBanished >= night.BanishTarget ? "★" : "☆")}";
            return stars + $"\n최다 퇴장 {record.mostBanished} / 목표 {night.BanishTarget}"
                + (record.bestSeconds >= 0 ? $"\n최단 {record.bestSeconds:0.0}초" : "\n생존 기록 없음");
        }
    }
}
