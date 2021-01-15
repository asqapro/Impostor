using Impostor.Api.Events;
using Impostor.Api.Events.Meeting;
using System;

namespace Impostor.Plugins.TrackMovement.Handlers
{
    public class MeetingEventListener : IEventListener
    {
        [EventListener]
        public void OnMeetingStarted(IMeetingStartedEvent e)
        {
            Console.WriteLine("Meeting > started");
        }

        [EventListener]
        public void OnMeetingEnded(IMeetingEndedEvent e)
        {
            Console.WriteLine("Meeting > ended");
        }
    }
}
