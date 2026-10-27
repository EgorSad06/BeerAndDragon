using System;
using UnityEngine;

// Настройки игрока (страница SETTINGS в главном меню). Хранятся в PlayerPrefs, грузятся до первой сцены.
//  - MasterVolume -- общая громкость (AudioListener.volume);
//  - MouseSensitivity -- множитель к sensX/sensY камеры (CameraPOV), InvertY -- инверсия мыши по вертикали;
//  - FieldOfView -- угол обзора от первого лица (PlayerStatusEffects прибавляет к нему ускорение от энергетика);
//  - CameraKick -- сила отдачи/тряски камеры от оружия и ударов (CameraPOV.AddRecoil), 0 -- выключено.
// Полноэкранный режим Unity запоминает сам, его здесь нет.
public static class GameSettings
{
    public const float DefaultVolume = 0.8f, DefaultSensitivity = 1f, DefaultFov = 60f, DefaultKick = 1f;
    public const float MinSensitivity = 0.1f, MaxSensitivity = 3f;
    public const float MinFov = 60f, MaxFov = 100f;

    private const string Prefix = "bd.settings.";

    public static float MasterVolume { get; private set; } = DefaultVolume;
    public static float MouseSensitivity { get; private set; } = DefaultSensitivity;
    public static bool InvertY { get; private set; }
    public static float FieldOfView { get; private set; } = DefaultFov;
    public static float CameraKick { get; private set; } = DefaultKick;

    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        Load();
        Apply();
    }

    public static void Load()
    {
        MasterVolume = ClampVolume(PlayerPrefs.GetFloat(Prefix + "volume", DefaultVolume));
        MouseSensitivity = ClampSensitivity(PlayerPrefs.GetFloat(Prefix + "sensitivity", DefaultSensitivity));
        InvertY = PlayerPrefs.GetInt(Prefix + "invertY", 0) != 0;
        FieldOfView = ClampFov(PlayerPrefs.GetFloat(Prefix + "fov", DefaultFov));
        CameraKick = ClampVolume(PlayerPrefs.GetFloat(Prefix + "kick", DefaultKick));
    }

    public static void SetMasterVolume(float v) { MasterVolume = ClampVolume(v); Save(); }
    public static void SetMouseSensitivity(float v) { MouseSensitivity = ClampSensitivity(v); Save(); }
    public static void SetInvertY(bool v) { InvertY = v; Save(); }
    public static void SetFieldOfView(float v) { FieldOfView = ClampFov(v); Save(); }
    public static void SetCameraKick(float v) { CameraKick = ClampVolume(v); Save(); }

    public static void ResetToDefaults()
    {
        MasterVolume = DefaultVolume;
        MouseSensitivity = DefaultSensitivity;
        InvertY = false;
        FieldOfView = DefaultFov;
        CameraKick = DefaultKick;
        Save();
    }

    // ---- чистые функции (EditMode-тесты) ----

    public static float ClampVolume(float v) => float.IsNaN(v) ? DefaultVolume : Mathf.Clamp01(v);
    public static float ClampSensitivity(float v) => float.IsNaN(v) ? DefaultSensitivity : Mathf.Clamp(v, MinSensitivity, MaxSensitivity);
    public static float ClampFov(float v) => float.IsNaN(v) ? DefaultFov : Mathf.Clamp(v, MinFov, MaxFov);

    private static void Save()
    {
        PlayerPrefs.SetFloat(Prefix + "volume", MasterVolume);
        PlayerPrefs.SetFloat(Prefix + "sensitivity", MouseSensitivity);
        PlayerPrefs.SetInt(Prefix + "invertY", InvertY ? 1 : 0);
        PlayerPrefs.SetFloat(Prefix + "fov", FieldOfView);
        PlayerPrefs.SetFloat(Prefix + "kick", CameraKick);
        PlayerPrefs.Save();
        Apply();
        Changed?.Invoke();
    }

    private static void Apply()
    {
        AudioListener.volume = MasterVolume;
    }
}
