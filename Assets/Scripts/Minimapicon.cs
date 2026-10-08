using Photon.Pun;
using UnityEngine;
using System.Collections;

/// <summary>
/// Her oyuncu prefabına eklenir.
/// Minimapda sadece kendin ve takım arkadaşlarını gösterir.
/// Düşmanlar için ikon oluşturulmaz.
/// </summary>
public class MinimapIcon : MonoBehaviour
{
    [Header("İkon Prefab")]
    public GameObject iconPrefab;

    private const float ICON_Y = 70f;
    private const float UPDATE_INTERVAL = 0.5f;

    private GameObject iconInstance;
    private PhotonView pv;
    private float nextColorCheck;

    private void Start()
    {
        pv = GetComponent<PhotonView>();

        if (iconPrefab == null)
        {
            Debug.LogWarning("MinimapIcon: iconPrefab atanmamış!", this);
            return;
        }

        // Takım bilgisi gelene kadar bekleyip ikonu oluştur
        StartCoroutine(WaitForTeamAndCreate());
    }

    private IEnumerator WaitForTeamAndCreate()
    {
        // Takım bilgisi gelene kadar bekle (max 5 saniye)
        float timeout = 5f;
        while (timeout > 0f)
        {
            if (pv.Owner.CustomProperties.TryGetValue(ScoreManager.KEY_TEAM, out object t))
            {
                string team = t as string ?? "None";
                if (team != "None" && !string.IsNullOrEmpty(team))
                    break;
            }
            timeout -= Time.deltaTime;
            yield return null;
        }

        // Düşman mı? İkon oluşturma
        if (!pv.IsMine && IsEnemy())
            yield break;

        // İkonu oluştur
        iconInstance = Instantiate(iconPrefab);
        iconInstance.name = "MinimapIcon_" + (pv != null ? pv.Owner.NickName : gameObject.name);
        iconInstance.layer = 6;

        foreach (Transform child in iconInstance.transform)
            child.gameObject.layer = 6;

        ApplyTeamColor();
    }

    private bool IsEnemy()
    {
        // Kendi takımımı al
        string myTeam = "None";
        if (PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue(ScoreManager.KEY_TEAM, out object mt))
            myTeam = mt as string ?? "None";

        // Bu oyuncunun takımını al
        string theirTeam = "None";
        if (pv.Owner.CustomProperties.TryGetValue(ScoreManager.KEY_TEAM, out object tt))
            theirTeam = tt as string ?? "None";

        // Farklı takımsa düşman
        return myTeam != theirTeam || myTeam == "None";
    }

    private void LateUpdate()
    {
        if (iconInstance == null) return;

        Vector3 pos = transform.position;
        iconInstance.transform.position = new Vector3(pos.x, ICON_Y, pos.z);

        // Rengi periyodik güncelle
        if (Time.time >= nextColorCheck)
        {
            nextColorCheck = Time.time + UPDATE_INTERVAL;
            ApplyTeamColor();
        }
    }

    private void ApplyTeamColor()
    {
        if (iconInstance == null || pv == null) return;

        Renderer rend = iconInstance.GetComponent<Renderer>();
        if (rend == null) return;

        if (rend.material == null || rend.material.shader == null)
            rend.material = new Material(Shader.Find("Standard"));

        if (pv.IsMine)
            rend.material.color = Color.green;   // Kendin → yeşil
        else
            rend.material.color = Color.cyan;    // Takım arkadaşı → mavi
    }

    private void OnDestroy()
    {
        if (iconInstance != null)
            Destroy(iconInstance);
    }
}