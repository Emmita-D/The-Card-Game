// Assets/_Project/Scripts/UI/CardView.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Reflection;
using Game.Match.Cards;
using Game.Core;

public class CardView : MonoBehaviour
{
    [Header("Prefab references")]
    [SerializeField] private Image frame;               // SOLID border (root Image on CardView)
    [SerializeField] private Image art;                 // Art
    [SerializeField] private Transform manaPips;        // Parent; contains disabled "ManaPip" template
    [SerializeField] private Image manaPipTemplateImg;  // Image on the ManaPip template (child)
    [SerializeField] private TMP_Text title;            // Title
    [SerializeField] private Image titleFrameImg;       // Image on TitleFrame background
    [SerializeField] private TMP_Text effectText;       // Effect text
    [SerializeField] private Image effectAreaBg;        // Effect panel bg (already swaps by realm)

    [SerializeField] private GameObject atkBadge; [SerializeField] private TMP_Text atkText; [SerializeField] private Image atkBadgeImg;
    [SerializeField] private GameObject hpBadge; [SerializeField] private TMP_Text hpText; [SerializeField] private Image hpBadgeImg;
    [SerializeField] private GameObject rangeBadge; [SerializeField] private TMP_Text rangeText; [SerializeField] private Image rangeBadgeImg;
    [SerializeField] private GameObject raceBadge; [SerializeField] private TMP_Text raceText; [SerializeField] private Image raceBadgeImg;

    [SerializeField] private Image sizeIcon;            // Size sprite
    [SerializeField] private TMP_Text sizeText;         // Fallback text like "2x3"
    [SerializeField] private Image moveIcon;    // MoveIcon (Ground/Flying)
    [SerializeField] private Image attackIcon;  // AttackIcon (Melee/Ranged)
    [SerializeField] private GameObject moveBadge;     // the whole badge GO
    [SerializeField] private Image moveBadgeImg;   // background image of the badge
    [SerializeField] private Image moveIconImg;    // inner icon image

    [SerializeField] private GameObject attackTypeBadge;
    [SerializeField] private Image attackTypeBadgeImg;
    [SerializeField] private Image attackTypeIconImg;

    [Header("Move badge & icons (by Realm)")]
    [SerializeField] private Sprite moveBadgeEmpyrean;
    [SerializeField] private Sprite moveBadgeInfernum;
    [SerializeField] private Sprite moveIconGroundEmpyrean;
    [SerializeField] private Sprite moveIconGroundInfernum;
    [SerializeField] private Sprite moveIconFlyingEmpyrean;
    [SerializeField] private Sprite moveIconFlyingInfernum;

    [Header("Ability icon sprites")]
    [SerializeField] private Sprite iconGround;
    [SerializeField] private Sprite iconFlying;
    [SerializeField] private Sprite iconMelee;
    [SerializeField] private Sprite iconRanged;

    [Header("AttackType badge & icons (by Realm)")]
    [SerializeField] private Sprite attackBadgeEmpyrean;
    [SerializeField] private Sprite attackBadgeInfernum;
    [SerializeField] private Sprite attackIconMeleeEmpyrean;
    [SerializeField] private Sprite attackIconMeleeInfernum;
    [SerializeField] private Sprite attackIconRangedEmpyrean;
    [SerializeField] private Sprite attackIconRangedInfernum;

    [Header("Optional overlays")]
    [SerializeField] private Image realmTint;
    [SerializeField] private Image realmFrame;

    [Header("Legend")]
    [SerializeField] private Image legendRibbon;
    [SerializeField] private Sprite ribbonLegendEmpyrean;
    [SerializeField] private Sprite ribbonLegendInfernum;

    [Header("Frame sprites (realm only)")]
    [SerializeField] private Sprite frameEmpyrean;      // white frame/border
    [SerializeField] private Sprite frameInfernum;      // black frame/border

    [Header("Title frame sprites (realm)")]
    [SerializeField] private Sprite titleEmpyrean;
    [SerializeField] private Sprite titleInfernum;

    [Header("Mana pip sprites (realm)")]
    [SerializeField] private Sprite manaPipEmpyrean;
    [SerializeField] private Sprite manaPipInfernum;

