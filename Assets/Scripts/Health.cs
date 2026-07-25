using System;
using UnityEngine;

public class Health : MonoBehaviour
{
    [SerializeField] private int maxHealth = 100;
    [SerializeField] private bool destroyOnDeath = false;

    public int MaxHealth => maxHealth;
    public int CurrentHealth { get; private set; }
    public bool IsDead => CurrentHealth <= 0;

    public event Action<int, int> OnHealthChanged; // current, max
    public event Action OnDamaged;
    public event Action OnHealed;
    public event Action OnDied;

    private void Awake()
    {
        CurrentHealth = maxHealth;
    }

    public void TakeDamage(int amount)
    {
        if (amount <= 0 || IsDead)
        {
            return;
        }

        CurrentHealth = Mathf.Max(CurrentHealth - amount, 0);
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
        OnDamaged?.Invoke();

        if (CurrentHealth == 0)
        {
            Die();
        }
    }

    public void Heal(int amount)
    {
        if (amount <= 0 || IsDead)
        {
            return;
        }

        CurrentHealth = Mathf.Min(CurrentHealth + amount, maxHealth);
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
        OnHealed?.Invoke();
    }

    public void SetMaxHealth(int newMaxHealth, bool refill = false)
    {
        maxHealth = Mathf.Max(newMaxHealth, 1);
        CurrentHealth = refill ? maxHealth : Mathf.Min(CurrentHealth, maxHealth);
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
    }

    public void ResetHealth()
    {
        CurrentHealth = maxHealth;
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
    }

    private void Die()
    {
        OnDied?.Invoke();

        if (destroyOnDeath)
        {
            Destroy(gameObject);
        }
    }
}