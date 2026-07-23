using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{

    public void exitButton(){
        Application.Quit();
        Debug.Log("Game Closed");
    }

    public void playButton()
    {
        SceneManager.LoadScene("Game");
    }
}
