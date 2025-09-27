using System;
using _GAME.Scripts.Networking;
using TMPro;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.UI
{
    public class UINotice : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI noticeText;

        private void Start()
        {
            GameEvent.OnRoleAssigned += OnRoleAssigned;
            HideNotice();
        }

        private void OnDestroy()
        {
            GameEvent.OnRoleAssigned -= OnRoleAssigned;
        }

        private void OnRoleAssigned()
        {
            var role = GameManager.Instance.GetPlayerRoleWithId(NetworkController.Instance.LocalClientId);
            //Debug.Log($"[UINotice] Local player assigned role: {role}");
            switch (role)
            {
                case Role.Hider:
                    ShowNotice("You are a Hider! Find a good spot to hide.");
                    break;
                case Role.Seeker:
                    ShowNotice("You are a Seeker! Find and tag all Hiders.");
                    break;
                case Role.None:
                default:
                    Debug.LogWarning("[UINotice] Role is None or unrecognized.");
                    return;
            }
        }

        private void ShowNotice(string message)
        {
            noticeText.text = message;
            noticeText.gameObject.SetActive(true);
            CancelInvoke(nameof(HideNotice));
            //Disable after 3 seconds
            Invoke(nameof(HideNotice), 3f);
        }
        
        private void HideNotice()
        {
            noticeText.gameObject.SetActive(false);
        }
    }
}
