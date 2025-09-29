using _GAME.Scripts.DesignPattern.Interaction;
using Unity.Netcode;

namespace _GAME.Scripts.HideAndSeek.Object
{
    public class Cage : NetworkBehaviour
    {
        private readonly NetworkVariable<bool> _isActive = new NetworkVariable<bool>(false);
        public bool IsActive => _isActive.Value;
        public void SetActive(bool active)
        {
            if (!IsServer) return;
            _isActive.Value = active;;
        }
        
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _isActive.OnValueChanged += OnActiveChanged;
            this.gameObject.SetActive(false);
        }

        public override void OnNetworkDespawn()
        {
            _isActive.OnValueChanged -= OnActiveChanged;
            base.OnNetworkDespawn();
        }

        private void OnActiveChanged(bool previousValue, bool newValue)
        {
            this.gameObject.SetActive(newValue);
        }
    }
}
