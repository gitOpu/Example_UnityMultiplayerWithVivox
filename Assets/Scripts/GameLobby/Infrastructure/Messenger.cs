﻿using System;
using System.Collections.Generic;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace LobbyRelaySample
{
    /// <summary>
    /// Core mechanism for routing messages to arbitrary listeners.
    /// This allows components with unrelated responsibilities to interact without becoming coupled, since message senders don't
    /// need to know what (if anything) is receiving their messages.
    /// </summary>
    public class Messenger : IMessenger
    {
        private List<IReceiveMessages> m_receivers = new List<IReceiveMessages>();
        private const float k_durationToleranceMs = 10;

        // We need to handle subscribers who modify the receiver list, e.g. a subscriber who unsubscribes in their OnReceiveMessage.
        private Queue<Action> m_pendingReceivers = new Queue<Action>();
        private int m_recurseCount = 0;

        /// <summary>
        /// Assume that you won't receive messages in a specific order.
        /// </summary>
        public virtual void Subscribe(IReceiveMessages receiver)
        {
            m_pendingReceivers.Enqueue(() => { DoSubscribe(receiver); });

            void DoSubscribe(IReceiveMessages receiver)
            {
                if (receiver != null && !m_receivers.Contains(receiver))
                    m_receivers.Add(receiver);
            }
        }

        public virtual void Unsubscribe(IReceiveMessages receiver)
        {
            m_pendingReceivers.Enqueue(() => { DoUnsubscribe(receiver); });

            void DoUnsubscribe(IReceiveMessages receiver)
            {
                m_receivers.Remove(receiver);
            }
        }

        /// <summary>
        /// Send a message to any subscribers, who will decide how to handle the message.
        /// </summary>
        /// <param name="msg">If there's some data relevant to the recipient, include it here.</param>
        public virtual void OnReceiveMessage(MessageType type, object msg)
        {
            if (m_recurseCount > 5)
            {   Debug.LogError("OnReceiveMessage recursion detected! Is something calling OnReceiveMessage when it receives a message?");
                return;
            }

            if (m_recurseCount == 0) // This will increment if a new or existing subscriber calls OnReceiveMessage while handling a message. This is expected occasionally but shouldn't go too deep.
                while (m_pendingReceivers.Count > 0)
                    m_pendingReceivers.Dequeue()?.Invoke();

            m_recurseCount++;
            Stopwatch stopwatch = new Stopwatch();
            foreach (IReceiveMessages receiver in m_receivers)
            {
                stopwatch.Restart();
                receiver.OnReceiveMessage(type, msg);
                stopwatch.Stop();
                if (stopwatch.ElapsedMilliseconds > k_durationToleranceMs)
                    Debug.LogWarning($"Message recipient \"{receiver}\" took too long to process message \"{msg}\" of type {type}");
            }
            m_recurseCount--;
        }

        public void OnReProvided(IMessenger previousProvider)
        {
            if (previousProvider is Messenger)
                m_receivers.AddRange((previousProvider as Messenger).m_receivers);
        }
    }

    /// <summary>
    /// Ensure that message contents are obvious but not dependent on spelling strings correctly.
    /// </summary>
    public enum MessageType
    {
        // These are assigned arbitrary explicit values so that if a MessageType is serialized and more enum values are later inserted/removed, the serialized values need not be reassigned.
        // (If you want to remove a message, make sure it isn't serialized somewhere first.)
        None = 0,
        RenameRequest = 1,
        JoinLobbyRequest = 2,
        CreateLobbyRequest = 3,
        QueryLobbies = 4,
        QuickJoin = 5,

        ChangeMenuState = 100,
        ConfirmInGameState = 101,
        LobbyUserStatus = 102,
        UserSetEmote = 103,
        ClientUserApproved = 104,
        ClientUserSeekingDisapproval = 105,
        EndGame = 106,

        StartCountdown = 200,
        CancelCountdown = 201,
        CompleteCountdown = 202,
        MinigameBeginning = 203,
        InstructionsShown = 204,
        MinigameEnding = 205,

        DisplayErrorPopup = 300,
    }

    /// <summary>
    /// Something that wants to subscribe to messages from arbitrary, unknown senders.
    /// </summary>
    public interface IReceiveMessages
    {
        void OnReceiveMessage(MessageType type, object msg);
    }

    /// <summary>
    /// Something to which IReceiveMessages can send/subscribe for arbitrary messages.
    /// </summary>
    public interface IMessenger : IReceiveMessages, IProvidable<IMessenger>
    {
        void Subscribe(IReceiveMessages receiver);
        void Unsubscribe(IReceiveMessages receiver);
    }
}
