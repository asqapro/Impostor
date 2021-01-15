using Impostor.Api.Events;
using Impostor.Api.Events.Player;
using System;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Numerics;
using System.Text;

namespace Impostor.Plugins.TrackMovement.Handlers
{
    public class GameEventListener : IEventListener
    {
        private readonly ILogger<GameEventListener> _logger;
        

        public GameEventListener(ILogger<GameEventListener> logger)
        {
            _logger = logger;
            
        }

        [EventListener(EventPriority.Monitor)]
        public void OnGame(IGameEvent e)
        {
            Console.WriteLine(e.GetType().Name + " triggered");
        }

        [EventListener]
        public void OnGameCreated(IGameCreatedEvent e)
        {
            Console.WriteLine("Game > created");
        }

        [EventListener]
        public void OnGameStarting(IGameStartingEvent e)
        {
            Console.WriteLine("Game > starting");
        }

        [EventListener]
        public void OnGameStarted(IGameStartedEvent e)
        {
            Console.WriteLine("Game > started");

            foreach (var player in e.Game.Players)
            {
                var info = player.Character.PlayerInfo;

                Console.WriteLine($"- {info.PlayerName} {info.IsImpostor}");
            }
        }

        [EventListener]
        public void OnGameEnded(IGameEndedEvent e)
        {
            Console.WriteLine("Game > ended");
            Console.WriteLine("- Reason: " + e.GameOverReason);
        }

        [EventListener]
        public void OnGameDestroyed(IGameDestroyedEvent e)
        {
            Console.WriteLine("Game > destroyed");
        }

        [EventListener]
        public void OnPlayerJoined(IGamePlayerJoinedEvent e)
        {
            Console.WriteLine("Player joined a game.");
        }

        [EventListener]
        public void OnPlayerLeftGame(IGamePlayerLeftEvent e)
        {
            Console.WriteLine("Player left a game.");
        }

        [EventListener]
        public async ValueTask OnPlayerChat(IPlayerChatEvent e)
        {
            if (e.Message.StartsWith("/"))
            {
                String[] commandPieces = e.Message.Split(" ", 2, StringSplitOptions.RemoveEmptyEntries);

                

                        /*var info = player.Character.PlayerInfo;
                        if (info.PlayerName == commandPieces[1])
                        {
                            var imp = e.Game.Players.First(x => x.Character.PlayerInfo.IsImpostor);
                            var currPos = imp.Character.NetworkTransform.Position;
                            Console.WriteLine("Curr pos: " + currPos);
                            await player.Character.SetMurderedByAsync(imp);
                            await imp.Character.NetworkTransform.SnapToAsync(currPos);
                            Console.WriteLine("Post kill pos: " + imp.Character.NetworkTransform.Position);
                        }*/
                    //}
                //}
            }
        }
    }
}
