public class CooldownTimer : UnityEngine.MonoBehaviour
{
    public float Duration = 2f;

    private float _remaining;

    public float Remaining => UnityEngine.Mathf.Max(0f, _remaining);

    public bool Trigger()
    {
        if (_remaining > 0f)
        {
            return false;
        }

        _remaining = Duration;
        return true;
    }

    public void Tick(float dt)
    {
        if (_remaining <= 0f)
        {
            return;
        }

        _remaining -= dt;
        if (_remaining < 0f)
        {
            _remaining = 0f;
        }
    }
}