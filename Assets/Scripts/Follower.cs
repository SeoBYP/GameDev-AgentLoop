using UnityEngine;

public class Follower : MonoBehaviour
{
    [SerializeField] private float _speed = 3f;
    [SerializeField] private Transform _target;

    private Rigidbody _body;

    public bool HasTarget => _target != null;

    private void Awake() => _body = GetComponent<Rigidbody>();

    private void Update() => Tick(Time.deltaTime);

    public void Tick(float deltaTime)
    {
        if (_target == null)
        {
            return;
        }

        transform.position = Vector3.MoveTowards(transform.position, _target.position, _speed * deltaTime);
    }
}