using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class SimpleFPRigidbodyController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5f;
    public float sprintMultiplier = 1.7f;
    public string horizontalAxis = "Horizontal";
    public string verticalAxis = "Vertical";

    [Header("Look")]
    public Camera playerCamera;
    public float mouseLookSensitivity = 2.0f;      // multiplier for mouse delta (degrees per mouse unit)
    public float controllerLookSensitivity = 180f; // degrees per second at full stick
    public bool invertY = false;
    public float pitchMin = -89f;
    public float pitchMax = 89f;

    [Header("Controller (legacy Input Manager)")]
    public string rightStickHorizontalAxis = "RightStickHorizontal";
    public string rightStickVerticalAxis = "RightStickVertical";

    [Header("Look smoothing & deadzone")]
    [Range(0f, 0.5f)] public float controllerDeadzone = 0.18f;   // ignore small drift
    [Range(0f, 30f)] public float controllerLookSmoothing = 8f;  // larger = smoother/slower
    [Range(0f, 30f)] public float mouseLookSmoothing = 0f;       // 0 = immediate mouse

    [Header("Debug")]
    public bool debugRightStickValues = false;

    [Header("Other")]
    public bool lockCursor = true;

    // internals
    Rigidbody rb;
    float currentYaw = 0f;
    float currentPitch = 0f;
    Vector3 velocityInput = Vector3.zero;

    // smoothing state for controller (raw -1..1)
    Vector2 smoothedControllerStick = Vector2.zero;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null) Debug.LogError("[SimpleFP] Rigidbody missing.");

        if (playerCamera == null) Debug.LogWarning("[SimpleFP] playerCamera not assigned. Assign camera for pitch control.");

        currentYaw = transform.eulerAngles.y;
        if (playerCamera) currentPitch = playerCamera.transform.localEulerAngles.x;
        if (currentPitch > 180f) currentPitch -= 360f;
    }

    void Start()
    {
        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void Update()
    {
        // ===== Movement (legacy Input Manager) =====
        float h = Input.GetAxis(horizontalAxis);
        float v = Input.GetAxis(verticalAxis);
        float speedMultiplier = Input.GetKey(KeyCode.LeftShift) ? sprintMultiplier : 1f;

        Vector3 inputLocal = new Vector3(h, 0f, v);
        inputLocal = Vector3.ClampMagnitude(inputLocal, 1f);
        velocityInput = transform.TransformDirection(inputLocal) * (moveSpeed * speedMultiplier);

        // ===== Mouse look =====
        float rawMouseX = Input.GetAxis("Mouse X");
        float rawMouseY = Input.GetAxis("Mouse Y");

        // Optionally smooth mouse (0 = immediate)
        float mouseX = (mouseLookSmoothing <= 0f) ? rawMouseX : Mathf.Lerp(0f, rawMouseX, mouseLookSmoothing * Time.deltaTime);
        float mouseY = (mouseLookSmoothing <= 0f) ? rawMouseY : Mathf.Lerp(0f, rawMouseY, mouseLookSmoothing * Time.deltaTime);

        // Mouse contributes immediate degrees (per frame)
        currentYaw += mouseX * mouseLookSensitivity;
        currentPitch += (invertY ? mouseY : -mouseY) * mouseLookSensitivity;

        // ===== Controller right stick =====
        float rsX = 0f;
        float rsY = 0f;
        TryReadLegacyRightStick(ref rsX, ref rsY); // will be zero if axes missing

        if (debugRightStickValues)
        {
            Debug.Log($"[RightStick RAW] {rightStickHorizontalAxis}: {rsX:F3}  {rightStickVerticalAxis}: {rsY:F3}");
        }

        // Apply deadzone + remap so that values outside deadzone map smoothly to 0..1 range
        Vector2 remapped = ApplyDeadzoneAndRemap(new Vector2(rsX, rsY), controllerDeadzone);

        // Smooth stick values to avoid jitter
        smoothedControllerStick = Vector2.Lerp(smoothedControllerStick, remapped, Mathf.Clamp01(controllerLookSmoothing * Time.deltaTime));

        // Controller stick is -1..1 after remap and smoothing. Convert to degrees this frame:
        float controllerYawDelta = smoothedControllerStick.x * controllerLookSensitivity * Time.deltaTime;
        float controllerPitchDelta = (invertY ? smoothedControllerStick.y : -smoothedControllerStick.y) * controllerLookSensitivity * Time.deltaTime;

        currentYaw += controllerYawDelta;
        currentPitch += controllerPitchDelta;

        // Clamp pitch and apply camera rotation
        currentPitch = Mathf.Clamp(currentPitch, pitchMin, pitchMax);
        if (playerCamera != null)
            playerCamera.transform.localRotation = Quaternion.Euler(currentPitch, 0f, 0f);
    }

    void FixedUpdate()
    {
        if (rb == null) return;

        Vector3 newPosition = rb.position + velocityInput * Time.fixedDeltaTime;
        rb.MovePosition(newPosition);

        Quaternion targetRotation = Quaternion.Euler(0f, currentYaw, 0f);
        rb.MoveRotation(targetRotation);
    }

    // Reads legacy Input.GetAxis for configured axis names. Gracefully handles missing axes.
    private void TryReadLegacyRightStick(ref float outX, ref float outY)
    {
        outX = 0f;
        outY = 0f;
        try
        {
            outX = Input.GetAxis(rightStickHorizontalAxis);
            outY = Input.GetAxis(rightStickVerticalAxis);
        }
        catch (System.ArgumentException)
        {
            // axis not defined in Input Manager: leave at zero
        }
    }

    // Apply deadzone: values within deadzone = 0.
    // Values outside are remapped so that deadzone -> 0 and 1 -> 1 (preserve sign).
    private Vector2 ApplyDeadzoneAndRemap(Vector2 v, float deadzone)
    {
        if (deadzone <= 0f) return v;
        Vector2 r = Vector2.zero;
        r.x = RemapAxisWithDeadzone(v.x, deadzone);
        r.y = RemapAxisWithDeadzone(v.y, deadzone);
        return r;
    }

    private float RemapAxisWithDeadzone(float val, float deadzone)
    {
        float a = Mathf.Abs(val);
        if (a <= deadzone) return 0f;
        // remap a from [deadzone..1] to [0..1]
        float sign = Mathf.Sign(val);
        float mapped = (a - deadzone) / (1f - deadzone);
        return Mathf.Clamp01(mapped) * sign;
    }

    // Debug helper to quickly toggle cursor
    public void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}

