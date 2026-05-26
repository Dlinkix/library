using UnityEngine;
using UnityEngine.InputSystem.Composites;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SceneSwap : MonoBehaviour
{
    public Button button;
    public int BuildScene;

    void Start()
    {
        button.onClick.AddListener(SceneChange);
        
    }

    void SceneChange()
    {
        SceneManager.LoadScene(BuildScene);
    }
}
