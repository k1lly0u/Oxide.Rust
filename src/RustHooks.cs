using Network;
using Rust.Ai;
using Rust.Ai.HTN;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using uMod.Configuration;
using uMod.Libraries.Universal;
using uMod.Plugins;
using uMod.RemoteConsole;
using UnityEngine;

namespace uMod.Rust
{
    /// <summary>
    /// Game hooks and wrappers for the core Rust plugin
    /// </summary>
    public partial class Rust
    {
        internal bool isPlayerTakingDamage;
        internal static string ipPattern = @":{1}[0-9]{1}\d*";

        #region Modifications

        // Disable native RCON if custom RCON is enabled
        [HookMethod("IOnRconInitialize")]
        private object IOnRconInitialize() => Interface.uMod.Config.Rcon.Enabled ? (object)true : null;

        // Set default values for command-line arguments and hide output
        [HookMethod("IOnRunCommandLine")]
        private object IOnRunCommandLine()
        {
            foreach (KeyValuePair<string, string> pair in Facepunch.CommandLine.GetSwitches())
            {
                string value = pair.Value;
                if (value == "")
                {
                    value = "1";
                }

                string str = pair.Key.Substring(1);
                ConsoleSystem.Option options = ConsoleSystem.Option.Unrestricted;
                options.PrintOutput = false;
                ConsoleSystem.Run(options, str, value);
            }
            return true;
        }

        #endregion Modifications

        #region Server Hooks

