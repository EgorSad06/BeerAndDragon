using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Здоровье игрока. Вешается на корень Player. Смерть -- перезапуск сцены через restartDelay.
public class PlayerHealth : MonoBehaviour, IDamageable
{
    public float maxHealth = 100f;
    public float restartDelay = 3f;

    public float Health { get; private set; }
    public bool IsDead { get; private set; }
    public bool CanHeal => !IsDead && Health + pendingHeal < maxHealth;

    public event Action<float> Damaged;   // сколько сняли
    public event Action Died;

    private float pendingHeal;            // сколько ещё "дохиливается" по времени

    void Awake()
    {
        Health = maxHealth;
    }

    public void TakeDamage(float amount)
    {
        if (IsDead || amount <= 0f) return;

        Health = Mathf.Max(0f, Health - amount);
        Damaged?.Invoke(amount);

        if (Health <= 0f)
            Die();
    }

    public void Heal(float amount)
    {
        if (IsDead) return;
        Health = Mathf.Min(maxHealth, Health + amount);
    }

    public void HealOverTime(float amount, float duration)
    {
        if (duration <= 0f) { Heal(amount); return; }
        StartCoroutine(HealRoutine(amount, duration));
    }

    private IEnumerator HealRoutine(float amount, float duration)
    {
        pendingHeal += amount;
        float healed = 0f;
        float t = 0f;
        while (t < duration && !IsDead)
        {
            t += Time.deltaTime;
            float target = amount * Mathf.Clamp01(t / duration);
            Heal(target - healed);
            pendingHeal -= target - healed;
            healed = target;
            yield return null;
        }
        pendingHeal = Mathf.Max(0f, pendingHeal - (amount - healed));
    }

    private void Die()
    {
        IsDead = true;
        GameLog.Info("Player", "Игрок погиб, перезапуск сцены через " + restartDelay + " с");
        PlayerInputLock.Locked = true;

        KnightMovment movement = GetComponent<KnightMovment>();
        if (movement != null) movement.enabled = false;

        Died?.Invoke();
        Invoke(nameof(Restart), restartDelay);
    }

    private void Restart()
    {
        PlayerInputLock.Locked = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
