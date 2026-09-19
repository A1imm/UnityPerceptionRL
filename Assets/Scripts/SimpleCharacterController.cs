using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class SimpleCharacterController : MonoBehaviour
{
    [Tooltip("Maksymalne nachylenie terenu, po którym postać może się poruszać/skakać.")]
    [Range(5f, 60f)]
    public float slopeLimit = 45f;

    [Tooltip("Prędkość ruchu w metrach na sekundę.")]
    public float moveSpeed = 5f;

    [Tooltip("Prędkość obrotu w stopniach na sekundę, w lewo (+) lub w prawo (-).")]
    public float turnSpeed = 300f;

    [Tooltip("Czy postać może skakać.")]
    public bool allowJump = true;

    [Tooltip("Prędkość pionowa nadawana przy skoku, w metrach na sekundę.")]
    public float jumpSpeed = 4f;

    public bool IsGrounded { get; private set; }

    public float ForwardInput { get; set; }

    public float TurnInput { get; set; }

    public bool JumpInput { get; set; }

    private Rigidbody rb;
    private CapsuleCollider capsuleCollider;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        capsuleCollider = GetComponent<CapsuleCollider>();
    }

    private void FixedUpdate()
    {
        CheckGrounded();
        ProcessActions();
    }

    private void CheckGrounded()
    {
        IsGrounded = false;

        float capsuleHeight = Mathf.Max(capsuleCollider.radius * 2f, capsuleCollider.height);
        Vector3 capsuleBottom = transform.TransformPoint(
            capsuleCollider.center - Vector3.up * capsuleHeight / 2f);
        float radius = transform.TransformVector(capsuleCollider.radius, 0f, 0f).magnitude;

        Ray ray = new Ray(capsuleBottom + transform.up * 0.01f, -transform.up);

        if (Physics.Raycast(ray, out RaycastHit hit, radius * 5f))
        {
            float normalAngle = Vector3.Angle(hit.normal, transform.up);

            if (normalAngle < slopeLimit)
            {
                float maxDist = radius / Mathf.Cos(Mathf.Deg2Rad * normalAngle) - radius + 0.02f;

                if (hit.distance < maxDist)
                {
                    IsGrounded = true;
                }
            }
        }
    }

    private void ProcessActions()
    {
        if (TurnInput != 0f)
        {
            float angle = Mathf.Clamp(TurnInput, -1f, 1f) * turnSpeed;
            transform.Rotate(Vector3.up, Time.fixedDeltaTime * angle);
        }

        if (IsGrounded)
        {
            rb.linearVelocity = Vector3.zero;

            if (JumpInput && allowJump)
            {
                rb.linearVelocity += Vector3.up * jumpSpeed;
            }

            rb.linearVelocity += transform.forward * Mathf.Clamp(ForwardInput, -1f, 1f) * moveSpeed;
        }
        else
        {
            if (!Mathf.Approximately(ForwardInput, 0f))
            {
                Vector3 verticalVelocity = Vector3.Project(rb.linearVelocity, Vector3.up);
                rb.linearVelocity = verticalVelocity
                    + transform.forward * Mathf.Clamp(ForwardInput, -1f, 1f) * moveSpeed / 2f;
            }
        }
    }
}