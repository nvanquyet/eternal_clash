using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace _GAME.Scripts.Lobbies.UI.Version2
{
    public class NumberPlayerDropdown : MonoBehaviour
    {
        [SerializeField] private TMP_Dropdown dropdown; // Options: "4", "8"
        public Action<int> OnPlayerSelected;
        
        public void Init(bool localIsHost, List<int> numberPlayerAvailable, Action<int> onPlayerSelected)
        {
            this.OnPlayerSelected = onPlayerSelected;
            dropdown.interactable = localIsHost;
            
            //Set value for dropdown
            SetDropdownOptions(numberPlayerAvailable);
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

            OnPlayerSelected?.Invoke(value);
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
    }
}