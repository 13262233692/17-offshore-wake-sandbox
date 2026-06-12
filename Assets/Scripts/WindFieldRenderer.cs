using UnityEngine;

[ExecuteInEditMode]
public class WindFieldRenderer : MonoBehaviour
{
    public enum VisualizationMode
    {
        WindSpeed = 0,
        WakeDeficit = 1,
        Turbulence = 2,
        Combined = 3
    }

    [Header("References")]
    public WindFieldManager windManager;
    public Material windMaterial;

    [Header("Visualization")]
    public VisualizationMode vizMode = VisualizationMode.Combined;
    public float colorScale = 1.0f;
    public bool showStreamlines = false;

    private MeshRenderer _meshRenderer;
    private Material _runtimeMaterial;

    void Start()
    {
        _meshRenderer = GetComponent<MeshRenderer>();
        if (_meshRenderer == null)
        {
            _meshRenderer = gameObject.AddComponent<MeshRenderer>();
        }

        if (windMaterial != null)
        {
            _runtimeMaterial = new Material(windMaterial);
            _meshRenderer.material = _runtimeMaterial;
        }
    }

    void Update()
    {
        if (windManager == null)
        {
            windManager = FindObjectOfType<WindFieldManager>();
        }

        if (_runtimeMaterial == null || windManager == null) return;

        if (windManager.VelocityTexture != null)
        {
            _runtimeMaterial.SetTexture("_MainTex", windManager.VelocityTexture);
        }

        if (windManager.TurbulenceTexture != null)
        {
            _runtimeMaterial.SetTexture("_TurbulenceTex", windManager.TurbulenceTexture);
        }

        _runtimeMaterial.SetFloat("_FreeStreamSpeed", windManager.freeStreamSpeed);
        _runtimeMaterial.SetInt("_VizMode", (int)vizMode);
        _runtimeMaterial.SetFloat("_ColorScale", colorScale);
        _runtimeMaterial.SetFloat("_ShowStreamlines", showStreamlines ? 1f : 0f);
    }

    void OnDestroy()
    {
        if (_runtimeMaterial != null)
        {
            Destroy(_runtimeMaterial);
        }
    }
}
