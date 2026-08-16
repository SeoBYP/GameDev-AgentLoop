using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AgentLoop.Runtime
{
    public class Jumper : MonoBehaviour
    {
        [SerializeField] private float _jumpDuration = 0.5f;

        private InputAction _jumpAction;
        private float _jumpTimer;

        public bool IsJumping { get; private set; }

        public event Action Jumped;
        public event Action Landed;

        private void Awake()
        {
            _jumpDuration = Mathf.Max(0f, _jumpDuration);
            _jumpAction = new InputAction("Jump", InputActionType.Button, "<Keyboard>/space");
        }

        private void OnEnable()
        {
            _jumpAction.performed += OnJumpPerformed;
            _jumpAction.Enable();
        }

        private void OnDisable()
        {
            _jumpAction.performed -= OnJumpPerformed;
            _jumpAction.Disable();
        }

        private void OnDestroy()
        {
            _jumpAction.Dispose();
        }

        private void OnJumpPerformed(InputAction.CallbackContext context)
        {
            TryJump();
        }

        public bool TryJump()
        {
            if (IsJumping)
            {
                return false;
            }

            IsJumping = true;
            _jumpTimer = _jumpDuration;
            Jumped?.Invoke();
            return true;
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        public void Tick(float deltaTime)
        {
            if (!IsJumping)
            {
                return;
            }

            _jumpTimer -= deltaTime;
            if (_jumpTimer <= 0f)
            {
                _jumpTimer = 0f;
                IsJumping = false;
                Landed?.Invoke();
            }
        }
    }
}