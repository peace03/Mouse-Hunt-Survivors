using System;

namespace MidnightMouse
{
    /// <summary>밤 ID에 연결된 생존 판의 원기록이며 씬 오브젝트에 붙이지 않는다.</summary>
    [Serializable]
    public sealed class CampaignNightRecord
    {
        public string nightId;
        public bool survived;
        public bool requestCompleted;
        public int mostBanished;
        public float bestSeconds = -1f;
    }
}
