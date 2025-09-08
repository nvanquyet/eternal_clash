using UnityEngine.InputSystem;

namespace _GAME.Scripts.Utils
{
    /// <summary>
    /// Simple factory để tạo unique InputAction cho từng player instance
    /// Tránh conflict trong multiplayer
    /// </summary>
    public static class InputActionFactory
    {
        /// <summary>
        /// Tạo một InputAction độc lập từ InputActionReference
        /// Mỗi player sẽ có action riêng, không bị conflict
        /// </summary>
        public static InputAction CreateUniqueAction(InputActionReference reference, int instanceId)
        {
            if (reference?.action == null) return null;

            var sourceAction = reference.action;
            var newAction = new InputAction(
                name: $"{sourceAction.name}_{instanceId}",
                type: sourceAction.type
            );

            // Copy all bindings
            CopyBindings(sourceAction, newAction);
            return newAction;
        }

        /// <summary> 
        /// Copy bindings từ source sang target action
        /// </summary>
        private static void CopyBindings(InputAction source, InputAction target)
        {
            if (source == null || target == null) return;

            foreach (var t in source.bindings)
            {
                target.AddBinding(t);
            }
        }
    }
}