    [Header("Badge sprites by Realm")]
    [SerializeField] private Sprite atkEmpyrean, atkInfernum;
    [SerializeField] private Sprite hpEmpyrean, hpInfernum;
    [SerializeField] private Sprite rangeEmpyrean, rangeInfernum;
    [SerializeField] private Sprite raceEmpyrean, raceInfernum;
    [SerializeField] private Sprite effectBgEmpyrean, effectBgInfernum;

    [System.Serializable]
    public class SizeIconSet
    {
        public Sprite size1x1, size1x2, size1x3, size1x4;
        public Sprite size2x1, size2x2, size2x3, size2x4;
        public Sprite size3x1, size3x2, size3x3, size3x4;
        public Sprite size4x1, size4x2, size4x3, size4x4;
        public Sprite Pick(int w, int h)
        {
            switch (w)
            {
                case 1: switch (h) { case 1: return size1x1; case 2: return size1x2; case 3: return size1x3; case 4: return size1x4; } break;
                case 2: switch (h) { case 1: return size2x1; case 2: return size2x2; case 3: return size2x3; case 4: return size2x4; } break;
                case 3: switch (h) { case 1: return size3x1; case 2: return size3x2; case 3: return size3x3; case 4: return size3x4; } break;
                case 4: switch (h) { case 1: return size4x1; case 2: return size4x2; case 3: return size4x3; case 4: return size4x4; } break;
            }
            return null;
        }
    }

    [Header("Size icon sets by Realm")]
    [SerializeField] private SizeIconSet sizeIconsEmpyrean;
    [SerializeField] private SizeIconSet sizeIconsInfernum;

    [Header("Text colors by Realm")]
    [SerializeField] private Color textEmpyrean = Color.black;
    [SerializeField] private Color textInfernum = Color.white;

