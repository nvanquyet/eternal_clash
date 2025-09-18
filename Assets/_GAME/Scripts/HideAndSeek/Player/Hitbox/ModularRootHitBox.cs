using _GAME.Scripts.DesignPattern.Interaction;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Player.HitBox
{
    public class ModularRootHitBox : ADefendable
    {
        public override bool Interact(IInteractable target) => false;

        public override void OnInteracted(IInteractable initiator) { }
        
        [ContextMenu("Setup ModularHitBox")]
        private void SetupModularHitBox()
        {
            var hitBox = GetComponentsInChildren<Collider>();
            foreach(var col in hitBox)
            {
                if (col.gameObject == gameObject) continue;
                if (col.GetComponent<ModularHitBox>()) continue;
                col.gameObject.AddComponent<ModularHitBox>();
            }
        }
    }
}