        /// <summary>
        /// Called when a remote console command is received
        /// </summary>
        /// <returns></returns>
        /// <param name="sender"></param>
        /// <param name="command"></param>
        [HookMethod("IOnRconCommand")]
        private object IOnRconCommand(IPEndPoint sender, string command)
        {
            if (sender != null && !string.IsNullOrEmpty(command))
            {
                RemoteMessage message = RemoteMessage.GetMessage(command);
                if (!string.IsNullOrEmpty(message?.Message))
                {
                    string[] fullCommand = CommandLine.Split(message.Message);
                    if (fullCommand.Length >= 1)
                    {
                        string cmd = fullCommand[0].ToLower();
                        string[] args = fullCommand.Skip(1).ToArray();

                        // Call universal hook
                        if (Interface.CallHook("OnRconCommand", sender, cmd, args) != null)
                        {
                            return true;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Called when a server command was run
        /// </summary>
        /// <param name="arg"></param>
        /// <returns></returns>
        [HookMethod("IOnServerCommand")]
        private object IOnServerCommand(ConsoleSystem.Arg arg)
        {
            if (arg == null || arg.Connection != null && arg.Player() == null)
            {
                return true; // Ignore console commands from client during connection
            }

            if (arg.cmd.FullName == "chat.say")
            {
                return null; // Skip chat commands, those are handled elsewhere
            }

            IPlayer player = arg.Player()?.IPlayer;
            if (player != null)
            {
                return Interface.CallHook("OnPlayerCommand", player, arg.cmd.FullName, arg.Args);
            }

            return Interface.CallHook("OnServerCommand", arg.cmd.FullName, arg.Args);
        }

        #endregion Server Hooks

        #region Player Hooks

        /// <summary>
        /// Called when a player attempts to pickup a DoorCloser entity
        /// </summary>
        /// <param name="player"></param>
        /// <param name="entity"></param>
        /// <returns></returns>
        [HookMethod("ICanPickupEntity")]
        private object ICanPickupEntity(BasePlayer player, DoorCloser entity)
        {
            object callHook = Interface.CallHook("CanPickupEntity", player, entity);
            return callHook is bool result && result ? (object)true : null;
        }

        /// <summary>
        /// Called when a BasePlayer is attacked
        /// This is used to call OnEntityTakeDamage for a BasePlayer when attacked
        /// </summary>
        /// <param name="player"></param>
        /// <param name="info"></param>
        [HookMethod("IOnBasePlayerAttacked")]
        private object IOnBasePlayerAttacked(BasePlayer player, HitInfo info)
        {
            if (!serverInitialized || player == null || info == null || player.IsDead() || isPlayerTakingDamage || player is NPCPlayer)
            {
                return null;
            }

            if (Interface.CallHook("OnEntityTakeDamage", player, info) != null)
            {
                return true;
            }

            isPlayerTakingDamage = true;
            try
            {
                player.OnAttacked(info);
            }
            finally
            {
                isPlayerTakingDamage = false;
            }
            return true;
        }

        /// <summary>
        /// Called when a BasePlayer is hurt
        /// This is used to call OnEntityTakeDamage when the player was hurt without being attacked
        /// </summary>
        /// <param name="player"></param>
        /// <param name="info"></param>
        /// <returns></returns>
        [HookMethod("IOnBasePlayerHurt")]
        private object IOnBasePlayerHurt(BasePlayer player, HitInfo info)
        {
            return isPlayerTakingDamage ? null : Interface.CallHook("OnEntityTakeDamage", player, info);
        }

        /// <summary>
        /// Called when a server group is set for an ID (i.e. banned)
        /// </summary>
        /// <param name="steamId"></param>
        /// <param name="group"></param>
        /// <param name="name"></param>
        /// <param name="reason"></param>
        [HookMethod("IOnServerUsersSet")]
        private void IOnServerUsersSet(ulong steamId, ServerUsers.UserGroup group, string name, string reason)
        {
            if (serverInitialized)
            {
                string id = steamId.ToString();
                IPlayer player = Universal.PlayerManager.FindPlayerById(id);
                if (group == ServerUsers.UserGroup.Banned)
                {
                    // Call universal hooks
                    if (player != null)
                    {
                        Interface.CallHook("OnPlayerBanned", player, reason);
                    }
                    Interface.CallHook("OnPlayerBanned", name, id, player?.Address ?? "0", reason);
                    Interface.CallDeprecatedHook("OnUserBanned", "OnPlayerBanned", new DateTime(2018, 07, 01), name, id, player?.Address ?? "0", reason);
                }
            }
        }

        /// <summary>
        /// Called when a server group is removed for an ID (i.e. unbanned)
        /// </summary>
        /// <param name="steamId"></param>
        [HookMethod("IOnServerUsersRemove")]
        private void IOnServerUsersRemove(ulong steamId)
        {
            if (serverInitialized)
            {
                string id = steamId.ToString();
                IPlayer player = Universal.PlayerManager.FindPlayerById(id);
                if (ServerUsers.Is(steamId, ServerUsers.UserGroup.Banned))
                {
                    // Call universal hooks
                    if (player != null)
                    {
                        Interface.CallHook("OnPlayerUnbanned", player);
                    }
                    Interface.CallHook("OnPlayerUnbanned", player?.Name ?? "Unnamed", id, player?.Address ?? "0");
                    Interface.CallDeprecatedHook("OnUserUnbanned", "OnPlayerUnbanned", new DateTime(2018, 07, 01), player?.Name ?? "Unnamed", id, player?.Address ?? "0");
                }
            }
        }

        /// <summary>
        /// Called when the player is attempting to connect
        /// </summary>
        /// <param name="connection"></param>
        /// <returns></returns>
        [HookMethod("IOnUserApprove")]
        private object IOnUserApprove(Connection connection)
        {
            string username = connection.username;
            string userId = connection.userid.ToString();
            string ipAddress = Regex.Replace(connection.ipaddress, ipPattern, "");

            if (permission.IsLoaded)
            {
                // Update player's stored username
                permission.UpdateNickname(userId, username);

                // Set default groups, if necessary
                uModConfig.DefaultGroups defaultGroups = Interface.uMod.Config.Options.DefaultGroups;
                if (!permission.UserHasGroup(userId, defaultGroups.Players))
                {
                    permission.AddUserGroup(userId, defaultGroups.Players);
                }
                if (connection.authLevel == 2 && !permission.UserHasGroup(userId, defaultGroups.Administrators))
                {
                    permission.AddUserGroup(userId, defaultGroups.Administrators);
                }
                else if (connection.authLevel == 1 && !permission.UserHasGroup(userId, defaultGroups.Moderators))
                {
                    permission.AddUserGroup(userId, defaultGroups.Moderators);
                }
            }

            // Let universal know
            Universal.PlayerManager.PlayerJoin(connection.userid, username); // TODO: Handle this automatically

            // Call universal hook
            object loginUniversal = Interface.CallHook("CanPlayerLogin", username, userId, ipAddress);
            object loginDeprecated = Interface.CallDeprecatedHook("CanUserLogin", "CanPlayerLogin", new DateTime(2018, 07, 01), username, userId, ipAddress);
            object canLogin = loginUniversal ?? loginDeprecated;
            if (canLogin is string || canLogin is bool && !(bool)canLogin)
            {
                // Reject player with message
                ConnectionAuth.Reject(connection, canLogin is string ? canLogin.ToString() : lang.GetMessage("ConnectionRejected", this, userId));
                return true;
            }

            // Let plugins know
            Interface.CallHook("OnPlayerApproved", username, userId, ipAddress);
            Interface.CallDeprecatedHook("OnUserApprove", "OnPlayerApprove", new DateTime(2018, 07, 01), username, userId, ipAddress);
            Interface.CallDeprecatedHook("OnUserApproved", "OnPlayerApproved", new DateTime(2018, 07, 01), username, userId, ipAddress);

            return null;
        }

        /// <summary>
        /// Called when the player has been banned by EAC
        /// </summary>
        /// <param name="connection"></param>
        [HookMethod("IOnPlayerBanned")]
        private void IOnPlayerBanned(Connection connection)
        {
            string ip = Regex.Replace(connection.ipaddress, ipPattern, "");
            string reason = connection.authStatus ?? "Unknown"; // TODO: Localization

            // Call universal hooks
            IPlayer player = (connection.player as BasePlayer)?.IPlayer;
            if (player != null)
            {
                Interface.CallHook("OnPlayerBanned", player, reason);
                Interface.CallDeprecatedHook("OnUserBanned", "OnPlayerBanned", new DateTime(2018, 07, 01), player, reason);
            }
            Interface.CallHook("OnPlayerBanned", connection.username, connection.userid.ToString(), ip, reason);
        }

        /// <summary>
        /// Called when the player sends a chat message
        /// </summary>
        /// <param name="arg"></param>
        /// <returns></returns>
        [HookMethod("IOnPlayerChat")]
        private object IOnPlayerChat(ConsoleSystem.Arg arg)
        {
            // Get the full chat message
            string message = arg.GetString(0).Trim();
            if (string.IsNullOrEmpty(message))
            {
                // Ignore if message is empty
                return true;
            }

            // Get player object
            IPlayer player = (arg.Connection.player as BasePlayer)?.IPlayer;
            if (player != null)
            {
                // Call universal hook
                object chatUniversal = Interface.CallHook("OnPlayerChat", player, message);
                object chatDeprecated = Interface.CallDeprecatedHook("OnUserChat", "OnPlayerChat", new DateTime(2018, 07, 01), player, message);
                return chatUniversal ?? chatDeprecated;
            }

            return null;
        }

        /// <summary>
        /// Called when the player sends a chat command
        /// </summary>
        /// <param name="arg"></param>
        /// <returns></returns>
        [HookMethod("IOnPlayerCommand")]
        private void IOnPlayerCommand(ConsoleSystem.Arg arg)
        {
            string command = arg.GetString(0).Trim();

            // Check if it is a chat command
            if (string.IsNullOrEmpty(command) || command[0] != '/' || command.Length <= 1)
            {
                return;
            }

            ParseCommand(command.TrimStart('/'), out string cmd, out string[] args);
            if (cmd == null)
            {
                return;
            }

            // Get universal player object
            IPlayer player = (arg.Connection.player as BasePlayer)?.IPlayer;
            if (player == null)
            {
                return;
            }

            // Is the command blocked?
            object commandUniversal = Interface.CallHook("OnPlayerChat", player, cmd, args);
            object commandDeprecated = Interface.CallDeprecatedHook("OnUserChat", "OnPlayerChat", new DateTime(2018, 07, 01), player, cmd, args);
            if (commandUniversal != null || commandDeprecated != null)
            {
                return;
            }

            // Is it a valid chat command?
            if (!Universal.CommandSystem.HandleChatMessage(player, command))
            {
                if (Interface.uMod.Config.Options.Modded)
                {
                    player.Reply(string.Format(lang.GetMessage("UnknownCommand", this, player.Id), cmd));
                }
            }
        }

        /// <summary>
        /// Called when the player has disconnected
        /// </summary>
        /// <param name="basePlayer"></param>
        /// <param name="reason"></param>
        [HookMethod("OnPlayerDisconnected")]
        private void OnPlayerDisconnected(BasePlayer basePlayer, string reason)
        {
            // Let universal know
            Universal.PlayerManager.PlayerDisconnected(basePlayer);

            IPlayer player = basePlayer.IPlayer;
            if (player != null)
            {
                // Call universal hook
                Interface.CallHook("OnPlayerDisconnected", player, reason);
                Interface.CallDeprecatedHook("OnUserDisconnected", "OnPlayerDisconnected", new DateTime(2018, 07, 01), player, reason);
            }
        }

        /// <summary>
        /// Called when the player has connected
        /// </summary>
        /// <param name="basePlayer"></param>
        [HookMethod("OnPlayerInit")]
        private void OnPlayerInit(BasePlayer basePlayer)
        {
            // Set default language for player if not set
            if (string.IsNullOrEmpty(lang.GetLanguage(basePlayer.UserIDString)))
            {
                lang.SetLanguage(basePlayer.net.connection.info.GetString("global.language", "en"), basePlayer.UserIDString);
            }

            // Let universal know
            Universal.PlayerManager.PlayerConnected(basePlayer);

            IPlayer player = Universal.PlayerManager.FindPlayerById(basePlayer.UserIDString);
            if (player != null)
            {
                // Set IPlayer object on BasePlayer
                basePlayer.IPlayer = player;

                // Call universal hook
                Interface.CallHook("OnPlayerConnected", player);
                Interface.CallDeprecatedHook("OnUserConnected", "OnPlayerConnected", new DateTime(2018, 07, 01), player);
            }
        }

        /// <summary>
        /// Called when the player has been kicked
        /// </summary>
        /// <param name="basePlayer"></param>
        /// <param name="reason"></param>
        [HookMethod("OnPlayerKicked")]
        private void OnPlayerKicked(BasePlayer basePlayer, string reason)
        {
            IPlayer player = basePlayer.IPlayer;
            if (player != null)
            {
                // Call universal hook
                Interface.CallHook("OnPlayerKicked", player, reason);
                Interface.CallDeprecatedHook("OnUserKicked", "OnPlayerKicked", new DateTime(2018, 07, 01), player);
            }
        }

        /// <summary>
        /// Called when the player is respawning
        /// </summary>
        /// <param name="basePlayer"></param>
        /// <returns></returns>
        [HookMethod("OnPlayerRespawn")]
        private object OnPlayerRespawn(BasePlayer basePlayer)
        {
            // Call universal hook
            IPlayer player = basePlayer.IPlayer;
            object respawnUniversal = Interface.CallHook("OnPlayerRespawn", player);
            object respawnDeprecated = Interface.CallDeprecatedHook("OnUserRespawn", "OnPlayerRespawn", new DateTime(2018, 07, 01), player);
            return respawnUniversal ?? respawnDeprecated;
        }

        /// <summary>
        /// Called when the player has respawned
        /// </summary>
        /// <param name="basePlayer"></param>
        [HookMethod("OnPlayerRespawned")]
        private void OnPlayerRespawned(BasePlayer basePlayer)
        {
            IPlayer player = basePlayer.IPlayer;
            if (player != null)
            {
                // Call universal hook
                Interface.CallHook("OnPlayerRespawned", player);
                Interface.CallDeprecatedHook("OnUserRespawned", "OnPlayerRespawned", new DateTime(2018, 07, 01), player);
            }
        }

        #endregion Player Hooks

        #region Entity Hooks

        /// <summary>
        /// Called when a BaseCombatEntity takes damage
        /// This is used to call OnEntityTakeDamage for anything other than a BasePlayer
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="info"></param>
        /// <returns></returns>
        [HookMethod("IOnBaseCombatEntityHurt")]
        private object IOnBaseCombatEntityHurt(BaseCombatEntity entity, HitInfo info)
        {
            return entity is BasePlayer ? null : Interface.CallHook("OnEntityTakeDamage", entity, info);
        }

        private int GetPlayersSensed(NPCPlayerApex npc, Vector3 position, float distance, BaseEntity[] targetList)
        {
            return BaseEntity.Query.Server.GetInSphere(position, distance, targetList,
                entity =>
                {
                    BasePlayer player = entity as BasePlayer;
                    object callHook = player != null && npc != null && player != npc ? Interface.CallHook("OnNpcPlayerTarget", npc, player) : null;
                    if (callHook != null)
                    {
                        foreach (Memory.SeenInfo seenInfo in npc.AiContext.Memory.All)
                        {
                            if (seenInfo.Entity == player)
                            {
                                npc.AiContext.Memory.All.Remove(seenInfo);
                                break;
                            }
                        }

                        foreach (Memory.ExtendedInfo extendedInfo in npc.AiContext.Memory.AllExtended)
                        {
                            if (extendedInfo.Entity == player)
                            {
                                npc.AiContext.Memory.AllExtended.Remove(extendedInfo);
                                break;
                            }
                        }
                    }

                    return player != null && callHook == null && player.isServer && !player.IsSleeping() && !player.IsDead() && player.Family != npc.Family;
                });
        }

        /// <summary>
        /// Called when an Apex NPC player tries to target an entity based on closeness
        /// </summary>
        /// <param name="npc"></param>
        /// <returns></returns>
        [HookMethod("IOnNpcPlayerSenseClose")]
        private object IOnNpcPlayerSenseClose(NPCPlayerApex npc)
        {
            NPCPlayerApex.EntityQueryResultCount = GetPlayersSensed(npc, npc.ServerPosition, npc.Stats.CloseRange, NPCPlayerApex.EntityQueryResults);
            return true;
        }

        /// <summary>
        /// Called when an Apex NPC player tries to target an entity based on vision
        /// </summary>
        /// <param name="npc"></param>
        /// <returns></returns>
        [HookMethod("IOnNpcPlayerSenseVision")]
        private object IOnNpcPlayerSenseVision(NPCPlayerApex npc)
        {
            NPCPlayerApex.PlayerQueryResultCount = GetPlayersSensed(npc, npc.ServerPosition, npc.Stats.VisionRange, NPCPlayerApex.PlayerQueryResults);
            return true;
        }

        /// <summary>
        /// Called when a Murderer NPC player tries to target an entity
        /// </summary>
        /// <param name="npc"></param>
        /// <param name="target"></param>
        /// <returns></returns>
        [HookMethod("IOnNpcPlayerTarget")]
        private object IOnNpcPlayerTarget(NPCPlayerApex npc, BaseEntity target)
        {
            if (Interface.CallHook("OnNpcPlayerTarget", npc, target) != null)
            {
                return 0f;
            }

            return null;
        }

        /// <summary>
        /// Called when an HTN NPC player tries to target an entity
        /// </summary>
        /// <param name="npc"></param>
        /// <param name="target"></param>
        /// <returns></returns>
        [HookMethod("IOnHtnNpcPlayerTarget")]
        private object IOnHtnNpcPlayerTarget(IHTNAgent npc, BasePlayer target)
        {
            if (npc != null && Interface.CallHook("OnNpcPlayerTarget", npc.Body, target) != null)
            {
                npc.AiDomain.NpcContext.BaseMemory.Forget(0f);
                npc.AiDomain.NpcContext.BaseMemory.PrimaryKnownEnemyPlayer.PlayerInfo.Player = null;
                return true;
            }

            return null;
        }

        /// <summary>
        /// Called when an NPC animal tries to target an entity
        /// </summary>
        /// <param name="npc"></param>
        /// <param name="target"></param>
        /// <returns></returns>
        [HookMethod("IOnNpcTarget")]
        private object IOnNpcTarget(BaseNpc npc, BaseEntity target)
        {
            object callHook = Interface.CallHook("OnNpcTarget", npc, target);
            if (callHook != null)
            {
                npc.SetFact(BaseNpc.Facts.HasEnemy, 0);
                npc.SetFact(BaseNpc.Facts.EnemyRange, 3);
                npc.SetFact(BaseNpc.Facts.AfraidRange, 1);
                return 0f;
            }

            return null;
        }

        #endregion Entity Hooks

        #region Item Hooks

        /// <summary>
        /// Called when an item loses durability
        /// </summary>
        /// <param name="item"></param>
        /// <param name="amount"></param>
        /// <returns></returns>
        [HookMethod("IOnLoseCondition")]
        private object IOnLoseCondition(Item item, float amount)
        {
            // Call hook for plugins
            Interface.CallHook("OnLoseCondition", item, amount);

            float condition = item.condition;
            item.condition -= amount;
            if (item.condition <= 0f && item.condition < condition)
            {
                item.OnBroken();
            }

            return true;
        }

        #endregion Item Hooks
    }
}
