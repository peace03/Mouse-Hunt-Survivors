using UnityEngine;

namespace MidnightMouse
{
    /// <summary>게임의 전체 음량과 화면 모드만 저장하고 Unity에 적용한다.</summary>
    public static class GameSettings
    {
        private const string VolumeKey = "MidnightMouse.Settings.Volume.v1";
        private const string ScreenKey = "MidnightMouse.Settings.FullScreen.v1";
        public static float Volume { get; private set; } = 1f;
        public static bool FullScreen { get; private set; }

        /// <summary>저장되지 않은 항목에는 기본 음량과 현재 창 모드를 사용한다.</summary>
        public static void Load()
        {
            SetVolume(PlayerPrefs.GetFloat(VolumeKey, 1f));
            int mode = PlayerPrefs.GetInt(ScreenKey, Screen.fullScreen ? 1 : 0);
            SetFullScreen(mode == 1);
        }

        /// <summary>손상된 저장 값도 안전한 범위로 보정해 전체 음량에 즉시 적용한다.</summary>
        public static void SetVolume(float value)
        {
            Volume = float.IsNaN(value) || float.IsInfinity(value) ? 1f : Mathf.Clamp01(value);
            AudioListener.volume = Volume;
        }

        /// <summary>Windows에서 전체 화면 창과 일반 창 사이를 전환한다.</summary>
        public static void SetFullScreen(bool value)
        {
            FullScreen = value;
            Screen.fullScreenMode = value ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        }

        /// <summary>창을 닫거나 종료할 때만 저장해 슬라이더 조작마다 디스크를 쓰지 않는다.</summary>
        public static void Save()
        {
            PlayerPrefs.SetFloat(VolumeKey, Volume);
            PlayerPrefs.SetInt(ScreenKey, FullScreen ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
