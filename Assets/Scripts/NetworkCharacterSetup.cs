using Photon.Pun;
using UnityEngine;
using InfimaGames.LowPolyShooterPack;

public class NetworkCharacterSetup : MonoBehaviourPun, IPunObservable
{
    private Vector3 remotePosition;
    private bool isRemote = false;

    [Header("Footstep Sesleri")]
    [Tooltip("Movement.cs'teki audioClipWalking ile aynı clip'i bağla")]
    public AudioClip audioClipWalking;
    [Tooltip("Movement.cs'teki audioClipRunning ile aynı clip'i bağla")]
    public AudioClip audioClipRunning;

    // Karşı oyuncunun sesini çalan AudioSource
    private AudioSource footstepSource;

    // Pozisyon değişiminden hız hesaplamak için
    private Vector3 lastPosition;
    private float   remoteSpeed;
    private bool    remoteRunning;

    private void Awake()
    {
        GameObject remoteBody = transform.Find("Capsule")?.gameObject;

        if (photonView.IsMine)
        {
            if (remoteBody != null) remoteBody.SetActive(false);

            Transform skeletonLocal = transform.Find("SK_FP_CH_Default_Root");
            if (skeletonLocal != null) skeletonLocal.gameObject.SetActive(true);
            return;
        }

        isRemote = true;

        // Karakter modelini kapat
        Transform skeletonRoot = transform.Find("SK_FP_CH_Default_Root");
        if (skeletonRoot != null) skeletonRoot.gameObject.SetActive(false);

        // Capsule'ü göster ve renklendir (geciktirerek — takım bilgisi henüz gelmemiş olabilir)
        if (remoteBody != null)
        {
            remoteBody.SetActive(true);
            StartCoroutine(ApplyTeamColorLate(remoteBody));
        }

        // Rigidbody kapat
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.detectCollisions = true;
        }

        // Tüm behaviour'ları kapat — AudioSource'lar hariç
        Behaviour[] behaviours = GetComponentsInChildren<Behaviour>(true);
        foreach (var b in behaviours)
        {
            if (b is NetworkCharacterSetup) continue;
            if (b is PhotonView)            continue;
            if (b is AudioSource)           continue;
            if (b is MinimapIcon)           continue; // Minimap ikonu çalışmaya devam etmeli
            b.enabled = false;
        }

        // Kamerayı kapat
        Camera[] cameras = GetComponentsInChildren<Camera>(true);
        foreach (var cam in cameras)
            cam.gameObject.SetActive(false);

        // Karşı oyuncu için ayrı bir footstep AudioSource oluştur
        // (Movement.cs'in AudioSource'undan bağımsız, o zaten disabled)
        footstepSource = gameObject.AddComponent<AudioSource>();
        footstepSource.spatialBlend = 1f;   // 3D ses — mesafeyle azalır
        footstepSource.rolloffMode  = AudioRolloffMode.Linear;
        footstepSource.minDistance  = 1f;
        footstepSource.maxDistance  = 20f;
        footstepSource.loop         = true;
        footstepSource.volume       = 0.6f;

        lastPosition   = transform.position;
        remotePosition = transform.position;
    }

    private System.Collections.IEnumerator ApplyTeamColorLate(GameObject remoteBody)
    {
        // Takım bilgisi gelene kadar bekle (max 5 saniye)
        float timeout = 5f;
        string team = "None";
        while (timeout > 0f)
        {
            if (photonView.Owner.CustomProperties.TryGetValue(ScoreManager.KEY_TEAM, out object t))
            {
                team = t as string ?? "None";
                if (team != "None") break;
            }
            timeout -= Time.deltaTime;
            yield return null;
        }

        Renderer rend = remoteBody.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.material = new Material(Shader.Find("Standard"));
            rend.material.color = team == "Blue" ? Color.blue : Color.red;
        }
    }

    private void Update()
    {
        if (!isRemote) return;

        // Pozisyon interpolasyonu
        transform.position = Vector3.Lerp(transform.position, remotePosition, Time.deltaTime * 10f);

        // Hız hesapla — ağdan gelen pozisyon değişimine göre
        remoteSpeed = Vector3.Distance(transform.position, lastPosition) / Time.deltaTime;
        lastPosition = transform.position;

        // Footstep ses yönetimi
        PlayRemoteFootsteps();
    }

    private void PlayRemoteFootsteps()
    {
        if (footstepSource == null) return;

        bool isMoving = remoteSpeed > 0.5f;   // hareket eşiği
        bool isRunning = remoteSpeed > 6f;     // koşma eşiği (speedRunning = 9)

        if (isMoving)
        {
            AudioClip targetClip = isRunning ? audioClipRunning : audioClipWalking;

            // Clip değişince güncelle
            if (footstepSource.clip != targetClip)
            {
                footstepSource.clip = targetClip;
                footstepSource.Play();
            }
            else if (!footstepSource.isPlaying)
            {
                footstepSource.Play();
            }
        }
        else
        {
            if (footstepSource.isPlaying)
                footstepSource.Pause();
        }
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(transform.position);
            stream.SendNext(IsRunning());
        }
        else
        {
            remotePosition = (Vector3)stream.ReceiveNext();
            remoteRunning  = (bool)stream.ReceiveNext();
        }
    }

    // Local tarafta karakter koşuyor mu — Movement.cs'ten bağımsız basit kontrol
    private bool IsRunning()
    {
        if (!photonView.IsMine) return false;
        var character = GetComponent<CharacterBehaviour>();
        return character != null && character.IsRunning();
    }
}