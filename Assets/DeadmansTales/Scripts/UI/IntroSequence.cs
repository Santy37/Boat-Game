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
        StartCoroutine(PlayIntro());
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
        if (storyText == null || storyCanvasGroup == null)
        {
            Debug.LogError(
                "[Intro Sequence] Story Text or Canvas Group is missing.",
                this
            );

            yield break;
        }

        foreach (string line in storyLines)
        {
            storyText.text = line;

            yield return FadeTo(1f);
            yield return new WaitForSecondsRealtime(displayDuration);
            yield return FadeTo(0f);
        }

        LoadMainMenu();
    }

    private IEnumerator FadeTo(float targetAlpha)
    {
        float startingAlpha = storyCanvasGroup.alpha;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;

            storyCanvasGroup.alpha = Mathf.Lerp(
                startingAlpha,
                targetAlpha,
                elapsed / fadeDuration
            );

            yield return null;
        }

        storyCanvasGroup.alpha = targetAlpha;
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
}