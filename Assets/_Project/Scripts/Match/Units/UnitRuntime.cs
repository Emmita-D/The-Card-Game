using UnityEngine;
using Game.Match.Cards;
using Game.Core;

namespace Game.Match.Units
{
    public class UnitRuntime : MonoBehaviour
    {
        [Header("Stats (debug)")]
        public string displayName;
        public int attack;
        public int health;
        public float rangeMeters;
        public Realm realm;

        // Simple visual tint so you can tell Empyrean/Infernum at a glance
        public void InitFrom(CardSO so)
        {
            if (so == null) return;
            displayName = so.cardName;
            attack = so.attack;
            health = so.health;
            rangeMeters = so.rangeMeters;
            realm = so.realm;

            var rend = GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                // Use a material instance so you don't edit the shared asset
                var mats = rend.materials;
                if (mats.Length > 0)
                {
                    var c = (so.realm == Realm.Infernum)
                        ? new Color(0.9f, 0.25f, 0.2f)   // reddish
                        : new Color(0.3f, 0.55f, 1f);     // bluish
                    mats[0].color = c;
                    rend.materials = mats;
                }
            }

            gameObject.name = $"{so.cardName}_Unit";
        }
    }
}
