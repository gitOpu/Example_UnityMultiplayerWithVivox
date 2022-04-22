using LobbyRelaySample.relay;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace LobbyRelaySample
{
    /// <summary>
    /// Sets up and runs the entire sample.
    /// All the Data that is important gets updated in here, the GameManager in the mainScene has all the references
    /// needed to run the game.
    /// </summary>
    public class GameManager : MonoBehaviour, IReceiveMessages
    {
        #region UI elements that observe the local state. These should be assigned the observers in the scene during Start.
        /// <summary>
        /// The Observer/Observed Pattern is great for keeping the UI in Sync with the actual Values.
        /// Each list below represents a single Observed class that gets updated by other parts of the code, and will
        /// trigger the list of Observers that are looking for changes in that class.
        ///
        /// The list is serialized, so you can navigate to the Observers via the Inspector to see who's watching.
        /// </summary>

        [SerializeField]
        private List<LocalMenuStateObserver> m_LocalMenuStateObservers = new List<LocalMenuStateObserver>();
        [SerializeField]
        private List<LocalLobbyObserver> m_LocalLobbyObservers = new List<LocalLobbyObserver>();
        [SerializeField]
        private List<LobbyUserObserver> m_LocalUserObservers = new List<LobbyUserObserver>();
        [SerializeField]
        private List<LobbyServiceDataObserver> m_LobbyServiceObservers = new List<LobbyServiceDataObserver>();

        #endregion

        private LocalMenuState m_LocalMenuState = new LocalMenuState();
        private LobbyUser m_localUser;
        private LocalLobby m_localLobby;
        private LobbyServiceData m_lobbyServiceData = new LobbyServiceData();
        private LobbyContentHeartbeat m_lobbyContentHeartbeat = new LobbyContentHeartbeat();

        private RelayUtpSetup m_relaySetup;
        private RelayUtpClient m_relayClient;

        private vivox.VivoxSetup m_vivoxSetup = new vivox.VivoxSetup();
        [SerializeField]
        private List<vivox.VivoxUserHandler> m_vivoxUserHandlers;

        /// <summary>Rather than a setter, this is usable in-editor. It won't accept an enum, however.</summary>
        public void SetLobbyColorFilter(int color)
        {
            m_lobbyColorFilter = (LobbyColor)color;
        }

        private LobbyColor m_lobbyColorFilter;

        #region Setup

        private void Awake()
        {
            // Do some arbitrary operations to instantiate singletons.
#pragma warning disable IDE0059 // Unnecessary assignment of a value
            var unused = Locator.Get;
#pragma warning restore IDE0059

            Locator.Get.Provide(new Auth.Identity(OnAuthSignIn));
            Application.wantsToQuit += OnWantToQuit;
        }

        private void Start()
        {
            m_localLobby = new LocalLobby { State = LobbyState.Lobby };
            m_localUser = new LobbyUser();
            m_localUser.DisplayName = "New Player";
            Locator.Get.Messenger.Subscribe(this);
            BeginObservers();
        }

        private void OnAuthSignIn()
        {
            Debug.Log("Signed in.");
            m_localUser.ID = Locator.Get.Identity.GetSubIdentity(Auth.IIdentityType.Auth).GetContent("id");
            m_localUser.DisplayName = NameGenerator.GetName(m_localUser.ID);
            m_localLobby.AddPlayer(m_localUser); // The local LobbyUser object will be hooked into UI before the LocalLobby is populated during lobby join, so the LocalLobby must know about it already when that happens.
            StartVivoxLogin();
        }

        private void BeginObservers()
        {
            foreach (var gameStateObs in m_LocalMenuStateObservers)
                gameStateObs.BeginObserving(m_LocalMenuState);
            foreach (var serviceObs in m_LobbyServiceObservers)
                serviceObs.BeginObserving(m_lobbyServiceData);
            foreach (var lobbyObs in m_LocalLobbyObservers)
                lobbyObs.BeginObserving(m_localLobby);
            foreach (var userObs in m_LocalUserObservers)
                userObs.BeginObserving(m_localUser);
        }
        #endregion

        /// <summary>
        /// The Messaging System handles most of the core Lobby Service calls, and catches the callbacks from those calls.
        /// These In turn update the observed variables and propagates the events to the game.
        /// When looking for the interactions, look up the MessageType and search for it in the code to see where it is used outside this script.
        /// EG. Locator.Get.Messenger.OnReceiveMessage(MessageType.RenameRequest, name);
        /// </summary>
        public void OnReceiveMessage(MessageType type, object msg)
        {
            if (type == MessageType.CreateLobbyRequest)
            {
                LocalLobby.LobbyData createLobbyData = (LocalLobby.LobbyData)msg;
                LobbyAsyncRequests.Instance.CreateLobbyAsync(createLobbyData.LobbyName, createLobbyData.MaxPlayerCount, createLobbyData.Private, m_localUser, (r) =>
                    {   lobby.ToLocalLobby.Convert(r, m_localLobby);
                        OnCreatedLobby();
                    },
                    OnFailedJoin);
            }
            else if (type == MessageType.JoinLobbyRequest)
            {
                LocalLobby.LobbyData lobbyInfo = (LocalLobby.LobbyData)msg;
                LobbyAsyncRequests.Instance.JoinLobbyAsync(lobbyInfo.LobbyID, lobbyInfo.LobbyCode, m_localUser, (r) =>
                    {   lobby.ToLocalLobby.Convert(r, m_localLobby);
                        OnJoinedLobby();
                    },
                    OnFailedJoin);
            }
            else if (type == MessageType.QueryLobbies)
            {
                m_lobbyServiceData.State = LobbyQueryState.Fetching;
                LobbyAsyncRequests.Instance.RetrieveLobbyListAsync(
                    qr => {
                        if (qr != null)
                            OnLobbiesQueried(lobby.ToLocalLobby.Convert(qr));
                    },
                    er => {
                        OnLobbyQueryFailed();
                    },
                    m_lobbyColorFilter);
            }
            else if (type == MessageType.QuickJoin)
            {
                LobbyAsyncRequests.Instance.QuickJoinLobbyAsync(m_localUser, m_lobbyColorFilter, (r) =>
                    {   lobby.ToLocalLobby.Convert(r, m_localLobby);
                        OnJoinedLobby();
                    },
                    OnFailedJoin);
            }
            else if (type == MessageType.RenameRequest)
            {
                string name = (string)msg;
                if (string.IsNullOrWhiteSpace(name))
                {
                    Locator.Get.Messenger.OnReceiveMessage(MessageType.DisplayErrorPopup, "Empty Name not allowed."); // Lobby error type, then HTTP error type.
                    return;
            	}
                m_localUser.DisplayName = (string)msg;
            }
            else if (type == MessageType.ClientUserApproved)
            {   ConfirmApproval();
            }
            else if (type == MessageType.UserSetEmote)
            {   EmoteType emote = (EmoteType)msg;
                m_localUser.Emote = emote;
            }
            else if (type == MessageType.LobbyUserStatus)
            {   m_localUser.UserStatus = (UserStatus)msg;
            }
            else if (type == MessageType.StartCountdown)
            {   m_localLobby.State = LobbyState.CountDown;
            }
            else if (type == MessageType.CancelCountdown)
            {   m_localLobby.State = LobbyState.Lobby;
            }
            else if (type == MessageType.CompleteCountdown)
            {   if (m_relayClient is RelayUtpHost)
                    (m_relayClient as RelayUtpHost).SendInGameState();
            }
            else if (type == MessageType.ChangeMenuState)
            {   SetGameState((GameState)msg);
            }
            else if (type == MessageType.ConfirmInGameState)
            {   m_localUser.UserStatus = UserStatus.InGame;
                m_localLobby.State = LobbyState.InGame;
            }
            else if (type == MessageType.EndGame)
            {   m_localLobby.State = LobbyState.Lobby;
                SetUserLobbyState();
            }
        }

        private void SetGameState(GameState state)
        {
            bool isLeavingLobby = (state == GameState.Menu || state == GameState.JoinMenu) && m_LocalMenuState.State == GameState.Lobby;
            m_LocalMenuState.State = state;
            if (isLeavingLobby)
                OnLeftLobby();
        }

        private void OnLobbiesQueried(IEnumerable<LocalLobby> lobbies)
        {
            var newLobbyDict = new Dictionary<string, LocalLobby>();
            foreach (var lobby in lobbies)
                newLobbyDict.Add(lobby.LobbyID, lobby);

            m_lobbyServiceData.State = LobbyQueryState.Fetched;
            m_lobbyServiceData.CurrentLobbies = newLobbyDict;
        }

        private void OnLobbyQueryFailed()
        {
            m_lobbyServiceData.State = LobbyQueryState.Error;
        }

        private void OnCreatedLobby()
        {
            m_localUser.IsHost = true;
            OnJoinedLobby();
        }

        private void OnJoinedLobby()
        {
            LobbyAsyncRequests.Instance.BeginTracking(m_localLobby.LobbyID);
            m_lobbyContentHeartbeat.BeginTracking(m_localLobby, m_localUser);
            SetUserLobbyState();

            // The host has the opportunity to reject incoming players, but to do so the player needs to connect to Relay without having game logic available.
            // In particular, we should prevent players from joining voice chat until they are approved.
            OnReceiveMessage(MessageType.LobbyUserStatus, UserStatus.Connecting);
            if (m_localUser.IsHost)
            {
                StartRelayConnection();
                StartVivoxJoin();
            }
            else
            {
                StartRelayConnection();
            }
        }

        private void OnLeftLobby()
        {
            m_localUser.ResetState();
            LobbyAsyncRequests.Instance.LeaveLobbyAsync(m_localLobby.LobbyID, ResetLocalLobby);
            m_lobbyContentHeartbeat.EndTracking();
            LobbyAsyncRequests.Instance.EndTracking();
            m_vivoxSetup.LeaveLobbyChannel();

            if (m_relaySetup != null)
            {   Component.Destroy(m_relaySetup);
                m_relaySetup = null;
            }
            if (m_relayClient != null)
            {
                m_relayClient.Dispose();
                StartCoroutine(FinishCleanup());

                // We need to delay slightly to give the disconnect message sent during Dispose time to reach the host, so that we don't destroy the connection without it being flushed first.
                IEnumerator FinishCleanup()
                {
                    yield return null;
                    Component.Destroy(m_relayClient);
                    m_relayClient = null;
                }
            }
        }

        /// <summary>
        /// Back to Join menu if we fail to join for whatever reason.
        /// </summary>
        private void OnFailedJoin()
        {
            SetGameState(GameState.JoinMenu);
        }

        private void StartVivoxLogin()
        {
            m_vivoxSetup.Initialize(m_vivoxUserHandlers, OnVivoxLoginComplete);

            void OnVivoxLoginComplete(bool didSucceed)
            {
                if (!didSucceed)
                {   Debug.LogError("Vivox login failed! Retrying in 5s...");
                    StartCoroutine(RetryConnection(StartVivoxLogin, m_localLobby.LobbyID));
                }
            }
        }

        private void StartVivoxJoin()
        {
            m_vivoxSetup.JoinLobbyChannel(m_localLobby.LobbyID, OnVivoxJoinComplete);

            void OnVivoxJoinComplete(bool didSucceed)
            {
                if (!didSucceed)
                {   Debug.LogError("Vivox connection failed! Retrying in 5s...");
                    StartCoroutine(RetryConnection(StartVivoxJoin, m_localLobby.LobbyID));
                }
            }
        }

        private void StartRelayConnection()
        {
            if (m_localUser.IsHost)
                m_relaySetup = gameObject.AddComponent<RelayUtpSetupHost>();
            else
                m_relaySetup = gameObject.AddComponent<RelayUtpSetupClient>();
            m_relaySetup.BeginRelayJoin(m_localLobby, m_localUser, OnRelayConnected);

            void OnRelayConnected(bool didSucceed, RelayUtpClient client)
            {
                Component.Destroy(m_relaySetup);
                m_relaySetup = null;

                if (!didSucceed)
                {   Debug.LogError("Relay connection failed! Retrying in 5s...");
                    StartCoroutine(RetryConnection(StartRelayConnection, m_localLobby.LobbyID));
                    return;
                }

                m_relayClient = client;
                if (m_localUser.IsHost)
                    CompleteRelayConnection();
                else
                    Debug.Log("Client is now waiting for approval...");
            }
        }

        private IEnumerator RetryConnection(Action doConnection, string lobbyId)
        {
            yield return new WaitForSeconds(5);
            if (m_localLobby != null && m_localLobby.LobbyID == lobbyId && !string.IsNullOrEmpty(lobbyId)) // Ensure we didn't leave the lobby during this waiting period.
                doConnection?.Invoke();
        }

        private void ConfirmApproval()
        {
            if (!m_localUser.IsHost && m_localUser.IsApproved)
            {
                CompleteRelayConnection();
                StartVivoxJoin();
            }
        }

        private void CompleteRelayConnection()
        {
            OnReceiveMessage(MessageType.LobbyUserStatus, UserStatus.Lobby);
        }

        private void SetUserLobbyState()
        {
            SetGameState(GameState.Lobby);
            OnReceiveMessage(MessageType.LobbyUserStatus, UserStatus.Lobby);
        }

        private void ResetLocalLobby()
        {
            m_localLobby.CopyObserved(new LocalLobby.LobbyData(), new Dictionary<string, LobbyUser>());
            m_localLobby.AddPlayer(m_localUser); // As before, the local player will need to be plugged into UI before the lobby join actually happens.
            m_localLobby.RelayServer = null;
        }

        #region Teardown

        /// <summary>
        /// In builds, if we are in a lobby and try to send a Leave request on application quit, it won't go through if we're quitting on the same frame.
        /// So, we need to delay just briefly to let the request happen (though we don't need to wait for the result).
        /// </summary>
        private IEnumerator LeaveBeforeQuit()
        {
            ForceLeaveAttempt();
            yield return null;
            Application.Quit();
        }

        private bool OnWantToQuit()
        {
            bool canQuit = string.IsNullOrEmpty(m_localLobby?.LobbyID);
            StartCoroutine(LeaveBeforeQuit());
            return canQuit;
        }

        private void OnDestroy()
        {
            ForceLeaveAttempt();
        }

        private void ForceLeaveAttempt()
        {
            Locator.Get.Messenger.Unsubscribe(this);
            if (!string.IsNullOrEmpty(m_localLobby?.LobbyID))
            {
                LobbyAsyncRequests.Instance.LeaveLobbyAsync(m_localLobby?.LobbyID, null);
                m_localLobby = null;
            }
        }

        #endregion
    }
}
