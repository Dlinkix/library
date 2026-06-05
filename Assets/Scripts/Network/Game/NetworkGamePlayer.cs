using Mirror;
using UnityEngine;

public class NetworkGamePlayer : NetworkBehaviour
{
    [SyncVar] public string PlayerName = "Unknown";
    [SyncVar] private Vector3 spawnPosition;
    private GameObject uiObject;
    private bool uiCreated = false;

    public override void OnStartServer()
    {
        spawnPosition = transform.position;
    }

    public override void OnStartClient()
    {
        CreateUI();
    }

    private void CreateUI()
    {
        if (uiCreated) return;

        GameObject uiPrefab = Resources.Load<GameObject>("UI/PlayerUI");
        if (uiPrefab == null) return;

        uiObject = Instantiate(uiPrefab);

        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas != null)
        {
            uiObject.transform.SetParent(canvas.transform, false);
        }

        uiObject.transform.position = spawnPosition;

        uiCreated = true;
    }

    public override void OnStopClient()
    {
        if (uiObject != null)
            Destroy(uiObject);
    }
}