using UnityEngine;

public sealed class IntroShipBob : MonoBehaviour
{
    [SerializeField]
    private float bobHeight = 8f;

    [SerializeField]
    private float bobSpeed = 1f;

    [SerializeField]
    private float rockAmount = 1.5f;

    private RectTransform rectTransform;
    private Vector2 startingPosition;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        startingPosition = rectTransform.anchoredPosition;
    }

    private void Update()
    {
        float wave = Mathf.Sin(Time.unscaledTime * bobSpeed);

        rectTransform.anchoredPosition =
            startingPosition + Vector2.up * (wave * bobHeight);

        rectTransform.localRotation =
            Quaternion.Euler(0f, 0f, wave * rockAmount);
    }
}