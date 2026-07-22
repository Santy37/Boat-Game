using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The map screen, drawn as a node graph inside a framed panel: your current
/// island sits at the bottom, routes branch upward, and the boss sits at the
/// top.
///
/// Every joined player picks their OWN island from the row above using their
/// Left/Right keys, then presses Interact to lock it in. Once everyone has
/// locked in, the island with the most votes wins - ties are broken randomly.
///
/// The panel is deliberately built as a template: assign <see cref="panelTexture"/>
/// (and later node artwork) to restyle it without touching any logic.
/// </summary>
public class RunMap : MonoBehaviour
{
    [Header("Screen")]
    [Tooltip("Fill the whole screen behind the map.")]
    [SerializeField] private bool drawBackground = true;
    [SerializeField] private Color backgroundColor = new Color(0.07f, 0.16f, 0.34f, 1f);

    [Header("Map Panel")]
    [Tooltip("Optional artwork for the panel. Leave empty for a flat colour.")]
    [SerializeField] private Texture2D panelTexture;
    [SerializeField] private Vector2 panelSize = new Vector2(1120f, 720f);
    [Tooltip("Offset from the centre of the screen.")]
    [SerializeField] private Vector2 panelOffset = new Vector2(0f, 40f);
    [SerializeField] private Color panelColor = new Color(0.09f, 0.11f, 0.15f, 0.96f);
    [SerializeField] private Color panelBorderColor = new Color(0.55f, 0.45f, 0.25f);
    [SerializeField] private float panelBorderWidth = 3f;

    [Header("Node Layout (inside the panel)")]
    [SerializeField] private Vector2 nodeSize = new Vector2(150f, 62f);
    [Tooltip("Vertical gap between map rows.")]
    [SerializeField] private float levelSpacing = 150f;
    [Tooltip("Horizontal gap between islands on the same row.")]
    [SerializeField] private float slotSpacing = 230f;
    [Tooltip("Distance from the BOTTOM of the panel to the first row.")]
    [SerializeField] private float bottomMargin = 90f;

    [Header("Status Panel")]
    [Tooltip("Offset from the top centre of the screen.")]
    [SerializeField] private Vector2 statusOffsetFromTopCenter = new Vector2(0f, 12f);
    [SerializeField] private float statusWidth = 480f;

    [Header("Node Look")]
    [SerializeField] private Color nodeColor = new Color(0.20f, 0.72f, 0.35f);
    [SerializeField] private Color currentColor = new Color(0.35f, 0.45f, 1f);
    [SerializeField] private Color choiceColor = new Color(1f, 0.80f, 0.20f);
    [SerializeField] private Color lineColor = new Color(0.75f, 0.75f, 0.75f, 1f);
    [SerializeField] private float lineWidth = 4f;
    [Tooltip("Marker drawn on the island you are standing on.")]
    [SerializeField] private string currentLabel = "★";

    private List<RunMapNode> choices = new List<RunMapNode>();

    /// <summary>Which choice index each player is voting for.</summary>
    private int[] playerChoice;
    private bool[] ready;
    private bool confirmed;

    private Texture2D pixel;
    private readonly Dictionary<int, int> levelCounts = new Dictionary<int, int>();

    private void Awake()
    {
        pixel = new Texture2D(1, 1);
        pixel.SetPixel(0, 0, Color.white);
        pixel.Apply();
    }

    private void OnDestroy()
    {
        if (pixel != null)
        {
            Destroy(pixel);
        }
    }

    private void Start()
    {
        if (!RunContext.HasActive)
        {
            Debug.LogError(
                "[Run Map] No active run. Start from the menu so a run " +
                "manager exists.",
                this);
            return;
        }

        choices = RunContext.Active.Map.NextFrom(RunContext.Active.CurrentNodeId);

        int seats = RunContext.Active.MaxPlayers;
        playerChoice = new int[seats];
        ready = new bool[seats];

        CacheLevelCounts();
    }

    private void CacheLevelCounts()
    {
        levelCounts.Clear();

        foreach (RunMapNode node in RunContext.Active.Map.Nodes)
        {
            levelCounts.TryGetValue(node.level, out int count);
            levelCounts[node.level] = count + 1;
        }
    }

