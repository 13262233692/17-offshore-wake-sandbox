using UnityEngine;

public class SceneSetup : MonoBehaviour
{
    [Header("Wind Farm Layout")]
    public int turbineRows = 3;
    public int turbineCols = 5;
    public float turbineSpacingX = 500f;
    public float turbineSpacingZ = 300f;
    public float rotorDiameter = 126f;
    public float hubHeight = 90f;

    [Header("Wind Field")]
    public float fieldSize = 1000f;
    public float freeStreamSpeed = 10f;
    public float viscosity = 0.0001f;

    [Header("References (Auto-assigned)")]
    public ComputeShader windSimShader;
    public ComputeShader turbineSrcShader;
    public ComputeShader aeroForceShader;
    public Shader windVizShader;
    public Shader bladeDeflectionShader;

    private GameObject _ocean;
    private GameObject _windFieldQuad;
    private GameObject _turbineParent;
    private WindFieldManager _windManager;

    void Awake()
    {
        SetupScene();
    }

    void SetupScene()
    {
        CreateOcean();
        CreateWindFieldQuad();
        CreateTurbineFarm();
        SetupWindManager();
        SetupCamera();
        SetupLighting();
    }

    void CreateOcean()
    {
        _ocean = GameObject.CreatePrimitive(PrimitiveType.Plane);
        _ocean.name = "Ocean";
        _ocean.transform.position = Vector3.zero;
        _ocean.transform.localScale = new Vector3(fieldSize / 10f, 1f, fieldSize / 10f);

        Renderer oceanRenderer = _ocean.GetComponent<Renderer>();
        oceanRenderer.material = new Material(Shader.Find("Standard"));
        oceanRenderer.material.color = new Color(0.05f, 0.15f, 0.3f, 1f);
        oceanRenderer.material.SetFloat("_Glossiness", 0.9f);
        oceanRenderer.material.SetFloat("_Metallic", 0.1f);
    }

    void CreateWindFieldQuad()
    {
        _windFieldQuad = GameObject.CreatePrimitive(PrimitiveType.Plane);
        _windFieldQuad.name = "WindField";
        _windFieldQuad.transform.position = new Vector3(0f, 0.5f, 0f);
        _windFieldQuad.transform.localScale = new Vector3(fieldSize / 10f, 1f, fieldSize / 10f);
        _windFieldQuad.transform.Rotate(Vector3.right, -90f);

        Destroy(_windFieldQuad.GetComponent<Collider>());

        WindFieldRenderer renderer = _windFieldQuad.AddComponent<WindFieldRenderer>();

        if (windVizShader != null)
        {
            Material windMat = new Material(windVizShader);
            renderer.windMaterial = windMat;
        }
    }

    void CreateTurbineFarm()
    {
        _turbineParent = new GameObject("TurbineFarm");
        TurbineController[] controllers = new TurbineController[turbineRows * turbineCols];

        float startX = -((turbineCols - 1) * turbineSpacingX) / 2f;
        float startZ = -((turbineRows - 1) * turbineSpacingZ) / 2f;

        int idx = 0;
        for (int row = 0; row < turbineRows; row++)
        {
            for (int col = 0; col < turbineCols; col++)
            {
                float staggerOffset = (row % 2 == 1) ? turbineSpacingX * 0.5f : 0f;
                Vector3 pos = new Vector3(
                    startX + col * turbineSpacingX + staggerOffset,
                    0f,
                    startZ + row * turbineSpacingZ
                );

                controllers[idx] = CreateTurbine(pos, row, col);
                idx++;
            }
        }

        if (_windManager != null)
        {
            _windManager.turbines = controllers;
        }
    }

