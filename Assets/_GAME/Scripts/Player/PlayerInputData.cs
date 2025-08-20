using Unity.Netcode;
using UnityEngine;

namespace _GAME.Scripts.Player
{
    [System.Serializable]
    public struct PlayerInputData : INetworkSerializable
    {
        public Vector2 moveInput;
        public bool jumpPressed;
        public bool sprintHeld;
        public bool dashPressed;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref moveInput);
            serializer.SerializeValue(ref jumpPressed);
            serializer.SerializeValue(ref sprintHeld);
            serializer.SerializeValue(ref dashPressed);
        }

        public static PlayerInputData Empty => new PlayerInputData
        {
            moveInput = Vector2.zero,
            jumpPressed = false,
            sprintHeld = false,
            dashPressed = false
        };
    }
    [System.Serializable]
    public struct PlayerTransformData : INetworkSerializable
    {
        public Vector3 position;
        public Quaternion rotation;
        

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref position);
            serializer.SerializeValue(ref rotation);
        }

        public static PlayerTransformData Empty => new PlayerTransformData
        {
            position = Vector3.zero,
            rotation = Quaternion.identity
        };
    }
    
    [System.Serializable]
    public struct PlayerPhysicData : INetworkSerializable
    {
        public Vector3 velocity;
        public Vector3 angularVelocity;
        public bool isGrounded;
        
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref velocity);
            serializer.SerializeValue(ref angularVelocity);
            serializer.SerializeValue(ref isGrounded);
        }

        public static PlayerPhysicData Empty => new PlayerPhysicData
        {
            velocity = Vector3.zero,
            angularVelocity = Vector3.zero,
            isGrounded = false
        };
    }
    
    
}