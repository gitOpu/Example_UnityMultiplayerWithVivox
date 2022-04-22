using System;
using JetBrains.Annotations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LobbyRelaySample.UI
{
    /// <summary>
    /// When inside a lobby, this will show information about a player, whether local or remote.
    /// </summary>
    [RequireComponent(typeof(LobbyUserObserver))]
    public class InLobbyUserUI : ObserverPanel<LobbyUser>
    {
        [SerializeField]
        TMP_Text m_DisplayNameText;

        [SerializeField]
        TMP_Text m_StatusText;
      
        [SerializeField]
        Image m_HostIcon;
        
        [SerializeField]
        Image m_EmoteImage;

        [SerializeField]
        Sprite[] m_EmoteIcons;

        [SerializeField]
        vivox.VivoxUserHandler m_vivoxUserHandler;

        public bool IsAssigned => UserId != null;

        public string UserId { get; private set; }
        private LobbyUserObserver m_observer;

        public void SetUser(LobbyUser myLobbyUser)
        {
            Show();
            if (m_observer == null)
                m_observer = GetComponent<LobbyUserObserver>();
            m_observer.BeginObserving(myLobbyUser);
            UserId = myLobbyUser.ID;
            m_vivoxUserHandler.SetId(UserId);
        }

        public void OnUserLeft()
        {
            UserId = null;
            Hide();
            m_observer.EndObserving();
        }

        public override void ObservedUpdated(LobbyUser observed)
        {
            m_DisplayNameText.SetText(observed.DisplayName);
            m_StatusText.SetText(SetStatusFancy(observed.UserStatus));
            m_EmoteImage.sprite = EmoteIcon(observed.Emote);
            m_HostIcon.enabled = observed.IsHost;
        }
        
        /// <summary>
        /// EmoteType to Icon Sprite
        /// m_EmoteIcon[0] = Smile
        /// m_EmoteIcon[1] = Frown
        /// m_EmoteIcon[2] = UnAmused
        /// m_EmoteIcon[3] = Tongue
        /// </summary>
        Sprite EmoteIcon(EmoteType type)
        {
            switch (type)
            {
                case EmoteType.None:
                    m_EmoteImage.color = Color.clear;
                    return null;
                case EmoteType.Smile:
                    m_EmoteImage.color = Color.white;
                    return m_EmoteIcons[0];
                case EmoteType.Frown:
                    m_EmoteImage.color = Color.white;
                    return m_EmoteIcons[1];
                case EmoteType.Unamused:
                    m_EmoteImage.color = Color.white;
                    return m_EmoteIcons[2];
                case EmoteType.Tongue:
                    m_EmoteImage.color = Color.white;
                    return m_EmoteIcons[3];
                default:
                    return null;
            }
        }

        string SetStatusFancy(UserStatus status)
        {
            switch (status)
            {
                case UserStatus.Lobby:
                    return "<color=#56B4E9>In Lobby</color>"; // Light Blue
                case UserStatus.Ready:
                    return "<color=#009E73>Ready</color>"; // Light Mint
                case UserStatus.Connecting:
                    return "<color=#F0E442>Connecting...</color>"; // Bright Yellow
                case UserStatus.InGame:
                    return "<color=#005500>In Game</color>"; // Green
                default:
                    return "";
            }
        }
    }
}