    TurbineController CreateTurbine(Vector3 position, int row, int col)
    {
        GameObject turbineRoot = new GameObject($"Turbine_{row}_{col}");
        turbineRoot.transform.SetParent(_turbineParent.transform);
        turbineRoot.transform.position = position;

        GameObject tower = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        tower.name = "Tower";
        tower.transform.SetParent(turbineRoot.transform);
        tower.transform.localPosition = new Vector3(0f, hubHeight * 0.5f, 0f);
        tower.transform.localScale = new Vector3(2f, hubHeight * 0.5f, 2f);
        tower.GetComponent<Renderer>().material.color = Color.white;

        GameObject nacelle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        nacelle.name = "Nacelle";
        nacelle.transform.SetParent(turbineRoot.transform);
        nacelle.transform.localPosition = new Vector3(0f, hubHeight, 0f);
        nacelle.transform.localScale = new Vector3(6f, 3f, 3f);
        nacelle.GetComponent<Renderer>().material.color = Color.white;

        GameObject rotorHub = new GameObject("RotorHub");
        rotorHub.transform.SetParent(turbineRoot.transform);
        rotorHub.transform.localPosition = new Vector3(3f, hubHeight, 0f);

        Material bladeMatInstance = null;
        if (bladeDeflectionShader != null)
        {
            bladeMatInstance = new Material(bladeDeflectionShader);
            bladeMatInstance.SetFloat("_BladeLength", rotorDiameter * 0.5f);
            bladeMatInstance.SetFloat("_ChordRoot", 4.5f);
            bladeMatInstance.SetFloat("_ChordTip", 1.2f);
            bladeMatInstance.SetFloat("_BladeThickness", 0.8f);
            bladeMatInstance.SetFloat("_TwistRoot", 12.0f);
            bladeMatInstance.SetFloat("_TwistTip", -3.0f);
        }

        GameObject[] blades = new GameObject[3];
        for (int b = 0; b < 3; b++)
        {
            GameObject blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blade.name = $"Blade_{b}";
            blade.transform.SetParent(rotorHub.transform);
            blade.transform.localRotation = Quaternion.Euler(0f, 0f, b * 120f);
            blade.transform.localScale = new Vector3(1f, rotorDiameter * 0.5f, 0.3f);
            blade.transform.localPosition = blade.transform.up * rotorDiameter * 0.25f;

            Renderer bladeRenderer = blade.GetComponent<Renderer>();
            if (bladeMatInstance != null)
            {
                bladeRenderer.material = bladeMatInstance;
            }
            else
            {
                bladeRenderer.material.color = Color.white;
            }

            blades[b] = blade;
        }

        TurbineController controller = turbineRoot.AddComponent<TurbineController>();
        controller.rotorDiameter = rotorDiameter;
        controller.bladeLength = rotorDiameter * 0.5f;
        controller.hubHeight = hubHeight;
        controller.bladesPerRotor = 3;
        controller.thrustCoefficient = 0.8f;
        controller.targetRPM = 12f;
        controller.yawAngleDegrees = 0f;
        controller.visualDeflectionScale = 15f;
        controller.rotorHub = rotorHub.transform;
        controller.bladeObjects = blades;

        return controller;
    }

    void SetupWindManager()
    {
        GameObject managerObj = new GameObject("WindFieldManager");
        _windManager = managerObj.AddComponent<WindFieldManager>();

        _windManager.freeStreamSpeed = freeStreamSpeed;
        _windManager.viscosity = viscosity;

        if (windSimShader != null)
            _windManager.windSimulationShader = windSimShader;
        if (turbineSrcShader != null)
            _windManager.turbineSourceShader = turbineSrcShader;
        if (aeroForceShader != null)
            _windManager.aerodynamicForceShader = aeroForceShader;

        WindFieldRenderer renderer = _windFieldQuad.GetComponent<WindFieldRenderer>();
        if (renderer != null)
        {
            renderer.windManager = _windManager;
        }
    }

    void SetupCamera()
    {
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(0f, fieldSize * 0.6f, -fieldSize * 0.4f);
            cam.transform.LookAt(new Vector3(0f, 0f, fieldSize * 0.1f));
            cam.farClipPlane = fieldSize * 2f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.05f, 0.1f, 1f);
        }
    }

    void SetupLighting()
    {
        GameObject dirLight = new GameObject("DirectionalLight");
        Light light = dirLight.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.95f, 0.85f);
        light.intensity = 1.2f;
        dirLight.transform.rotation = Quaternion.Euler(45f, -30f, 0f);

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.15f, 0.2f, 0.3f, 1f);
        RenderSettings.fog = true;
        RenderSettings.fogColor = new Color(0.6f, 0.7f, 0.8f, 1f);
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.0005f;
    }
}
