using UnityEngine;
using TMPro;
using Game.Match.State; // TurnController

public class TurnTimerHUD : MonoBehaviour
{
    [Header("Links")]
    [SerializeField] private TurnController turn;     // auto-found if left empty
    [SerializeField] private TMP_Text label;          // e.g., big number

    [Header("Timer")]
    [SerializeField] private float durationSeconds = 30f;
    [SerializeField] private bool autoStart = true;
    [SerializeField] private bool useUnscaledTime = false;

    float remaining;
    bool running;
    bool firedThisTurn; // prevents double EndTurn per countdown

    void Awake()
    {
        if (turn == null) turn = FindObjectOfType<TurnController>();
    }

    void OnEnable()
    {
        if (autoStart) StartTurnTimer();
        else UpdateLabel();
    }

    public void StartTurnTimer(float? overrideSeconds = null)
    {
        // Force-stop any previous tick (guards against same-frame stop/start races)
        running = false;

        remaining = overrideSeconds.HasValue ? overrideSeconds.Value : durationSeconds;
        firedThisTurn = false;
        UpdateLabel();

        // Start fresh
        running = true;
    }

    public void StopTimer()
    {
        running = false;
    }

    void Update()
    {
        if (!running) return;

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        remaining -= dt;
        if (remaining < 0f) remaining = 0f;

        UpdateLabel();

        if (remaining <= 0f && !firedThisTurn)
        {
            firedThisTurn = true;
            running = false;

            // Auto-end current turn. IMPORTANT: do NOT self-restart here.
            if (turn != null) turn.EndTurn();
        }
    }

    void UpdateLabel()
    {
        if (!label) return;
        int sec = Mathf.CeilToInt(remaining);
        label.text = sec.ToString();
    }
}
