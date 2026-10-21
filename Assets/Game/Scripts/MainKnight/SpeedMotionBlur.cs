using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Моушен блюр от скорости: пока игрок медленный (ходьба, бег) -- картинка чёткая, на подкате слегка смазывается,
// на скейте и в падении -- сильно. Размытие URP считается по движению камеры, поэтому сила задаётся скоростью
// игрока: стоя на месте можно крутить мышью без смаза.
//
// Вешается на камеру сам (Bootstrap) -- сцену менять не нужно. Чтобы настроить значения в инспекторе,
// добавь компонент на Main Camera вручную: тогда второй не создаётся.
[RequireComponent(typeof(Camera))]
public class SpeedMotionBlur : MonoBehaviour
{
    [Tooltip("Rigidbody игрока; если пусто -- найдётся по KnightMovement")]
    public Rigidbody body;

    [Header("Скорость, м/с")]
    public float startSpeed = 9f;      // ниже -- без размытия (бег 8)
    public float fullSpeed = 25f;      // выше -- максимальное размытие (скейт до 30)

    [Header("Размытие")]
    [Range(0f, 1f)] public float maxIntensity = 0.6f;
    [Range(0f, 0.2f)] public float clamp = 0.06f;           // предел длины смаза (доля экрана)
    public MotionBlurQuality quality = MotionBlurQuality.Medium;
    public float response = 6f;                             // как быстро размытие догоняет скорость, 1/с

    private Volume volume;
    private MotionBlur blur;
    private float current;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Application.isBatchMode) return;   // CI и прогоны без графики
        Camera cam = Camera.main;
        if (cam != null && cam.GetComponent<SpeedMotionBlur>() == null)
            cam.gameObject.AddComponent<SpeedMotionBlur>();
    }

    // Сила размытия для скорости: 0 до startSpeed, линейно до maxIntensity на fullSpeed, дальше не растёт
    public static float IntensityForSpeed(float speed, float startSpeed, float fullSpeed, float maxIntensity)
    {
        if (fullSpeed <= startSpeed) return speed >= startSpeed ? maxIntensity : 0f;
        return Mathf.Clamp01((speed - startSpeed) / (fullSpeed - startSpeed)) * maxIntensity;
    }

    void Awake()
    {
        // Постобработка камеры была выключена -- без неё Volume не действует
        GetComponent<Camera>().GetUniversalAdditionalCameraData().renderPostProcessing = true;

        // Свой профиль в памяти, а не правка ассета: изменения в Play не сохраняются в проект
        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "SpeedMotionBlur (runtime)";
        blur = profile.Add<MotionBlur>(true);
        blur.mode.value = MotionBlurMode.CameraOnly;
        blur.quality.value = quality;
        blur.clamp.value = clamp;
        blur.intensity.value = 0f;

        GameObject go = new GameObject("[SpeedMotionBlur]");
        go.transform.SetParent(transform, false);
        volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 100f;
        volume.sharedProfile = profile;
    }

    void Start()
    {
        if (body == null)
        {
            KnightMovement knight = FindFirstObjectByType<KnightMovement>();
            if (knight != null) body = knight.GetComponent<Rigidbody>();
        }
    }

    void LateUpdate()
    {
        float speed = body != null ? body.linearVelocity.magnitude : 0f;
        float target = IntensityForSpeed(speed, startSpeed, fullSpeed, maxIntensity);
        current = Mathf.MoveTowards(current, target, response * maxIntensity * Time.deltaTime);
        blur.intensity.value = current;
        blur.clamp.value = clamp;
        blur.quality.value = quality;
    }

    void OnDestroy()
    {
        if (volume != null)
        {
            Destroy(volume.sharedProfile);
            Destroy(volume.gameObject);
        }
    }
}
