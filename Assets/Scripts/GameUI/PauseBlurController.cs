using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public class PauseBlurController : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private Volume pauseVolume;
    [SerializeField] private float transitionDuration = 0.2f; // Time in seconds to fade

    private Coroutine fadeCoroutine;

    /// <summary>
    /// Call this from your Pause Manager when toggling pause state.
    /// </summary>
    public void SetPauseBlur(bool isPaused)
    {
        float targetWeight = isPaused ? 1f : 0f;

        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
        }

        fadeCoroutine = StartCoroutine(FadeVolumeWeight(targetWeight));
    }

    private IEnumerator FadeVolumeWeight(float targetWeight)
    {
        float startWeight = pauseVolume.weight;
        float elapsed = 0f;

        while (elapsed < transitionDuration)
        {
            // CRITICAL: Must use unscaledDeltaTime when Time.timeScale is 0
            elapsed += Time.unscaledDeltaTime;

            float progress = Mathf.Clamp01(elapsed / transitionDuration);
            // SmoothStep provides a natural ease-in / ease-out transition curve
            pauseVolume.weight = Mathf.SmoothStep(startWeight, targetWeight, progress);

            yield return null;
        }

        pauseVolume.weight = targetWeight;
    }
}