    private void Update()
    {
        if (confirmed || !RunContext.HasActive || choices.Count == 0)
        {
            return;
        }

        int seats = RunContext.Active.MaxPlayers;

        for (int i = 0; i < seats; i++)
        {
            if (!RunContext.Active.IsJoined(i))
            {
                continue;
            }

            KeyBindings binding = RunContext.Active.GetBindings(i);
            if (binding == null)
            {
                continue;
            }

            // Changing your pick unlocks you again.
            if (binding.LeftDown())
            {
                playerChoice[i] = (playerChoice[i] - 1 + choices.Count) % choices.Count;
                ready[i] = false;
            }

            if (binding.RightDown())
            {
                playerChoice[i] = (playerChoice[i] + 1) % choices.Count;
                ready[i] = false;
            }

            if (binding.InteractDown())
            {
                ready[i] = !ready[i];
            }
        }

        if (!AllJoinedReady())
        {
            return;
        }

        confirmed = true;
        RunContext.Active.ChooseDestination(choices[WinningChoice()].id);
    }

    /// <summary>
    /// Most votes wins. If several islands tie, one of them is picked at
    /// random so no player's vote is inherently worth more.
    /// </summary>
    private int WinningChoice()
    {
        int[] votes = CountVotes();

        int best = 0;
        for (int i = 1; i < votes.Length; i++)
        {
            if (votes[i] > votes[best])
            {
                best = i;
            }
        }

        List<int> tied = new List<int>();
        for (int i = 0; i < votes.Length; i++)
        {
            if (votes[i] == votes[best])
            {
                tied.Add(i);
            }
        }

        return tied.Count == 1 ? tied[0] : tied[Random.Range(0, tied.Count)];
    }

    private int[] CountVotes()
    {
        int[] votes = new int[Mathf.Max(1, choices.Count)];

        for (int i = 0; i < RunContext.Active.MaxPlayers; i++)
        {
            if (RunContext.Active.IsJoined(i) && choices.Count > 0)
            {
                votes[playerChoice[i]]++;
            }
        }

        return votes;
    }

