using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Скейт, который лежит в мире. Подойди и нажми E (логика -- в SkaterController на игроке).
// Пока на нём не едут -- обычный физический объект (можно пнуть, выстрелить в него).
// Анимации трюков проигрываются через Playables прямо из клипов FBX, без AnimatorController.
public class Skateboard : MonoBehaviour
{
    public Transform visual;                // пивот модели: +Z -- нос, низ колёс в y=0
    public Animator animator;
    public float deckHeight = 0.15f;        // высота верха деки над землёй (на неё ставим ноги)
    public float deckHalfLength = 0.5f;     // половина длины доски

    [Header("Клипы из FBX (можно оставить пустыми -- трюки будут процедурными)")]
    public AnimationClip idleClip;
    public AnimationClip ollieClip;
    public AnimationClip kickflipClip;
    public AnimationClip shuvitClip;

    private static readonly List<Skateboard> all = new List<Skateboard>();
    public static IReadOnlyList<Skateboard> All => all;

    public bool IsRidden { get; private set; }
    public Rigidbody Body { get; private set; }

    private Collider[] colliders;
    private PlayableGraph graph;

    void Awake()
    {
        Body = GetComponent<Rigidbody>();
        colliders = GetComponentsInChildren<Collider>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator != null) animator.applyRootMotion = false;
    }

    void OnEnable() => all.Add(this);
    void OnDisable() => all.Remove(this);

    void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
    }

    public void SetRidden(bool ridden)
    {
        IsRidden = ridden;
        if (Body != null)
        {
            if (!ridden) Body.isKinematic = false;
            else
            {
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
                Body.isKinematic = true;
            }
        }
        foreach (Collider c in colliders) c.enabled = !ridden;
        PlayClip(idleClip);
    }

    // Отлетает после бэйла
    public void Kick(Vector3 velocity)
    {
        if (Body == null || Body.isKinematic) return;
        Body.linearVelocity = velocity;
        Body.angularVelocity = Random.insideUnitSphere * 8f;
    }

    // Возвращает длительность клипа (0, если клипа нет)
    public float PlayClip(AnimationClip clip)
    {
        if (animator == null || clip == null) return 0f;
        if (graph.IsValid()) graph.Destroy();
        AnimationPlayableUtilities.PlayClip(animator, clip, out graph);
        return clip.length;
    }
}
