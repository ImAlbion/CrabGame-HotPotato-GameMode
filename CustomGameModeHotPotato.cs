using BepInEx;
using BepInEx.IL2CPP;
using BepInEx.IL2CPP.Utils;
using BepInEx.Logging;
using CustomGameModes;
using HarmonyLib;
using SteamworksNative;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GameModeInverted
{
    public sealed class CustomGameModeHotPotato : CustomGameModes.CustomGameMode
    {
        #region Fields and Properties

        internal static CustomGameModeHotPotato Instance;
        //internal static GameModeTag Tag;

        internal Harmony patches;

        // Game state variables
        internal static bool isGameStarted = false;
        internal static int startingPlayerCount = 0;
        internal static int framesSinceGameStarted = 60;
        internal static bool isTimeSet = false;
        internal static ulong taggedPlayerId = 0;
        internal static float bombTagTime = 25f;
        internal static int amountToKill = 4;

        public CustomGameModeHotPotato() : base
        (
            name: "Hot Potato",
            description: "• Its like Bomb Tag\n\n• But with snowballs\n\n• Good luck :)",
            gameModeType: GameModeType.Tag,
            vanillaGameModeType: GameModeType.Tag,
            waitForRoundOverToDeclareSoloWinner: true,

            shortModeTime: 80,
            mediumModeTime: 120,
            longModeTime: 180,

            compatibleMapNames: [
                "Bitter Beach",
                "Blueline",
                "Cocky Containers",
                "Color Climb",
                "Desert",
                "Dorm",
                "Plains",
                "Funky Field",
                "Hasty Hill",
                "Icy Crack",
                "Karlson",
                "Lanky Lava",
                "Playground",
                "Playground2",
                "Return to Monke",
                "Small Color Climb",
                "Small Beach",
                "Small Saloon",
                "Small Containers",
                "Tiny Town",
                "Tiny Town 2",
                "Crabfields",
                "Crabheat",
                "Crabland",
                "Sandstorm",
                "Snowtop",
                "Splat",
                "Splot",
                "Sunny Saloon",
                "Toxic Train",
            ],

            smallMapPlayers: 3,
            mediumAndSmallMapPlayers: 4,
            largeAndMediumMapPlayers: 7,
            largeMapPlayers: 12
        )
            => Instance = this;

        public override void PreInit()
        {
            patches = Harmony.CreateAndPatchAll(GetType());
        }
        public override void PostEnd()
        {
            ResetValues();
            patches?.UnpatchSelf();
        }
        public static void ResetValues()
        {
            isGameStarted = false;
            startingPlayerCount = 0;
            framesSinceGameStarted = 60;
            isTimeSet = false;
            taggedPlayerId = 0;
            bombTagTime = 25f;
            amountToKill = 4;
        }

        #endregion

        #region Harmony Patches

        [HarmonyPatch(typeof(GameModeTag), nameof(GameModeTag.OnFreezeOver))]
        [HarmonyPrefix]
        internal static bool PreOnFreezeOver(GameModeTag __instance)
        {
            if (!SteamManager.Instance.IsLobbyOwner())
                return false;

            isGameStarted = true;

            //__instance.field_Private_Int32_0 = 999; // so the game doesnt end early BOMBTAG VALUE

            // Get all alive players
            List<ulong> alivePlayers = GetAlivePlayers();

            // Set startingPlayerCount to the actual count of alive players
            startingPlayerCount = alivePlayers.Count;
            
            // Debug message to verify player count
            ServerSend.SendChatMessage(1, "Players in game: " + startingPlayerCount);

            TagRandomPlayer(); // Tag a random player

            amountToKill = Mathf.RoundToInt(startingPlayerCount * 0.5f);

            // Debug message to verify amountToKill value
            ServerSend.SendChatMessage(1, "Amount to kill set to: " + amountToKill);

            ServerSend.SendChatMessage(1, "Game started! " + amountToKill + " need to die!");

            if(amountToKill <= 0)
            {
                amountToKill = 1;
            }

            return false;
        }

        [HarmonyPatch(typeof(GameMode), nameof(GameMode.Update))]
        [HarmonyPostfix]
        public static void GameModeUpdate(GameMode __instance)
        {
            if (!SteamManager.Instance.IsLobbyOwner())
                return;
            // Check if the game has started
            if (!isGameStarted)
                return;

            // bomb tag timer logic
            bombTagTime -= Time.deltaTime;
            if (bombTagTime <= 0)
            {
                // Add extra protection against null references
                if (taggedPlayerId != 0 && GameManager.Instance != null && 
                    GameManager.Instance.activePlayers != null && 
                    GameManager.Instance.activePlayers.ContainsKey(taggedPlayerId) && 
                    GameManager.Instance.activePlayers[taggedPlayerId] != null)
                {
                    try 
                    {
                        // Now it's safe to call PlayerDied
                        ServerSend.PlayerDied(taggedPlayerId, 1, Vector3.zero);
                    }
                    catch (Exception ex)
                    {
                        // If it still fails, tag a random player instead of crashing
                        ServerSend.SendChatMessage(1, "Error killing tagged player: " + ex.Message);
                        bombTagTime = 5f; // Reset timer with shorter delay
                        TagRandomPlayer(); // Try to recover by tagging someone else
                    }
                }
                else
                {
                    TagRandomPlayer(); // If the tagged player doesn't exist, tag a random player
                }
            }
        }

        [HarmonyPatch(typeof(ServerSend), nameof(ServerSend.PlayerDied))]
        [HarmonyPostfix]
        public static void GameServerPlayerDied(ulong param_0, ulong param_1, Vector3 param_2)
        {
            if (!SteamManager.Instance.IsLobbyOwner())
                return;
            amountToKill--; // Decrease the amount to kill when a player dies
            
            // Add a debug message to verify player death
            ServerSend.SendChatMessage(1, "Player died! " + amountToKill + " more need to die.");
            
            // Check if we need to end the game
            if (amountToKill <= 0)
            {
                ServerSend.SendChatMessage(1, "Target number of kills reached! Game over!");
                isGameStarted = false;
                return;
            }

            // Check if the player who died is the tagged player
            if (param_0 == taggedPlayerId)
            {
                // pick a random player to be tagged
                //List<ulong> alivePlayers = GetAlivePlayers();
                //if (alivePlayers.Count == 0)
                //{
                //    ServerSend.SendChatMessage(1, "No players left to tag!");
                //    isGameStarted = false;
                //    return;
                //}
                //ulong randomPlayerId = alivePlayers[UnityEngine.Random.Range(0, alivePlayers.Count)];
                //taggedPlayerId = randomPlayerId;
                //ServerSend.TagPlayer(0, randomPlayerId); // Set the new tagged player
                TagRandomPlayer(); // Re-tag a player if the tagged player dies
                ServerSend.SendChatMessage(1, "Tagged player died! New player tagged.");
            }
            
            // Always reset the bomb timer when any player dies
            bombTagTime = 25f;
        }

        [HarmonyPatch(typeof(LobbyManager), nameof(LobbyManager.OnPlayerJoinLeaveUpdate))]
        [HarmonyPostfix]
        public static void LobbyManagerOnPlayerJoinLeave(CSteamID param_1, bool param_2)
        {
            if (!SteamManager.Instance.IsLobbyOwner())
                return;

            if(param_1.m_SteamID == taggedPlayerId)
            {
                //pick a random player to be tagged
                //List<ulong> alivePlayers = GetAlivePlayers();
                //if (alivePlayers.Count == 0)
                //    return;
                //ulong randomPlayerId = alivePlayers[UnityEngine.Random.Range(0, alivePlayers.Count)];
                //taggedPlayerId = randomPlayerId;
                //ServerSend.TagPlayer(0, taggedPlayerId);

                TagRandomPlayer(); // Re-tag a player if the tagged player leaves
            }
        }
        
        [HarmonyPatch(typeof(ServerSend), nameof(ServerSend.DropItem))]
        [HarmonyPrefix]
        public static bool ServerSendDropItem(ulong param_0, int param_1, int param_2, int param_3 = 0)
        {
            if(!isGameStarted)
                return true; // Continue with original method execution
            if (!SteamManager.Instance.IsLobbyOwner())
                return true; // Continue with original method execution

                return false; // Prevent dropping the stick
        }
        
        [HarmonyPatch(typeof(GameModeTag), nameof(GameModeTag.TagPlayer))]
        [HarmonyPostfix]
        public static void GameModeSetTag(ulong param_1, ulong param_2)
        {
            if (!SteamManager.Instance.IsLobbyOwner())
                return;

            // Check if BOTH players exist in the dictionary before accessing
            if (GameManager.Instance.activePlayers.ContainsKey(param_1) && 
                GameManager.Instance.activePlayers.ContainsKey(param_2))
            {
                ServerSend.SendChatMessage(1, GameManager.Instance.activePlayers[param_1].username + 
                    " tagged " + GameManager.Instance.activePlayers[param_2].username + ".");

                GameServer.ForceRemoveItemItemId(param_2, 10); // remove the stick from the player who was tagged
            }
            return;
        }

        [HarmonyPatch(typeof(ServerSend), nameof(ServerSend.PlayerDamage))]
        [HarmonyPostfix]
        public static void OnPlayerDamage(ulong param_0, ulong param_1, int param_2, Vector3 param_3, int param_4)
        {
            if (!SteamManager.Instance.IsLobbyOwner())
                return;
            // param_0 is the player who dealt damage
            // param_1 is the player who took damage
            // param_2 is the damage amount
            // param_3 is the position of the damage
            // param_4 is the item id of the item that dealt damage

            //check for the item id of the snowball
            if (param_4 != 9) // 9 is the item id for the snowball
                return;

            //check if the player who dealt damage is the tagged player
            if (param_0 != taggedPlayerId)
                return;

            if (taggedPlayerId != param_0)
                return;

            // check if the player who dealt damage is alive
            if (!GameManager.Instance.activePlayers.ContainsKey(param_0) || GameManager.Instance.activePlayers[param_0].dead)
                return; // If the player who dealt damage is dead, do nothing

            //tag the player who took damage
            taggedPlayerId = param_1; //tag the player who took damage
            ServerSend.TagPlayer(param_0, param_1); // 9 is the item id for the snowball
            GameServer.ForceRemoveItemItemId(param_0, 9); // remove the snowball from the player who tagged
            GameServer.ForceGiveWeapon(param_1, 9, SharedObjectManager.Instance.GetNextId()); // Give the snowball to the player who was tagged
        }

        private static IEnumerator GiveSnowballCoroutine(ulong playerId)
        {
            // Wait for half a second before giving the snowball
            yield return new WaitForSeconds(0.5f);
            
            // Check if the player is still tagged and alive
            if (playerId == taggedPlayerId && GameManager.Instance.activePlayers.ContainsKey(playerId) && 
                !GameManager.Instance.activePlayers[playerId].dead)
            {
                GameServer.ForceGiveWeapon(playerId, 9, SharedObjectManager.Instance.GetNextId());
            }
        }

        [HarmonyPatch(typeof(ServerSend), nameof(ServerSend.UseItemAll))]
        [HarmonyPostfix]
        public static void ThrownSnowball(ulong param_0, int param_1)
        {
            if (!SteamManager.Instance.IsLobbyOwner())
                return;
            
            // param_0 is the player who used the item
            // param_1 is the item id of the item that was used

            if (param_1 != 9) // 9 is the item id for the snowball
                return;
            if (taggedPlayerId != param_0)
                return; // Only allow the tagged player

            // Use a coroutine to give the player a new snowball after 0.5 seconds
            MonoBehaviourExtensions.StartCoroutine(LobbyManager.Instance, GiveSnowballCoroutine(param_0));
        }

        #endregion

        #region Player Utilities
        public static List<ulong> GetAlivePlayers()
        {
            List<ulong> list = new();
            foreach (var player in GameManager.Instance.activePlayers)
            {
                if (player == null || player.Value.dead) continue;
                list.Add(player.Key);
            }
            return list;
        }

        private static void TagRandomPlayer()
        {
            List<ulong> alivePlayers = GetAlivePlayers();
            if (alivePlayers.Count == 0)
            {
                ServerSend.SendChatMessage(1, "No players left to tag!");
                isGameStarted = false;
                return;
            }
            ulong randomPlayerId = alivePlayers[UnityEngine.Random.Range(0, alivePlayers.Count)];
            taggedPlayerId = randomPlayerId;
            ServerSend.TagPlayer(0, taggedPlayerId);
            GameServer.ForceGiveWeapon(taggedPlayerId, 9, SharedObjectManager.Instance.GetNextId()); // Give the snowball to the player who was tagged
        }

        #endregion
    }
}
