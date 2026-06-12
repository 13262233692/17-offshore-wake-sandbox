using UnityEngine;

public class TurbineController : MonoBehaviour
{
    [Header("Turbine Specifications")]
    public float rotorDiameter = 126f;
    public float ratedPower = 5e6f;
    public float hubHeight = 90f;

    [Header("Operational Parameters")]
    [Range(0.0f, 0.98f)]
    public float thrustCoefficient = 0.8f;
    [Range(0.0f, 30.0f)]
    public float targetRPM = 12.0f;
    [Range(0.0f, 360.0f)]
    public float yawAngleDegrees = 0.0f;

    [Header("Rotor Visual")]
    public Transform rotorBlade;

    [Header("Runtime State")]
    public float currentRPM;
    public float currentPower;
    public float effectiveWindSpeed;

    private WindFieldManager _windManager;
    private float _smoothedRPM;

    public float yawAngle => yawAngleDegrees * Mathf.Deg2Rad;

    public float rotorRadiusGrid
    {
        get
        {
            float gridSize = 1000f;
            return (rotorDiameter * 0.5f / gridSize) * WindFieldManager.GRID_SIZE;
        }
    }

    void Start()
    {
        _windManager = FindObjectOfType<WindFieldManager>();
        _smoothedRPM = targetRPM;
        currentRPM = targetRPM;
        effectiveWindSpeed = 10f;
    }

    void Update()
    {
        UpdateOperationalState();
        UpdateRotorVisual();
    }

    void UpdateOperationalState()
    {
        if (_windManager != null)
        {
            effectiveWindSpeed = _windManager.freeStreamSpeed;
        }
        else
        {
            effectiveWindSpeed = 10f;
        }

        effectiveWindSpeed = Mathf.Clamp(effectiveWindSpeed, 0.1f, 50f);

        float optimalTSR = 7.0f;
        float targetRPMFromWind = (optimalTSR * 60f * effectiveWindSpeed) / (Mathf.PI * Mathf.Max(rotorDiameter, 1f));
        targetRPMFromWind = Mathf.Clamp(targetRPMFromWind, 0f, 30f);

        float blendTarget = Mathf.Min(targetRPM, targetRPMFromWind);
        _smoothedRPM = Mathf.Lerp(_smoothedRPM, blendTarget, Time.deltaTime * 2f);
        _smoothedRPM = Mathf.Clamp(_smoothedRPM, 0f, 30f);
        currentRPM = _smoothedRPM;

        float tipSpeedRatio = (currentRPM * Mathf.PI * rotorDiameter) / (60f * effectiveWindSpeed + 0.001f);

        float powerCoeff = 0.48f * Mathf.Exp(-0.5f * Mathf.Pow((tipSpeedRatio - optimalTSR) / 2.0f, 2f));
        powerCoeff = Mathf.Clamp(powerCoeff, 0f, 0.5926f);

        float airDensity = 1.225f;
        float sweptArea = Mathf.PI * (rotorDiameter * 0.5f) * (rotorDiameter * 0.5f);
        currentPower = 0.5f * airDensity * sweptArea * Mathf.Pow(effectiveWindSpeed, 3f) * powerCoeff;
        currentPower = Mathf.Clamp(currentPower, 0f, ratedPower);
    }

    void UpdateRotorVisual()
    {
        if (rotorBlade != null)
        {
            rotorBlade.Rotate(Vector3.forward, currentRPM * 360f / 60f * Time.deltaTime, Space.Self);
        }
    }

    void OnDrawGizmosSelected()
    {
        float radiusWorld = rotorDiameter * 0.5f;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, radiusWorld);

        Vector3 windDir = new Vector3(Mathf.Cos(yawAngle), 0, Mathf.Sin(yawAngle));
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(transform.position, windDir * radiusWorld * 3f);

        Gizmos.color = Color.red;
        float wakeLength = radiusWorld * 10f;
        Vector3 wakeEnd = transform.position + windDir * wakeLength;
        Gizmos.DrawLine(transform.position, wakeEnd);

        for (int i = 1; i <= 5; i++)
        {
            float t = i / 5f;
            Vector3 wakePos = Vector3.Lerp(transform.position, wakeEnd, t);
            float wakeRadius = radiusWorld * (1f + t * 0.5f);
            Gizmos.DrawWireSphere(wakePos, wakeRadius);
        }
    }
}
