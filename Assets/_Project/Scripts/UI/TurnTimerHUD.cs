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
        remaining = overrideSeconds.HasValue ? overrideSeconds.Value : durationSeconds;
        running = true;
        firedThisTurn = false;
        UpdateLabel();
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

            // Auto-end current turn
            if (turn != null) turn.EndTurn();

            // Immediately start next turn’s countdown (idempotent even if TurnController also starts it)
            StartTurnTimer();
        }
    }

    void UpdateLabel()
    {
        if (!label) return;
        int sec = Mathf.CeilToInt(remaining);
        label.text = sec.ToString();
    }
}
