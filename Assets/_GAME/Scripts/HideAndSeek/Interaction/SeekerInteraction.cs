using _GAME.Scripts.DesignPattern.Interaction;
using _GAME.Scripts.HideAndSeek.Combat.Base;
using _GAME.Scripts.HideAndSeek.Player;

namespace _GAME.Scripts.HideAndSeek.Interaction
{
    public class SeekerInteraction : PlayerInteraction
    {
        public override void OnInteracted(IInteractable initiator)
        {
            if (initiator is AGun gun)
            {
                playerEquipment?.SetCurrentGun(gun);
            }
        }
        
    }
}