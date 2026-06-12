using UnityEngine;

public class TurbineController : MonoBehaviour
{
    [Header("Turbine Specifications")]
    public float rotorDiameter = 126f;
    public float ratedPower = 5e6f;
    public float hubHeight = 90f;

    [Header("Operational Parameters")]
    [Range(0.0f, 1.0f)]
    public float thrustCoefficient = 0.8f;
    [Range(0.0f, 20.0f)]
    public float targetRPM = 12.0f;
    [Range(0.0f, 360.0f)]
    public float yawAngleDegrees = 0.0f;

    [Header("Rotor Visual")]
    public Transform rotorBlade;
    public float visualSpinSpeed = 30f;

    [Header("Runtime State")]
    public float currentRPM;
    public float currentPower;
    public float effectiveWindSpeed;

    private WindFieldManager _windManager;
    private float _worldToGridScale;
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
    }

    void Update()
    {
        UpdateOperationalState();
        UpdateRotorVisual();
    }

    void UpdateOperationalState()
    {
        if (_windManager == null || _windManager.VelocityTexture == null) return;

        Vector2 gridPos = WorldToGrid(transform.position);
        int gx = Mathf.Clamp((int)gridPos.x, 0, WindFieldManager.GRID_SIZE - 1);
        int gy = Mathf.Clamp((int)gridPos.y, 0, WindFieldManager.GRID_SIZE - 1);

        RenderTexture velRT = _windManager.VelocityTexture;
        RenderTexture.active = velRT;
        Vector2 vel = new Vector2();
        float[] pixels = new float[4];
        velRT.GetPixelData<float>(0);
        RenderTexture.active = null;

        effectiveWindSpeed = _windManager.freeStreamSpeed;

        float tipSpeedRatio = (currentRPM * Mathf.PI * rotorDiameter) / (60f * effectiveWindSpeed + 0.001f);

        float optimalTSR = 7.0f;
        float powerCoeff = 0.48f * Mathf.Exp(-0.5f * Mathf.Pow((tipSpeedRatio - optimalTSR) / 2.0f, 2f));
        powerCoeff = Mathf.Clamp(powerCoeff, 0f, 0.5926f);

        float airDensity = 1.225f;
        float sweptArea = Mathf.PI * (rotorDiameter * 0.5f) * (rotorDiameter * 0.5f);
        currentPower = 0.5f * airDensity * sweptArea * Mathf.Pow(effectiveWindSpeed, 3f) * powerCoeff;
        currentPower = Mathf.Clamp(currentPower, 0f, ratedPower);

        float targetRPMFromWind = (optimalTSR * 60f * effectiveWindSpeed) / (Mathf.PI * rotorDiameter);
        _smoothedRPM = Mathf.Lerp(_smoothedRPM, Mathf.Min(targetRPM, targetRPMFromWind), Time.deltaTime * 2f);
        currentRPM = _smoothedRPM;
    }

    void UpdateRotorVisual()
    {
        if (rotorBlade != null)
        {
            rotorBlade.Rotate(Vector3.forward, currentRPM * 360f / 60f * Time.deltaTime, Space.Self);
        }
    }

    Vector2 WorldToGrid(Vector3 worldPos)
    {
        float gridSize = 1000f;
        float u = (worldPos.x / gridSize + 0.5f) * WindFieldManager.GRID_SIZE;
        float v = (worldPos.z / gridSize + 0.5f) * WindFieldManager.GRID_SIZE;
        return new Vector2(Mathf.Clamp(u, 0, WindFieldManager.GRID_SIZE - 1), Mathf.Clamp(v, 0, WindFieldManager.GRID_SIZE - 1));
    }

    void OnDrawGizmosSelected()
    {
        float gridSize = 1000f;
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
