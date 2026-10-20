using System;

namespace MidnightMouse
{
    /// <summary>게임 오브젝트 없이 한 판의 시간·체력·성장을 관리한다.</summary>
    public sealed class GameSession
    {
        public GamePhase Phase { get; private set; }
        public float Elapsed { get; private set; }
        public float Duration { get; private set; }
        public int Hearts { get; private set; }
        public int MaxHearts { get; private set; }
        public int CheeseCollected { get; private set; }
        public int Level { get; private set; }
        public int Experience { get; private set; }
        public int ExperienceToNext => (int)Math.Min(int.MaxValue, (long)_baseExperience + (long)_experienceStep * (Level - 1));
        public int CatsBanished { get; private set; }
        public float InvulnerabilityRemaining { get; private set; }
        private readonly int _startHearts;
        private readonly int _heartCap;
        private readonly float _invulnerability;
        private readonly int _baseExperience;
        private readonly int _experienceStep;

        /// <summary>인스펙터의 규칙을 받아 유효한 새 세션을 만든다.</summary>
        public GameSession(float duration = 300f, int startHearts = 3, float invulnerability = 1.5f,
            int baseExperience = 4, int experienceStep = 2, int heartCap = 5)
        {
            if (float.IsNaN(duration) || float.IsInfinity(duration) || duration <= 0f)
                throw new ArgumentOutOfRangeException(nameof(duration));
            if (startHearts < 1 || heartCap < startHearts)
                throw new ArgumentOutOfRangeException(nameof(startHearts));
            if (float.IsNaN(invulnerability) || float.IsInfinity(invulnerability) || invulnerability < 0f)
                throw new ArgumentOutOfRangeException(nameof(invulnerability));
            if (baseExperience < 1 || experienceStep < 0)
                throw new ArgumentOutOfRangeException(nameof(baseExperience));
            Duration = duration;
            _startHearts = startHearts;
            _heartCap = heartCap;
            _invulnerability = invulnerability;
            _baseExperience = baseExperience;
            _experienceStep = experienceStep;
            Hearts = MaxHearts = startHearts;
            Level = 1;
        }

        /// <summary>재시작을 포함해 모든 판별 진행 상태를 초기화한다.</summary>
        public void Start()
        {
            Phase = GamePhase.Playing;
            Elapsed = InvulnerabilityRemaining = 0f;
            Hearts = MaxHearts = _startHearts;
            CheeseCollected = Experience = CatsBanished = 0;
            Level = 1;
        }

        /// <summary>진행 상태에서만 시간과 무적을 줄이고 생존 완료를 판정한다.</summary>
        public void Tick(float delta)
        {
            if (Phase != GamePhase.Playing || delta <= 0f || float.IsNaN(delta) || float.IsInfinity(delta))
                return;
            InvulnerabilityRemaining = Math.Max(0f, InvulnerabilityRemaining - delta);
            Elapsed = Math.Min(Duration, Elapsed + delta);
            if (Elapsed >= Duration)
                Phase = GamePhase.Won;
        }

        /// <summary>피격이 허용된 경우 한 하트를 잃고 무적 또는 패배로 전환한다.</summary>
        public bool TryDamage()
        {
            if (Phase != GamePhase.Playing || InvulnerabilityRemaining > 0f)
                return false;
            Hearts--;
            InvulnerabilityRemaining = _invulnerability;
            if (Hearts == 0)
                Phase = GamePhase.Lost;
            return true;
        }

        /// <summary>실제 진행과 일시정지 사이에서만 전환한다.</summary>
        public void TogglePause()
        {
            if (Phase == GamePhase.Playing) Phase = GamePhase.Paused;
            else if (Phase == GamePhase.Paused) Phase = GamePhase.Playing;
        }

        /// <summary>현재 판의 진행을 멈추고 시작 화면으로 돌아간다.</summary>
        public void ReturnToTitle() => Phase = GamePhase.Title;

        /// <summary>치즈 한 개의 경험치를 반영하고 필요한 성장 선택을 연다.</summary>
        public void CollectCheese(int experience)
        {
            if (Phase != GamePhase.Playing || experience <= 0) return;
            CheeseCollected++;
            Experience = (int)Math.Min(int.MaxValue, (long)Experience + experience);
            AdvanceLevel();
        }

        /// <summary>한 성장을 확정하고 남은 경험치로 추가 선택이 필요한지 확인한다.</summary>
        public void CompleteUpgrade()
        {
            if (Phase != GamePhase.Upgrade) return;
            Phase = GamePhase.Playing;
            AdvanceLevel();
        }

        /// <summary>살아 있는 판에서 최대 체력을 상한까지 늘리고 한 하트를 회복한다.</summary>
        public void IncreaseMaxHearts()
        {
            if (Phase != GamePhase.Playing && Phase != GamePhase.Upgrade) return;
            MaxHearts = Math.Min(_heartCap, MaxHearts + 1);
            Hearts = Math.Min(MaxHearts, Hearts + 1);
        }

        /// <summary>진행 중 실제로 퇴장시킨 고양이 수를 기록한다.</summary>
        public void RegisterBanished()
        {
            if (Phase == GamePhase.Playing) CatsBanished++;
        }

        /// <summary>한 번에 하나의 선택을 열어 초과 경험치를 보존한다.</summary>
        private void AdvanceLevel()
        {
            if (Experience < ExperienceToNext) return;
            Experience -= ExperienceToNext;
            Level++;
            Phase = GamePhase.Upgrade;
        }
    }
}
