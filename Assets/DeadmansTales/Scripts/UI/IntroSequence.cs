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

    [SerializeField]
    private CanvasGroup titleCanvasGroup;

    [SerializeField]
    private TextMeshProUGUI skipText;

    [Header("Timing")]
    [SerializeField]
    [Min(0f)]
    private float fadeDuration = 1f;

    [SerializeField]
    [Min(0f)]
    private float displayDuration = 2.5f;

    [SerializeField]
    [Min(0f)]
    private float titleDisplayDuration = 3f;

    [SerializeField]
    [Min(0f)]
    private float skipTextVisibleDuration = 2f;

    [Header("Scene")]
    [SerializeField]
    private string mainMenuSceneName = "MainMenu";

    private readonly string[] storyLines =
    {
        "Every soul owes the sea a debt",

        "A pirate once challenged the creature beneath the waves",

        "He survived—but the Kraken claimed his soul",

        "Bound to a cursed vessel, he must conquer haunted islands " +
        "and destroy the monsters of the sea",

        "Only the Kraken's defeat can set him free"
    };

    private bool isLoading;

    private void Start()
    {
        Time.timeScale = 1f;

        // StoryText already starts visible in the Inspector.

        if (titleCanvasGroup != null)
        {
            titleCanvasGroup.alpha = 0f;
        }

        if (skipText != null)
        {
            SetTextAlpha(skipText, 1f);
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

        bool keyboardSkip =
            Keyboard.current != null &&
            (
                Keyboard.current.spaceKey.wasPressedThisFrame ||
                Keyboard.current.enterKey.wasPressedThisFrame ||
                Keyboard.current.escapeKey.wasPressedThisFrame
            );

        bool gamepadSkip =
            Gamepad.current != null &&
            Gamepad.current.buttonSouth.wasPressedThisFrame;

        if (keyboardSkip || gamepadSkip)
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
                "[Intro Sequence] Story Text, Story Canvas Group, " +
                "or Title Canvas Group is missing.",
                this
            );

            yield break;
        }

        foreach (string line in storyLines)
        {
            if (isLoading)
            {
                yield break;
            }

            storyText.text = line;

            yield return FadeCanvasGroup(
                storyCanvasGroup,
                1f
            );

            yield return new WaitForSecondsRealtime(
                displayDuration
            );

            yield return FadeCanvasGroup(
                storyCanvasGroup,
                0f
            );
        }

        if (isLoading)
        {
            yield break;
        }

        yield return FadeCanvasGroup(
            titleCanvasGroup,
            1f
        );

        yield return new WaitForSecondsRealtime(
            titleDisplayDuration
        );

        yield return FadeCanvasGroup(
            titleCanvasGroup,
            0f
        );

        LoadMainMenu();
    }

    private IEnumerator FadeCanvasGroup(
        CanvasGroup canvasGroup,
        float targetAlpha
    )
    {
        if (canvasGroup == null)
        {
            yield break;
        }

        if (fadeDuration <= 0f)
        {
            canvasGroup.alpha = targetAlpha;
            yield break;
        }

        float startingAlpha = canvasGroup.alpha;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            if (isLoading)
            {
                yield break;
            }

            elapsed += Time.unscaledDeltaTime;

            float progress = Mathf.Clamp01(
                elapsed / fadeDuration
            );

            canvasGroup.alpha = Mathf.Lerp(
                startingAlpha,
                targetAlpha,
                progress
            );

            yield return null;
        }

        canvasGroup.alpha = targetAlpha;
    }

    private IEnumerator FadeOutSkipText()
    {
        if (skipText == null)
        {
            yield break;
        }

        yield return new WaitForSecondsRealtime(
            skipTextVisibleDuration
        );

        if (fadeDuration <= 0f)
        {
            SetTextAlpha(skipText, 0f);
            yield break;
        }

        float startingAlpha = skipText.color.a;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            if (isLoading)
            {
                yield break;
            }

            elapsed += Time.unscaledDeltaTime;

            float progress = Mathf.Clamp01(
                elapsed / fadeDuration
            );

            float alpha = Mathf.Lerp(
                startingAlpha,
                0f,
                progress
            );

            SetTextAlpha(skipText, alpha);

            yield return null;
        }

        SetTextAlpha(skipText, 0f);
    }

    private static void SetTextAlpha(
        TextMeshProUGUI text,
        float alpha
    )
    {
        if (text == null)
        {
            return;
        }

        Color color = text.color;
        color.a = Mathf.Clamp01(alpha);
        text.color = color;
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