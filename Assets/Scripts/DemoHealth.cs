using UnityEngine;

public class DemoHealth : MonoBehaviour
{
    [SerializeField] private int _max = 100;

    public int Max => _max;
    public int Current { get; private set; }

    private void Awake() => Current = _max;

    public void TakeDamage(int amount) => Current = Mathf.Max(0, Current - amount);
    public void Heal(int amount)       => Current = Mathf.Min(_max, Current + amount);
}