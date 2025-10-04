using System;
using _GAME.Scripts.Core.Player;
using _GAME.Scripts.Core.Services;
using _GAME.Scripts.DesignPattern.Interaction;
using _GAME.Scripts.HideAndSeek;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.Core.Combat
{
    // ==================== HIT BOX DATA ====================

    [Serializable]
    public struct HitBoxData
    {
        public HitBoxType type;
        public float damageMultiplier;
        public float armorValue;

        public static HitBoxData Default => new HitBoxData
        {
            type = HitBoxType.Body,
            damageMultiplier = 1f,
            armorValue = 0f
        };
    }

    public enum HitBoxType
    {
        Body,
        Head,
        Limb,
        Critical
    }
    
    /// <summary>
    /// Optional component to manage multiple hitboxes
    /// </summary>
    public class HitBoxManager : MonoBehaviour
    {
        [SerializeField] private HitBox[] hitBoxes;

        private HitBox[] HitBoxes
        {
            get
            {
                if(hitBoxes == null || hitBoxes.Length == 0)
                {
                    hitBoxes = GetComponentsInChildren<HitBox>();
                }
                return hitBoxes;
            }
        }

        public void SetAllHitBoxesActive(bool active)
        {
            foreach (var hitBox in HitBoxes)
            {
                if (hitBox != null)
                    hitBox.gameObject.SetActive(active);
            }
        }

        public HitBox GetHitBox(HitBoxType type)
        {
            foreach (var hitBox in HitBoxes)
            {
                if (hitBox.Type == type)
                    return hitBox;
            }
            return null;
        }

        [ContextMenu("Setup HitBoxes")]
        private void SetupHitBoxes()
        {
            var colliders = GetComponentsInChildren<Collider>(true);
            foreach (var col in colliders)
            {
                if (col.gameObject == gameObject) continue;
                if (col.GetComponent<HitBox>() != null) continue;

                var hitBox = col.gameObject.AddComponent<HitBox>();
                Debug.Log($"Added HitBox to {col.gameObject.name}");
            }

            hitBoxes = GetComponentsInChildren<HitBox>();
        }
    }
}