using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MidnightMouse
{
    /// <summary>캠페인 파일을 검증하고 원자적으로 교체하며 씬 오브젝트에 붙이지 않는다.</summary>
    public sealed class CampaignStore
    {
        private readonly string _path;
        private bool _writeBlocked;
        public string Error { get; private set; }

        /// <summary>실제 사용자 저장 또는 테스트가 지정한 파일 경로를 고정한다.</summary>
        public CampaignStore(string path) => _path = Path.GetFullPath(path);

        /// <summary>없는 파일은 새 진행으로 시작하고 손상 파일은 보존하며 오류를 알린다.</summary>
        public CampaignProgress Load()
        {
            try
            {
                var progress = ReadExisting();
                Error = null;
                _writeBlocked = false;
                return progress ?? new CampaignProgress();
            }
            catch (Exception exception) when (IsStorageError(exception))
            {
                Error = "진행 기록을 불러오지 못했습니다. 기존 파일을 보존합니다: " + exception.Message;
                _writeBlocked = true;
                return new CampaignProgress();
            }
        }

        /// <summary>검증한 임시 파일을 교체하고 직전 원본 한 개를 .bak에 보관한다.</summary>
        public bool Save(CampaignProgress progress)
        {
            if (_writeBlocked) return false;
            string temporary = null;
            try
            {
                if (progress == null) throw new ArgumentNullException(nameof(progress));
                progress.Validate();
                // 불러오기를 건너뛰거나 실행 중 파일이 바뀌어도 손상 원본을 덮어쓰지 않는다.
                ReadExisting();
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(progress, true));
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(_path)) File.Replace(temporary, _path, _path + ".bak");
                else File.Move(temporary, _path);
                Error = null;
                return true;
            }
            catch (Exception exception) when (IsStorageError(exception))
            {
                Error = "진행 기록을 저장하지 못했습니다. 기존 파일을 보존합니다: " + exception.Message;
                return false;
            }
            finally
            {
                if (temporary != null)
                {
                    try { File.Delete(temporary); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        /// <summary>필수 필드가 없는 JSON도 손상으로 취급하고 알 수 없는 밤 ID는 유지한다.</summary>
        private CampaignProgress ReadExisting()
        {
            string json;
            try { json = File.ReadAllText(_path); }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            // Unity는 누락된 null 목록을 빈 목록으로 만들므로 잘못된 원소를 누락 감지에 쓴다.
            var progress = new CampaignProgress { version = 0, nights = new List<CampaignNightRecord> { new CampaignNightRecord() } };
            JsonUtility.FromJsonOverwrite(json, progress);
            progress.Validate();
            return progress;
        }

        /// <summary>파일·직렬화 실패를 화면에 보여 줄 수 있는 오류로 한정한다.</summary>
        private static bool IsStorageError(Exception exception) => exception is IOException
            || exception is UnauthorizedAccessException || exception is ArgumentException
            || exception is NotSupportedException || exception is FormatException;
    }
}
