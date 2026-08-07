using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    [Tooltip("The campaign Play starts: which levels, in what order, and who the party begins as.")]
    [SerializeField] private RunData campaign;

    public void exitButton(){
        Application.Quit();
        Debug.Log("Game Closed");
    }

    /// <summary>
    /// Starts a fresh run, then loads the first level.
    ///
    /// StartRun before the load, not after: it resets the RunManager, and a previous run that ended in
    /// defeat would otherwise still be sitting there for BattleManager to resume from - dead party,
    /// level three, no way back.
    /// </summary>
    public void playButton()
    {
        RunManager.StartRun(campaign);

        SceneManager.LoadScene("Game");
    }
}
