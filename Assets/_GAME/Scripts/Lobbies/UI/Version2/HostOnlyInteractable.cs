using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace _GAME.Scripts.Lobbies.UI.Version2
{
    [DisallowMultipleComponent]
    public class HostOnlyInteractable : MonoBehaviour
    {
        [SerializeField] private Selectable[] toDisableIfNotHost; // Button, TMP_InputField, Dropdown...

        public void Apply(bool isHost)
        {
            if (toDisableIfNotHost == null) return;
            foreach (var s in toDisableIfNotHost)
            {
                if (!s) continue;
                s.interactable = isHost;
            }
        }
    }
}