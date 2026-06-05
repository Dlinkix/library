using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class JoinLobbyMenu : MonoBehaviour
{
    [SerializeField] private NetworkManagerLobby networkManager = null;

    [Header("Ui")]
    [SerializeField] private GameObject landingPagePanel = null;
    [SerializeField] private TMP_InputField ipAddressInputField = null;
    [SerializeField] private Button joinButton = null;

    private void OnEnable()
    {
        NetworkManagerLobby.OnClientConnected += HadleClientConnected;
        NetworkManagerLobby.OnClientDisconnected += HandleClientDisconnected;
    }

    private void OnDisable()
    {
        NetworkManagerLobby.OnClientConnected -= HadleClientConnected;
        NetworkManagerLobby.OnClientDisconnected -= HandleClientDisconnected;
    }

    public void JoinLobby()
    {
        if (networkManager == null) return;

        string ipAddress = ipAddressInputField.text;
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            ipAddress = "localhost";
            if (ipAddressInputField != null)
                ipAddressInputField.text = ipAddress;
        }

        networkManager.networkAddress = ipAddress;
        networkManager.StartClient();

        if (joinButton != null)
            joinButton.interactable = false;
    }

    private void HadleClientConnected()
    {
        if (joinButton != null)
            joinButton.interactable = true;

        gameObject.SetActive(false);

        if (landingPagePanel != null)
            landingPagePanel.SetActive(false);
    }

    private void HandleClientDisconnected()
    {
        if (joinButton != null)
            joinButton.interactable = true;
    }
}