using UnityEngine;

namespace InspectionDemo
{
    /// <summary>
    /// 第一人称巡检漫游：WASD 移动、按住鼠标右键转视角、Shift 加速。
    /// 用 CharacterController 而不是刚体，移动没有惯性抖动，也不容易穿墙。
    ///
    /// 视角刻意绑在「按住右键」而不是「鼠标自由看」：
    /// 左键要用来放标记，如果鼠标一动就转头，落点会不断漂移，标注几乎不可能点准。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("移动")]
        public float moveSpeed = 4f;
        public float runMultiplier = 1.8f;

        [Header("视角")]
        public float lookSensitivity = 2.6f;
        public Transform cameraPivot;
        public float minPitch = -80f;
        public float maxPitch = 80f;

        [Header("重力")]
        public float gravity = -9.81f;

        CharacterController _controller;
        float _pitch;
        float _verticalVelocity;

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        void Update()
        {
            HandleLook();
            HandleMove();
        }

        void HandleLook()
        {
            if (!Input.GetMouseButton(1)) return;
            if (cameraPivot == null) return;

            float mx = Input.GetAxis("Mouse X") * lookSensitivity;
            float my = Input.GetAxis("Mouse Y") * lookSensitivity;

            // 水平转向转身体，垂直俯仰只转相机支架，避免身体跟着翻倒
            transform.Rotate(0f, mx, 0f, Space.Self);

            _pitch = Mathf.Clamp(_pitch - my, minPitch, maxPitch);
            cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        void HandleMove()
        {
            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");

            Vector3 direction = transform.right * h + transform.forward * v;
            if (direction.sqrMagnitude > 1f) direction.Normalize();

            if (_controller.isGrounded && _verticalVelocity < 0f)
            {
                // 贴地时给一个小的向下速度，防止在斜坡上「弹跳」
                _verticalVelocity = -2f;
            }
            _verticalVelocity += gravity * Time.deltaTime;

            float speed = moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? runMultiplier : 1f);
            Vector3 motion = direction * speed + Vector3.up * _verticalVelocity;
            _controller.Move(motion * Time.deltaTime);
        }
    }
}
