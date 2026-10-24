using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Звуки скелета (клипы -- Assets/Game/Assets/Sounds/Skeleton, назначает SkeletonCreepBuilder; источники -- CREDITS.txt там же):
//  - живой скелет раз в rattleInterval секунд тихо хрустит костями (boneCracks, со случайной высотой);
//  - удар оружием -- свист (swings): запускается так, чтобы самое громкое место записи пришлось на кадр удара
//    (SkeletonCreep.AttackStarted);
//  - лучник натягивает тетиву -- скрип (bowDraws, событие анимации OnArrowNock), выстрел -- тетива (bowShots,
//    SkeletonCreep.ArrowReleased);
//  - смерть -- громкий хруст костей.
// Звук объёмный (3D): AudioSource на скелете, слышно в пределах maxDistance.
[RequireComponent(typeof(SkeletonCreep))]
public class SkeletonSounds : MonoBehaviour
{
    public AudioClip[] boneCracks = new AudioClip[0];
    public AudioClip[] swings = new AudioClip[0];
    public AudioClip[] bowShots = new AudioClip[0];
    public AudioClip[] bowDraws = new AudioClip[0];

    [Tooltip("Пауза между тихими хрустами (с): от и до")]
    public Vector2 rattleInterval = new Vector2(5f, 14f);
    [Range(0f, 1f)] public float rattleVolume = 0.25f;
    [Range(0f, 1f)] public float swingVolume = 0.8f;
    [Range(0f, 1f)] public float bowVolume = 0.7f;
    [Range(0f, 1f)] public float drawVolume = 0.6f;
    [Range(0f, 1f)] public float deathVolume = 0.9f;
    public float maxDistance = 25f;

    private SkeletonCreep creep;
    private Damageable health;
    private AudioSource source;
    private float nextRattle;

    // где в клипе самое громкое место (с) -- на него ставится кадр удара
    private static readonly Dictionary<AudioClip, float> Peaks = new Dictionary<AudioClip, float>();

    void Awake()
    {
        creep = GetComponent<SkeletonCreep>();
        health = GetComponent<Damageable>();
        source = GetComponent<AudioSource>();
        if (source == null) source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 2f;
        source.maxDistance = maxDistance;
        source.dopplerLevel = 0f;
        ScheduleRattle();
    }

    void OnEnable()
    {
        creep.AttackStarted += OnAttackStarted;
        creep.ArrowReleased += OnArrowReleased;
        if (health != null) health.Died += OnDied;
    }

    void OnDisable()
    {
        creep.AttackStarted -= OnAttackStarted;
        creep.ArrowReleased -= OnArrowReleased;
        if (health != null) health.Died -= OnDied;
    }

    void Update()
    {
        if (creep.IsDead || Time.time < nextRattle) return;
        Play(boneCracks, rattleVolume, Random.Range(0.85f, 1.2f));
        ScheduleRattle();
    }

    private void ScheduleRattle()
    {
        nextRattle = Time.time + Random.Range(rattleInterval.x, rattleInterval.y);
    }

    // hitTime -- через сколько секунд удар достанет цель; меньше 0 -- в атаке нет удара (лучник)
    private void OnAttackStarted(float hitTime)
    {
        if (hitTime < 0f || swings.Length == 0) return;
        AudioClip clip = swings[Random.Range(0, swings.Length)];
        if (clip != null) StartCoroutine(SwingAfter(clip, Mathf.Max(0f, hitTime - Peak(clip))));
    }

    private IEnumerator SwingAfter(AudioClip clip, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (creep.IsDead) yield break;
        source.pitch = Random.Range(0.9f, 1.1f);
        source.PlayOneShot(clip, swingVolume);
    }

    // Вызывается событием анимации натягивания (archer_draw): стрела на тетиве, тянем
    private void OnArrowNock()
    {
        if (!creep.IsDead) Play(bowDraws, drawVolume, Random.Range(0.95f, 1.05f));
    }

    private static float Peak(AudioClip clip)
    {
        if (Peaks.TryGetValue(clip, out float t)) return t;
        t = clip.length * 0.5f;
        var data = new float[clip.samples * clip.channels];
        if (clip.loadState == AudioDataLoadState.Loaded && clip.GetData(data, 0))
        {
            int best = 0;
            for (int i = 1; i < data.Length; i++)
                if (Mathf.Abs(data[i]) > Mathf.Abs(data[best])) best = i;
            t = best / clip.channels / (float)clip.frequency;
        }
        Peaks[clip] = t;
        return t;
    }

    private void OnArrowReleased()
    {
        Play(bowShots, bowVolume, Random.Range(0.95f, 1.05f));
    }

    private void OnDied()
    {
        StopAllCoroutines();
        Play(boneCracks, deathVolume, Random.Range(0.9f, 1.05f));
    }

    private void Play(AudioClip[] clips, float volume, float pitch)
    {
        if (clips.Length == 0) return;
        AudioClip clip = clips[Random.Range(0, clips.Length)];
        if (clip == null) return;
        source.pitch = pitch;
        source.PlayOneShot(clip, volume);
    }
}
