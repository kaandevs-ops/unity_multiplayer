using Photon.Pun;
using UnityEngine;

/// <summary>
/// Bomba alanı Cube'una eklenir.
/// Oyuncu içeri girince BombSystem'e haber verir.
/// 
/// Kurulum:
///   - Cube oluştur, Box Collider → Is Trigger işaretle
///   - Mesh Renderer'ı kapat (görünmez alan)
///   - Bu scripti Cube'a ekle
///   - Tag: BombSiteA veya BombSiteB
/// </summary>
public class BombSiteTrigger : MonoBehaviour
{
    [Header("Site Adı (A veya B)")]
    public string siteName = "A";

    private void OnTriggerEnter(Collider other)
    {
        // Sadece local player'ı umursuyoruz
        var ph = other.GetComponentInParent<PlayerHealth>();
        if (ph == null || !ph.photonView.IsMine) return;

        BombSystem.Instance?.OnPlayerEnteredSite(this);
        Debug.Log($"[BombSite] {PhotonNetwork.LocalPlayer.NickName} → Site {siteName} alanına girdi");
    }

    private void OnTriggerExit(Collider other)
    {
        var ph = other.GetComponentInParent<PlayerHealth>();
        if (ph == null || !ph.photonView.IsMine) return;

        BombSystem.Instance?.OnPlayerExitedSite(this);
        Debug.Log($"[BombSite] {PhotonNetwork.LocalPlayer.NickName} → Site {siteName} alanından çıktı");
    }
}
