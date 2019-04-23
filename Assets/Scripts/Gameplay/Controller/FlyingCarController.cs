using UnityEngine;

public class FlyingCarController : MonoBehaviour
{
    public float ThrustScale = 5;
    public float VerticalScale = 60;
    public float HorizontalScale = 60;

    [Range(0, 1)]
    public float CarRotationSmooth = 1 / 20f;

    float m_Yaw;
    float m_Pitch;

    void FixedUpdate()
    {
        float forward = ThrustScale * (Input.GetAxis("RightTrigger") - Input.GetAxis("LeftTrigger"));
        float vertical = VerticalScale * Input.GetAxis("Vertical");
        float horizontal = HorizontalScale * Input.GetAxis("Horizontal");

        forward += ThrustScale * ((Input.GetMouseButton(0) ? 1 : 0) - (Input.GetMouseButton(1) ? 1 : 0));
        m_Yaw += horizontal * Time.fixedDeltaTime;
        m_Pitch += vertical * Time.fixedDeltaTime;

        var rotation = Quaternion.identity;
        rotation *= Quaternion.AngleAxis(m_Yaw, Vector3.up);
        rotation *= Quaternion.AngleAxis(m_Pitch, Vector3.right);
        gameObject.transform.rotation = Quaternion.Slerp(gameObject.transform.rotation, rotation, CarRotationSmooth);

        gameObject.transform.Translate(Vector3.forward * Time.deltaTime * forward);
    }
}
