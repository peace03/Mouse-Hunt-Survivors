using System;
using System.Collections.Generic;

namespace MidnightMouse
{
    /// <summary>캠페인 원기록에서 현재 별과 해금을 계산하며 씬 오브젝트에 붙이지 않는다.</summary>
    [Serializable]
    public sealed class CampaignProgress
    {
        public int version = 1;
        public bool introSeen;
        public int tutorialSeen;
        public List<CampaignNightRecord> nights = new List<CampaignNightRecord>();

        /// <summary>생존한 판의 부탁·최다 퇴장·최단 시간만 각각 누적한다.</summary>
        public void RecordResult(string nightId, bool survived, bool requestCompleted, int banished, float realSeconds)
        {
            if (string.IsNullOrWhiteSpace(nightId)) throw new ArgumentException("밤 ID가 비어 있습니다.", nameof(nightId));
            if (banished < 0) throw new ArgumentOutOfRangeException(nameof(banished));
            if (realSeconds < 0f || float.IsNaN(realSeconds) || float.IsInfinity(realSeconds))
                throw new ArgumentOutOfRangeException(nameof(realSeconds));
            if (!survived) return;
            var record = Find(nightId);
            if (record == null)
            {
                record = new CampaignNightRecord { nightId = nightId };
                nights.Add(record);
            }
            record.survived = true;
            record.requestCompleted |= requestCompleted;
            record.mostBanished = Math.Max(record.mostBanished, banished);
            record.bestSeconds = record.bestSeconds < 0f ? realSeconds : Math.Min(record.bestSeconds, realSeconds);
        }

        /// <summary>밤 순서와 무관하게 ID가 같은 원기록을 찾는다.</summary>
        public CampaignNightRecord Find(string nightId) => nights.Find(record => record.nightId == nightId);

        /// <summary>생존·부탁·현재 퇴장 목표를 독립적으로 합산한다.</summary>
        public int Stars(string nightId, int target)
        {
            if (target < 0) throw new ArgumentOutOfRangeException(nameof(target));
            var record = Find(nightId);
            if (record == null || !record.survived) return 0;
            return 1 + (record.requestCompleted ? 1 : 0) + (record.mostBanished >= target ? 1 : 0);
        }

        /// <summary>첫 밤 또는 현재 순서에서 바로 앞 밤을 생존한 경우에만 연다.</summary>
        public bool IsUnlocked(string[] nightIds, int index)
        {
            if (nightIds == null) throw new ArgumentNullException(nameof(nightIds));
            if (index < 0 || index >= nightIds.Length) throw new ArgumentOutOfRangeException(nameof(index));
            return index == 0 || Find(nightIds[index - 1])?.survived == true;
        }

        /// <summary>파일 경계에서 버전·중복 ID·원기록의 값과 생존 조건을 확인한다.</summary>
        internal void Validate()
        {
            if (version != 1) throw new FormatException($"지원하지 않는 캠페인 저장 버전입니다: {version}");
            if (tutorialSeen < 0 || nights == null) throw new FormatException("캠페인 안내 또는 밤 목록이 잘못되었습니다.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in nights)
            {
                if (record == null || string.IsNullOrWhiteSpace(record.nightId) || !ids.Add(record.nightId))
                    throw new FormatException("밤 ID가 비어 있거나 중복되었습니다.");
                if (record.mostBanished < 0 || float.IsNaN(record.bestSeconds) || float.IsInfinity(record.bestSeconds)
                    || (record.survived ? record.bestSeconds < 0f
                        : record.requestCompleted || record.mostBanished != 0 || record.bestSeconds != -1f))
                    throw new FormatException($"밤 원기록이 잘못되었습니다: {record.nightId}");
            }
        }
    }
}
