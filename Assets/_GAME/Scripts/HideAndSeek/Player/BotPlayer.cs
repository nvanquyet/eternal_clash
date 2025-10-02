using _GAME.Scripts.DesignPattern.Interaction;
using _GAME.Scripts.HideAndSeek.Player.AI;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace _GAME.Scripts.HideAndSeek.Player
{
    /// <summary>
    /// Bot NPC kế thừa RolePlayer để tái sử dụng health/combat system
    /// Bot CÓ THỂ CHẾT và trigger penalty cho Seeker
    /// </summary>
    public class BotPlayer : RolePlayer
    {
        [SerializeField] private Citizen_AIScript aiLogicController;

        public override void OnGameEnd(Role winnerRole) => aiLogicController.ClearAll();


        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!IsServer) return;
            networkRole.Value = Role.Bot;
        }

        protected override void InitializeSkills()
        {
        }

        public override void UseSkill(SkillType skillType, Vector3? targetPosition = null)
        {
        }

        public override void ApplyPenaltyForKillingBot()
        {
        }

        public override bool HasSkillsAvailable => false;

        protected override void HandleRegisterInput()
        {
        }

        protected override void HandleUnRegisterInput()
        {
        }

        public override void OnGameStart()
        {
        }

        public override void OnDeath(IAttackable killer)
        {
            if (IsServer)
            {
                ulong killerId = ResolveKillerClientId(killer);

                // ✅ Phân biệt: Bot chết do Seeker bắn nhầm
                GameEvent.OnBotKilled?.Invoke(killerId);
            }

            base.OnDeath(killer);
            aiLogicController.Kill();
        }
    }
}