    private bool AllJoinedReady()
    {
        if (RunContext.Active.JoinedCount == 0)
        {
            return false;
        }

        for (int i = 0; i < ready.Length; i++)
        {
            if (RunContext.Active.IsJoined(i) && !ready[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The framed area the map is drawn inside.</summary>
    private Rect MapPanel()
    {
        return new Rect(
            (Screen.width - panelSize.x) * 0.5f + panelOffset.x,
            (Screen.height - panelSize.y) * 0.5f + panelOffset.y,
            panelSize.x,
            panelSize.y);
    }

    // Level 0 (the lobby island) sits at the panel's bottom; boss at the top.
    private Vector2 NodeCentre(RunMapNode node)
    {
        levelCounts.TryGetValue(node.level, out int count);
        if (count <= 0)
        {
            count = 1;
        }

        Rect panel = MapPanel();

        float x = panel.center.x +
                  (node.slot - (count - 1) * 0.5f) * slotSpacing;

        float y = panel.yMax - bottomMargin - node.level * levelSpacing;

        return new Vector2(x, y);
    }

    private int ChoiceIndexOf(RunMapNode node)
    {
        for (int i = 0; i < choices.Count; i++)
        {
            if (choices[i].id == node.id)
            {
                return i;
            }
        }

        return -1;
    }

    private void OnGUI()
    {
        if (!RunContext.HasActive || ready == null)
        {
            return;
        }

        if (drawBackground)
        {
            Fill(new Rect(0f, 0f, Screen.width, Screen.height), backgroundColor);
        }

        DrawMapPanel();

        RunMapModel map = RunContext.Active.Map;
        int currentId = RunContext.Active.CurrentNodeId;
        int[] votes = CountVotes();

        // 1. Connecting lines first, so nodes draw on top of them.
        foreach (RunMapNode node in map.Nodes)
        {
            Vector2 from = NodeCentre(node);

            foreach (int nextId in node.next)
            {
                RunMapNode target = map.Get(nextId);
                if (target == null)
                {
                    continue;
                }

                DrawLine(from, NodeCentre(target), lineColor, lineWidth);
            }
        }

        // 2. Nodes.
        foreach (RunMapNode node in map.Nodes)
        {
            Vector2 centre = NodeCentre(node);

            Rect box = new Rect(
                centre.x - nodeSize.x * 0.5f,
                centre.y - nodeSize.y * 0.5f,
                nodeSize.x,
                nodeSize.y);

            bool isCurrent = node.id == currentId;
            int choiceIndex = ChoiceIndexOf(node);

            Color tint = nodeColor;
            if (isCurrent) tint = currentColor;
            else if (choiceIndex >= 0) tint = choiceColor;

            Color saved = GUI.color;
            GUI.color = tint;
            GUI.Box(box, string.Empty);
            GUI.color = saved;

            string label = isCurrent ? currentLabel : node.kind.ToString();
            GUI.Label(new Rect(box.x + 10f, box.y + 8f, box.width - 20f, 22f), label);

            if (choiceIndex >= 0)
            {
                GUI.Label(
                    new Rect(box.x + 10f, box.y + 30f, box.width - 20f, 22f),
                    $"votes: {votes[choiceIndex]}");

                DrawVoterMarkers(box, choiceIndex);
            }
        }

        DrawStatusPanel();
    }

    /// <summary>
    /// The backdrop the map sits on. Swap in <see cref="panelTexture"/> (a
    /// parchment or wood sprite) to restyle without touching layout.
    /// </summary>
    private void DrawMapPanel()
    {
        Rect panel = MapPanel();

        if (panelTexture != null)
        {
            Color saved = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(panel, panelTexture, ScaleMode.StretchToFill);
            GUI.color = saved;
            return;
        }

        Fill(panel, panelColor);

        if (panelBorderWidth > 0f)
        {
            float w = panelBorderWidth;
            Fill(new Rect(panel.x, panel.y, panel.width, w), panelBorderColor);
            Fill(new Rect(panel.x, panel.yMax - w, panel.width, w), panelBorderColor);
            Fill(new Rect(panel.x, panel.y, w, panel.height), panelBorderColor);
            Fill(new Rect(panel.xMax - w, panel.y, w, panel.height), panelBorderColor);
        }
    }

    /// <summary>Coloured chips under a choice showing who voted for it.</summary>
    private void DrawVoterMarkers(Rect box, int choiceIndex)
    {
        const float chip = 26f;
        const float gap = 4f;

        List<int> voters = new List<int>();
        for (int i = 0; i < RunContext.Active.MaxPlayers; i++)
        {
            if (RunContext.Active.IsJoined(i) && playerChoice[i] == choiceIndex)
            {
                voters.Add(i);
            }
        }

        if (voters.Count == 0)
        {
            return;
        }

        float totalWidth = voters.Count * chip + (voters.Count - 1) * gap;
        float x = box.center.x - totalWidth * 0.5f;
        float y = box.yMax + 6f;

        Color saved = GUI.color;

        foreach (int playerIndex in voters)
        {
            KeyBindings binding = RunContext.Active.GetBindings(playerIndex);
            Color chipColor = binding != null ? binding.color : Color.white;

            // Dim until that player has locked their choice in.
            if (!ready[playerIndex])
            {
                chipColor.a = 0.45f;
            }

            GUI.color = chipColor;
            GUI.DrawTexture(new Rect(x, y, chip, chip), pixel);
            GUI.color = Color.black;
            GUI.Label(new Rect(x + 6f, y + 3f, chip, 20f), $"P{playerIndex + 1}");
            GUI.color = saved;

            x += chip + gap;
        }
    }

    private void DrawStatusPanel()
    {
        float height = 78f + 26f * RunContext.Active.MaxPlayers;

        Rect panel = new Rect(
            (Screen.width - statusWidth) * 0.5f + statusOffsetFromTopCenter.x,
            statusOffsetFromTopCenter.y,
            statusWidth,
            height);

        GUI.Box(panel, "Vote for your destination");

        float y = panel.y + 28f;

        GUI.Label(
            new Rect(panel.x + 12f, y, statusWidth - 24f, 24f),
            $"Stage {RunContext.Active.Stage}");

        y += 24f;

        GUI.Label(
            new Rect(panel.x + 12f, y, statusWidth - 24f, 24f),
            "Left / Right picks your island    Interact locks it in");

        y += 26f;

        for (int i = 0; i < RunContext.Active.MaxPlayers; i++)
        {
            if (!RunContext.Active.IsJoined(i))
            {
                continue;
            }

            KeyBindings binding = RunContext.Active.GetBindings(i);
            string who = binding != null ? binding.displayName : $"Player {i + 1}";

            string pick = choices.Count > 0
                ? choices[playerChoice[i]].kind.ToString()
                : "-";

            GUI.Label(
                new Rect(panel.x + 12f, y, statusWidth - 24f, 24f),
                $"{who}: {pick}   {(ready[i] ? "[LOCKED]" : "choosing...")}");

            y += 26f;
        }
    }

    private void Fill(Rect rect, Color color)
    {
        Color saved = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, pixel);
        GUI.color = saved;
    }

    // OnGUI has no line primitive: stretch a 1x1 texture and rotate it.
    private void DrawLine(Vector2 a, Vector2 b, Color color, float width)
    {
        Matrix4x4 savedMatrix = GUI.matrix;
        Color savedColor = GUI.color;

        Vector2 delta = b - a;
        float length = delta.magnitude;
        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

        GUI.color = color;
        GUIUtility.RotateAroundPivot(angle, a);

        GUI.DrawTexture(
            new Rect(a.x, a.y - width * 0.5f, length, width), pixel);

        GUI.matrix = savedMatrix;
        GUI.color = savedColor;
    }
}
