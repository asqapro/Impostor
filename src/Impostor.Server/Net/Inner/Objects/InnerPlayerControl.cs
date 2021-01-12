using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Generic;
using Impostor.Api;
using Impostor.Api.Events.Managers;
using Impostor.Api.Innersloth;
using Impostor.Api.Net;
using Impostor.Api.Net.Messages;
using Impostor.Server.Events.Player;
using Impostor.Server.Net.Inner.Objects.Components;
using Impostor.Server.Net.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Impostor.Server.Net.Inner.Objects
{
    internal class CommandParser
    {
        public Dictionary<String, Command> Commands {get; set;}
        public Dictionary<String, bool> Enabled {get; set;}

    }

    internal class Command
    {
        public int Length {get; set;}
        public Char[] Delims {get; set;}
        public String Help {get; set;}
        public bool Hostonly {get; set;}
        public String Message {get; set;}
    }
    
    internal partial class InnerPlayerControl : InnerNetObject
    {
        private readonly ILogger<InnerPlayerControl> _logger;
        private readonly IEventManager _eventManager;
        private readonly Game _game;

        public InnerPlayerControl(ILogger<InnerPlayerControl> logger, IServiceProvider serviceProvider, IEventManager eventManager, Game game)
        {
            _logger = logger;
            _eventManager = eventManager;
            _game = game;

            Physics = ActivatorUtilities.CreateInstance<InnerPlayerPhysics>(serviceProvider, this, _eventManager, _game);
            NetworkTransform = ActivatorUtilities.CreateInstance<InnerCustomNetworkTransform>(serviceProvider, this, _game);

            Components.Add(this);
            Components.Add(Physics);
            Components.Add(NetworkTransform);

            PlayerId = byte.MaxValue;
        }

        public bool IsNew { get; private set; }

        public byte PlayerId { get; private set; }

        public InnerPlayerPhysics Physics { get; }

        public InnerCustomNetworkTransform NetworkTransform { get; }

        public InnerPlayerInfo PlayerInfo { get; internal set; }

        public override async ValueTask<byte[]> HandleRpc(ClientPlayer sender, ClientPlayer? target, RpcCalls call, IMessageReader reader)
        {

            byte[] editPayload = null;

            switch (call)
            {
                // Play an animation.
                case RpcCalls.PlayAnimation:
                {
                    if (!sender.IsOwner(this))
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.PlayAnimation)} to an unowned {nameof(InnerPlayerControl)}");
                    }

                    if (target != null)
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.PlayAnimation)} to a specific player instead of broadcast");
                    }

                    var animation = reader.ReadByte();
                    break;
                }

                // Complete a task.
                case RpcCalls.CompleteTask:
                {
                    if (!sender.IsOwner(this))
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.CompleteTask)} to an unowned {nameof(InnerPlayerControl)}");
                    }

                    if (target != null)
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.CompleteTask)} to a specific player instead of broadcast");
                    }

                    var taskId = reader.ReadPackedUInt32();
                    var task = PlayerInfo.Tasks[(int)taskId];
                    if (task == null)
                    {
                        _logger.LogWarning($"Client sent {nameof(RpcCalls.CompleteTask)} with a taskIndex that is not in their {nameof(InnerPlayerInfo)}");
                    }
                    else
                    {
                        task.Complete = true;
                        await _eventManager.CallAsync(new PlayerCompletedTaskEvent(_game, sender, this, task));
                    }

                    break;
                }

                // Update GameOptions.
                case RpcCalls.SyncSettings:
                {
                    if (!sender.IsHost)
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.SyncSettings)} but was not a host");
                    }

                    _game.Options.Deserialize(reader.ReadBytesAndSize());
                    break;
                }

                // Set Impostors.
                case RpcCalls.SetInfected:
                {
                    if (!sender.IsHost)
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.SetInfected)} but was not a host");
                    }

                    var length = reader.ReadPackedInt32();

                    for (var i = 0; i < length; i++)
                    {
                        var playerId = reader.ReadByte();
                        var player = _game.GameNet.GameData.GetPlayerById(playerId);
                        if (player != null)
                        {
                            player.IsImpostor = true;
                        }
                    }

                    if (_game.GameState == GameStates.Starting)
                    {
                        await _game.StartedAsync();
                    }

                    break;
                }

                // Player was voted out.
                case RpcCalls.Exiled:
                {
                    if (!sender.IsHost)
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.Exiled)} but was not a host");
                    }

                    if (target != null)
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.Exiled)} to a specific player instead of broadcast");
                    }

                    // TODO: Not hit?
                    Die(DeathReason.Exile);

                    await _eventManager.CallAsync(new PlayerExileEvent(_game, sender, this));
                    break;
                }

                // Validates the player name at the host.
                case RpcCalls.CheckName:
                {
                    if (target == null || !target.IsHost)
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.CheckName)} to the wrong player");
                    }

                    var name = reader.ReadString();
                    break;
                }

                // Update the name of a player.
                case RpcCalls.SetName:
                {
                    if (!sender.IsHost)
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.SetName)} but was not a host");
                    }

                    if (target != null)
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.SetName)} to a specific player instead of broadcast");
                    }

                    PlayerInfo.PlayerName = reader.ReadString();
                    break;
                }

                // Validates the color at the host.
                case RpcCalls.CheckColor:
                {
                    if (target == null || !target.IsHost)
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.CheckColor)} to the wrong player");
                    }

                    var color = reader.ReadByte();
                    break;
                }

                // Update the color of a player.
                case RpcCalls.SetColor:
                {
                    if (!sender.IsHost)
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.SetColor)} but was not a host");
                    }

                    if (target != null)
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.SetColor)} to a specific player instead of broadcast");
                    }

                    PlayerInfo.ColorId = reader.ReadByte();
                    break;
                }

                // Update the hat of a player.
                case RpcCalls.SetHat:
                {
                    if (!sender.IsOwner(this))
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.SetHat)} to an unowned {nameof(InnerPlayerControl)}");
                    }

                    if (target != null)
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.SetHat)} to a specific player instead of broadcast");
                    }

                    PlayerInfo.HatId = reader.ReadPackedUInt32();
                    break;
                }

                case RpcCalls.SetSkin:
                {
                    if (!sender.IsOwner(this))
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.SetSkin)} to an unowned {nameof(InnerPlayerControl)}");
                    }

                    if (target != null)
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.SetHat)} to a specific player instead of broadcast");
                    }

                    PlayerInfo.SkinId = reader.ReadPackedUInt32();
                    break;
                }

                // TODO: (ANTICHEAT) Location check?
                // only called by a non-host player on to start meeting
                case RpcCalls.ReportDeadBody:
                {
                    if (!sender.IsOwner(this))
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.ReportDeadBody)} to an unowned {nameof(InnerPlayerControl)}");
                    }

                    if (target != null)
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.ReportDeadBody)} to a specific player instead of broadcast");
                    }


                    var deadBodyPlayerId = reader.ReadByte();
                    // deadBodyPlayerId == byte.MaxValue -- means emergency call by button

                    break;
                }

                // TODO: (ANTICHEAT) Cooldown check?
                case RpcCalls.MurderPlayer:
                {
                    if (!sender.IsOwner(this))
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.MurderPlayer)} to an unowned {nameof(InnerPlayerControl)}");
                    }

                    if (target != null)
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.MurderPlayer)} to a specific player instead of broadcast");
                    }

                    if (!sender.Character.PlayerInfo.IsImpostor)
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.MurderPlayer)} as crewmate");
                    }

                    if (!sender.Character.PlayerInfo.CanMurder(_game))
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.MurderPlayer)} too fast");
                    }

                    sender.Character.PlayerInfo.LastMurder = DateTimeOffset.UtcNow;

                    var player = reader.ReadNetObject<InnerPlayerControl>(_game);
                    if (!player.PlayerInfo.IsDead)
                    {
                        player.Die(DeathReason.Kill);
                        await _eventManager.CallAsync(new PlayerMurderEvent(_game, sender, this, player));
                    }

                    break;
                }

                case RpcCalls.SendChat:
                {
                    if (!sender.IsOwner(this))
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.SendChat)} to an unowned {nameof(InnerPlayerControl)}");
                    }

                    if (target != null)
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.SendChat)} to a specific player instead of broadcast");
                    }
                    var chat = reader.ReadString();

                    if (chat.StartsWith("/"))
                    {
                        String commandParsePattern = @"(/\w+)\s+((?:\w+\s*)+)('.*')*";
                        var match = Regex.Match(chat, commandParsePattern);

                        var origin = sender.Character.PlayerInfo.PlayerName;
                        var dest = match.Groups[2].Value.Trim();

                        var chatMod = origin + " entered an invalid command or syntax";
                        var commandsFile = "CommandsList.txt";
                        
                        if (!(match.Groups[1].Value == "/whisper" && !match.Groups[3].Success))
                        {
                            try
                            {
                                foreach (var line in File.ReadLines(commandsFile))
                                {
                                    Char[] commandDelims = {':'};
                                    var commandSyntax = line.Split(commandDelims);
                                    if (commandSyntax[0] == match.Groups[1].Value)
                                    {
                                        chatMod = commandSyntax[1].Replace("%s", origin).Replace("%t", dest);
                                        break;
                                    }
                                }
                            }
                            catch
                            {
                                chatMod = "Failed to find list of commands. Inform the room host <" + _game.Host.Character.PlayerInfo.PlayerName + ">";
                            }
                        }

                        byte[] payload = System.Text.Encoding.ASCII.GetBytes(chatMod);
                        byte[] payloadLen = BitConverter.GetBytes(payload.Length);

                        byte[] fixedPayload = new byte[payload.Length + 1];

                        payload.CopyTo(fixedPayload, 1);
                        fixedPayload[0] = payloadLen[0];

                        editPayload = fixedPayload;
                    }

                    await _eventManager.CallAsync(new PlayerChatEvent(_game, sender, this, chat));

                    break;
                }

                case RpcCalls.StartMeeting:
                {
                    if (!sender.IsHost)
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.StartMeeting)} but was not a host");
                    }

                    if (target != null)
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.StartMeeting)} to a specific player instead of broadcast");
                    }

                    // deadBodyPlayerId == byte.MaxValue -- means emergency call by button
                    var deadBodyPlayerId = reader.ReadByte();
                    var deadPlayer = deadBodyPlayerId != byte.MaxValue
                        ? _game.GameNet.GameData.GetPlayerById(deadBodyPlayerId)?.Controller
                        : null;

                    await _eventManager.CallAsync(new PlayerStartMeetingEvent(_game, _game.GetClientPlayer(this.OwnerId), this, deadPlayer));
                    break;
                }

                case RpcCalls.SetScanner:
                {
                    if (!sender.IsOwner(this))
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.SetScanner)} to an unowned {nameof(InnerPlayerControl)}");
                    }

                    if (target != null)
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.SetScanner)} to a specific player instead of broadcast");
                    }

                    var on = reader.ReadBoolean();
                    var count = reader.ReadByte();
                    break;
                }

                case RpcCalls.SendChatNote:
                {
                    if (!sender.IsOwner(this))
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.SendChatNote)} to an unowned {nameof(InnerPlayerControl)}");
                    }

                    if (target != null)
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.SendChatNote)} to a specific player instead of broadcast");
                    }

                    var playerId = reader.ReadByte();
                    var chatNote = (ChatNoteType)reader.ReadByte();
                    break;
                }

                case RpcCalls.SetPet:
                {
                    if (!sender.IsOwner(this))
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.SetPet)} to an unowned {nameof(InnerPlayerControl)}");
                    }

                    if (target != null)
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.SetPet)} to a specific player instead of broadcast");
                    }

                    PlayerInfo.PetId = reader.ReadPackedUInt32();
                    break;
                }

                // TODO: Understand this RPC
                case RpcCalls.SetStartCounter:
                {
                    if (!sender.IsOwner(this))
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.SetStartCounter)} to an unowned {nameof(InnerPlayerControl)}");
                    }

                    if (target != null)
                    {
                        throw new ImpostorCheatException($"Client sent {nameof(RpcCalls.SetStartCounter)} to a specific player instead of broadcast");
                    }

                    // Used to compare with LastStartCounter.
                    var startCounter = reader.ReadPackedUInt32();

                    // Is either start countdown or byte.MaxValue
                    var secondsLeft = reader.ReadByte();
                    if (secondsLeft < byte.MaxValue)
                    {
                        await _eventManager.CallAsync(new PlayerSetStartCounterEvent(_game, sender, this, secondsLeft));
                    }

                    break;
                }

                default:
                {
                    _logger.LogWarning("{0}: Unknown rpc call {1}", nameof(InnerPlayerControl), call);
                    break;
                }
            }
            return editPayload;
        }

        public override bool Serialize(IMessageWriter writer, bool initialState)
        {
            throw new NotImplementedException();
        }

        public override void Deserialize(IClientPlayer sender, IClientPlayer? target, IMessageReader reader, bool initialState)
        {
            if (!sender.IsHost)
            {
                throw new ImpostorCheatException($"Client attempted to send data for {nameof(InnerPlayerControl)} as non-host");
            }

            if (initialState)
            {
                IsNew = reader.ReadBoolean();
            }

            PlayerId = reader.ReadByte();
        }

        internal void Die(DeathReason reason)
        {
            PlayerInfo.IsDead = true;
            PlayerInfo.LastDeathReason = reason;
        }
    }
}