    public void Bind(CardSO so)
    {
        if (title) title.text = GetString(so, "cardName", "title", "name") ?? "(Card)";
        if (art) art.sprite = GetSprite(so, "artSprite", "art", "sprite", "cardArt", "illustration", "image");

        var type = GetEnum<CardType>(so, "type") ?? CardType.Unit;
        var realm = GetEnum<Realm>(so, "realm") ?? Realm.Empyrean;
        bool isUnit = type == CardType.Unit;
        bool isLegend = GetBool(so, "isLegend", "legend", "legendary");

        // --- Movement & AttackType badges (Units & Legendary Units only) ---
        if (isUnit)
        {
            // Movement (enum first; fallback to bool-style fields if present)
            var mv = GetEnum<MovementType>(so, "movement");
            bool isFlying = (mv != null && mv.Value == MovementType.Flying)
                            || GetBool(so, "isFlying", "hasFlight", "flying");

            if (moveBadge) moveBadge.SetActive(true);
            if (moveBadgeImg) moveBadgeImg.sprite = (realm == Realm.Infernum ? moveBadgeInfernum : moveBadgeEmpyrean);
            if (moveIconImg)
            {
                moveIconImg.sprite = (realm == Realm.Infernum)
                    ? (isFlying ? moveIconFlyingInfernum : moveIconGroundInfernum)
                    : (isFlying ? moveIconFlyingEmpyrean : moveIconGroundEmpyrean);
                moveIconImg.color = Color.white;
                moveIconImg.raycastTarget = false;
                moveIconImg.preserveAspect = true;
            }

            // Attack type (from AttackMode)
            var atkMode = GetEnum<AttackMode>(so, "attackMode") ?? AttackMode.Melee;

            if (attackTypeBadge) attackTypeBadge.SetActive(true);
            if (attackTypeBadgeImg) attackTypeBadgeImg.sprite = (realm == Realm.Infernum ? attackBadgeInfernum : attackBadgeEmpyrean);
            if (attackTypeIconImg)
            {
                attackTypeIconImg.sprite = (realm == Realm.Infernum)
                    ? (atkMode == AttackMode.Ranged ? attackIconRangedInfernum : attackIconMeleeInfernum)
                    : (atkMode == AttackMode.Ranged ? attackIconRangedEmpyrean : attackIconMeleeEmpyrean);
                attackTypeIconImg.color = Color.white;
                attackTypeIconImg.raycastTarget = false;
                attackTypeIconImg.preserveAspect = true;
            }
        }
        else
        {
            if (moveBadge) moveBadge.SetActive(false);
            if (attackTypeBadge) attackTypeBadge.SetActive(false);
        }

        // Frame swap (border)
        if (frame)
        {
            frame.sprite = (realm == Realm.Infernum) ? frameInfernum : frameEmpyrean;
            frame.color = Color.white; // keep opaque
        }

        // Title frame swap
        if (titleFrameImg)
        {
            titleFrameImg.sprite = (realm == Realm.Infernum) ? titleInfernum : titleEmpyrean;
            titleFrameImg.color = Color.white;
        }

        // Legend ribbon
        if (legendRibbon)
        {
            legendRibbon.gameObject.SetActive(isLegend);
            if (isLegend) legendRibbon.sprite = (realm == Realm.Infernum ? ribbonLegendInfernum : ribbonLegendEmpyrean);
        }

        // Realm overlays (optional)
        if (realmTint) realmTint.color = new Color(realm == Realm.Infernum ? 0.65f : 0.22f, realm == Realm.Infernum ? 0.18f : 0.45f, realm == Realm.Infernum ? 0.12f : 0.85f, 0.18f);
        if (realmFrame) realmFrame.color = Color.white;

        // Badges & effect bg by realm
        if (atkBadgeImg) atkBadgeImg.sprite = (realm == Realm.Infernum ? atkInfernum : atkEmpyrean);
        if (hpBadgeImg) hpBadgeImg.sprite = (realm == Realm.Infernum ? hpInfernum : hpEmpyrean);
        if (rangeBadgeImg) rangeBadgeImg.sprite = (realm == Realm.Infernum ? rangeInfernum : rangeEmpyrean);
        if (raceBadgeImg) raceBadgeImg.sprite = (realm == Realm.Infernum ? raceInfernum : raceEmpyrean);
        if (effectAreaBg) effectAreaBg.sprite = (realm == Realm.Infernum ? effectBgInfernum : effectBgEmpyrean);

        // Text color by realm (title, rules, stats, etc.)
        var tc = (realm == Realm.Infernum) ? textInfernum : textEmpyrean;
        SetTextColor(title, tc);
        SetTextColor(effectText, tc);
        SetTextColor(atkText, tc);
        SetTextColor(hpText, tc);
        SetTextColor(rangeText, tc);
        SetTextColor(raceText, tc);
        SetTextColor(sizeText, tc);

        // Race label
        if (raceBadge) raceBadge.SetActive(true);
        var raceEnum = GetEnum<Race>(so, "race");
        if (raceText) raceText.text = raceEnum != null ? PrettyRace(raceEnum.Value)
                                                       : (GetString(so, "raceText", "raceName") ?? "");

        // Unit stats & range
        if (atkBadge) atkBadge.SetActive(isUnit);
        if (hpBadge) hpBadge.SetActive(isUnit);
        if (atkText) atkText.text = isUnit ? GetInt(so, "attack", "atk").ToString() : "";
        if (hpText) hpText.text = isUnit ? GetInt(so, "health", "hp").ToString() : "";

        var attackMode = GetEnum<AttackMode>(so, "attackMode");
        float rangeM = GetFloat(so, "rangeMeters", "range", "rangeM");
        bool showRange = isUnit && (attackMode == AttackMode.Ranged || rangeM > 0.01f);
        if (rangeBadge) rangeBadge.SetActive(showRange);
        if (showRange && rangeText) rangeText.text = Mathf.RoundToInt(rangeM) + "m";

        // Effect text
        if (effectText) effectText.text = GetString(so, "rulesText", "effectText", "description", "text") ?? "";

        // Size icon (units only)
        GetIntFootprint(so, out int w, out int h);
        if (isUnit)
        {
            var set = (realm == Realm.Infernum) ? sizeIconsInfernum : sizeIconsEmpyrean;
            var spr = set != null ? set.Pick(w, h) : null;
            if (sizeIcon) { sizeIcon.sprite = spr; sizeIcon.enabled = spr != null; }
            if (sizeText) { sizeText.gameObject.SetActive(spr == null); if (spr == null) sizeText.text = $"{w}x{h}"; }
        }
        else
        {
            if (sizeIcon) sizeIcon.enabled = false;
            if (sizeText) sizeText.gameObject.SetActive(false);
        }

        // Mana pips: units only (and use realm-specific pip sprite)
        var pipSprite = (realm == Realm.Infernum) ? manaPipInfernum : manaPipEmpyrean;
        BuildPips(isUnit ? GetInt(so, "manaStars", "mana", "cost", "stars") : 0, pipSprite);
    }

