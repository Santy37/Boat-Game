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
    private float titleDisplayDuration = 2f;



    [Header("Scene")]
    [SerializeField]
    private string mainMenuSceneName = "MainMenu";

    private readonly string[] storyLines =
    {
        "Every soul owes the sea a debt",

        "A pirate once challenged the creature beneath the waves",

        "He survived—but the Kraken claimed his soul",

        "Bound to a cursed vessel, he sails in search of freedom"
    };

    private bool canSkip;
    private bool loadingScene;

    private void Awake()
    {
        if (storyCanvasGroup != null)
        {
            // The first story line begins visible.
            storyCanvasGroup.alpha = 1f;
        }

        if (titleCanvasGroup != null)
        {
            // The title remains hidden until the story finishes.
            titleCanvasGroup.alpha = 0f;
        }

        if (skipText != null)
        {
            skipText.gameObject.SetActive(true);
        }
    }

    private void Start()
    {
        if (
            storyText == null ||
            storyCanvasGroup == null ||
            titleCanvasGroup == null
        )
        {
            Debug.LogError(
                "[Intro Sequence] Required UI references are missing.",
                this
            );

            return;
        }

        storyText.text = storyLines[0];

        StartCoroutine(PlayIntro());
      
    }

    private void Update()
    {
        if (
            !canSkip ||
            loadingScene ||
            Keyboard.current == null
        )
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;

        bool skipPressed =
            keyboard.spaceKey.wasPressedThisFrame ||
            keyboard.escapeKey.wasPressedThisFrame ||
            keyboard.enterKey.wasPressedThisFrame ||
            keyboard.numpadEnterKey.wasPressedThisFrame;

        if (skipPressed)
        {
            LoadMainMenu();
        }
    }

    private IEnumerator PlayIntro()
    {
        // Prevent the key that opened the scene from immediately skipping it.
        yield return null;
        canSkip = true;

        // The first story line is already visible.
        yield return new WaitForSeconds(displayDuration);

        yield return FadeCanvasGroup(
            storyCanvasGroup,
            storyCanvasGroup.alpha,
            0f
        );

        // Display and fade all remaining story lines.
        for (int i = 1; i < storyLines.Length; i++)
        {
            storyText.text = storyLines[i];

            yield return FadeCanvasGroup(
                storyCanvasGroup,
                0f,
                1f
            );

            yield return new WaitForSeconds(displayDuration);

            yield return FadeCanvasGroup(
                storyCanvasGroup,
                1f,
                0f
            );
        }

        storyText.gameObject.SetActive(false);

        // Fade the title in last.
        yield return FadeCanvasGroup(
            titleCanvasGroup,
            0f,
            1f
        );

        // Keep the title visible for two seconds.
        yield return new WaitForSeconds(titleDisplayDuration);

        // Automatically switch to the Main Menu.
        LoadMainMenu();
    }

    

    private IEnumerator FadeCanvasGroup(
        CanvasGroup canvasGroup,
        float startingAlpha,
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

        float elapsedTime = 0f;
        canvasGroup.alpha = startingAlpha;

        while (elapsedTime < fadeDuration)
        {
            elapsedTime += Time.deltaTime;

            canvasGroup.alpha = Mathf.Lerp(
                startingAlpha,
                targetAlpha,
                elapsedTime / fadeDuration
            );

            yield return null;
        }

        canvasGroup.alpha = targetAlpha;
    }

    private void LoadMainMenu()
    {
        if (loadingScene)
        {
            return;
        }

        loadingScene = true;
        StopAllCoroutines();
        SceneManager.LoadScene(mainMenuSceneName);
    }
}