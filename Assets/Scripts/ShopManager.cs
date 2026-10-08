using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using InfimaGames.LowPolyShooterPack;

public class ShopManager : MonoBehaviour
{
    public static ShopManager Instance { get; private set; }

    [Header("UI")]
    public GameObject shopPanel;

    [Header("Silah Butonları")]
    public Button buyRifleButton;
    public Button buyHandgunButton;
    public TMP_Text rifleStatusText;
    public TMP_Text handgunStatusText;

    [Header("Kalkan Butonları")]
    public Button buyHalfShieldButton;
    public Button buyFullShieldButton;
    public TMP_Text halfShieldStatusText;
    public TMP_Text fullShieldStatusText;

    [Header("Fiyatlar")]
    public int riflePrice      = 2900;
    public int handgunPrice    = 500;
    public int halfShieldPrice = 400;
    public int fullShieldPrice = 700;

    private bool shopOpen   = false;
    private bool buyPhaseActive = false; // Sadece buy phase'de açılabilir

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    private void Start()
    {
        if (shopPanel) shopPanel.SetActive(false);

        if (buyRifleButton)      buyRifleButton.onClick.AddListener(() => BuyWeapon(0, riflePrice));
        if (buyHandgunButton)    buyHandgunButton.onClick.AddListener(() => BuyWeapon(1, handgunPrice));
        if (buyHalfShieldButton) buyHalfShieldButton.onClick.AddListener(() => BuyShield(50f, halfShieldPrice));
        if (buyFullShieldButton) buyFullShieldButton.onClick.AddListener(() => BuyShield(100f, fullShieldPrice));
    }

    private void Update()
    {
        // B tuşu sadece buy phase'de çalışır
        // Eğer RoundManager yoksa (test modu) her zaman açılabilir
        bool canOpen = buyPhaseActive || RoundManager.Instance == null;

        if (Input.GetKeyDown(KeyCode.B) && canOpen)
            ToggleShop();
    }

    // ── RoundManager tarafından çağrılır ─────────────────────────────

    /// <summary>
    /// Buy phase başladığında RoundManager RPC'si çağırır.
    /// Otomatik olarak shop açılır.
    /// </summary>
    public void OpenShopForBuyPhase()
    {
        buyPhaseActive = true;
        OpenShop();
    }

    /// <summary>
    /// Fight phase başladığında RoundManager çağırır — buy phase tamamen biter.
    /// </summary>
    public void CloseShop()
    {
        buyPhaseActive = false; // Artık buy phase bitti, B tuşu çalışmaz
        ForceCloseShop();
    }

