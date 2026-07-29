using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public sealed class IntroSequence : MonoBehaviour
{
    [Header("UI")]
    [SerializeField]
    private TextMeshProUGUI storyText;

    [SerializeField]
    private CanvasGroup storyCanvasGroup;

    [Header("Timing")]
    [SerializeField]
    private float fadeDuration = 1f;

    [SerializeField]
    private CanvasGroup titleCanvasGroup;

    [SerializeField]
    private CanvasGroup skipCanvasGroup;

    [SerializeField]
    private float skipTextVisibleDuration = 2f;

    [SerializeField]
    private float displayDuration = 2.5f;

    [Header("Scene")]
    [SerializeField]
    private string mainMenuSceneName = "MainMenu";

    private readonly string[] storyLines =
    {
        "Every soul owes the sea a debt",

        "A pirate once challenged the creature beneath the waves",

        "He survived—but the Kraken claimed his soul",

        "Bound to a cursed vessel, he must conquer haunted islands and destroy the monsters of the sea",

        "Only the Kraken's defeat can set him free"
    };

    private bool isLoading;

    private void Start()
    {
        Time.timeScale = 1f;

        if (skipCanvasGroup != null)
        {
            skipCanvasGroup.alpha = 1f;
        }

        StartCoroutine(PlayIntro());
        StartCoroutine(FadeOutSkipText());
    }

    private void Update()
    {
        if (isLoading)
        {
            return;
        }

        bool skipPressed =
            Keyboard.current != null &&
            (
                Keyboard.current.spaceKey.wasPressedThisFrame ||
                Keyboard.current.enterKey.wasPressedThisFrame ||
                Keyboard.current.escapeKey.wasPressedThisFrame
            );

        if (skipPressed)
        {
            LoadMainMenu();
        }
    }

    private IEnumerator PlayIntro()
    {
        if (
            storyText == null ||
            storyCanvasGroup == null ||
            titleCanvasGroup == null
        )
        {
            Debug.LogError(
                "[Intro Sequence] A UI reference is missing.",
                this
            );

            yield break;
        }

        titleCanvasGroup.alpha = 0f;

        foreach (string line in storyLines)
        {
            storyText.text = line;

            yield return FadeCanvasGroup(storyCanvasGroup, 1f);
            yield return new WaitForSecondsRealtime(displayDuration);
            yield return FadeCanvasGroup(storyCanvasGroup, 0f);
        }

        yield return FadeCanvasGroup(titleCanvasGroup, 1f);
        yield return new WaitForSecondsRealtime(3f);
        yield return FadeCanvasGroup(titleCanvasGroup, 0f);

        LoadMainMenu();
    }

    private IEnumerator FadeCanvasGroup(
     CanvasGroup canvasGroup,
     float targetAlpha
 )
    {
        float startingAlpha = canvasGroup.alpha;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;

            canvasGroup.alpha = Mathf.Lerp(
                startingAlpha,
                targetAlpha,
                elapsed / fadeDuration
            );

            yield return null;
        }

        canvasGroup.alpha = targetAlpha;
    }

    private void LoadMainMenu()
    {
        if (isLoading)
        {
            return;
        }

        isLoading = true;
        SceneManager.LoadScene(mainMenuSceneName);
    }

    private IEnumerator FadeOutSkipText()
    {
        if (skipCanvasGroup == null)
        {
            yield break;
        }

        yield return new WaitForSecondsRealtime(
            skipTextVisibleDuration
        );

        yield return FadeCanvasGroup(
            skipCanvasGroup,
            0f
        );
    }
}