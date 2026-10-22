using System;
using System.IO;
using UnityEngine;

namespace MidnightMouse
{
    /// <summary>중앙 게임 제어기의 밤 선택·원기록·부탁·첫 판 안내 연결.</summary>
    public sealed partial class GameController
    {
        private NightDefinition[] _nights;
        private string[] _nightIds;
        private CampaignStore _campaignStore;
        private M1Layout _layout;
        private bool _campaignRecorded;
        [SerializeField, Min(1), Tooltip("첫 조작 안내 표시 시간(초). 길수록 읽기 쉬움. 권장 5.")] private float _guideDuration = 5f;
        private readonly RunEvents _events = new RunEvents();
        public NightDefinition CurrentNight { get; private set; }
        public NightDefinition[] Nights => _nights;
        public CampaignProgress Progress { get; private set; }
        public string SaveError => _campaignStore.Error;
        public RunEvents Events => _events;
        public bool IsCampaignResultReady => CurrentNight == null || _campaignRecorded;
        public string FirstGuide { get; private set; } = "";
        public float GuideUntil { get; private set; }
        public float StageSeconds => CurrentNight != null ? CurrentNight.StageSeconds : _rules.StageDuration;
        public float BaitImmunity => CurrentNight != null ? CurrentNight.BaitImmunity : _rules.BaitImmunity;

        /// <summary>밤 에셋과 독립된 진행 파일을 한 번 읽는다.</summary>
        private void InitializeCampaign()
        {
            _nights = Resources.LoadAll<NightDefinition>("Campaign/Nights");
            Array.Sort(_nights, (a, b) => a.Order.CompareTo(b.Order));
            _nightIds = new string[_nights.Length];
            for (int i = 0; i < _nights.Length; i++)
            {
                if (!_nights[i].IsValid()) throw new InvalidOperationException("밤 데이터 오류: " + _nights[i].name);
                _nightIds[i] = _nights[i].Id;
                for (int j = 0; j < i; j++)
                    if (_nightIds[j] == _nightIds[i] || _nights[j].Order == _nights[i].Order)
                        throw new InvalidOperationException("중복 밤 ID 또는 순서: " + _nightIds[i]);
                int slots = 0;
                foreach (var entry in _nights[i].Cats) slots += entry.counts[4];
                if (slots > _cats.Length) throw new InvalidOperationException("고양이 슬롯 부족: " + _nightIds[i]);
            }
            string path = Application.isBatchMode
                ? Path.Combine(Application.temporaryCachePath, "CampaignTests", Guid.NewGuid().ToString("N") + ".json")
                : Path.Combine(Application.persistentDataPath, "campaign-v1.json");
            _campaignStore = new CampaignStore(path);
            Progress = _campaignStore.Load();
            _layout = FindAnyObjectByType<M1Layout>();
        }

        /// <summary>현재 순서에서 이전 밤의 생존 원기록으로 해금을 계산한다.</summary>
        public bool IsNightUnlocked(int index) => index >= 0 && index < _nights.Length && Progress.IsUnlocked(_nightIds, index);

        /// <summary>열린 캠페인 밤을 새 판으로 시작한다.</summary>
        public bool BeginNight(int index)
        {
            if (Phase != GamePhase.Title || !IsNightUnlocked(index)) return false;
            CurrentNight = _nights[index];
            StartRun();
            return true;
        }

        /// <summary>공유 에셋을 수정하지 않고 이번 밤 길이로 세션을 만든다.</summary>
        private GameSession CreateSession() => new GameSession(Duration, _rules.StartingHearts, _rules.Invulnerability,
            _rules.BaseExperience, _rules.ExperienceStep, _rules.HeartCap);

        /// <summary>밤의 규칙 배율은 기본값에 이 지점에서 한 번만 곱한다.</summary>
        public float ChaseSpeed(CatDefinition kind) => (kind != null ? kind.Chase : _rules.ChaseSpeed) * (CurrentNight != null ? CurrentNight.ChaseMultiplier : 1f);

