using UnityEngine;

public class WindFieldManager : MonoBehaviour
{
    public const int GRID_SIZE = 512;
    public const int THREAD_GROUPS = GRID_SIZE / 16;

    [Header("Simulation Parameters")]
    public float dt = 0.02f;
    public float viscosity = 0.0001f;
    public float dissipation = 0.999f;
    public int pressureIterations = 40;
    public float freeStreamSpeed = 10.0f;
    public float airDensity = 1.225f;

    [Header("Compute Shaders")]
    public ComputeShader windSimulationShader;
    public ComputeShader turbineSourceShader;

    [Header("Turbine Configuration")]
    public TurbineController[] turbines;

    private RenderTexture _velocityRT;
    private RenderTexture _velocityPrevRT;
    private RenderTexture _pressureRT;
    private RenderTexture _pressurePrevRT;
    private RenderTexture _divergenceRT;
    private RenderTexture _turbulenceRT;

    private ComputeBuffer _turbineDataBuffer;

    private int _advectionKernel;
    private int _diffusionKernel;
    private int _divergenceKernel;
    private int _pressureKernel;
    private int _gradientSubtractKernel;
    private int _boundaryKernel;
    private int _copyKernel;
    private int _turbineSourceKernel;

    public RenderTexture VelocityTexture => _velocityRT;
    public RenderTexture TurbulenceTexture => _turbulenceRT;

    public struct TurbineGPUData
    {
        public Vector2 gridPos;
        public float rotorRadius;
        public float thrustCoeff;
        public float rpm;
        public float yawAngle;
        public float _pad0;
        public float _pad1;
    }

    void Start()
    {
        InitializeRenderTextures();
        InitializeKernels();
        InitializeBuffers();
        InitializeWindField();
    }

    void InitializeRenderTextures()
    {
        RenderTextureDescriptor desc = new RenderTextureDescriptor(GRID_SIZE, GRID_SIZE, RenderTextureFormat.RGFloat, 0);
        desc.enableRandomWrite = true;
        desc.filterMode = FilterMode.Bilinear;
        desc.wrapMode = TextureWrapMode.Clamp;

        _velocityRT = CreateRT(desc);
        _velocityPrevRT = CreateRT(desc);

        RenderTextureDescriptor sDesc = new RenderTextureDescriptor(GRID_SIZE, GRID_SIZE, RenderTextureFormat.RFloat, 0);
        sDesc.enableRandomWrite = true;
        sDesc.filterMode = FilterMode.Bilinear;
        sDesc.wrapMode = TextureWrapMode.Clamp;

        _pressureRT = CreateRT(sDesc);
        _pressurePrevRT = CreateRT(sDesc);
        _divergenceRT = CreateRT(sDesc);
        _turbulenceRT = CreateRT(sDesc);
    }

    RenderTexture CreateRT(RenderTextureDescriptor desc)
    {
        RenderTexture rt = new RenderTexture(desc);
        rt.Create();
        return rt;
    }

    void InitializeKernels()
    {
        _advectionKernel = windSimulationShader.FindKernel("Advection");
        _diffusionKernel = windSimulationShader.FindKernel("Diffusion");
        _divergenceKernel = windSimulationShader.FindKernel("ComputeDivergence");
        _pressureKernel = windSimulationShader.FindKernel("PressureSolve");
        _gradientSubtractKernel = windSimulationShader.FindKernel("PressureGradientSubtract");
        _boundaryKernel = windSimulationShader.FindKernel("ApplyBoundaryConditions");
        _copyKernel = windSimulationShader.FindKernel("CopyField");

        _turbineSourceKernel = turbineSourceShader.FindKernel("ApplyTurbineSources");
    }

    void InitializeBuffers()
    {
        _turbineDataBuffer = new ComputeBuffer(64, System.Runtime.InteropServices.Marshal.SizeOf(typeof(TurbineGPUData)));
    }

    void InitializeWindField()
    {
        windSimulationShader.SetTexture(_boundaryKernel, "_Velocity", _velocityRT);
        windSimulationShader.SetTexture(_boundaryKernel, "_Pressure", _pressureRT);
        windSimulationShader.Dispatch(_boundaryKernel, THREAD_GROUPS, THREAD_GROUPS, 1);

        SwapVelocity();
        SwapPressure();
    }

    void Update()
    {
        UpdateTurbineData();

        ApplyTurbineSources();
        Diffuse();
        Advect();
        Project();
        ApplyBoundary();

        ClearTurbulenceDecay();
    }

    void UpdateTurbineData()
    {
        if (turbines == null || turbines.Length == 0) return;

        TurbineGPUData[] data = new TurbineGPUData[turbines.Length];
        for (int i = 0; i < turbines.Length; i++)
        {
            if (turbines[i] == null) continue;

            Vector3 worldPos = turbines[i].transform.position;
            Vector2 gridPos = WorldToGrid(worldPos);

            data[i] = new TurbineGPUData
            {
                gridPos = gridPos,
                rotorRadius = turbines[i].rotorRadiusGrid,
                thrustCoeff = turbines[i].thrustCoefficient,
                rpm = turbines[i].currentRPM,
                yawAngle = turbines[i].yawAngle,
                _pad0 = 0,
                _pad1 = 0
            };
        }
        _turbineDataBuffer.SetData(data);
    }

    Vector2 WorldToGrid(Vector3 worldPos)
    {
        float gridSize = 1000f;
        float u = (worldPos.x / gridSize + 0.5f) * GRID_SIZE;
        float v = (worldPos.z / gridSize + 0.5f) * GRID_SIZE;
        return new Vector2(Mathf.Clamp(u, 0, GRID_SIZE - 1), Mathf.Clamp(v, 0, GRID_SIZE - 1));
    }

