using System.Collections;
using UnityEngine;

[RequireComponent(typeof(CanvasGroup))]
public sealed class LevelStartUI : MonoBehaviour
{
    [SerializeField]
    [Min(0f)]
    private float visibleDuration = 5f;

    [SerializeField]
    [Min(0f)]
    private float fadeDuration = 1f;

    private CanvasGroup canvasGroup;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    private IEnumerator Start()
    {
        yield return new WaitForSecondsRealtime(visibleDuration);

        float elapsedTime = 0f;

        while (elapsedTime < fadeDuration)
        {
            elapsedTime += Time.unscaledDeltaTime;

            canvasGroup.alpha = Mathf.Lerp(
                1f,
                0f,
                elapsedTime / fadeDuration
            );

            yield return null;
        }

        canvasGroup.alpha = 0f;
        gameObject.SetActive(false);
    }
}