    // ------------- helpers -------------
    void SetTextColor(TMP_Text t, Color c) { if (t) t.color = c; }

    void BuildPips(int count, Sprite pipSprite)
    {
        if (!manaPips) return;

        // Find template child named "ManaPip"
        GameObject template = null;
        for (int i = 0; i < manaPips.childCount; i++)
        {
            var ch = manaPips.GetChild(i);
            if (ch.name.Contains("ManaPip")) { template = ch.gameObject; break; }
        }
        if (template == null && manaPips.childCount > 0) template = manaPips.GetChild(0).gameObject;

        // Clear old (keep template)
        for (int i = manaPips.childCount - 1; i >= 0; i--)
        {
            var child = manaPips.GetChild(i).gameObject;
            if (child == template) continue;
            Destroy(child);
        }
        if (template == null) return;

        // Ensure template image shows the correct realm sprite (without enabling it)
        if (manaPipTemplateImg != null && pipSprite != null) manaPipTemplateImg.sprite = pipSprite;
        template.SetActive(false);

        for (int i = 0; i < count; i++)
        {
            var g = Instantiate(template, manaPips);
            g.name = $"ManaPip_{i + 1}";
            var img = g.GetComponent<Image>();
            if (img != null && pipSprite != null) img.sprite = pipSprite;
            g.SetActive(true);
        }
    }

    string PrettyRace(Race r) => r.ToString().Equals("Vorgco", StringComparison.OrdinalIgnoreCase) ? "Vorg’co" : r.ToString();

    // reflection helpers (same as before)
    static FieldInfo FindField(object obj, params string[] names) { var t = obj.GetType(); foreach (var n in names) { var f = t.GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance); if (f != null) return f; } return null; }
    static PropertyInfo FindProp(object obj, params string[] names) { var t = obj.GetType(); foreach (var n in names) { var p = t.GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance); if (p != null) return p; } return null; }
    static string GetString(object o, params string[] names) { var f = FindField(o, names); if (f != null) return f.GetValue(o)?.ToString(); var p = FindProp(o, names); if (p != null) return p.GetValue(o)?.ToString(); return null; }
    static int GetInt(object o, params string[] names) { var f = FindField(o, names); if (f != null) return Convert.ToInt32(f.GetValue(o) ?? 0); var p = FindProp(o, names); if (p != null) return Convert.ToInt32(p.GetValue(o) ?? 0); return 0; }
    static float GetFloat(object o, params string[] names) { var f = FindField(o, names); if (f != null) return Convert.ToSingle(f.GetValue(o) ?? 0f); var p = FindProp(o, names); if (p != null) return Convert.ToSingle(p.GetValue(o) ?? 0f); return 0f; }
    static Sprite GetSprite(object o, params string[] names) { var f = FindField(o, names); if (f != null) return f.GetValue(o) as Sprite; var p = FindProp(o, names); if (p != null) return p.GetValue(o) as Sprite; return null; }
    static bool GetBool(object o, params string[] names) { var f = FindField(o, names); if (f != null) return Convert.ToBoolean(f.GetValue(o) ?? false); var p = FindProp(o, names); if (p != null) return Convert.ToBoolean(p.GetValue(o) ?? false); return false; }
    static TEnum? GetEnum<TEnum>(object o, params string[] names) where TEnum : struct
    {
        var f = FindField(o, names); if (f != null) { var v = f.GetValue(o); if (v is TEnum te) return te; if (v != null && Enum.TryParse(v.ToString(), out te)) return te; }
        var p = FindProp(o, names); if (p != null) { var v = p.GetValue(o); if (v is TEnum te) return te; if (v != null && Enum.TryParse(v.ToString(), out te)) return te; }
        return null;
    }
    static void GetIntFootprint(CardSO so, out int w, out int h)
    {
        w = GetInt(so, "sizeW", "widthTiles", "footprintW", "w");
        h = GetInt(so, "sizeH", "heightTiles", "footprintH", "h");
        if (w <= 0) w = 1; if (h <= 0) h = 1;
    }
}
