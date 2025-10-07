using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.OnScreen;

namespace _GAME.Scripts.UI.Base
{
    [RequireComponent(typeof(RectTransform))]
    public class OnScreenDragToStick : OnScreenControl,
        IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Header("Control Mode")]
        [Tooltip("Position: Gửi vị trí stick [-1,1] (movement)\nDelta: Gửi delta mượt (camera)")]
        [SerializeField] private ControlMode controlMode = ControlMode.Position;

        [Header("Map to stick")]
        [Tooltip("Position mode: <Gamepad>/leftStick\nDelta mode: <Mouse>/delta hoặc <Gamepad>/rightStick")]
        [SerializeField] private string stickControlPath = "<Gamepad>/leftStick";

        [Header("Tuning (pixels)")]
        [SerializeField] private float deadZone = 15f;
        [SerializeField] private float maxRadius = 100f;

        [Header("Position Mode Smoothing")]
        [Tooltip("Làm mượt vector đầu ra của Position mode (giây)")]
        [SerializeField] private float positionSmoothing = 0.12f;
        [Tooltip("Độ đàn hồi khi vượt bán kính: 0 = tâm cố định, 1 = bám sát ngón")]
        [Range(0f, 1f)] [SerializeField] private float followElasticity = 0.25f; // GIẢM để chính xác hơn
        [Tooltip("Giảm tốc độ di chuyển tổng thể (0.5 = 50% tốc độ)")]
        [Range(0.1f, 1f)] [SerializeField] private float globalSpeedMultiplier = 0.4f; // GIẢM để kiểm soát tốt hơn

        [Header("Precision Control")]
        [Tooltip("Bật chế độ chính xác: giảm tốc khi di chuyển chậm")]
        [SerializeField] private bool enablePrecisionMode = true;
        [Tooltip("Ngưỡng để kích hoạt precision (% của maxRadius, 0-1)")]
        [Range(0.3f, 0.8f)] [SerializeField] private float precisionThreshold = 0.6f;
        [Tooltip("Giảm tốc trong vùng precision (0.3 = còn 30%)")]
        [Range(0.2f, 0.8f)] [SerializeField] private float precisionSlowdown = 0.5f;
        [Tooltip("Response curve: 1 = tuyến tính, >1 = edge nhanh hơn, <1 = center nhạy hơn")]
        [Range(0.5f, 2f)] [SerializeField] private float responseCurve = 1.2f;

        [Header("Delta Mode Settings")]
        [Tooltip("Độ nhạy Delta mode (camera)")]
        [SerializeField] private float deltaSensitivity = 0.8f;
        [Tooltip("Làm mượt delta để tránh giật (giây)")]
        [SerializeField] private float deltaSmoothing = 0.3f;
        [Tooltip("Giới hạn delta mỗi frame để chống spike (px/frame)")]
        [SerializeField] private float maxDeltaPerFrame = 25f;
        
        [Header("Output Divisor")]
        [Tooltip("Chia giá trị output trục X (VD: 3 = giảm 1/3, 10 = giảm 1/10)")]
        [SerializeField] private float outputDivisorX = 4f; // TĂNG để chậm hơn
        [Tooltip("Chia giá trị output trục Y (VD: 3 = giảm 1/3, 10 = giảm 1/10)")]
        [SerializeField] private float outputDivisorY = 4f;

        [Header("Aim Settings")]
        [Tooltip("Trạng thái aim (bật/tắt từ input khác)")]
        [SerializeField] private bool isAiming = false;
        [Tooltip("Giảm tốc khi aim (0.3 = còn 30%)")]
        [Range(0.05f, 1f)] [SerializeField] private float aimSlowdown = 0.35f;
        [Tooltip("Thu nhỏ maxRadius khi aim (0.5 = còn 50%)")]
        [Range(0.2f, 1f)] [SerializeField] private float aimRadiusScale = 0.6f;

        [Header("Drag Area (optional)")]
        [Tooltip("Giới hạn vùng kéo; ra ngoài sẽ thoát nếu exitWhenOut = true")]
        [SerializeField] private RectTransform dragArea;
        [SerializeField] private bool exitWhenOut = true;

        // State
        private RectTransform rt;
        private Camera uiCam;
        private int activePointer = -1;
        private Vector2 pressPos;
        private Vector2 lastFramePos;

        // Smoothing state
        private Vector2 smoothedDelta;
        private Vector2 deltaVelocity;
        private Vector2 smoothedPosOut;
        private Vector2 posVelocity;

        // Precision tracking
        private Vector2 lastRawDelta;
        private float dragSpeed;

        protected override string controlPathInternal
        {
            get => stickControlPath;
            set => stickControlPath = value;
        }

        public enum ControlMode { Position, Delta }

        private float EffectiveMaxRadius => isAiming ? (maxRadius * aimRadiusScale) : maxRadius;
        private float EffectiveDeltaSensitivity => isAiming ? (deltaSensitivity * aimSlowdown) : deltaSensitivity;

        void Awake()
        {
            rt = GetComponent<RectTransform>();
            var canvas = GetComponentInParent<Canvas>();
            uiCam = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        }

        void Update()
        {
            // Delta mode: tự hãm về 0 khi không drag
            if (controlMode == ControlMode.Delta && activePointer == -1)
            {
                smoothedDelta = Vector2.SmoothDamp(smoothedDelta, Vector2.zero, ref deltaVelocity, deltaSmoothing);
                if (smoothedDelta.sqrMagnitude < 0.0001f)
                {
                    smoothedDelta = Vector2.zero;
                    deltaVelocity = Vector2.zero;
                }
                SendValueToControl(DivideOutput(smoothedDelta));
            }

            // Position mode: thả tay thì hãm về 0 mượt
            if (controlMode == ControlMode.Position && activePointer == -1 && smoothedPosOut != Vector2.zero)
            {
                smoothedPosOut = Vector2.SmoothDamp(smoothedPosOut, Vector2.zero, ref posVelocity, positionSmoothing);
                if (smoothedPosOut.sqrMagnitude < 0.0001f)
                {
                    smoothedPosOut = Vector2.zero;
                    posVelocity = Vector2.zero;
                }
                SendValueToControl(DivideOutput(smoothedPosOut));
            }

            // Cập nhật tốc độ vuốt cho precision mode
            if (activePointer != -1 && controlMode == ControlMode.Position)
            {
                dragSpeed = Mathf.Lerp(dragSpeed, lastRawDelta.magnitude / Time.deltaTime, 0.2f);
            }
            else
            {
                dragSpeed = 0f;
            }
        }

        public void SetAiming(bool aiming) => isAiming = aiming;

        public void OnPointerDown(PointerEventData e)
        {
            if (activePointer != -1) return;
            if (!RectTransformUtility.RectangleContainsScreenPoint(rt, e.position, uiCam)) return;

            activePointer = e.pointerId;
            pressPos = e.position;
            lastFramePos = e.position;

            smoothedDelta = Vector2.zero;
            deltaVelocity = Vector2.zero;
            smoothedPosOut = Vector2.zero;
            posVelocity = Vector2.zero;
            lastRawDelta = Vector2.zero;
            dragSpeed = 0f;

            SendValueToControl(Vector2.zero);
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (e.pointerId != activePointer) return;
            lastFramePos = e.position;

            if (exitWhenOut && !InsideDragArea(e.position))
            {
                ForcePointerUp();
                return;
            }
            UpdateStick(e.position);
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != activePointer) return;

            if (exitWhenOut && !InsideDragArea(e.position))
            {
                ForcePointerUp();
                return;
            }

            UpdateStick(e.position);
        }

        public void OnEndDrag(PointerEventData e) { }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != activePointer) return;
            activePointer = -1;
        }

        private void UpdateStick(Vector2 screenPos)
        {
            if (controlMode == ControlMode.Position) UpdatePositionMode(screenPos);
            else UpdateDeltaMode(screenPos);
        }

        /// <summary>
        /// Position mode với precision control và response curve
        /// </summary>
        private void UpdatePositionMode(Vector2 screenPos)
        {
            Vector2 rawDelta = screenPos - pressPos;
            lastRawDelta = rawDelta;

            // Adaptive center với elasticity GIẢM để ổn định hơn
            float maxR = EffectiveMaxRadius;
            float dist = rawDelta.magnitude;
            if (dist > maxR)
            {
                float overflow = dist - maxR;
                pressPos += rawDelta.normalized * (overflow * followElasticity);
                rawDelta = screenPos - pressPos;
            }

            // Deadzone
            if (rawDelta.magnitude < deadZone)
            {
                smoothedPosOut = Vector2.SmoothDamp(smoothedPosOut, Vector2.zero, ref posVelocity, positionSmoothing);
                SendValueToControl(DivideOutput(smoothedPosOut));
                return;
            }

            // Normalize về [-1, 1]
            float normalizedDist = Mathf.Clamp01(rawDelta.magnitude / maxR);
            
            // Áp dụng response curve (làm mượt response)
            float curvedDist = Mathf.Pow(normalizedDist, responseCurve);
            
            Vector2 target = rawDelta.normalized * curvedDist;

            // PRECISION MODE: Giảm tốc khi di chuyển chậm/nhỏ
            if (enablePrecisionMode && normalizedDist < precisionThreshold)
            {
                float precisionFactor = Mathf.Lerp(precisionSlowdown, 1f, normalizedDist / precisionThreshold);
                target *= precisionFactor;
            }

            // Global speed multiplier
            target *= globalSpeedMultiplier;

            // Giảm tốc khi aim
            if (isAiming) target *= aimSlowdown;

            // Smoothing output
            smoothedPosOut = Vector2.SmoothDamp(smoothedPosOut, target, ref posVelocity, positionSmoothing);
            SendValueToControl(DivideOutput(smoothedPosOut));
        }

        /// <summary>
        /// Delta mode với precision control
        /// </summary>
        private void UpdateDeltaMode(Vector2 screenPos)
        {
            Vector2 frameDelta = screenPos - lastFramePos;
            lastFramePos = screenPos;

            // Deadzone theo dịch chuyển tức thời
            if (frameDelta.magnitude < deadZone * 0.5f)
            {
                smoothedDelta = Vector2.SmoothDamp(smoothedDelta, Vector2.zero, ref deltaVelocity, deltaSmoothing);
                SendValueToControl(DivideOutput(smoothedDelta));
                return;
            }

            // Chống spike
            frameDelta = Vector2.ClampMagnitude(frameDelta, maxDeltaPerFrame);

            // Sensitivity
            frameDelta *= EffectiveDeltaSensitivity;

            // Global multiplier
            frameDelta *= globalSpeedMultiplier;

            // Smoothing
            smoothedDelta = Vector2.SmoothDamp(smoothedDelta, frameDelta, ref deltaVelocity, deltaSmoothing);
            SendValueToControl(DivideOutput(smoothedDelta));
        }

        /// <summary>
        /// Chia output theo từng trục riêng biệt
        /// </summary>
        private Vector2 DivideOutput(Vector2 value)
        {
            return new Vector2(
                value.x / Mathf.Max(1f, outputDivisorX),
                value.y / Mathf.Max(1f, outputDivisorY)
            );
        }

        private bool InsideDragArea(Vector2 screenPos)
        {
            var target = dragArea ? dragArea : rt;
            return RectTransformUtility.RectangleContainsScreenPoint(target, screenPos, uiCam);
        }

        private void ForcePointerUp()
        {
            activePointer = -1; 

            smoothedDelta = Vector2.zero;
            deltaVelocity = Vector2.zero;
            smoothedPosOut = Vector2.zero;
            posVelocity = Vector2.zero;
            SendValueToControl(Vector2.zero);
        }
    }
}