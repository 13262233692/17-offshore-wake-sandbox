using UnityEngine;

public class TurbineController : MonoBehaviour
{
    [Header("Turbine Specifications")]
    public float rotorDiameter = 126f;
    public float ratedPower = 5e6f;
    public float hubHeight = 90f;
    public int bladesPerRotor = 3;

    [Header("Blade Structural Properties (Euler-Bernoulli)")]
    public float bladeLength = 63f;
    public float youngsModulus_GPa = 45f;
    public float rootMomentOfInertia_m4 = 0.0025f;
    public float tipMomentOfInertia_m4 = 0.00004f;
    public float massPerUnitLength_kgm = 180f;
    public float shearModulus_GPa = 18f;
    public float torsionalConstant_m4 = 0.0008f;
    public float structuralDamping = 0.03f;

    [Header("Operational Parameters")]
    [Range(0.0f, 0.98f)]
    public float thrustCoefficient = 0.8f;
    [Range(0.0f, 30.0f)]
    public float targetRPM = 12.0f;
    [Range(0.0f, 360.0f)]
    public float yawAngleDegrees = 0.0f;
    [Range(-20.0f, 40.0f)]
    public float collectivePitchDegrees = 0.0f;

    [Header("Visual References")]
    public Transform rotorHub;
    public GameObject[] bladeObjects;
    public float visualDeflectionScale = 15.0f;
    public bool showStrainColor = true;

    [Header("Runtime Aeroelastic State")]
    public float currentRPM;
    public float currentPower;
    public float effectiveWindSpeed;
    public float tipDeflectionFlapwise_m;
    public float tipDeflectionEdgewise_m;
    public float tipTorsion_deg;
    public float rootBendingMoment_kNm;
    public float rootShear_kN;
    public float aerodynamicTorque_kNm;
    public float stressRatio;

    public float yawAngle => yawAngleDegrees * Mathf.Deg2Rad;
    public float pitchAngleRad => collectivePitchDegrees * Mathf.Deg2Rad;

    public float rotorRadiusGrid
    {
        get
        {
            float gridSize = 1000f;
            return (rotorDiameter * 0.5f / gridSize) * WindFieldManager.GRID_SIZE;
        }
    }

    private WindFieldManager _windManager;
    private float _smoothedRPM;

    private MaterialPropertyBlock[] _bladeMPBs;
    private Renderer[] _bladeRenderers;

    private const int BLADE_SEGMENTS = 16;
    private float[] _sectionLift;
    private float[] _sectionMoment;
    private float[] _sectionEI;
    private float[] _sectionDeflection;
    private float[] _sectionSlope;
    private float[] _sectionTorsion;

    private float _prevTipFlap;
    private float _prevTipEdge;
    private float _prevTipTorsion;
    private float _dynamicOscillationPhase;

    void Awake()
    {
        intializeStructuralArrays();
    }

    void intializeStructuralArrays()
    {
        _sectionLift = new float[BLADE_SEGMENTS];
        _sectionMoment = new float[BLADE_SEGMENTS];
        _sectionEI = new float[BLADE_SEGMENTS];
        _sectionDeflection = new float[BLADE_SEGMENTS];
        _sectionSlope = new float[BLADE_SEGMENTS];
        _sectionTorsion = new float[BLADE_SEGMENTS];

        for (int s = 0; s < BLADE_SEGMENTS; s++)
        {
            float sFrac = (float)s / (BLADE_SEGMENTS - 1);
            float sQuad = sFrac * sFrac;
            float taper = Mathf.Lerp(1f, 0.02f, sQuad);
            _sectionEI[s] = youngsModulus_GPa * 1e9f *
                Mathf.Lerp(rootMomentOfInertia_m4, tipMomentOfInertia_m4, sQuad) *
                Mathf.Lerp(1f, 0.5f, taper);
        }
    }

    void Start()
    {
        _windManager = FindObjectOfType<WindFieldManager>();
        _smoothedRPM = targetRPM;
        currentRPM = targetRPM;
        effectiveWindSpeed = 10f;

        InitializeBladeMPBs();
    }

    void InitializeBladeMPBs()
    {
        if (bladeObjects == null || bladeObjects.Length == 0) return;

        _bladeMPBs = new MaterialPropertyBlock[bladeObjects.Length];
        _bladeRenderers = new Renderer[bladeObjects.Length];

        for (int i = 0; i < bladeObjects.Length; i++)
        {
            if (bladeObjects[i] == null) continue;

            _bladeRenderers[i] = bladeObjects[i].GetComponent<Renderer>();
            if (_bladeRenderers[i] == null) continue;

            _bladeMPBs[i] = new MaterialPropertyBlock();
            _bladeRenderers[i].GetPropertyBlock(_bladeMPBs[i]);
        }
    }

