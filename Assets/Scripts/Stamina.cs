using UnityEngine;

public class Stamina : MonoBehaviour
{
    [SerializeField] private int _max = 100;

    public int Max => _max;
    public int Current { get; private set; }

    private void Awake() => Current = _max;

    public void Use(int amount)     => Current = Mathf.Max(0, Current - amount);
    public void Recover(int amount) => Current = Mathf.Min(Max, Current + amount);
}