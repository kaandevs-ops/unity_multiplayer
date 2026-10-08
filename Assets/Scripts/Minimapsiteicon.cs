using UnityEngine;

/// <summary>
/// BombSite_A ve BombSite_B objelerine eklenir.
/// Minimapda A veya B yazısı gösteren sabit bir ikon oluşturur.
/// </summary>
public class MinimapSiteIcon : MonoBehaviour
{
    [Header("Site Ayarları")]
    public string siteLabel = "A";
    public Color siteColor = Color.yellow;

    [Header("Pozisyon")]
    [Tooltip("Zeminin Y değeri. BombSite objen 3.05'teyse zeminin gerçek Y'sini buraya yaz (genellikle 0).")]
    public float groundY = 0.1f;

    private GameObject iconInstance;
    private Vector3 basePos;

    private void Start()
    {
        CreateIcon();
    }

    private void Update()
    {
        // Inspector'dan groundY değişince anında yansısın
        if (iconInstance != null)
        {
            iconInstance.transform.position = new Vector3(basePos.x, groundY, basePos.z);
        }
    }

    private void CreateIcon()
    {
        if (iconInstance != null)
            Destroy(iconInstance);

        basePos = transform.position;

        iconInstance = GameObject.CreatePrimitive(PrimitiveType.Quad);
        iconInstance.name = "MinimapSiteIcon_" + siteLabel;
        iconInstance.layer = 6; // MinimapOnly layer

        Destroy(iconInstance.GetComponent<Collider>());

        Collider col = GetComponent<Collider>();
        float sizeX = 10f;
        float sizeZ = 10f;
        if (col != null)
        {
            sizeX = col.bounds.size.x;
            sizeZ = col.bounds.size.z;
        }

        iconInstance.transform.position = new Vector3(basePos.x, groundY, basePos.z);
        iconInstance.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        iconInstance.transform.localScale = new Vector3(sizeX, sizeZ, 1f);

        Renderer rend = iconInstance.GetComponent<Renderer>();
        Material mat = new Material(Shader.Find("Unlit/Color"));
        mat.color = siteColor;
        rend.material = mat;
        rend.material.renderQueue = 2000;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;

        GameObject textObj = new GameObject("SiteLabel_" + siteLabel);
        textObj.transform.SetParent(iconInstance.transform);
        textObj.transform.localPosition = new Vector3(0f, 0f, -0.05f);
        textObj.transform.localRotation = Quaternion.identity;

        float scaleX = sizeX > 0 ? 1f / sizeX : 0.1f;
        float scaleZ = sizeZ > 0 ? 1f / sizeZ : 0.1f;
        textObj.transform.localScale = new Vector3(scaleX * 3f, scaleZ * 3f, 1f);
        textObj.layer = 6; // MinimapOnly layer

        TextMesh tm = textObj.AddComponent<TextMesh>();
        tm.text = siteLabel;
        tm.fontSize = 60;
        tm.alignment = TextAlignment.Center;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.color = Color.black;
        tm.fontStyle = FontStyle.Bold;

        Renderer textRend = textObj.GetComponent<Renderer>();
        if (textRend != null)
        {
            textRend.material.renderQueue = 2001; // Quad'ın (2000) üstünde render olsun
            textRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            textRend.receiveShadows = false;
        }
    }

    private void OnDestroy()
    {
        if (iconInstance != null)
            Destroy(iconInstance);
    }
}