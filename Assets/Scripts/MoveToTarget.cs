using System;
using UnityEngine;

[DisallowMultipleComponent]
public class MoveToTarget : MonoBehaviour
{
    private const float ArrivalThreshold = 0.001f;

    [SerializeField] private float _moveSpeed = 5f;

    private Vector3 _targetPosition;
    private bool _isMoving;

    public event Action OnArrived;

    public bool IsMoving => _isMoving;
    public Vector3 TargetPosition => _targetPosition;
    public float MoveSpeed => _moveSpeed;

    public bool SetTarget(Vector3 targetPosition)
    {
        _targetPosition = targetPosition;
        _isMoving = (transform.position - _targetPosition).sqrMagnitude > ArrivalThreshold * ArrivalThreshold;
        return _isMoving;
    }

    private void OnValidate()
    {
        _moveSpeed = Mathf.Max(0f, _moveSpeed);
    }

    private void Update()
    {
        if (!_isMoving)
        {
            return;
        }

        Tick(Time.deltaTime);
    }

    public void Tick(float deltaTime)
    {
        if (!_isMoving)
        {
            return;
        }

        Vector3 currentPosition = transform.position;
        Vector3 newPosition = Vector3.MoveTowards(currentPosition, _targetPosition, _moveSpeed * deltaTime);
        transform.position = newPosition;

        if ((newPosition - _targetPosition).sqrMagnitude <= ArrivalThreshold * ArrivalThreshold)
        {
            _isMoving = false;
            OnArrived?.Invoke();
        }
    }
}