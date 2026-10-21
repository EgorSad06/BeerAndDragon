using UnityEngine;

// Звуки дробовика, сгенерированные в коде (пока нет записанных): выстрел и перезарядка двустволки.
// Synth* возвращают отсчёты (моно, -1..1) и не трогают аудиосистему -- их проверяют тесты;
// Clip* оборачивают их в AudioClip. Шум -- с фиксированным seed, звук одинаковый при каждом запуске.
public static class WeaponSounds
{
    public const int SampleRate = 44100;
    public const float ShotLength = 0.6f;
    public const float ReloadLength = 0.85f;

    // Моменты событий перезарядки, с: открыть стволы, два патрона, закрыть (перезарядка в сцене -- 0,9 с)
    public static readonly float[] ReloadEvents = { 0.02f, 0.28f, 0.46f, 0.72f };

    // Выстрел: резкий хлопок (шум, ~10 мс) + низкий удар со спадом частоты 110 -> 40 Гц + «хвост» глухого шума
    public static float[] SynthShot(int seed, int sampleRate = SampleRate)
    {
        int n = Mathf.RoundToInt(ShotLength * sampleRate);
        float[] d = new float[n];
        System.Random rng = new System.Random(seed);
        float phase = 0f, lp1 = 0f, lp2 = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / sampleRate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);

            float crack = noise * Mathf.Exp(-t * 90f);

            float freq = Mathf.Lerp(40f, 110f, Mathf.Exp(-t * 18f));
            phase += 2f * Mathf.PI * freq / sampleRate;
            float boom = Mathf.Sin(phase) * Mathf.Exp(-t * 7f);

            // двухполюсный НЧ-фильтр шума, частота среза падает -- «гул» затухает и темнеет
            float a = Mathf.Lerp(0.02f, 0.25f, Mathf.Exp(-t * 10f));
            lp1 += a * (noise - lp1);
            lp2 += a * (lp1 - lp2);
            float body = lp2 * 3f * Mathf.Exp(-t * 5f);

            float attack = Mathf.Clamp01(t / 0.0015f);   // без щелчка в первом отсчёте
            d[i] = attack * (0.55f * crack + 0.9f * boom + 0.8f * body);
        }
        return Saturate(d, 1.8f);
    }

    // Перезарядка двустволки: металлический щелчок, два глухих «тук» патронов, тяжёлый «клац» закрытия
    public static float[] SynthReload(int sampleRate = SampleRate)
    {
        float[] d = new float[Mathf.RoundToInt(ReloadLength * sampleRate)];
        System.Random rng = new System.Random(11);
        Click(d, sampleRate, ReloadEvents[0], 0.5f, 2600f, rng);
        Thud(d, sampleRate, ReloadEvents[1], 0.7f, rng);
        Thud(d, sampleRate, ReloadEvents[2], 0.6f, rng);
        Click(d, sampleRate, ReloadEvents[3], 0.9f, 1700f, rng);
        Thud(d, sampleRate, ReloadEvents[3] + 0.004f, 0.5f, rng);
        return Saturate(d, 1.2f);
    }

    private static AudioClip[] shots;
    private static AudioClip reload;

    // Три варианта выстрела, чтобы очередь не звучала одинаково
    public static AudioClip RandomShot()
    {
        if (shots == null)
        {
            shots = new AudioClip[3];
            for (int i = 0; i < shots.Length; i++) shots[i] = ToClip("ShotgunShot" + i, SynthShot(100 + i));
        }
        return shots[Random.Range(0, shots.Length)];
    }

    public static AudioClip Reload => reload != null ? reload : (reload = ToClip("ShotgunReload", SynthReload()));

    public static AudioClip ToClip(string name, float[] data, int sampleRate = SampleRate)
    {
        AudioClip clip = AudioClip.Create(name, data.Length, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Короткий металлический щелчок: шумовой удар + два затухающих призвука
    private static void Click(float[] d, int sr, float at, float gain, float tone, System.Random rng)
    {
        int start = Mathf.RoundToInt(at * sr), len = Mathf.RoundToInt(0.06f * sr);
        for (int i = 0; i < len && start + i < d.Length; i++)
        {
            float t = (float)i / sr;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-t * 600f);
            float ring = (Mathf.Sin(2f * Mathf.PI * tone * t) + 0.6f * Mathf.Sin(2f * Mathf.PI * tone * 1.47f * t)) * Mathf.Exp(-t * 90f);
            d[start + i] += gain * (0.7f * noise + 0.4f * ring);
        }
    }

    // Глухой удар: низкий тон 170 Гц + приглушённый шум
    private static void Thud(float[] d, int sr, float at, float gain, System.Random rng)
    {
        int start = Mathf.RoundToInt(at * sr), len = Mathf.RoundToInt(0.08f * sr);
        float lp = 0f;
        for (int i = 0; i < len && start + i < d.Length; i++)
        {
            float t = (float)i / sr;
            lp += 0.15f * ((float)(rng.NextDouble() * 2.0 - 1.0) - lp);
            float env = Mathf.Exp(-t * 45f);
            d[start + i] += gain * env * (0.7f * Mathf.Sin(2f * Mathf.PI * 170f * t) + 1.2f * lp);
        }
    }

    // Мягкое ограничение (tanh) и нормировка к пику 0,9: громко, но без цифрового клиппинга
    private static float[] Saturate(float[] d, float drive)
    {
        float peak = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float x = Mathf.Clamp(d[i] * drive, -10f, 10f);
            float e = Mathf.Exp(2f * x);
            d[i] = (e - 1f) / (e + 1f);
            peak = Mathf.Max(peak, Mathf.Abs(d[i]));
        }
        if (peak > 0f)
            for (int i = 0; i < d.Length; i++) d[i] *= 0.9f / peak;
        return d;
    }
}