    /// <summary>
    /// Sadece paneli kapat — buyPhaseActive'e dokunma.
    /// B tuşuyla kapatınca bunu kullan.
    /// </summary>
    private void ForceCloseShop()
    {
        shopOpen = false;
        if (shopPanel) shopPanel.SetActive(false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

    // ── Manuel Aç/Kapat ───────────────────────────────────────────────

    private void OpenShop()
    {
        shopOpen = true;
        if (shopPanel) shopPanel.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        RefreshShopUI();
    }

    public void ToggleShop()
    {
        // B ile kapatınca buyPhaseActive'e dokunmuyoruz
        if (shopOpen) ForceCloseShop();
        else          OpenShop();
    }

    // ── UI ────────────────────────────────────────────────────────────

    private void RefreshShopUI()
    {
        int money = ScoreManager.Instance?.GetLocalMoney() ?? 0;
        Inventory inv = GetInventory();

        if (rifleStatusText)
        {
            bool owned = inv != null && inv.IsUnlocked(0);
            rifleStatusText.text = owned ? "Satın Alındı" : $"${riflePrice}";
            if (buyRifleButton) buyRifleButton.interactable = !owned && money >= riflePrice;
        }

        if (handgunStatusText)
        {
            bool owned = inv != null && inv.IsUnlocked(1);
            handgunStatusText.text = owned ? "Satın Alındı" : $"${handgunPrice}";
            if (buyHandgunButton) buyHandgunButton.interactable = !owned && money >= handgunPrice;
        }

        PlayerHealth ph = GetPlayerHealth();
        float currentShield = ph != null ? ph.currentShield : 0f;

        if (halfShieldStatusText)
        {
            bool canBuy = money >= halfShieldPrice && currentShield < 50f;
            halfShieldStatusText.text = $"${halfShieldPrice}";
            if (buyHalfShieldButton) buyHalfShieldButton.interactable = canBuy;
        }

        if (fullShieldStatusText)
        {
            bool canBuy = money >= fullShieldPrice && currentShield < 100f;
            fullShieldStatusText.text = $"${fullShieldPrice}";
            if (buyFullShieldButton) buyFullShieldButton.interactable = canBuy;
        }
    }

    // ── Satın Alma ────────────────────────────────────────────────────

    private void BuyWeapon(int index, int price) => StartCoroutine(BuyWeaponRoutine(index, price));

    private System.Collections.IEnumerator BuyWeaponRoutine(int index, int price)
    {
        if (ScoreManager.Instance == null) yield break;
        if (ScoreManager.Instance.GetLocalMoney() < price) yield break;

        Inventory inv = GetInventory();
        if (inv == null) yield break;
        if (inv.IsUnlocked(index)) yield break;

        ScoreManager.Instance.SpendMoney(price);
        inv.UnlockWeapon(index);

        var characters = FindObjectsByType<Character>(FindObjectsSortMode.None);
        foreach (var c in characters)
        {
            if (c.GetComponent<PhotonView>() is PhotonView pv && pv.IsMine)
            {
                c.ForceRefreshWeapon();

                // Range sahnesindeyse sonsuz mermi uygula
                if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Range")
                {
                    yield return new WaitForSeconds(0.3f);
                    foreach (var w in FindObjectsByType<Weapon>(FindObjectsSortMode.None))
                        w.SetInfiniteAmmo();
                }
                break;
            }
        }

        // Shop'u kapatma — buy phase'de açık kalsın, oyuncu başka şey alabilir
        RefreshShopUI();
        GetPlayerHealth()?.UpdateHUD();
    }

    private void BuyShield(float amount, int price)
    {
        if (ScoreManager.Instance == null) return;
        if (ScoreManager.Instance.GetLocalMoney() < price) return;

        PlayerHealth ph = GetPlayerHealth();
        if (ph == null) return;
        if (ph.currentShield >= amount) return;

        ScoreManager.Instance.SpendMoney(price);
        ph.BuyShield(amount);

        RefreshShopUI();
        GetPlayerHealth()?.UpdateHUD();
    }

    // ── Ölünce Envanter Sıfırla ───────────────────────────────────────

    /// Ölünce çağrılır — tüm silahlar gider, raund başında tekrar satın alınır.
    public void ResetInventory()
    {
        Inventory inv = GetInventory();
        if (inv != null) inv.ResetWeapons();

        var characters = FindObjectsByType<Character>(FindObjectsSortMode.None);
        foreach (var c in characters)
        {
            if (c.GetComponent<PhotonView>() is PhotonView pv && pv.IsMine)
            { c.ResetWeaponSetup(); break; }
        }
    }

    // ── Yardımcılar ───────────────────────────────────────────────────

    private Inventory GetInventory()
    {
        var characters = FindObjectsByType<Character>(FindObjectsSortMode.None);
        foreach (var c in characters)
        {
            if (c.GetComponent<PhotonView>() is PhotonView pv && pv.IsMine)
                return c.GetInventory() as Inventory;
        }
        return null;
    }

    private PlayerHealth GetPlayerHealth()
    {
        var healths = FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);
        foreach (var ph in healths)
        {
            if (ph.GetComponent<PhotonView>() is PhotonView pv && pv.IsMine)
                return ph;
        }
        return null;
    }

    /// Tüm unlocked silahların mermisini sıfırlar — raund başı çağrılır.
    public void ResetAllAmmunition()
    {
        Inventory inv = GetInventory();
        if (inv == null) return;

        var characters = FindObjectsByType<Character>(FindObjectsSortMode.None);
        foreach (var c in characters)
        {
            if (c.GetComponent<PhotonView>() is PhotonView pv && pv.IsMine)
            {
                // Tüm unlocked silahların mermisini sıfırla
                var weapons = c.GetComponentsInChildren<InfimaGames.LowPolyShooterPack.Weapon>(true);
                foreach (var w in weapons)
                    w.ResetAmmunition();
                break;
            }
        }
    }
}