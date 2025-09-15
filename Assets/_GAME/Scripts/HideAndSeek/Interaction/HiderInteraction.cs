using _GAME.Scripts.DesignPattern.Interaction;
using _GAME.Scripts.HideAndSeek.Combat.Base;
using _GAME.Scripts.HideAndSeek.Player;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Interaction
{
	public class HiderInteraction : PlayerInteraction
	{
		public override void OnInteracted(IInteractable initiator)
		{
			if (initiator is AGun gun)
			{
				Debug.Log($"[HiderInteraction] Hiders cannot pick up guns. Interaction ignored.");
			}
		}
	}
}