    void ApplyTurbineSources()
    {
        turbineSourceShader.SetTexture(_turbineSourceKernel, "_Velocity", _velocityRT);
        turbineSourceShader.SetTexture(_turbineSourceKernel, "_Turbulence", _turbulenceRT);
        turbineSourceShader.SetBuffer(_turbineSourceKernel, "_Turbines", _turbineDataBuffer);
        turbineSourceShader.SetInt("_TurbineCount", turbines != null ? turbines.Length : 0);
        turbineSourceShader.SetFloat("_Dt", dt);
        turbineSourceShader.SetFloat("_FreeStreamSpeed", freeStreamSpeed);
        turbineSourceShader.SetFloat("_AirDensity", airDensity);
        turbineSourceShader.SetFloat("_Time", Time.time);
        turbineSourceShader.Dispatch(_turbineSourceKernel, THREAD_GROUPS, THREAD_GROUPS, 1);

        CopyVelocity();
    }

    void Diffuse()
    {
        windSimulationShader.SetFloat("_Viscosity", viscosity);
        windSimulationShader.SetFloat("_Dt", dt);

        windSimulationShader.SetTexture(_diffusionKernel, "_Velocity", _velocityRT);
        windSimulationShader.SetTexture(_diffusionKernel, "_VelocityPrev", _velocityPrevRT);
        windSimulationShader.Dispatch(_diffusionKernel, THREAD_GROUPS, THREAD_GROUPS, 1);

        SwapVelocity();
    }

    void Advect()
    {
        windSimulationShader.SetFloat("_Dt", dt);
        windSimulationShader.SetFloat("_Dissipation", dissipation);
        windSimulationShader.SetFloat("_InvGridSize", 1f / GRID_SIZE);

        windSimulationShader.SetTexture(_advectionKernel, "_Velocity", _velocityRT);
        windSimulationShader.SetTexture(_advectionKernel, "_VelocityPrev", _velocityPrevRT);
        windSimulationShader.Dispatch(_advectionKernel, THREAD_GROUPS, THREAD_GROUPS, 1);

        SwapVelocity();
    }

    void Project()
    {
        windSimulationShader.SetTexture(_divergenceKernel, "_Velocity", _velocityRT);
        windSimulationShader.SetTexture(_divergenceKernel, "_Divergence", _divergenceRT);
        windSimulationShader.Dispatch(_divergenceKernel, THREAD_GROUPS, THREAD_GROUPS, 1);

        ClearRT(_pressureRT);
        ClearRT(_pressurePrevRT);

        for (int i = 0; i < pressureIterations; i++)
        {
            windSimulationShader.SetTexture(_pressureKernel, "_Pressure", _pressureRT);
            windSimulationShader.SetTexture(_pressureKernel, "_PressurePrev", _pressurePrevRT);
            windSimulationShader.SetTexture(_pressureKernel, "_Divergence", _divergenceRT);
            windSimulationShader.Dispatch(_pressureKernel, THREAD_GROUPS, THREAD_GROUPS, 1);

            SwapPressure();
        }

        windSimulationShader.SetTexture(_gradientSubtractKernel, "_Velocity", _velocityRT);
        windSimulationShader.SetTexture(_gradientSubtractKernel, "_Pressure", _pressureRT);
        windSimulationShader.Dispatch(_gradientSubtractKernel, THREAD_GROUPS, THREAD_GROUPS, 1);
    }

    void ApplyBoundary()
    {
        windSimulationShader.SetTexture(_boundaryKernel, "_Velocity", _velocityRT);
        windSimulationShader.SetTexture(_boundaryKernel, "_Pressure", _pressureRT);
        windSimulationShader.Dispatch(_boundaryKernel, THREAD_GROUPS, THREAD_GROUPS, 1);
    }

    void ClearTurbulenceDecay()
    {
        RenderTexture tmp = RenderTexture.GetTemporary(GRID_SIZE, GRID_SIZE, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
        Graphics.Blit(_turbulenceRT, tmp);
        Graphics.Blit(tmp, _turbulenceRT);
        RenderTexture.ReleaseTemporary(tmp);
    }

    void CopyVelocity()
    {
        windSimulationShader.SetTexture(_copyKernel, "_Velocity", _velocityRT);
        windSimulationShader.SetTexture(_copyKernel, "_VelocityPrev", _velocityPrevRT);
        windSimulationShader.SetTexture(_copyKernel, "_Pressure", _pressureRT);
        windSimulationShader.SetTexture(_copyKernel, "_PressurePrev", _pressurePrevRT);
        windSimulationShader.Dispatch(_copyKernel, THREAD_GROUPS, THREAD_GROUPS, 1);
    }

    void SwapVelocity()
    {
        RenderTexture tmp = _velocityRT;
        _velocityRT = _velocityPrevRT;
        _velocityPrevRT = tmp;
    }

    void SwapPressure()
    {
        RenderTexture tmp = _pressureRT;
        _pressureRT = _pressurePrevRT;
        _pressurePrevRT = tmp;
    }

    void ClearRT(RenderTexture rt)
    {
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(false, true, Color.clear);
        RenderTexture.active = prev;
    }

    void OnDestroy()
    {
        ReleaseRT(_velocityRT);
        ReleaseRT(_velocityPrevRT);
        ReleaseRT(_pressureRT);
        ReleaseRT(_pressurePrevRT);
        ReleaseRT(_divergenceRT);
        ReleaseRT(_turbulenceRT);

        if (_turbineDataBuffer != null)
            _turbineDataBuffer.Release();
    }

    void ReleaseRT(RenderTexture rt)
    {
        if (rt != null)
        {
            rt.Release();
            Destroy(rt);
        }
    }
}
