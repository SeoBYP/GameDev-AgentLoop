using System.Collections.Generic;
using UnityEngine;

public class ScoreTracker : MonoBehaviour
{
    [SerializeField] private int _window = 16;

    private readonly List<int> _buffer = new List<int>(64);

    public int Total { get; private set; }

    public void Record(int score)
    {
        _buffer.Clear();
        for (int i = 0; i < _window; i++)
        {
            _buffer.Add(score + i);
        }

        int sum = 0;
        for (int i = 0; i < _buffer.Count; i++)
        {
            sum += _buffer[i];
        }

        Total = sum;
    }
}