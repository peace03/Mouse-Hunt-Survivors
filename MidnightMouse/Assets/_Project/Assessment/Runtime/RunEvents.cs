namespace MidnightMouse
{
    /// <summary>CSV 유무와 무관하게 한 판의 함정 작동·부탁 결과를 모은다.</summary>
    public sealed class RunEvents
    {
        public int RequestBestHits { get; private set; }
        public bool RequestCompleted { get; private set; }

        /// <summary>새 판에서 진행과 완료 표시를 초기화한다.</summary>
        public void Reset() { RequestBestHits = 0; RequestCompleted = false; }

        /// <summary>함정이 보장하는 작동별 고유 타격 수를 부탁 조건과 비교한다.</summary>
        public void TrapHit(TrapKind kind, int uniqueHits, NightDefinition night)
        {
            if (night == null || night.LullabyRequest || night.BallWakeRequest || kind != night.RequestTrap) return;
            RequestBestHits = System.Math.Max(RequestBestHits, uniqueHits);
            RequestCompleted |= RequestBestHits >= night.RequestHits;
        }

        /// <summary>공으로 깨어난 뒤 시간 안의 첫 함정 타격을 부탁에 반영한다.</summary>
        public void BallWakeHit(NightDefinition night)
        {
            if (night == null || !night.BallWakeRequest) return;
            RequestBestHits++;
            RequestCompleted |= RequestBestHits >= night.RequestHits;
        }

        /// <summary>미끼로 잠든 한 번의 수면에서 첫 함정 피격만 호출해 자장가를 센다.</summary>
        public void BaitSleepHit(NightDefinition night)
        {
            if (night == null || !night.LullabyRequest) return;
            RequestBestHits++;
            RequestCompleted |= RequestBestHits >= night.RequestHits;
        }
    }
}
