using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

namespace _GAME.Scripts.Lobbies.UI
{
    public class LobbySettingUI : MonoBehaviour
    {
        [Header("UI References")] 
        [SerializeField] private TextMeshProUGUI codeLabel;

        [SerializeField] private TMP_InputField lobbyNameInput;
        [SerializeField] private TMP_InputField passwordInput;
        [SerializeField] private TMP_Dropdown gameModeDropdown;
        [SerializeField] private TMP_Dropdown statusDropdown;
        [SerializeField] private TMP_Dropdown maxPlayerDropdown;
        [SerializeField] private Button saveButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private Toggle passwordVisibilityToggle;

        [Header("Settings")] [SerializeField] private List<string> gameModes = new List<string>
        {
            "Hide and Seek",
            "Team Deathmatch",
            "Capture the Flag",
            "Free for All"
        };

        [SerializeField] private List<string> statusOptions = new List<string>
        {
            "Public",
            "Private",
            "Friends Only"
        };

        [SerializeField] private List<int> maxPlayerOptions = new List<int>
        {
            2, 4, 6, 8, 10, 12, 16, 20
        };

        private Lobby currentLobby;
        private bool isEditing = false;

        // Events
        public System.Action<Lobby> OnLobbySaved;
        public System.Action OnLobbyCanceled;

        private void Start()
        {
            InitializeUI();
            SetupEventListeners();
        }

        private void InitializeUI()
        {
            // Setup dropdowns
            SetupGameModeDropdown();
            SetupStatusDropdown();
            SetupMaxPlayerDropdown();

            // Setup password visibility toggle
            if (passwordVisibilityToggle != null)
            {
                passwordVisibilityToggle.onValueChanged.AddListener(TogglePasswordVisibility);
                passwordInput.contentType = TMP_InputField.ContentType.Password;
            }
        }

        private void SetupEventListeners()
        {
            saveButton?.onClick.AddListener(OnSaveClicked);
            cancelButton?.onClick.AddListener(OnCancelClicked);

            // Validation listeners
            lobbyNameInput?.onValueChanged.AddListener(ValidateInputs);
            passwordInput?.onValueChanged.AddListener(ValidateInputs);
        }

        private void SetupGameModeDropdown()
        {
            if (gameModeDropdown != null)
            {
                gameModeDropdown.ClearOptions();
                gameModeDropdown.AddOptions(gameModes);
                gameModeDropdown.onValueChanged.AddListener(OnGameModeChanged);
            }
        }

        private void SetupStatusDropdown()
        {
            if (statusDropdown != null)
            {
                statusDropdown.ClearOptions();
                statusDropdown.AddOptions(statusOptions);
                statusDropdown.onValueChanged.AddListener(OnStatusChanged);
            }
        }

        private void SetupMaxPlayerDropdown()
        {
            if (maxPlayerDropdown != null)
            {
                maxPlayerDropdown.ClearOptions();
                List<string> maxPlayerStrings = new List<string>();
                foreach (int count in maxPlayerOptions)
                {
                    maxPlayerStrings.Add(count.ToString());
                }

                maxPlayerDropdown.AddOptions(maxPlayerStrings);
                maxPlayerDropdown.onValueChanged.AddListener(OnMaxPlayerChanged);
            }
        }

        public void DisplayLobby(Lobby lobbyData)
        {
            if (lobbyData == null)
            {
                Debug.LogError("Lobby data is null!");
                return;
            }

            currentLobby = lobbyData;
            isEditing = false;

            // Display read-only information
            if (codeLabel != null) codeLabel.text = lobbyData.LobbyCode;

            // Fill editable fields
            if (lobbyNameInput != null) lobbyNameInput.text = lobbyData.Name;
            if (passwordInput != null) passwordInput.text = lobbyData.Data["password"]?.Value ?? "";

            // Set dropdown values
            SetDropdownValue(gameModeDropdown, gameModes, lobbyData.Data["game_mode"]?.Value ?? "Hide and seek");
            SetDropdownValue(statusDropdown, statusOptions, lobbyData.IsPrivate ? "Private" : "Public");
            SetMaxPlayerDropdown(lobbyData.MaxPlayers);
            ValidateInputs("");
        }

