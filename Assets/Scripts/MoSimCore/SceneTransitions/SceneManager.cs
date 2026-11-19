using System.Collections;
using System.Linq;
using Eflatun.SceneReference;
using UnityEngine;
using Debug = System.Diagnostics.Debug;

namespace MoSimCore.SceneTransitions
{
    /// <summary>
    /// Manages scene loading and transitions with animation effects.
    /// Implements the singleton pattern and persists across scene changes.
    /// </summary>
    public class SceneManager : MonoBehaviour
    {
        /// <summary>Gets the singleton instance of the SceneManager.</summary>
        public static SceneManager Instance { get; private set; }

        public bool IsLoading { get; private set; }

        [Tooltip("GameObject containing all available SceneTransition components as children.")]
        public GameObject transitionsContainer;
        
        private SceneTransition[] _transitions;

        /// <summary>
        /// Initializes the singleton instance and caches available transitions.
        /// </summary>
        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
            
            _transitions = transitionsContainer.GetComponentsInChildren<SceneTransition>();
        }
        
        /// <summary>
        /// Loads a scene with a transition animation.
        /// </summary>
        /// <param name="scene">The SceneReference to the scene to load.</param>
        /// <param name="transitionName">The name of the transition GameObject to use.</param>
        /// <param name="delayAfterLoad">Delay in seconds after scene loads before transitioning out.</param>
        public void LoadScene(SceneReference scene, string transitionName, float delayAfterLoad = 0.5f)
        {
            LoadScene(scene.Name, transitionName, delayAfterLoad);
        }

        /// <summary>
        /// Loads a scene with a transition animation.
        /// </summary>
        /// <param name="sceneName">The name of the scene to load.</param>
        /// <param name="transitionName">The name of the transition GameObject to use.</param>
        /// <param name="delayAfterLoad">Delay in seconds after scene loads before transitioning out.</param>
        public void LoadScene(string sceneName, string transitionName, float delayAfterLoad = 0.5f)
        {
            if (IsLoading)
            {
                return;
            }

            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                throw new System.ArgumentException($"'{sceneName}' is not a scene in the build.", nameof(sceneName));
            }

            var transition = _transitions.First(t => t.name == transitionName);

            IsLoading = true;
            StartCoroutine(LoadSceneAsync(sceneName, transition, delayAfterLoad));
        }

        /// <summary>
        /// Plays a full transition animation (in and out).
        /// </summary>
        /// <param name="transitionName">The name of the transition GameObject to play.</param>
        public void PlayTransition(string transitionName)
        {
            StartCoroutine(Transition(transitionName));
        }

        /// <summary>
        /// Executes a complete transition (in then out) without loading a scene.
        /// </summary>
        /// <param name="transitionName">The name of the transition GameObject to use.</param>
        /// <returns>An enumerator for coroutine execution.</returns>
        public IEnumerator Transition(string transitionName)
        {
            var transition = _transitions.First(t => t.name == transitionName);
            
            yield return transition.AnimateTransitionIn();
            
            yield return transition.AnimateTransitionOut();
        }

        /// <summary>
        /// Executes a single-direction transition (either in or out).
        /// Only works with CrossFade transitions.
        /// </summary>
        /// <param name="transitionIn">If true, animates transition in; if false, animates transition out.</param>
        /// <param name="transitionName">The name of the CrossFade transition GameObject to use.</param>
        /// <param name="initialAlpha">Optional initial alpha value to set before animating (-1 to skip).</param>
        /// <returns>An enumerator for coroutine execution.</returns>
        public IEnumerator Transition(bool transitionIn, string transitionName, float initialAlpha = -1)
        {
            var transition = _transitions.First(t => t.name == transitionName);
            var crossFade = transition as CrossFade;
            if (crossFade != null)
            {
                if (!Mathf.Approximately(initialAlpha, -1))
                {
                    crossFade.SetInitialAlpha(initialAlpha);
                }
                
                if (transitionIn)
                {
                    yield return crossFade.AnimateTransitionIn();
                }
                else
                {
                    yield return crossFade.AnimateTransitionOut();
                }
            }
        }

        /// <summary>
        /// Asynchronously loads a scene with transition animations.
        /// Waits for the scene to fully load before transitioning out.
        /// </summary>
        /// <param name="sceneName">The name of the scene to load.</param>
        /// <param name="transition">The transition to use.</param>
        /// <param name="delayAfterLoad">Delay in seconds after scene loads before transitioning out.</param>
        /// <returns>An enumerator for coroutine execution.</returns>
        private IEnumerator LoadSceneAsync(string sceneName, SceneTransition transition, float delayAfterLoad)
        {
            yield return transition.AnimateTransitionIn();

            var scene = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(sceneName);

            Debug.Assert(scene != null, nameof(scene) + " != null");

            scene.completed += _ => IsLoading = false;

            while (!scene.isDone)
            {
                yield return null;
            }

            yield return new WaitForSeconds(delayAfterLoad);
            
            yield return transition.AnimateTransitionOut();
        }
    }
}