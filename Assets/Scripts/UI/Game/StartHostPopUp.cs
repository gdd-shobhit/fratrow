using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.FPSSample_2.Client
{
    [RequireComponent(typeof(UIDocument))]
    public class StartHostPopUp : MonoBehaviour
    {
        static class UIElementNames
        {
            public const string PortInputField = "PortField";
            public const string StartButton = "StartButton";
            public const string CancelButton = "CancelButton";
        }

        VisualElement m_StartHostPopUp;
        Button m_StartButton;
        Button m_CancelButton;

        void OnEnable()
        {
            m_StartHostPopUp = GetComponent<UIDocument>().rootVisualElement;

            m_StartHostPopUp.SetBinding("style.display", new DataBinding
            {
                dataSource = GameSettings.Instance,
                dataSourcePath = new PropertyPath(GameSettings.StartHostStylePropertyName),
                bindingMode = BindingMode.ToTarget,
            });

            var portInputField = m_StartHostPopUp.Q<TextField>(UIElementNames.PortInputField);
            portInputField.SetBinding("value", new DataBinding
            {
                dataSource = ConnectionSettings.Instance,
                dataSourcePath = new PropertyPath(nameof(ConnectionSettings.Port)),
                bindingMode = BindingMode.TwoWay,
            });

            m_StartButton = m_StartHostPopUp.Q<Button>(UIElementNames.StartButton);
            m_StartButton.clicked += OnStartPressed;
            m_StartButton.SetBinding("enabledSelf", new DataBinding
            {
                dataSource = ConnectionSettings.Instance,
                dataSourcePath = new PropertyPath(nameof(ConnectionSettings.IsNetworkEndpointValid)),
                bindingMode = BindingMode.ToTarget,
            });

            m_CancelButton = m_StartHostPopUp.Q<Button>(UIElementNames.CancelButton);
            m_CancelButton.clicked += OnCancelPressed;
        }

        void OnDisable()
        {
            m_StartButton.clicked -= OnStartPressed;
            m_CancelButton.clicked -= OnCancelPressed;
        }

        static void OnStartPressed() => GameSettings.Instance.CancellableUserInputPopUp.SetResult();

        static void OnCancelPressed()
        {
            GameSettings.Instance.CancellableUserInputPopUp.SetCanceled();
            ConnectionSettings.Instance.IPAddress = ConnectionSettings.DefaultServerAddress;
            ConnectionSettings.Instance.Port = ConnectionSettings.DefaultServerPort.ToString();
        }
    }
}
