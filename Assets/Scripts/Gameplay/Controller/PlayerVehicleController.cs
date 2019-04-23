using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Cinemachine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerVehicleController : MonoBehaviour
{
    #region Public Vars
    public float m_Acceleration = 5.0f;
    public float m_Deceleration = 10.0f;
    public float m_BreakForce = 20.0f;
    public float m_MaxSpeed = 50.0f;
    public float m_MinSpeed = -100.0f;

    public float m_RollAutoLevelVelocity = 5.0f;
    public float m_PitchAutoLevelVelocity = 5.0f;
    public float m_MaxBankAngle = 45.0f;
    public float m_MaxYawAngle = 45.0f;
    public float m_MaxPitchAngle = 20.0f;
    public float m_BankVolatility = 2.0f;
    public float m_BankRotationSpeed = 3.0f;
    public float m_ManualRollMaxSpeed = 4.0f;
    public float m_ManualRollAcceleration = 0.5f;
    public float m_MinimumBankVelocity = 4.0f;
    public float m_RotationVelocity = 5.0f;
    public float m_FollowCameraZBreakZoom = 2.0f;
    public float m_FollowCameraZBreakSpeed = 2.0f;

    public bool m_InvertPitch = false;
    public AnimationCurve m_BankVolatilityCurve = new AnimationCurve();
    public AnimationCurve m_YawVolatilityCurve = new AnimationCurve();
    public AnimationCurve m_PitchVolatilityCurve = new AnimationCurve();
    #endregion

    #region Private Vars
    private float m_Thrust = 0.0f;
    private float m_MaxVelocity = 0.0f;
    private float m_ManualRollValue = 0.0f;
    private float m_ManualRollSpeed = 0.0f;
    private float m_BankAmount = 0.0f;
    private float m_DragBreakForce = 1.8f;
    private float m_YawKickBack = 2.0f;
    private float m_YawBreakRotation = 0.0f;
    private float3 m_ControlDirection = float3.zero;
    private quaternion m_TempRotation = quaternion.identity;

    private float m_RightTrigger = 0.0f;
    private float m_LeftTrigger = 0.0f;
    private float m_RightTrigger2 = 0.0f;
    private float m_LeftTrigger2 = 0.0f;
    private float m_RightStickHoriz = 0.0f;
    private float m_RightStickClick = 0.0f;
    private float m_FollowCameraZFollow = 0;
    public float m_PitchForce = 4.0f;
    private float3 m_ChildEularRotationDeg = float3.zero;
    private static float3 c_VEC_RIGHT = new float3(1, 0, 0);
    private static float3 c_VEC_FORWARD = new float3(0, 0, 1);
    private static float3 c_VEC_UP = new float3(0, 1, 0);

    [System.Flags]
    private enum VEHCILE_INPUT
    {
        NONE,
        ACCELERATE,
        BRAKE
    };
    private VEHCILE_INPUT m_VehicleInput = VEHCILE_INPUT.NONE;

    private Rigidbody m_Rigidbody = null;
    private Transform m_ChildTransform = null;
    private CinemachineOrbitalTransposer m_Transposer = null;
    #endregion

    void Awake()
    {
        m_ChildTransform = transform.GetChild(0);
        m_Rigidbody = GetComponent<Rigidbody>();

        Transform cineChild = transform.parent.GetChild(1);
        CinemachineVirtualCamera vCam = cineChild.GetComponent<CinemachineVirtualCamera>();
        m_Transposer = vCam.GetCinemachineComponent<CinemachineOrbitalTransposer>();
        m_FollowCameraZFollow = m_Transposer.m_FollowOffset.z;

        m_MaxVelocity = ((m_MaxSpeed / m_Rigidbody.drag) - Time.fixedDeltaTime * m_MaxSpeed) / m_Rigidbody.mass;
    }

    private void CalculateThrust()
    {
        if (m_RightTrigger > 0)
            m_Thrust += m_Acceleration * m_RightTrigger;
        else if (m_VehicleInput == VEHCILE_INPUT.ACCELERATE)
            m_Thrust += m_Acceleration;

        if (m_VehicleInput == VEHCILE_INPUT.BRAKE)
            m_Rigidbody.drag = m_DragBreakForce;
        else
            m_Rigidbody.drag = 1.0f;

        if (m_Thrust > m_MaxSpeed)
            m_Thrust = m_MaxSpeed;

        if (m_Thrust < m_MinSpeed)
            m_Thrust = m_MinSpeed;
    }

    private void CalculateThrustDepreciation()
    {
        if (m_RightTrigger > 0 || m_VehicleInput == VEHCILE_INPUT.ACCELERATE)
            return;

        if (m_Thrust > 0)
            m_Thrust -= m_Deceleration;

        if (m_Thrust < 0)
            m_Thrust = 0;
    }

    private void AutoLevelCar()
    {
        m_Rigidbody.AddRelativeTorque(c_VEC_FORWARD * math.dot(transform.right, c_VEC_UP) * -m_RollAutoLevelVelocity);

        if (m_RightTrigger > 0 || m_LeftTrigger > 0 || m_VehicleInput != VEHCILE_INPUT.NONE || m_ControlDirection.x > 0 || m_ControlDirection.y > 0
            || m_RightTrigger2 > 0 || m_LeftTrigger2 > 0)
            return;

        m_Rigidbody.AddRelativeTorque(c_VEC_RIGHT * math.dot(transform.forward, c_VEC_UP) * m_PitchAutoLevelVelocity);
    }

    private void CalculateBanking()
    {
        float3 vehicleVelocityLocal = math.rotate(transform.worldToLocalMatrix, m_Rigidbody.velocity);
        vehicleVelocityLocal.y = 0;

        float momentum = math.clamp((vehicleVelocityLocal.x / m_MaxVelocity) * m_BankVolatility, -1, 1);
        m_BankAmount = m_MaxBankAngle * math.sign(momentum) * m_BankVolatilityCurve.Evaluate(math.abs(momentum));
        m_ChildEularRotationDeg.z = m_BankAmount;
    }

    private void RollVehicle()
    {
        if (m_RightTrigger2 > 0 || m_LeftTrigger2 > 0)
        {
            if (m_ManualRollValue == 0)
                m_ManualRollValue = m_ChildEularRotationDeg.z;

            m_ManualRollSpeed += m_RightTrigger2 > 0 ? -m_ManualRollAcceleration : m_ManualRollAcceleration;

            if (math.abs(m_ManualRollSpeed) > m_ManualRollMaxSpeed)
                m_ManualRollSpeed = m_ManualRollMaxSpeed * math.sign(m_ManualRollSpeed);

            m_ManualRollValue += m_ManualRollSpeed;
            m_ManualRollValue %= 360;
            m_ChildEularRotationDeg.z = m_ManualRollValue;
        }
        else if (math.abs(m_ManualRollValue) > 0)
        {
            float bA = ((m_BankAmount + 360) % 360);
            float mR = ((m_ManualRollValue + 360) % 360);

            float innerRot = 0.0f;
            float outerRot = 0.0f;

            outerRot = bA > mR ? -mR - (360 - bA) : bA + (360 - mR);
            innerRot = bA - mR;

            float sD = (m_ManualRollSpeed * m_ManualRollSpeed) / (2 * m_ManualRollAcceleration);

            // stopping distance if going wrong direction
            if (math.sign(innerRot) != math.sign(m_ManualRollValue))
                innerRot += math.sign(innerRot) * sD;

            if (math.sign(outerRot) != math.sign(m_ManualRollValue))
                outerRot += math.sign(outerRot) * sD;

            // overshoot distance if sD > ((m_ManualRollSpeed*m_ManualRollSpeed) / (2 * m_ManualRollAcceleration) > distanceToTarget)
            if (sD > math.abs(innerRot))
                innerRot += math.sign(innerRot) * (sD - math.abs(innerRot));

            if (sD > math.abs(outerRot))
                outerRot += math.sign(outerRot) * (sD - math.abs(outerRot));

            float target = math.abs(outerRot) < math.abs(innerRot) ? (bA > mR ? -mR - (360 - bA) : bA + (360 - mR)) : bA - mR;

            if (math.abs(target) < math.abs(m_ManualRollSpeed) && math.abs(m_ManualRollSpeed) <= 1)
            {
                m_ManualRollValue = 0;
                m_ChildEularRotationDeg.z = m_BankAmount;
                m_ManualRollSpeed = 0;
                return;
            }

            if ((m_ManualRollSpeed * m_ManualRollSpeed) / (2 * m_ManualRollAcceleration) > math.abs(target)) // s = (v^2-u^2) / 2a
            {
                if (m_ManualRollSpeed > 0)
                    m_ManualRollSpeed -= m_ManualRollAcceleration;
                else
                    m_ManualRollSpeed += m_ManualRollAcceleration;
            }
            else
            {
                m_ManualRollSpeed += target < 0 ? -m_ManualRollAcceleration : m_ManualRollAcceleration;

                if (math.abs(m_ManualRollSpeed) > m_ManualRollMaxSpeed)
                    m_ManualRollSpeed = m_ManualRollMaxSpeed * math.sign(m_ManualRollSpeed);
            }

            m_ManualRollValue += m_ManualRollSpeed;
            m_ManualRollValue %= 360;
            m_ChildEularRotationDeg.z = m_ManualRollValue;
        }
    }

    private void BreakingPseudoPhysics()
    {
        if (m_Thrust < 0 || m_VehicleInput != VEHCILE_INPUT.BRAKE)
        {

            m_YawBreakRotation = math.lerp(m_YawBreakRotation, m_YawBreakRotation > 180 ? 360 : 0, Time.fixedDeltaTime * m_YawKickBack);
            m_ChildEularRotationDeg.y = m_YawBreakRotation;

            m_ChildEularRotationDeg.x = math.lerp(m_ChildEularRotationDeg.x, 0, Time.fixedDeltaTime * m_PitchForce);

            if (m_Transposer.m_FollowOffset.z != m_FollowCameraZFollow)
                m_Transposer.m_FollowOffset.z = math.lerp(m_Transposer.m_FollowOffset.z, m_FollowCameraZFollow, Time.fixedDeltaTime * m_FollowCameraZBreakSpeed);

            return;
        }

        float3 vehicleVelocityLocal = math.rotate(transform.worldToLocalMatrix, m_Rigidbody.velocity);
        vehicleVelocityLocal.y = 0;

        float relSpeed = math.clamp((math.length(vehicleVelocityLocal) / m_MaxVelocity), 0, 1);
        float pitchAmount = m_MaxPitchAngle * m_PitchVolatilityCurve.Evaluate(math.abs(relSpeed));
        float lerpTime = pitchAmount > m_ChildEularRotationDeg.x ? Time.fixedDeltaTime * m_PitchForce * (math.length(vehicleVelocityLocal) / m_MaxVelocity) : Time.fixedDeltaTime * m_PitchForce;
        m_ChildEularRotationDeg.x = math.lerp(m_ChildEularRotationDeg.x, pitchAmount, lerpTime);

        if (pitchAmount > m_ChildEularRotationDeg.x)
            m_Transposer.m_FollowOffset.z = math.lerp(m_Transposer.m_FollowOffset.z, m_FollowCameraZFollow + m_FollowCameraZBreakZoom, (math.length(vehicleVelocityLocal) / m_MaxVelocity) * Time.fixedDeltaTime * m_FollowCameraZBreakSpeed);
        else
            m_Transposer.m_FollowOffset.z = math.lerp(m_Transposer.m_FollowOffset.z, m_FollowCameraZFollow, Time.fixedDeltaTime * m_FollowCameraZBreakSpeed);


        float momentum = math.clamp(vehicleVelocityLocal.x / m_MaxVelocity, -1, 1);
        float targetYawAmount = m_MaxYawAngle * -math.sign(momentum) * m_YawVolatilityCurve.Evaluate(math.abs(momentum));
        m_YawBreakRotation = math.lerp(m_YawBreakRotation, targetYawAmount, Time.fixedDeltaTime);
        m_ChildEularRotationDeg.y = m_YawBreakRotation;
    }

    private void FreeCam()
    {
        if (m_RightStickClick > 0)
            m_Transposer.m_RecenterToTargetHeading.RecenterNow();
    }

    void Update()
    {
        m_VehicleInput = VEHCILE_INPUT.NONE;
        m_ControlDirection = Vector3.zero;

        m_ControlDirection.y = Input.GetAxis("Horizontal");
        m_ControlDirection.x = -Input.GetAxis("Vertical");

        if (m_InvertPitch)
            m_ControlDirection.x = -m_ControlDirection.x;

        m_RightTrigger = Input.GetAxis("RightTrigger");
        m_LeftTrigger = Input.GetAxis("LeftTrigger");

        m_RightTrigger2 = Input.GetAxis("RightTrigger2");
        m_LeftTrigger2 = Input.GetAxis("LeftTrigger2");

        m_RightStickHoriz = Input.GetAxis("RightStick_Horizontal");
        m_RightStickClick = Input.GetAxis("RightStick_Click");

        if (Input.GetKey(KeyCode.LeftShift))
            m_VehicleInput |= VEHCILE_INPUT.ACCELERATE;

        if (Input.GetKey(KeyCode.B) || m_LeftTrigger > 0)
            m_VehicleInput |= VEHCILE_INPUT.BRAKE;

        if (Input.GetKey(KeyCode.Q))
            m_LeftTrigger2 = 1.0f;

        if (Input.GetKey(KeyCode.E))
            m_RightTrigger2 = 1.0f;

        if (Input.GetKey(KeyCode.UpArrow))
            m_ControlDirection.x = -1.0f;

        if (Input.GetKey(KeyCode.DownArrow))
            m_ControlDirection.x = 1.0f;

        if (Input.GetKey(KeyCode.RightArrow))
            m_ControlDirection.y = 1.0f;

        if (Input.GetKey(KeyCode.LeftArrow))
            m_ControlDirection.y = -1.0f;


        // Hide and lock cursor when right mouse button pressed
        if (Input.GetMouseButtonDown(1))
        {
            Cursor.lockState = CursorLockMode.Locked;
        }

        // Unlock and show cursor when right mouse button released
        if (Input.GetMouseButtonUp(1))
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        if (Input.GetMouseButton(1))
        {
            Vector2 mouseInput = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));

            m_ControlDirection.x = -mouseInput.y;
            m_ControlDirection.y = mouseInput.x;
        }

        if (Input.GetMouseButton(0))
            m_RightTrigger = 1.0f;

        FreeCam();
    }

    private void FixedUpdate()
    {
        m_ChildEularRotationDeg = m_ChildTransform.localEulerAngles;

        CalculateThrust();
        CalculateThrustDepreciation();
        CalculateBanking();
        AutoLevelCar();
        RollVehicle();
        BreakingPseudoPhysics();

        m_Rigidbody.AddRelativeTorque(m_ControlDirection * m_RotationVelocity);
        m_Rigidbody.AddForce(transform.forward.normalized * m_Thrust);

        m_ChildTransform.localRotation = quaternion.Euler(math.radians(m_ChildEularRotationDeg));
    }
}
