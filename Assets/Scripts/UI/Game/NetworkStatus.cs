using System;
using System.Collections;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.FPSSample_2.Client
{
    [RequireComponent(typeof(UIDocument))]
    public class NetworkStatus : MonoBehaviour
    {
        static class UIElementNames
        {
            public const string ConnectionStatus = "ConnectionStatus";
            public const string NetworkingRole = "NetworkingRole";
            public const string SessionName = "SessionName";
        }

        VisualElement m_Root;
        Label m_ConnectionStatus;
        Label m_NetworkingRole;
        Label m_SessionName;

        void OnEnable()
        {
            m_Root = GetComponent<UIDocument>().rootVisualElement;

            m_ConnectionStatus = m_Root.Q<Label>(UIElementNames.ConnectionStatus);
            m_NetworkingRole = m_Root.Q<Label>(UIElementNames.NetworkingRole);
            m_SessionName = m_Root.Q<Label>(UIElementNames.SessionName);

            StartCoroutine(UpdateNetworkStatusInfo());
        }

        void OnDisable()
        {
            StopCoroutine(UpdateNetworkStatusInfo());
        }


        IEnumerator UpdateNetworkStatusInfo()
        {
            while (true)
            {
                m_ConnectionStatus.text = "Connection: ";
                switch(ConnectionSettings.Instance.GameConnectionState)
                {
                    case ConnectionState.State.Disconnected:
                        m_ConnectionStatus.text += "Offline";
                        m_NetworkingRole.text = m_SessionName.text = string.Empty;
                        break;
                    case ConnectionState.State.Connecting:
                        m_ConnectionStatus.text += "Connecting ...";
                        m_NetworkingRole.text = m_SessionName.text = string.Empty;
                        break;
                    case ConnectionState.State.Connected:
                        m_ConnectionStatus.text += "Connected";
                        m_NetworkingRole.text = "Role: ";
                        if (ClientServerBootstrap.HasServerWorld)
                        {
                            m_NetworkingRole.text += "Server";
                        }
                        else
                        {
                            m_NetworkingRole.text += "Client";
                        }

                        if (GameManager.GameConnection != null &&
                            GameManager.GameConnection.Session != null)
                        {
                            m_SessionName.text = $"Session: {GameManager.GameConnection.Session.Name}";
                        }
                        else
                        {
                            m_SessionName.text = string.Empty;
                        }


                        break;
                }
                yield return (new WaitForSeconds(1.0f));
            }
        }
    }
}
