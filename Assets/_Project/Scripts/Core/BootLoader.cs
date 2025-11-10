using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Core.Boot
{
    /// <summary>
    /// Loads the initial gameplay scene (CardPhase) additively.
    /// This lives in the Boot scene.
    /// </summary>
    public class BootLoader : MonoBehaviour
    {
        [SerializeField] private string initialSceneName = "CardPhase";

        private void Start()
        {
            if (string.IsNullOrWhiteSpace(initialSceneName))
            {
                Debug.LogError("[BootLoader] Initial scene name is empty.");
                return;
            }

            // Load the card phase on top of Boot
            SceneManager.LoadSceneAsync(initialSceneName, LoadSceneMode.Additive);
        }
    }
}
