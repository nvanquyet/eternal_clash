using System;
using UnityEngine;
using Unity.Netcode;

namespace HideAndSeekGame.Core
{
    #region Enums
    
    public enum GameMode
    {
        PersonVsPerson,    // Case 1: Người-người
        PersonVsObject     // Case 2: Người-vật
    }
    
    public enum PlayerRole
    {
        Hider,      // Người trốn
        Seeker      // Người tìm
    }
    
    public enum GameState
    {
        Lobby,
        Preparation,    // Setup phase
        Playing,
        GameOver
    }
    
    public enum TaskType
    {
        ShapeSort,     // Sắp xếp hình
        BarSlider,     // Kéo thanh bar
        ButtonPress,   // Nhấn nút
        CodeInput      // Nhập mã
    }
    
    public enum SkillType
    {
        // Hider skills
        FreezeSeeker,      // Đóng băng người tìm
        Teleport,          // Dịch chuyển
        ShapeShift,        // Thay đổi hình dạng
        
        // Seeker skills  
        Detect,            // Phát hiện người trốn
        FreezeHider        // Đóng băng người trốn
    }
    
    public enum ObjectType
    {
        Table,      // Bàn - 10hp
        Container,  // Container - 100hp  
        TrashBag,   // Túi rác - 1hp
        Chair,      // Ghế - 5hp
        Barrel      // Thùng - 50hp
    }

    #endregion
    
    #region Core Interfaces
    
    public interface IGamePlayer : INetworkBehaviour
    {
        ulong ClientId { get; }
        PlayerRole Role { get; }
        string PlayerName { get; }
        bool IsAlive { get; }
        Vector3 Position { get; }
        
        void SetRole(PlayerRole role);
        void OnGameStart();
        void OnGameEnd(PlayerRole winnerRole);
    }
    
    public interface IHider : IGamePlayer
    {
        int CompletedTasks { get; }
        int TotalTasks { get; }
        bool HasSkillsAvailable { get; }
        
        void UseSkill(SkillType skillType, Vector3? targetPosition = null);
        void CompleteTask(int taskId);
        void OnTaskCompleted(int taskId);
    }
    
    public interface ISeeker : IGamePlayer  
    {
        float CurrentHealth { get; }
        float MaxHealth { get; }
        bool HasSkillsAvailable { get; }
        bool CanShoot { get; }
        
        void Shoot(Vector3 direction, Vector3 hitPoint);
        void UseSkill(SkillType skillType, Vector3? targetPosition = null);
        void TakeDamage(float damage);
        void RestoreHealth(float amount);
    }
    
    public interface IGameTask
    {
        int TaskId { get; }
        TaskType Type { get; }
        bool IsCompleted { get; }
        Vector3 Position { get; }
        
        void StartTask(IHider hider);
        void CompleteTask();
        void OnTaskInteraction(IHider hider);
    }
    
    public interface IObjectDisguise
    {
        ObjectType Type { get; }
        float MaxHealth { get; }
        float CurrentHealth { get; }
        bool IsOccupied { get; }
        
        void OccupyObject(IHider hider);
        void ReleaseObject();
        void TakeDamage(float damage, ISeeker attacker);
    }
    
    public interface ISkill
    {
        SkillType Type { get; }
        float Cooldown { get; }
        int UsesPerGame { get; }
        int RemainingUses { get; }
        bool CanUse { get; }
        
        void UseSkill(IGamePlayer caster, Vector3? targetPosition = null);
        void StartCooldown();
    }

    #endregion
    
    #region Data Structures
    
    [System.Serializable]
    public struct GameSettings
    {
        public GameMode gameMode;
        public int maxPlayers;
        public float gameTime;           // Thời gian chơi (giây)
        public int tasksToComplete;      // Số nhiệm vụ cần hoàn thành (Case 1)
        public float objectSwapTime;     // Thời gian đổi đồ vật (Case 2)
        public float seekerHealth;       // Máu người tìm
        public float environmentDamage;  // Sát thương khi bắn vào môi trường
        public float hiderKillReward;    // Máu hồi phục khi bắn trúng hider
    }
    
    [System.Serializable]
    public struct SkillData
    {
        public SkillType type;
        public float cooldown;
        public int usesPerGame;
        public float duration;           // Thời gian hiệu ứng
        public float range;              // Phạm vi tác dụng
        public string description;
    }
    
    [System.Serializable]
    public struct ObjectData
    {
        public ObjectType type;
        public float health;
        public Vector3 size;
        public GameObject prefab;
        public string displayName;
    }
    
    [System.Serializable]
    public struct TaskData
    {
        public TaskType type;
        public float completionTime;     // Thời gian hoàn thành
        public string description;
        public GameObject prefab;
    }

    #endregion
    
    #region Network Data Structures
    
    [System.Serializable]
    public struct NetworkPlayerData : INetworkSerializable
    {
        public ulong clientId;
        public PlayerRole role;
        public Vector3 position;
        public bool isAlive;
        public float health;
        
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref clientId);
            serializer.SerializeValue(ref role);
            serializer.SerializeValue(ref position);
            serializer.SerializeValue(ref isAlive);
            serializer.SerializeValue(ref health);
        }
    }
    
    [System.Serializable]
    public struct NetworkGameState : INetworkSerializable
    {
        public GameState state;
        public GameMode mode;
        public float timeRemaining;
        public int completedTasks;
        public int totalTasks;
        public int alivePlayers;
        
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref state);
            serializer.SerializeValue(ref mode);
            serializer.SerializeValue(ref timeRemaining);
            serializer.SerializeValue(ref completedTasks);
            serializer.SerializeValue(ref totalTasks);
            serializer.SerializeValue(ref alivePlayers);
        }
    }

    #endregion
}