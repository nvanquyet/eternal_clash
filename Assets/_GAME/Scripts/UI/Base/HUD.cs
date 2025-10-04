using System;
using System.Collections.Generic;
using GAME.Scripts.DesignPattern;
using UnityEngine;

namespace _GAME.Scripts.UI.Base
{
    public class HUD : Singleton<HUD>
    {
        [Header("UI Elements")]
        [Tooltip("Phần tử đầu tiên có thể coi là Main UI nếu muốn khởi tạo mặc định")]
        [SerializeField] private BaseUI[] uiElements;

        [SerializeField] private bool usingStack = true;
        private readonly Dictionary<UIType, BaseUI> uiDictionary = new Dictionary<UIType, BaseUI>();
        private readonly Stack<BaseUI> uiStack = new Stack<BaseUI>();

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Lấy cả inactive để đảm bảo editor gom đủ các UI
            uiElements = GetComponentsInChildren<BaseUI>(true);
        }
#endif

        protected override void OnAwake()
        {
            base.OnAwake();
            InitDictionaries();
        }

        private void Start()
        {
            if (!usingStack)
            {
                Debug.LogWarning("[HUD] Chức năng stack UI đang tắt. Vui lòng sử dụng các phương thức Show/Hide trực tiếp trên BaseUI.");
                return;
            }
            // Tuỳ nhu cầu: nếu muốn show sẵn main UI (phần tử đầu) thì bật đoạn dưới
            if (uiElements != null && uiElements.Length > 0 && uiElements[0] != null)
            {
                var main = uiElements[0];
                // Đảm bảo không trùng trong stack
                RemoveExistingFromStack(main);
                uiStack.Push(main);
                main.Show(null, false);
                //Hide all other UIs
                for (int i = 1; i < uiElements.Length; i++)
                {
                    if (uiElements[i] != null)
                    {
                        uiElements[i].Hide(null, false);
                    }
                }
                Debug.Log($"[HUD] Đã khởi tạo UI mặc định: {main.UIType}");
            }
        }

        private void InitDictionaries()
        {
            uiDictionary.Clear();
            if (uiElements == null) return;

            foreach (var e in uiElements)
            {
                if (e == null) continue;
                if (uiDictionary.ContainsKey(e.UIType))
                {
                    Debug.LogWarning($"[HUD] Trùng UIType {e.UIType}, bỏ qua: {e.name}");
                    continue;
                }
                uiDictionary.Add(e.UIType, e);
            }
        }
        
        /// <summary>
        /// Get UI từ dictionary with type.
        /// </summary>
        /// <param name="type"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T GetUI<T>(UIType type) where T : BaseUI
        {
            //Get UI từ dictionary
            if (uiDictionary.TryGetValue(type, out var ui))
            {
                return ui as T;
            }
            else
            {
                Debug.LogError($"[HUD] Không tìm thấy UI với type {type}");
                return null;
            }
        }

        /// <summary>
        /// Show UI mới: ẩn UI hiện tại nhưng không xoá khỏi stack; đẩy UI mới lên top.
        /// </summary>
        public void Show(UIType uiType, Action callBack = null, bool fading = true)
        {
            if (!uiDictionary.TryGetValue(uiType, out var ui))
            {
                Debug.LogError($"[HUD] UI type {uiType} không tồn tại");
                return;
            }

            // Nếu top đã là UI cần show => bỏ qua
            var top = uiStack.Count > 0 ? uiStack.Peek() : null;
            if (top == ui)
            {
                Debug.LogWarning($"[HUD] UI {uiType} đang ở trên cùng rồi");
                return;
            }

            // Ẩn UI hiện tại (nếu có) nhưng KHÔNG pop
            if (top != null) top.Hide(null, fading);

            // Nếu UI đã nằm đâu đó trong stack, loại bỏ bản cũ trước khi đẩy lên top (tránh duplicated)
            RemoveExistingFromStack(ui);

            // Đẩy UI mới và show
            uiStack.Push(ui);
            ui.Show(callBack, fading);
        }

        /// <summary>
        /// Hide UI đang ở top; sau đó show lại UI trước đó.
        /// </summary>
        public void Hide(UIType uiType, Action callBack = null, bool fading = true)
        {
            if (uiStack.Count == 0)
            {
                Debug.LogWarning("[HUD] Stack rỗng, không có UI để hide");
                return;
            }

            if (!uiDictionary.TryGetValue(uiType, out var ui))
            {
                Debug.LogError($"[HUD] UI type {uiType} không tồn tại");
                return;
            }

            var currentTop = uiStack.Peek();
            if (currentTop != ui)
            {
                Debug.LogWarning($"[HUD] Chỉ được hide UI đang ở top. Top hiện tại: {currentTop.UIType}, yêu cầu: {uiType}");
                return;
            }

            // Hide top rồi pop nó ra
            currentTop.Hide(callBack, fading);
            uiStack.Pop();

            // Show lại UI phía dưới (nếu có)
            var previous = uiStack.Count > 0 ? uiStack.Peek() : null;
            if (previous != null)
            {
                previous.Show(null, fading);
            }
        }

        /// <summary>
        /// Shortcut quay lại UI trước đó (tương đương Hide(top)).
        /// </summary>
        public void GoBack(bool fading = true)
        {
            if (uiStack.Count == 0) return;
            var currentTop = uiStack.Peek();
            Hide(currentTop.UIType, null, fading);
        }

        // =========================
        // Helpers
        // =========================

        /// <summary>
        /// Loại bỏ một instance UI khỏi stack (nếu có) nhưng giữ nguyên thứ tự các phần tử còn lại.
        /// </summary>
        private void RemoveExistingFromStack(BaseUI target)
        {
            if (!uiStack.Contains(target)) return;

            // Stack.ToArray() trả về mảng theo thứ tự từ TOP -> BOTTOM
            var arr = uiStack.ToArray();
            uiStack.Clear();

            // Duyệt từ bottom -> top để đẩy lại đúng thứ tự ban đầu
            for (int i = arr.Length - 1; i >= 0; i--)
            {
                if (arr[i] == target) continue; // bỏ instance cũ
                uiStack.Push(arr[i]);
            }
        }
    }
}
