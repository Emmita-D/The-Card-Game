using Game.Match.Mana;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ManaHUD : MonoBehaviour
{
    [Header("Source")]
    [SerializeField] ManaPool pool;

    [Header("Crystals")]
    [SerializeField] RectTransform row;
    [SerializeField] Image crystalTemplate;       // disabled prefab Image
    [SerializeField] Sprite crystalOn;
    [SerializeField] Sprite crystalOff;
    [SerializeField] int spacing = 6;             // px gap between crystals

    [Header("Label")]
    [SerializeField] TextMeshProUGUI combinedLabel;

    readonly List<Image> crystals = new();

    void Awake()
    {
        if (crystalTemplate != null) crystalTemplate.gameObject.SetActive(false);
    }

    void OnEnable()
    {
        if (pool != null) pool.OnChanged += HandlePoolChanged;
    }

    void OnDisable()
    {
        if (pool != null) pool.OnChanged -= HandlePoolChanged;
    }

    void Start()
    {
        // First-time build & paint
        if (pool != null)
        {
            BuildRow(pool.Slots);
            HandlePoolChanged(pool.Current, pool.Slots);
        }
    }

    void BuildRow(int slots)
    {
        if (row == null || crystalTemplate == null) return;

        while (crystals.Count < slots)
        {
            var img = Instantiate(crystalTemplate, row);
            img.gameObject.SetActive(true);                 // <- make the clone active ✅

            var rt = (RectTransform)img.transform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = crystalTemplate.rectTransform.sizeDelta;

            img.enabled = true;
            img.sprite = crystalOff;
            img.type = Image.Type.Simple;
            img.preserveAspect = true;

            crystals.Add(img);
        } while (crystals.Count > slots)

        {
            var last = crystals[^1];
            crystals.RemoveAt(crystals.Count - 1);
            if (last) Destroy(last.gameObject);
        }

        // Position horizontally
        float w = crystalTemplate.rectTransform.sizeDelta.x;
        for (int i = 0; i < crystals.Count; i++)
        {
            var rt = (RectTransform)crystals[i].transform;
            rt.anchoredPosition = new Vector2(i * (w + spacing), 0f);
        }
    }

    void HandlePoolChanged(int current, int slots)
    {
        // Keep row matched to slots just in case slots changed
        if (crystals.Count != slots) BuildRow(slots);

        for (int i = 0; i < crystals.Count; i++)
        {
            var img = crystals[i];
            if (!img) continue;
            img.sprite = (i < current) ? crystalOn : crystalOff;
            img.color = Color.white; // ensure no tint is hiding colors
        }

        if (combinedLabel != null)
            combinedLabel.text = $"{current}/{slots}";
    }

    // Optional helper if you wire the Pool at runtime
    public void SetPool(ManaPool p)
    {
        if (pool != null) pool.OnChanged -= HandlePoolChanged;
        pool = p;
        if (pool != null) pool.OnChanged += HandlePoolChanged;

        BuildRow(pool != null ? pool.Slots : 0);
        if (pool != null) HandlePoolChanged(pool.Current, pool.Slots);
    }
}
