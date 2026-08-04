using UnityEngine;

public class Stamina : MonoBehaviour
{
    public int Max = 100;
    public int Current;

    private void Awake() => Current = Max;

    public void Use(int amount)     => Current = Mathf.Max(0, Current - amount);
    public void Recover(int amount) => Current = Mathf.Min(Max, Current + amount);
}