        /// <summary>선택한 밤에 맞게 고양이 슬롯을 비우고 최초 종류별 구성을 배정한다.</summary>
        private void ResetNightActors()
        {
            _events.Reset(); _campaignRecorded = false; FirstGuide = ""; GuideUntil = 0;
            _enteredCats = 0;
            foreach (var cat in _cats) cat.SetAvailable(false);
            foreach (var trap in _traps) trap.SetNightEnabled(CurrentNight == null || trap.Kind == TrapKind.Cup || CurrentNight.Vacuum);
            if (CurrentNight == null)
            {
                int count = Mathf.Min(_rules.GetCatCount(0), _cats.Length);
                for (int i = 0; i < count; i++) EnterCat(null);
            }
            else foreach (var entry in CurrentNight.Cats)
                for (int i = 0; i < entry.counts[0]; i++) EnterCat(entry.kind, true);
        }

        /// <summary>비어 있는 다음 슬롯에 종류를 배정하고 펫도어에서 입장시킨다.</summary>
        private void EnterCat(CatDefinition kind, bool initial = false)
        {
            if (_enteredCats >= _cats.Length) return;
            CatController cat = _cats[_enteredCats];
            Vector3 spawn = _layout != null && _layout.PetDoors.Count > 0 ? _layout.PetDoors[_enteredCats % _layout.PetDoors.Count] : cat.transform.position;
            Vector3? sleepPoint = null;
            if (kind != null && kind.Kind == CatKind.Lazy)
            {
                int index = 0;
                for (int i = 0; i < _enteredCats; i++) if (_cats[i].Definition != null && _cats[i].Definition.Kind == CatKind.Lazy) index++;
                if (_layout == null || index >= _layout.SleepPoints.Count) throw new InvalidOperationException("게으름냥 수면 자리 부족");
                sleepPoint = _layout.SleepPoints[index];
            }
            cat.Configure(kind, spawn, sleepPoint, initial);
            _enteredCats++;
            cat.SetAvailable(true);
        }

        /// <summary>기존 슬롯의 종류를 바꾸지 않고 현재 단계에서 모자란 종류만 찾는다.</summary>
        private CatDefinition MissingKind()
        {
            foreach (var entry in CurrentNight.Cats)
            {
                int count = 0;
                for (int i = 0; i < _enteredCats; i++) if (_cats[i].Definition == entry.kind) count++;
                if (count < CurrentNight.Count(entry.kind, Elapsed)) return entry.kind;
            }
            return null;
        }

        /// <summary>타격 고유 수를 개발 기록과 분리된 부탁 판정에 전달한다.</summary>
        public void RecordTrapHit(TrapKind kind, int uniqueHits) => _events.TrapHit(kind, uniqueHits, CurrentNight);

        /// <summary>미끼로 재운 게으름냥의 첫 함정 피격을 자장가 부탁에 반영한다.</summary>
        public void RecordBaitSleepHit() => _events.BaitSleepHit(CurrentNight);

        /// <summary>생존한 캠페인 판의 원기록만 한 번 저장한다.</summary>
        private void FinishCampaign()
        {
            if (_campaignRecorded || CurrentNight == null) return;
            _campaignRecorded = true;
            Progress.RecordResult(CurrentNight.Id, Phase == GamePhase.Won, _events.RequestCompleted, CatsBanished, RealElapsed);
            SaveProgress();
        }

        /// <summary>처음 본 안내 표시를 기록하고 저장 오류는 UI가 읽도록 남긴다.</summary>
        public void SaveProgress() => _campaignStore.Save(Progress);

        /// <summary>첫날 안내는 조건을 만족한 최초 한 번만 보여 준다.</summary>
        public void ShowGuide(int bit, string message)
        {
            if (CurrentNight == null || CurrentNight.Order != 1 || (Progress.tutorialSeen & bit) != 0) return;
            Progress.tutorialSeen |= bit;
            FirstGuide = message;
            GuideUntil = Time.unscaledTime + _guideDuration;
            SaveProgress();
        }

        /// <summary>첫날 컵 안에 고양이가 들어오면 함정 안내를 띄운다.</summary>
        private void UpdateFirstGuide()
        {
            if (CurrentNight == null || CurrentNight.Order != 1 || (Progress.tutorialSeen & 4) != 0) return;
            foreach (var trap in _traps)
            {
                if (trap.Kind != TrapKind.Cup) continue;
                foreach (var cat in _cats)
                    if (cat.CanReceiveHit && Vector3.ProjectOnPlane(cat.transform.position - trap.EffectPosition, Vector3.up).sqrMagnitude <= _rules.CupRadius * _rules.CupRadius)
                    { ShowGuide(4, "노란 선 끝 스위치에서 E · 맞은 고양이는 스위치로 온다"); return; }
            }
        }
    }
}
