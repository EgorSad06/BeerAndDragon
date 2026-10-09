using UnityEngine;

// Звуки очков на скейте (Jet Set Radio):
//  - каждый трюк в комбо -- "спрей"; звуки идут лесенкой 1-2-3-4 и с каждым трюком выше по тону;
//  - 4-й трюк подряд -- финальный звук (success), дальше спреи в случайном порядке, без повторов;
//  - приземлил комбо -- спрей пониже (чем больше очков, тем громче);
//  - бэйл -- спрей, замедленный до грустного "уаа".
// Вешается на Player (Setup Skate Sounds сам подставит клипы из Assets/Game/Assets/Sounds/JetSet).
public class SkateSounds : MonoBehaviour
{
    public SkaterController skater;         // найдётся сам
    public AudioSource source;              // создастся сам

    [Header("Clips")]
    public AudioClip[] trickClips;          // spray-1..4
    public AudioClip comboFinisher;         // success
    public int finisherAt = 4;

    [Header("Variety")]
    [Range(0f, 1f)] public float volume = 0.8f;
    public Vector2 pitchJitter = new Vector2(0.94f, 1.06f);
    public float pitchStepPerTrick = 0.05f; // каждый следующий трюк в комбо чуть выше
    public float maxPitchStep = 0.3f;
    public Vector2 landPitch = new Vector2(0.8f, 0.92f);
    public float bailPitch = 0.55f;

    private int lastClip = -1;
    private bool finisherPlayed;

    void Awake()
    {
        if (skater == null) skater = GetComponent<SkaterController>();
        if (source == null)
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
        }
    }

    void OnEnable()
    {
        if (skater == null) return;
        skater.TrickAdded += OnTrick;
        skater.ComboLanded += OnLanded;
        skater.BailedEvent += OnBail;
    }

    void OnDisable()
    {
        if (skater == null) return;
        skater.TrickAdded -= OnTrick;
        skater.ComboLanded -= OnLanded;
        skater.BailedEvent -= OnBail;
    }

    private void OnTrick(string trickName)
    {
        int count = skater.ComboTricks.Count;

        if (count >= finisherAt && !finisherPlayed && comboFinisher != null)
        {
            finisherPlayed = true;
            Play(comboFinisher, 1f, 1f);
            return;
        }

        int index = count <= finisherAt ? (count - 1) : RandomIndex();
        float pitch = Random.Range(pitchJitter.x, pitchJitter.y) + Mathf.Min((count - 1) * pitchStepPerTrick, maxPitchStep);
        PlayTrickClip(index, pitch, volume);
    }

    private void OnLanded(int value)
    {
        finisherPlayed = false;
        // Чем жирнее комбо -- тем громче "приземление"
        float loud = Mathf.Lerp(0.5f, 1f, Mathf.InverseLerp(100f, 5000f, value));
        PlayTrickClip(RandomIndex(), Random.Range(landPitch.x, landPitch.y), volume * loud);
    }

    private void OnBail(string reason)
    {
        finisherPlayed = false;
        PlayTrickClip(RandomIndex(), bailPitch * Random.Range(0.95f, 1.05f), volume * 0.8f);
    }

    // Случайный клип, но не тот же, что звучал последним
    private int RandomIndex()
    {
        if (trickClips == null || trickClips.Length == 0) return -1;
        if (trickClips.Length == 1) return 0;
        if (lastClip < 0) return Random.Range(0, trickClips.Length);
        int i = Random.Range(0, trickClips.Length - 1);
        if (i >= lastClip) i++;
        return Mathf.Clamp(i, 0, trickClips.Length - 1);
    }

    private void PlayTrickClip(int index, float pitch, float vol)
    {
        if (trickClips == null || trickClips.Length == 0) return;
        index = ((index % trickClips.Length) + trickClips.Length) % trickClips.Length;
        lastClip = index;
        Play(trickClips[index], pitch, vol);
    }

    // PlayOneShot берёт pitch источника, поэтому на каждый звук -- свой временный источник
    private void Play(AudioClip clip, float pitch, float vol)
    {
        if (clip == null || source == null) return;
        if (Mathf.Approximately(pitch, 1f))
        {
            source.PlayOneShot(clip, vol);
            return;
        }
        GameObject go = new GameObject("SkateSfx");
        go.transform.SetParent(transform, false);
        AudioSource a = go.AddComponent<AudioSource>();
        a.spatialBlend = 0f;
        a.outputAudioMixerGroup = source.outputAudioMixerGroup;
        a.pitch = pitch;
        a.volume = vol;
        a.clip = clip;
        a.Play();
        Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.1f);
    }
}
