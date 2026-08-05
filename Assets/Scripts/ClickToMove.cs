using UnityEngine;

[DisallowMultipleComponent]
public class ClickToMove : MonoBehaviour
{
    [SerializeField] private float _moveSpeed = 5f;
    [SerializeField] private float _arrivalThreshold = 0.05f;
    [SerializeField] private LayerMask _groundLayerMask = ~0;
    [SerializeField] private float _maxRaycastDistance = 100f;

    private Camera _mainCamera;
    private Vector3 _targetPosition;
    private float _arrivalThresholdSqr;
    private bool _hasTarget;

    public event System.Action<Vector3> OnTargetChanged;

    public bool IsMoving => _hasTarget;
    public Vector3 TargetPosition => _targetPosition;

    private void Awake()
    {
        _mainCamera = Camera.main;
        _targetPosition = transform.position;
        _arrivalThresholdSqr = _arrivalThreshold * _arrivalThreshold;
    }

    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            TryIssueMoveCommand();
        }

        Tick(Time.deltaTime);
    }

    private void TryIssueMoveCommand()
    {
        if (_mainCamera == null)
        {
            return;
        }

        Ray ray = _mainCamera.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, _maxRaycastDistance, _groundLayerMask))
        {
            SetTargetPosition(hit.point);
        }
    }

    public bool SetTargetPosition(Vector3 worldPosition)
    {
        if (float.IsNaN(worldPosition.x) || float.IsNaN(worldPosition.y) || float.IsNaN(worldPosition.z) ||
            float.IsInfinity(worldPosition.x) || float.IsInfinity(worldPosition.y) || float.IsInfinity(worldPosition.z))
        {
            return false;
        }

        _targetPosition = worldPosition;
        _hasTarget = true;
        OnTargetChanged?.Invoke(_targetPosition);
        return true;
    }

    public void Tick(float deltaTime)
    {
        if (!_hasTarget)
        {
            return;
        }

        Vector3 currentPosition = transform.position;
        Vector3 newPosition = Vector3.MoveTowards(currentPosition, _targetPosition, _moveSpeed * deltaTime);
        transform.position = newPosition;

        if ((newPosition - _targetPosition).sqrMagnitude <= _arrivalThresholdSqr)
        {
            transform.position = _targetPosition;
            _hasTarget = false;
        }
    }
}