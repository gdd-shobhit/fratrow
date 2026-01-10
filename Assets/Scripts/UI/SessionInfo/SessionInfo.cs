using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Profiling;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace Unity.FPSSample_2.Client
{
    [RequireComponent(typeof(UIDocument))]
    public class SessionInfo : MonoBehaviour
    {
        const float k_UpdateSessionInfoDelay = 2f;
        const string k_RedColor = "#ff5555";
        const string k_OrangeColor = "#ffb86c";
        const string k_GreenColor = "#50fa7b";

        static class UIElementNames
        {
            public const string SessionInfoContainer = "SessionInfoContainer";
            public const string BinaryInfoLabel = "BinaryInfo";
            public const string ArgsInfoLabel = "ArgsInfo";
            public const string RightInfoLabel = "RightInfo";
            public const string HardwareInfoLabel = "HardwareInfo";
            public const string ConnectionInfoLabel = "ConnectionInfo";
            public const string ShowSessionInfo = "ShowSessionInfo";
        }

        VisualElement m_SessionInfoBanner;
        VisualElement m_SessionInfoContainer;

        Label m_BinaryInfoLabel;
        Label m_RightInfoLabel;
        Label m_HardwareInfoLabel;
        Label m_ConnectionInfoLabel;
        Label m_ArgsInfoLabel;
        Label m_ShowSessionInfo;

        string m_RightInfo;
        string m_ConnectionInfo;
        string m_MachineInfo;

        ProfilerRecorder m_SystemMemoryRecorder;
        ProfilerRecorder m_GcMemoryRecorder;
        ProfilerRecorder m_CpuRecorder;
        ProfilerRecorder m_CpuToGpuRecorder;
        ProfilerRecorder m_GpuRecorder;
        ProfilerRecorder m_GcCountRecorder;
        ProfilerRecorder m_DrawCallsRecorder;
        ProfilerRecorder m_FilesOpenRecorder;
        ProfilerRecorder m_FilesBytesReadRecorder;
        readonly StringBuilder m_StringBuilder = new StringBuilder(256);
        float m_Timer;

        void OnEnable()
        {
            m_SessionInfoBanner = GetComponent<UIDocument>().rootVisualElement;

            m_SessionInfoContainer = m_SessionInfoBanner.Q<VisualElement>(UIElementNames.SessionInfoContainer);
            m_BinaryInfoLabel = m_SessionInfoBanner.Q<Label>(UIElementNames.BinaryInfoLabel);
            m_RightInfoLabel = m_SessionInfoBanner.Q<Label>(UIElementNames.RightInfoLabel);
            m_HardwareInfoLabel = m_SessionInfoBanner.Q<Label>(UIElementNames.HardwareInfoLabel);
            m_ConnectionInfoLabel = m_SessionInfoBanner.Q<Label>(UIElementNames.ConnectionInfoLabel);
            m_ConnectionInfoLabel.RegisterCallback<ClickEvent>(CopySessionCode);
            m_ArgsInfoLabel = m_SessionInfoBanner.Q<Label>(UIElementNames.ArgsInfoLabel);
            m_ShowSessionInfo = m_SessionInfoBanner.Q<Label>(UIElementNames.ShowSessionInfo);

            // https://docs.unity3d.com/6000.1/Documentation/Manual/frame-timing-manager.html
            m_SystemMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
            m_GcMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Used Memory");
            m_CpuRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "CPU Main Thread Frame Time", 15);
            m_CpuToGpuRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "CPU Render Thread Frame Time", 15);
            m_GpuRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GPU Frame Time", 15);
            m_GcCountRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC Allocation In Frame Count", 15);
            m_DrawCallsRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Draw Calls Count", 15);
            m_FilesOpenRecorder = ProfilerRecorder.StartNew(ProfilerCategory.FileIO, "File Handles Open", 15);
            m_FilesBytesReadRecorder = ProfilerRecorder.StartNew(ProfilerCategory.FileIO, "File Bytes Read", 60);

            m_RightInfo = $"{{0}} | {{1}}";

            var hasArgs = HasArgs(out var args);
            m_ArgsInfoLabel.style.display = hasArgs ? DisplayStyle.Flex : DisplayStyle.None;
            m_ArgsInfoLabel.text = args;

            m_ConnectionInfo = $"Player: \"{{0}}\" on \"{Environment.MachineName}\" | {{1}}";

            var burst = BurstCompiler.IsEnabled ? " +BC" : "";
            var qualityPreset = $"{QualitySettings.names[QualitySettings.GetQualityLevel()]}";
            var targetFrameRate = Application.targetFrameRate > 0 ? Application.targetFrameRate.ToString() : "OFF";
            var vSync = QualitySettings.vSyncCount > 0 ? $"{QualitySettings.vSyncCount}th @ {Screen.currentResolution.refreshRateRatio.value}hz" : "OFF";
            m_MachineInfo = $"<color=#8be9fd>{SystemInfo.operatingSystem} | {SystemInfo.deviceModel} | {SystemInfo.processorType} | {SystemInfo.graphicsDeviceName}</color>";

            m_BinaryInfoLabel.text = $"{Application.productName} by {Application.companyName} | Ver {Application.version}{burst} | {GetBuildType()} | QSetting:{qualityPreset} | TargetFPS:{targetFrameRate} | VSync:{vSync}";
           
            if (ConnectionSettings.Instance != null)
            {
                ConnectionSettings.Instance.propertyChanged += OnConnectionStateChanged;
            }
            
            UpdateSessionInfo();
        }

        void CopySessionCode(ClickEvent _)
        {
            if (ConnectionSettings.Instance.GameConnectionState != ConnectionState.State.Disconnected
                && !string.IsNullOrEmpty(ConnectionSettings.Instance.SessionCode))
            {
                GUIUtility.systemCopyBuffer = ConnectionSettings.Instance.SessionCode;
                Debug.Log($"Session code {ConnectionSettings.Instance.SessionCode} was copied to clipboard.");
            }
        }

        void ToggleSessionInfoVisibility()
        {
            m_SessionInfoContainer.style.display = m_SessionInfoContainer.style.display == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void OnDisable()
        {
            m_ConnectionInfoLabel.UnregisterCallback<ClickEvent>(CopySessionCode);
            
            if (ConnectionSettings.Instance != null)
            {
                ConnectionSettings.Instance.propertyChanged -= OnConnectionStateChanged;
            }
        }

        private void OnConnectionStateChanged(object sender, BindablePropertyChangedEventArgs e)
        {
            if (e.propertyName == ConnectionSettings.ConnectionStatusStylePropertyName)
            {
                UpdateSessionInfo();
            }
        }

        void OnDestroy()
        {
            m_SystemMemoryRecorder.Dispose();
            m_GcMemoryRecorder.Dispose();
            m_CpuToGpuRecorder.Dispose();
            m_CpuRecorder.Dispose();
            m_GpuRecorder.Dispose();
            m_GcCountRecorder.Dispose();
            m_DrawCallsRecorder.Dispose();
            m_FilesOpenRecorder.Dispose();
            m_FilesBytesReadRecorder.Dispose();
        }

        void LateUpdate()
        {
            if (m_SessionInfoContainer.style.display == DisplayStyle.None)
                return;

            m_Timer += Time.deltaTime;
            if (m_Timer >= k_UpdateSessionInfoDelay)
            {
                m_Timer -= k_UpdateSessionInfoDelay;

                UpdateSessionInfo();
            }
        }

        static string GetBuildType()
        {
            if (Application.isEditor)
                return "EDITOR";
            return Debug.isDebugBuild ? "DEVELOP" : "RELEASE";
        }

        static bool HasArgs(out string args)
        {
            args = "";
            if (Application.isEditor)
                return false;

            var commandLineArgs = Environment.GetCommandLineArgs();

            StringBuilder sb = new StringBuilder();
            sb.Append("Args: ");

            // Ignore the first, as it's just the full path.
            for (var i = 1; i < commandLineArgs.Length; i++)
            {
                sb.Append(commandLineArgs[i]);
                if (i < commandLineArgs.Length - 1) sb.Append(' ');
            }
            args = sb.ToString();
            return commandLineArgs.Length > 1;
        }

        void UpdateSessionInfo()
        {
            var netcodeInfo = GetNetcodeInfoFromSystem();
            var playerName = GameSettings.Instance.PlayerName;
            var networkRole = ClientServerBootstrap.HasServerWorld ? NetworkRole.Host : NetworkRole.Client;
            var interactionTypeText = "click";

#if UNITY_ANDROID || UNITY_IOS
            interactionTypeText = "tap";
#endif
            var sessionState =
                ConnectionSettings.Instance.GameConnectionState != ConnectionState.State.Disconnected && ConnectionSettings.Instance.SessionCode != null
                ? $"Session-Code: <b>{ConnectionSettings.Instance.SessionCode}</b> ({interactionTypeText} to copy) | Role: <b>{networkRole.ToString()}</b>"
                : "No Session";

            m_ConnectionInfoLabel.text = string.Format(m_ConnectionInfo, playerName, sessionState);
            m_RightInfoLabel.text = string.Format(m_RightInfo, netcodeInfo, GetFps());
            m_HardwareInfoLabel.text =  GetCurrentParameters();
        }

        string GetFps()
        {
            var avgFps = Mathf.CeilToInt(1f / Time.smoothDeltaTime);
            if (avgFps <= 0) avgFps = Mathf.CeilToInt(1f / Time.deltaTime);
            var targetFps = GetImpliedTargetFps();
            var targetFPSInt = (int)targetFps;
            return $"<color={GetPingColor()}>FPS {avgFps.ToString()} / {targetFPSInt.ToString(CultureInfo.InvariantCulture)}</color>";

            double GetImpliedTargetFps()
            {
                var screenRefreshRate = Screen.currentResolution.refreshRateRatio.value;
                if (QualitySettings.vSyncCount <= 0)
                {
                    if (Application.targetFrameRate > 0)
                        return Application.targetFrameRate;
                    return screenRefreshRate;
                }
                return screenRefreshRate / QualitySettings.vSyncCount;
            }
            string GetPingColor()
            {
                var currentFpsVsTargetFpsRatio = avgFps / targetFps;
                if (currentFpsVsTargetFpsRatio >= 0.99f)
                    return k_GreenColor;
                if (currentFpsVsTargetFpsRatio >= 0.5f || avgFps >= 60)
                    return k_OrangeColor;
                return k_RedColor;
            }
        }

        string GetCurrentParameters()
        {
            // Clear the cached StringBuilder instead of creating a new one.
            m_StringBuilder.Clear();
            bool needsSeparator = false;

            void AppendSeparator()
            {
                if (needsSeparator)
                {
                    m_StringBuilder.Append(" | ");
                }
                needsSeparator = true;
            }
            
            if (m_CpuRecorder.Valid)
            {
                AppendSeparator();
                m_StringBuilder.Append("CPU:").Append((GetRecorderFrameAverage(m_CpuRecorder) * (1e-6f)).ToString("F1")).Append("ms");
            }

            if (m_CpuToGpuRecorder.Valid)
            {
                AppendSeparator();
                m_StringBuilder.Append("Render:").Append((GetRecorderFrameAverage(m_CpuToGpuRecorder) * (1e-6f)).ToString("F1")).Append("ms");
            }

            if (m_GpuRecorder.Valid)
            {
                AppendSeparator();
                m_StringBuilder.Append("GPU:").Append((GetRecorderFrameAverage(m_GpuRecorder) * (1e-6f)).ToString("F1")).Append("ms");
            }

            if (m_DrawCallsRecorder.Valid)
            {
                AppendSeparator();
                m_StringBuilder.Append("Draw Calls:").Append(GetRecorderFrameAverage(m_DrawCallsRecorder).ToString("0"));
            }

            if (m_GcMemoryRecorder.Valid)
            {
                AppendSeparator();
                // For integers/longs, ToString() is efficient and doesn't box.
                m_StringBuilder.Append("Managed:").Append(m_GcMemoryRecorder.LastValue / (1024 * 1024)).Append("MB");
            }

            if (m_SystemMemoryRecorder.Valid)
            {
                AppendSeparator();
                m_StringBuilder.Append("Native:").Append(m_SystemMemoryRecorder.LastValue / (1024 * 1024)).Append("MB");
            }

            if (m_GcCountRecorder.Valid)
            {
                AppendSeparator();
                m_StringBuilder.Append("GC/Frame:").Append(GetRecorderFrameAverage(m_GcCountRecorder).ToString("0"));
            }

            if (m_FilesOpenRecorder.Valid)
            {
                AppendSeparator();
                m_StringBuilder.Append("Files Open:").Append(m_FilesOpenRecorder.LastValue.ToString("0"));
            }

            if (m_FilesBytesReadRecorder.Valid)
            {
                AppendSeparator();
                m_StringBuilder.Append("File Read Bytes:").Append((GetRecorderFrameAverage(m_FilesBytesReadRecorder, false) / (1024 * 1024)).ToString("0.0")).Append("MB");
            }

            if (!string.IsNullOrEmpty(m_MachineInfo))
            {
                AppendSeparator();
                m_StringBuilder.Append(m_MachineInfo);
            }

            return m_StringBuilder.ToString();
        }
        
        static double GetRecorderFrameAverage(ProfilerRecorder recorder, bool avg = true)
        {
            var samplesCount = recorder.Capacity;
            if (samplesCount == 0)
                return 0;

            double r = 0;
            // unsafe
            // {
            //     var samples = stackalloc ProfilerRecorderSample[samplesCount];
            //     recorder.CopyTo(samples, samplesCount);
            //     for (var i = 0; i < samplesCount; ++i)
            //         r += samples[i].Value;
            //     if (avg) r /= samplesCount;
            // }

            return r;
        }
        
        /// <summary>
        /// Reads the cached network status from the NetworkStatusSystem.
        /// </summary>
        /// <returns>A formatted string with network status, or a default message if not available.</returns>
        private string GetNetcodeInfoFromSystem()
        {
            var clientWorld = ClientServerBootstrap.ClientWorld;
            
            if (clientWorld == null || !clientWorld.IsCreated || NetworkStatusSystem.StatusEntity == Entity.Null)
                return $"<color={k_RedColor}>No client world!</color>";

            var entityManager = clientWorld.EntityManager;

            var status = entityManager.GetComponentData<NetworkStatusSingleton>(
                NetworkStatusSystem.StatusEntity);
            
            return status.Status.ToString();
        }
    }
}
