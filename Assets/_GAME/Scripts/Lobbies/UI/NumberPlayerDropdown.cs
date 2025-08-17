using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace _GAME.Scripts.Lobbies.UI
{
    public class NumberPlayerDropdown : MonoBehaviour
    {
        [SerializeField] private TMP_Dropdown dropdown; // Options: "4", "8"
        private Action<int> onPlayerSelected = null;
        [SerializeField] private List<int> numberPlayerAvailable = new List<int> { 4, 8 };  // Default options    
        
        public int SelectedNumberPlayer { get; private set; }
        
        public void SubscribeCallback(Action<int> onPlayerSelected)
        {
            this.onPlayerSelected += onPlayerSelected;
        }
        
        public void UnsubscribeCallback(Action<int> onPlayerSelected)
        {
            this.onPlayerSelected -= onPlayerSelected;
        }
        
        
        public void Init()
        {
            //Set value for dropdown
            if (dropdown == null)
            {
                Debug.LogError("Dropdown is not assigned in the inspector.");
                return;
            }

            this.dropdown.interactable = false;
            SelectedNumberPlayer = numberPlayerAvailable[0]; // Default to 4
            SetDropdownOptions(numberPlayerAvailable);
        }
        
        public void Init(bool localIsHost, int value)
        {
            dropdown.interactable = localIsHost;
            
            //Set value for dropdown
            if (dropdown == null)
            {
                Debug.LogError("Dropdown is not assigned in the inspector.");
                return;
            }

            SelectedNumberPlayer = value;
            //Set value to dropdown
            int optionIndex = numberPlayerAvailable.IndexOf(value);
            if (optionIndex < 0)
            {
                Debug.LogWarning($"Value {value} is not in the available options. Defaulting to first option.");
                optionIndex = 0; // Default to first option if value not found
            }
            dropdown.SetValueWithoutNotify(optionIndex);
            dropdown.onValueChanged.AddListener(OnChanged);
        }

        private void OnDestroy()
        {
            dropdown.onValueChanged.RemoveListener(OnChanged);
        }

        private void OnChanged(int optionIndex)
        {
            optionIndex = Mathf.Clamp(optionIndex, 0, numberPlayerAvailable.Count - 1);
            var value = numberPlayerAvailable[optionIndex];
            SelectedNumberPlayer = value;
            onPlayerSelected?.Invoke(value);
        }
        
        public void SetValueWithoutNotify(int value)
        {
            if (dropdown == null)
            {
                Debug.LogError("Dropdown is not assigned in the inspector.");
                return;
            }

            int optionIndex = numberPlayerAvailable.IndexOf(value);
            if (optionIndex < 0)
            {
                Debug.LogWarning($"Value {value} is not in the available options. Defaulting to first option.");
                optionIndex = 0; // Default to first option if value not found
            }
            dropdown.SetValueWithoutNotify(optionIndex);
        }
        
        private void SetDropdownOptions(List<int> values)
        {
            // Xóa các option cũ
            dropdown.ClearOptions();

            // Chuyển list<int> sang list<string>
            List<string> options = values.ConvertAll(v => v.ToString());

            // Gán lại vào dropdown
            dropdown.AddOptions(options);

            // Nếu muốn set luôn giá trị chọn mặc định:
            dropdown.SetValueWithoutNotify(0); // chọn phần tử đầu tiên
        }

        public void SetInteractable(bool isInteractable)
        {
            if (dropdown == null)
            {
                Debug.LogError("Dropdown is not assigned in the inspector.");
                return;
            }
            dropdown.interactable = isInteractable;
        }
    }
}