        private void SetDropdownValue(TMP_Dropdown dropdown, List<string> options, string value)
        {
            if (dropdown != null && !string.IsNullOrEmpty(value))
            {
                int index = options.FindIndex(x => x.Equals(value, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    dropdown.value = index;
                }
            }
        }

        private void SetMaxPlayerDropdown(int maxPlayer)
        {
            if (maxPlayerDropdown != null)
            {
                int index = maxPlayerOptions.FindIndex(x => x == maxPlayer);
                if (index >= 0)
                {
                    maxPlayerDropdown.value = index;
                }
            }
        }

        private void SetEditingMode(bool canEdit)
        {
            // Enable/disable input fields based on host status
            if (lobbyNameInput != null) lobbyNameInput.interactable = canEdit;
            if (passwordInput != null) passwordInput.interactable = canEdit;
            if (gameModeDropdown != null) gameModeDropdown.interactable = canEdit;
            if (statusDropdown != null) statusDropdown.interactable = canEdit;
            if (maxPlayerDropdown != null) maxPlayerDropdown.interactable = canEdit;
            if (passwordVisibilityToggle != null) passwordVisibilityToggle.interactable = canEdit;

            // Show/hide save button for host only
            if (saveButton != null) saveButton.gameObject.SetActive(canEdit);
        }

        private void ValidateInputs(string value)
        {
            bool isValid = !(lobbyNameInput != null && string.IsNullOrWhiteSpace(lobbyNameInput.text));
            // Enable/disable save button
            if (saveButton != null)
            {
                saveButton.interactable = isValid && currentLobby != null;// && currentLobby.isHost;
            }
        }

        private void OnSaveClicked()
        {
            if (currentLobby == null)// || !currentLobby.isHost)
            {
                Debug.LogWarning("Cannot save: Not host or no lobby data");
                return;
            }

            // Update lobby data with current UI values
            UpdateLobbyDataFromUI();

            // Trigger save event
            OnLobbySaved?.Invoke(currentLobby);

            Debug.Log($"Lobby saved: {currentLobby.Name}");
        }

        private void OnCancelClicked()
        {
            // Reset UI to original values if editing
            if (isEditing && currentLobby != null)
            {
                DisplayLobby(currentLobby);
            }

            OnLobbyCanceled?.Invoke();
            Debug.Log("Lobby changes canceled");
        }

        private async void UpdateLobbyDataFromUI()
        {
            try
            {
                if (currentLobby == null) return;
                var option = new UpdateLobbyOptions()
                {
                    Name = lobbyNameInput?.text ?? "",
                    Password = passwordInput?.text ?? "",
                    MaxPlayers = maxPlayerOptions[maxPlayerDropdown.value],
                    Data = new Dictionary<string, DataObject>()
                    {
                        { "game_mode", new DataObject(DataObject.VisibilityOptions.Public, gameModeDropdown?.options[gameModeDropdown.value].text ?? "Hide and Seek") },
                        { "status", new DataObject(DataObject.VisibilityOptions.Public, statusDropdown?.options[statusDropdown.value].text ?? "Public")}
                    }
                };
                // Update the lobby with new data
                var result = await LobbyHandler.Instance.UpdateLobbyAsync(codeLabel.text, option);
                if (result)
                {
                    Debug.Log("Lobby updated successfully.");
                }
                else
                {
                    Debug.LogError("Failed to update lobby.");
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
        }

        private void TogglePasswordVisibility(bool showPassword)
        {
            if (passwordInput != null)
            {
                passwordInput.contentType =
                    showPassword ? TMP_InputField.ContentType.Standard : TMP_InputField.ContentType.Password;
                passwordInput.ForceLabelUpdate();
            }
        }

        private void OnGameModeChanged(int index)
        {
            if (currentLobby != null && index < gameModes.Count)
            {
                isEditing = true;
                ValidateInputs("");
            }
        }

        private void OnStatusChanged(int index)
        {
            if (currentLobby != null && index < statusOptions.Count)
            {
                isEditing = true;

                // Auto-clear password if status is not Private
                if (statusOptions[index] != "Private" && passwordInput != null)
                {
                    passwordInput.text = "";
                }

                ValidateInputs("");
            }
        }

        private void OnMaxPlayerChanged(int index)
        {
            if (currentLobby != null && index < maxPlayerOptions.Count)
            {
                isEditing = true;
                ValidateInputs("");
            }
        }

        // Additional utility methods
        public void AddGameMode(string gameMode)
        {
            if (!gameModes.Contains(gameMode))
            {
                gameModes.Add(gameMode);
                SetupGameModeDropdown();
            }
        }

        private void OnDestroy()
        {
            // Clean up event listeners
            saveButton?.onClick.RemoveAllListeners();
            cancelButton?.onClick.RemoveAllListeners();
            passwordVisibilityToggle?.onValueChanged.RemoveAllListeners();
            lobbyNameInput?.onValueChanged.RemoveAllListeners();
            passwordInput?.onValueChanged.RemoveAllListeners();
            gameModeDropdown?.onValueChanged.RemoveAllListeners();
            statusDropdown?.onValueChanged.RemoveAllListeners();
            maxPlayerDropdown?.onValueChanged.RemoveAllListeners();
        }
    }
}