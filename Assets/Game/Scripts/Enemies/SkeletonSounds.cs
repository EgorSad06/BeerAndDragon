using System.Collections;
using UnityEngine;

// Звуки скелета (клипы -- Assets/Game/Assets/Sounds/Skeleton, назначает SkeletonCreepBuilder):
//  - живой скелет раз в rattleInterval секунд тихо хрустит костями (boneCracks, со случайной высотой);
//  - удар оружием -- свист (swings): начинается за swingLead до кадра удара (SkeletonCreep.AttackStarted);
//  - выстрел лучника -- щелчок тетивы (bowShots, SkeletonCreep.ArrowReleased);
//  - смерть -- громкий хруст костей.
// Звук объёмный (3D): AudioSource на скелете, слышно в пределах maxDistance.
[RequireComponent(typeof(SkeletonCreep))]
public class SkeletonSounds : MonoBehaviour
{
    public AudioClip[] boneCracks = new AudioClip[0];
    public AudioClip[] swings = new AudioClip[0];
    public AudioClip[] bowShots = new AudioClip[0];

    [Tooltip("Пауза между тихими хрустами (с): от и до")]
    public Vector2 rattleInterval = new Vector2(5f, 14f);
    [Range(0f, 1f)] public float rattleVolume = 0.25f;
    [Range(0f, 1f)] public float swingVolume = 0.8f;
    [Range(0f, 1f)] public float bowVolume = 0.7f;
    [Range(0f, 1f)] public float deathVolume = 0.9f;
    [Tooltip("За сколько секунд до кадра удара начинается свист (громче всего он примерно через 0.25 с)")]
    public float swingLead = 0.3f;
    public float maxDistance = 25f;

    private SkeletonCreep creep;
    private Damageable health;
    private AudioSource source;
    private float nextRattle;

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
        if (hitTime >= 0f && swings.Length > 0) StartCoroutine(SwingAfter(Mathf.Max(0f, hitTime - swingLead)));
    }

    private IEnumerator SwingAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (!creep.IsDead) Play(swings, swingVolume, Random.Range(0.9f, 1.1f));
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
