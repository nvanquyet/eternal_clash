using System;

namespace _GAME.Scripts.Networking.Relay
{
    public static  class RelayEvent
    {
        public static event Action<string> OnRelayHostReady;   // joinCode
        public static event Action         OnRelayClientReady; // sau khi set transport
        public static event Action<string> OnRelayError;       // message
        
        
        public static void TriggerRelayHostReady(string code)  => OnRelayHostReady?.Invoke(code);
        public static void TriggerRelayClientReady()           => OnRelayClientReady?.Invoke();
        public static void TriggerRelayError(string message)   => OnRelayError?.Invoke(message);
    }
}