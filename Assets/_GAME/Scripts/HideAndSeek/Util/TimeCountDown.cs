using System;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Util
{
    public class TimeCountDown : NetworkBehaviour
    {
        // Đồng bộ thời gian còn lại giữa server và client
        private NetworkVariable<float> countdownTime = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        private NetworkVariable<FixedString64Bytes> countdownLabel = new NetworkVariable<FixedString64Bytes>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        private bool isCountingDown = false;

        public float CountdownTime => countdownTime.Value;
        public bool IsCountingDown => isCountingDown;

        // Events
        public static Action<float> OnCountdownUpdated;
        public static Action OnCountdownFinished;

        [SerializeField] private TextMeshProUGUI labelText;
        [SerializeField] private TextMeshProUGUI countdownText;

        #region Server Methods

        [ContextMenu("Test Start Countdown")]
        private void TestCountDown()
        {
            StartCountdownServerRpc(10f);
        }

        [ContextMenu("Test Stop Countdown")]
        private void TestStopCountDown()
        {
            StopCountdownServerRpc();
        }


        [ServerRpc(RequireOwnership = false)]
        public void StartCountdownServerRpc(float time)
        {
            if (time <= 0f) return;
            if (isCountingDown) CancelInvoke(nameof(UpdateCountdown));

            countdownTime.Value = time;
            countdownLabel.Value = "";

            isCountingDown = true;
            InvokeRepeating(nameof(UpdateCountdown), 1f, 1f);
        }

        [ServerRpc(RequireOwnership = false)]
        public void StartCountdownServerRpc(float time, string label)
        {
            if (time <= 0f) return;
            if (isCountingDown) CancelInvoke(nameof(UpdateCountdown));

            countdownTime.Value = time;
            countdownLabel.Value = label;
            isCountingDown = true;
            InvokeRepeating(nameof(UpdateCountdown), 1f, 1f);
        }


        [ServerRpc(RequireOwnership = false)]
        public void StopCountdownServerRpc()
        {
            if (!isCountingDown) return;

            isCountingDown = false;
            CancelInvoke(nameof(UpdateCountdown));

            // báo client để UI tự xử lý ẩn/khóa
            StopCountdownClientRpc();
            Debug.Log($"Countdown stopped on server.");
        }

        private void UpdateCountdown()
        {
            if (!isCountingDown) return;

            countdownTime.Value--;

            if (countdownTime.Value <= 0f)
            {
                countdownTime.Value = 0f;
                isCountingDown = false;
                CancelInvoke(nameof(UpdateCountdown));
                OnCountdownFinished?.Invoke();
                Debug.Log($"Countdown finished on server.");
            }
        }

        #endregion

        #region Client Methods

        // Khuyến nghị: sub ở OnNetworkSpawn/Despawn để chắc chắn object đã spawn
        public override void OnNetworkSpawn()
        {
            countdownTime.OnValueChanged += HandleCountdownChanged;
            countdownLabel.OnValueChanged += HandleCountDownLabelChanged;
        }

        public override void OnNetworkDespawn()
        {
            countdownTime.OnValueChanged -= HandleCountdownChanged;
            countdownLabel.OnValueChanged -= HandleCountDownLabelChanged;
        }

        private void HandleCountdownChanged(float previous, float current)
        {
            // Update UI
            if (countdownText != null)
            {
                if (!countdownText.gameObject.activeSelf) countdownText.gameObject.SetActive(true);
                countdownText.text = Mathf.CeilToInt(current).ToString();
            }

            // Notify listeners
            OnCountdownUpdated?.Invoke(current);

            // Nếu client detect countdown kết thúc
            if (current <= 0f && previous > 0f)
            {
                Debug.Log($"Countdown finished on client.");
                if (countdownText.gameObject.activeSelf) countdownText.gameObject.SetActive(false);
                if (labelText)
                {
                    labelText.gameObject.SetActive(false);
                    labelText.text = "";
                }

                OnCountdownFinished?.Invoke();
            }
        }


        private void HandleCountDownLabelChanged(FixedString64Bytes previous, FixedString64Bytes current)
        {
            var text = current.ToString();

            if (string.IsNullOrEmpty(text))
            {
                if (labelText)
                {
                    labelText.gameObject.SetActive(false);
                    labelText.text = "";
                }

                return;
            }

            if (!labelText) return;
            if (!labelText.gameObject.activeSelf) labelText.gameObject.SetActive(true);
            labelText.text = text;
        }

        [ClientRpc]
        private void StopCountdownClientRpc()
        {
            // Ẩn/khóa UI đếm ngược nếu muốn
            if (countdownText) countdownText.gameObject.SetActive(false);
            if (labelText)
            {
                labelText.gameObject.SetActive(false);
                labelText.text = "";
            }
        }

        #endregion
    }
}