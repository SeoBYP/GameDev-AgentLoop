using System;

namespace Roguelike.Core
{
    public class Actor
    {
        public event Action<Actor> Died;

        private readonly string _name;
        private int _x;
        private int _y;
        private readonly int _maxHealth;
        private int _currentHealth;
        private bool _hasDied;

        public Actor(string name, int x, int y, int maxHealth)
        {
            if (maxHealth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxHealth), maxHealth, "Max health must be greater than zero.");
            }

            _name = name;
            _x = x;
            _y = y;
            _maxHealth = maxHealth;
            _currentHealth = maxHealth;
        }

        public string Name => _name;
        public int X => _x;
        public int Y => _y;
        public int MaxHealth => _maxHealth;
        public int CurrentHealth => _currentHealth;
        public bool IsAlive => _currentHealth > 0;

        public void TakeDamage(int amount)
        {
            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), amount, "Damage amount cannot be negative.");
            }

            _currentHealth = Math.Clamp(_currentHealth - amount, 0, _maxHealth);

            if (!IsAlive && !_hasDied)
            {
                _hasDied = true;
                Died?.Invoke(this);
            }
        }

        public void Heal(int amount)
        {
            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), amount, "Heal amount cannot be negative.");
            }

            _currentHealth = Math.Clamp(_currentHealth + amount, 0, _maxHealth);
        }

        public void MoveTo(int x, int y)
        {
            _x = x;
            _y = y;
        }
    }
}