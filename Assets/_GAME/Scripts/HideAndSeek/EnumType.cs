using _GAME.Scripts.DesignPattern.Interaction;
using Unity.Netcode;
using UnityEngine;
using System;

namespace _GAME.Scripts.HideAndSeek
{
    #region Enums
    
    public enum GameMode
    {
        PersonVsPerson,    // Case 1: Người-người
        PersonVsObject     // Case 2: Người-vật
    }
    
    public enum Role
    {
        None,  // Chưa chọn
        Bot,
        Hider,      // Người trốn
        Seeker      // Người tìm
    }
    
    public enum GameState
    {
        PreparingGame,
        Playing,
        GameEnded
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
        None,
        // Hider skills
        FreezeSeeker,      // Đóng băng người tìm
        Teleport,          // Dịch chuyển
        
        // Seeker skills  
        Detect,            // Phát hiện người trốn
        FreezeHider,        // Đóng băng người trốn
        Rush
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
    
    public interface IGamePlayer 
    {
        ulong ClientId { get; }
        Role Role { get; }
        string PlayerName { get; }
        bool IsAlive { get; }
        Vector3 Position { get; }
        void SetRole(Role role);
        void OnGameStart();
        void OnGameEnd(Role winnerRole);
        bool HasSkillsAvailable { get; }
        
        void UseSkill(SkillType skillType, Vector3? targetPosition = null);
        void ApplyPenaltyForKillingBot();
    }
    
    public interface IHider : IGamePlayer
    {
        int CompletedTasks { get; }
        int TotalTasks { get; }
        
        void CompleteTask(int taskId);
        void OnTaskCompleted(int taskId);
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
        bool IsOccupied { get; }
        
        void OccupyObject(IHider hider);
        void ReleaseObject();
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
        float GetCooldownTime();
        float GetEffectDuration();
    }

    #endregion
    
    #region Network Data Structures
    
    [System.Serializable]
    public struct NetworkPlayerData : INetworkSerializable, IEquatable<NetworkPlayerData>
    {
        public ulong clientId;
        public Role role;
        public bool isAlive;
        
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref clientId);
            serializer.SerializeValue(ref role);
            serializer.SerializeValue(ref isAlive);
        }
        
        public bool Equals(NetworkPlayerData other)
        {
            return clientId == other.clientId &&
                   role == other.role &&
                   isAlive == other.isAlive;
        }
        
        public override bool Equals(object obj)
        {
            return obj is NetworkPlayerData other && Equals(other);
        }
        
        public override int GetHashCode()
        {
            return HashCode.Combine(clientId, role, isAlive);
        }
        
        public static bool operator ==(NetworkPlayerData left, NetworkPlayerData right)
        {
            return left.Equals(right);
        }
        
        public static bool operator !=(NetworkPlayerData left, NetworkPlayerData right)
        {
            return !left.Equals(right);
        }
    }
    
   
    #endregion
}