    void Update()
    {
        UpdateOperationalState();
        ComputeAerodynamicLoads();
        SolveEulerBernoulliBeam();
        UpdateBladeDeformationMPB();
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

    void ComputeAerodynamicLoads()
    {
        float omega = currentRPM * 2f * Mathf.PI / 60f;
        float airDensity = 1.225f;

        rootBendingMoment_kNm = 0f;
        rootShear_kN = 0f;
        aerodynamicTorque_kNm = 0f;

        float ctClamped = Mathf.Clamp(thrustCoefficient, 0f, 0.98f);
        float axInduction = 0.5f * (1f - Mathf.Sqrt(Mathf.Max(0.0001f, 1f - ctClamped)));
        float effectiveAeroSpeed = effectiveWindSpeed * (1f - axInduction);

        float dynamicPressure = 0.5f * airDensity * effectiveAeroSpeed * effectiveAeroSpeed;

        for (int s = 0; s < BLADE_SEGMENTS; s++)
        {
            float sFrac = (float)s / (BLADE_SEGMENTS - 1);
            float rLocal = sFrac * bladeLength;
            float dr = bladeLength / BLADE_SEGMENTS;

            float chord = 4.5f * (1f - 0.6f * sFrac);
            float omegaR = omega * (rLocal + 0.5f * dr);
            float relSpeed = Mathf.Sqrt(effectiveAeroSpeed * effectiveAeroSpeed + omegaR * omegaR);
            float inflowAngle = Mathf.Atan2(effectiveAeroSpeed, Mathf.Max(0.001f, omegaR));
            float twist = Mathf.Lerp(12f, -3f, sFrac) * Mathf.Deg2Rad;
            float AOA = inflowAngle - twist - pitchAngleRad;
            AOA = Mathf.Clamp(AOA, -0.3f, 0.3f);

            float CL = 2f * Mathf.PI * Mathf.Sin(AOA) * Mathf.Exp(-0.1f * sFrac * sFrac);
            float CD = 0.01f + 0.05f * AOA * AOA;

            float localDP = 0.5f * airDensity * relSpeed * relSpeed;
            float sectionLiftForce = CL * localDP * chord * dr;
            float sectionDragForce = CD * localDP * chord * dr;

            float liftNorm = 1f;
            if (effectiveWindSpeed > 12f)
            {
                float stallFactor = Mathf.Clamp((effectiveWindSpeed - 12f) / 13f, 0f, 1f);
                liftNorm *= Mathf.Lerp(1f, 0.6f, stallFactor);
            }
            sectionLiftForce *= liftNorm;

            _sectionLift[s] = sectionLiftForce;
            float moment = sectionLiftForce * (rLocal + dr * 0.5f);
            _sectionMoment[s] = moment;

            rootShear_kN += sectionLiftForce * 0.001f;
            rootBendingMoment_kNm += moment * 0.001f;

            float tangentForce = (sectionDragForce * Mathf.Cos(inflowAngle) +
                                 sectionLiftForce * Mathf.Sin(AOA) * 0.1f);
            aerodynamicTorque_kNm += tangentForce * rLocal * 0.001f;
        }

        float ratedBending = ratedPower * 0.000008f;
        stressRatio = Mathf.Clamp01(rootBendingMoment_kNm / Mathf.Max(ratedBending, 0.001f));
    }

    void SolveEulerBernoulliBeam()
    {
        float[] shear = new float[BLADE_SEGMENTS];
        float[] moment = new float[BLADE_SEGMENTS];
        float[] curvature = new float[BLADE_SEGMENTS];
        float[] slope = new float[BLADE_SEGMENTS];
        float[] deflection = new float[BLADE_SEGMENTS];

        float[] loadDist = new float[BLADE_SEGMENTS];
        float centrifugalStiffening = 0f;

        float omega = currentRPM * 2f * Mathf.PI / 60f;
        for (int s = 0; s < BLADE_SEGMENTS; s++)
        {
            float sFrac = (float)s / (BLADE_SEGMENTS - 1);
            float rLocal = sFrac * bladeLength;
            float dr = bladeLength / BLADE_SEGMENTS;

            loadDist[s] = _sectionLift[s] / Mathf.Max(dr, 0.0001f);

            float massSegment = massPerUnitLength_kgm * dr;
            centrifugalStiffening += massSegment * omega * omega * rLocal;
        }

        for (int iter = 0; iter < 3; iter++)
        {
            shear[BLADE_SEGMENTS - 1] = 0f;
            moment[BLADE_SEGMENTS - 1] = 0f;

            for (int s = BLADE_SEGMENTS - 2; s >= 0; s--)
            {
                float dr = bladeLength / BLADE_SEGMENTS;
                float sFrac = (float)s / (BLADE_SEGMENTS - 1);
                float sFracNext = (float)(s + 1) / (BLADE_SEGMENTS - 1);
                float avgLoad = 0.5f * (loadDist[s] + loadDist[s + 1]);

                float CFsofteningFactor = 1f / (1f + centrifugalStiffening * 0.000001f * sFrac);
                float geoStiff = 1f + (1f - CFsofteningFactor) * Mathf.Pow(sFrac, 3f);

                shear[s] = shear[s + 1] + avgLoad * dr * geoStiff;
                moment[s] = moment[s + 1] + shear[s + 1] * dr + 0.5f * avgLoad * dr * dr * geoStiff;
            }

            float nonlinearFactor = 1f + stressRatio * 0.5f;
            float plasticSoftening = Mathf.Lerp(1f, 0.85f, stressRatio * stressRatio);

            for (int s = 0; s < BLADE_SEGMENTS; s++)
            {
                float effectiveEI = _sectionEI[s] * plasticSoftening / nonlinearFactor;
                curvature[s] = moment[s] / Mathf.Max(effectiveEI, 1e-6f);
            }

            slope[0] = 0f;
            for (int s = 1; s < BLADE_SEGMENTS; s++)
            {
                float dr = bladeLength / BLADE_SEGMENTS;
                float avgCurv = 0.5f * (curvature[s] + curvature[s - 1]);
                slope[s] = slope[s - 1] + avgCurv * dr;
            }

            deflection[0] = 0f;
            for (int s = 1; s < BLADE_SEGMENTS; s++)
            {
                float dr = bladeLength / BLADE_SEGMENTS;
                float avgSlope = 0.5f * (slope[s] + slope[s - 1]);
                deflection[s] = deflection[s - 1] + avgSlope * dr;

                float largeDeflection = Mathf.Abs(slope[s]);
                if (largeDeflection > 0.05f)
                {
                    float correction = 1f + largeDeflection * largeDeflection / 24f;
                    deflection[s] = deflection[s - 1] + avgSlope * dr * correction;
                }
            }
        }

        for (int s = 0; s < BLADE_SEGMENTS; s++)
        {
            _sectionDeflection[s] = deflection[s];
            _sectionSlope[s] = slope[s];
        }

        float rawTipFlap = deflection[BLADE_SEGMENTS - 1];

        float torsionalRigidity = shearModulus_GPa * 1e9f * torsionalConstant_m4;
        for (int s = 0; s < BLADE_SEGMENTS; s++)
        {
            float sFrac = (float)s / (BLADE_SEGMENTS - 1);
            float shearFlow = _sectionLift[s] * 0.05f * (1f - 0.3f * sFrac);
            float dr = bladeLength / BLADE_SEGMENTS;
            _sectionTorsion[s] = (shearFlow * (bladeLength - sFrac * bladeLength)) /
                                 Mathf.Max(torsionalRigidity * (1f + sFrac), 1f) *
                                 Mathf.Rad2Deg * dr;
        }

        float rawTipTorsion = 0f;
        for (int s = 0; s < BLADE_SEGMENTS; s++) rawTipTorsion += _sectionTorsion[s];

        float rawTipEdge = rawTipFlap * 0.15f + aerodynamicTorque_kNm * 0.0003f;

        float naturalPeriod = 2f * Mathf.PI / Mathf.Sqrt(3f * _sectionEI[0] /
            Mathf.Max(massPerUnitLength_kgm * Mathf.Pow(bladeLength, 4f), 0.001f));
        float vibFreq = Mathf.Max(0.3f, 1f / Mathf.Max(naturalPeriod, 0.1f));
        _dynamicOscillationPhase += Time.deltaTime * vibFreq * 2f * Mathf.PI;

        float resFactor = Mathf.Abs(omega / (2f * Mathf.PI) - vibFreq);
        float resonanceBoost = 1f + Mathf.Exp(-resFactor * resFactor * 50f) * 0.5f * stressRatio;

        float dampingFactor = Mathf.Exp(-structuralDamping * Time.deltaTime * 10f);
        float hysteresis = 0.85f;
        tipDeflectionFlapwise_m = _prevTipFlap * hysteresis + rawTipFlap * (1f - hysteresis);
        tipDeflectionFlapwise_m *= dampingFactor * resonanceBoost;
        tipDeflectionFlapwise_m = Mathf.Clamp(tipDeflectionFlapwise_m, -3f, 10f);

        tipDeflectionEdgewise_m = _prevTipEdge * hysteresis + rawTipEdge * (1f - hysteresis);
        tipDeflectionEdgewise_m *= dampingFactor;
        tipDeflectionEdgewise_m = Mathf.Clamp(tipDeflectionEdgewise_m, -0.5f, 1.5f);

        tipTorsion_deg = _prevTipTorsion * hysteresis + rawTipTorsion * (1f - hysteresis);
        tipTorsion_deg *= dampingFactor;
        tipTorsion_deg = Mathf.Clamp(tipTorsion_deg, -15f, 15f);

        tipDeflectionFlapwise_m += Mathf.Sin(_dynamicOscillationPhase) * 0.005f * stressRatio;
        tipTorsion_deg += Mathf.Sin(_dynamicOscillationPhase * 1.7f) * 0.02f * stressRatio;

        _prevTipFlap = tipDeflectionFlapwise_m;
        _prevTipEdge = tipDeflectionEdgewise_m;
        _prevTipTorsion = tipTorsion_deg;
    }

    void UpdateBladeDeformationMPB()
    {
        if (_bladeMPBs == null) return;

        float dispFlap = tipDeflectionFlapwise_m;
        float dispEdge = tipDeflectionEdgewise_m;
        float dispTorque = tipTorsion_deg;

        for (int i = 0; i < bladeObjects.Length; i++)
        {
            if (_bladeMPBs[i] == null || _bladeRenderers[i] == null) continue;

            float phase = (float)i / bladesPerRotor;
            float perBladeVariation = 1f + Mathf.Sin(_dynamicOscillationPhase + phase * 2f * Mathf.PI) * 0.05f * stressRatio;

            _bladeMPBs[i].SetFloat("_FlapwiseTip", dispFlap * perBladeVariation);
            _bladeMPBs[i].SetFloat("_EdgewiseTip", dispEdge * perBladeVariation);
            _bladeMPBs[i].SetFloat("_TorsionTip", dispTorque * perBladeVariation);
            _bladeMPBs[i].SetFloat("_DynamicScale", visualDeflectionScale);
            _bladeMPBs[i].SetFloat("_BladeLength", bladeLength);
            _bladeMPBs[i].SetFloat("_GustNoise", 0.3f * (effectiveWindSpeed / 10f));
            _bladeMPBs[i].SetFloat("_Time", Time.time + phase * 0.017f);

            _bladeRenderers[i].SetPropertyBlock(_bladeMPBs[i]);
        }
    }

    void UpdateRotorVisual()
    {
        if (rotorHub != null)
        {
            rotorHub.Rotate(Vector3.forward, currentRPM * 360f / 60f * Time.deltaTime, Space.Self);
        }
    }

    public struct AeroData
    {
        public float tipFlap;
        public float tipEdge;
        public float tipTorsion;
        public float rootMoment;
        public float stress;
    }

    public AeroData GetAeroData()
    {
        return new AeroData
        {
            tipFlap = tipDeflectionFlapwise_m,
            tipEdge = tipDeflectionEdgewise_m,
            tipTorsion = tipTorsion_deg,
            rootMoment = rootBendingMoment_kNm,
            stress = stressRatio
        };
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

        if (rotorHub != null)
        {
            Vector3 hubPos = transform.position + new Vector3(0f, hubHeight, 0f);
            Vector3 displayFlap = -windDir * tipDeflectionFlapwise_m * visualDeflectionScale;

            Gizmos.color = Color.Lerp(Color.green, Color.red, stressRatio);
            for (int i = 0; i < 3; i++)
            {
                float rot = i * 120f * Mathf.Deg2Rad + (rotorHub != null ? rotorHub.localEulerAngles.z * Mathf.Deg2Rad : 0);
                Vector3 bladeBase = hubPos + new Vector3(Mathf.Cos(rot) * 3f, Mathf.Sin(rot) * 3f, 0);
                Vector3 bladeTip = bladeBase +
                    new Vector3(Mathf.Cos(rot), Mathf.Sin(rot), 0) * bladeLength +
                    displayFlap * 0.1f;
                Gizmos.DrawLine(bladeBase, bladeTip);
                Gizmos.DrawSphere(bladeTip, 0.5f);
            }
        }
    }
}
