using System.Threading.Tasks;
using Unity.Networking.Transport;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;

namespace Unity.FPSSample_2
{
    /// <summary>
    /// This extension methods help with some specificities of the <see cref="ISession"/> interface.
    /// </summary>
    public static class SessionExtension
    {
        /// <summary>
        /// This method is used to know if the current <see cref="ISession"/> is a pure server and not a client.
        /// </summary>
        /// <returns>True if the session is only a server and does not have a client, false otherwise.</returns>
        public static bool IsServer(this ISession session)
        {
            return session.IsHost && session.CurrentPlayer?.Id == null;
        }
    }

    /// <summary>
    /// This class is a wrapper around <see cref="MultiplayerService.Instance"/> session API.
    /// </summary>
    /// <remarks>
    /// The purpose of this class is to encapsulate the need for a custom <see cref="INetworkHandler"/> that we are using
    /// to retrieve the Listen and Connect <see cref="NetworkEndpoint"/> while connecting through the <see cref="ISession"/> API.
    /// This is done in order to control the Worlds creation and Connection in <see cref="GameManager.StartGameAsync"/>.
    /// </remarks>
    public class GameConnection
    {
        public ISession Session { get; private set; }
        public NetworkEndpoint ListenEndpoint { get; private set; }
        public NetworkEndpoint ConnectEndpoint { get; private set; }
        public NetworkType SessionConnectionType { get; private set; }

        public static async Task<GameConnection> CreateorJoinGameAsync()
        {
            var gameConnection = new GameConnection();
            await StartServicesAsync();

            var networkHandler = new EntityNetworkHandler();
            var options = new SessionOptions()
            {
                Name = GameSettings.Instance.SessionName,
                MaxPlayers = GameManager.MaxPlayer
            }.WithRelayNetwork().WithNetworkHandler(networkHandler);
            gameConnection.Session = await MultiplayerService.Instance.CreateOrJoinSessionAsync(options.Name, options);

            gameConnection.ConnectEndpoint = await networkHandler.ConnectEndpoint;
            gameConnection.ListenEndpoint = await networkHandler.ListenEndpoint;
            gameConnection.SessionConnectionType = await networkHandler.SessionConnectionType;
            return gameConnection;
        }

        public static async Task<GameConnection> JoinGameAsync()
        {
            var gameConnection = new GameConnection();
            await StartServicesAsync();

            var networkHandler = new EntityNetworkHandler();
            JoinSessionOptions options = new JoinSessionOptions();


            options.WithNetworkHandler(networkHandler);
            gameConnection.Session = await MultiplayerService.Instance.JoinSessionByCodeAsync(ConnectionSettings.Instance.SessionCode, options);
            gameConnection.ConnectEndpoint = await networkHandler.ConnectEndpoint;
            gameConnection.ListenEndpoint = await networkHandler.ListenEndpoint;
            gameConnection.SessionConnectionType = await networkHandler.SessionConnectionType;
            return gameConnection;
        }
        
        public static GameConnection GetServerConnectionSettings(ushort port)
        {
            ConnectionSettings.Instance.Port = port.ToString();
            var gameConnection = new GameConnection();
            gameConnection.ListenEndpoint = NetworkEndpoint.AnyIpv4.WithPort(port);
            gameConnection.ConnectEndpoint = default;
            gameConnection.SessionConnectionType = NetworkType.Direct;
            return gameConnection;
        }

        public static GameConnection GetServerConnectionSettings(NetworkEndpoint listenEndpoint)
        {
            ConnectionSettings.Instance.Port = listenEndpoint.Port.ToString();
            var gameConnection = new GameConnection();
            gameConnection.ListenEndpoint = listenEndpoint;
            gameConnection.ConnectEndpoint = default;
            gameConnection.SessionConnectionType = NetworkType.Direct;
            return gameConnection;
        }
        
        public static Task<GameConnection> HostGameAsync()
        {
            ushort port = ushort.Parse(ConnectionSettings.Instance.Port);
            var gameConnection = new GameConnection();
            gameConnection.ListenEndpoint = NetworkEndpoint.AnyIpv4.WithPort(port);
            gameConnection.ConnectEndpoint = NetworkEndpoint.LoopbackIpv4.WithPort(port);
            gameConnection.SessionConnectionType = NetworkType.Direct;
            return Task.FromResult(gameConnection);
        }

        public static Task<GameConnection> ConnectGameAsync()
        {
            ushort port = ushort.Parse(ConnectionSettings.Instance.Port);
            var gameConnection = new GameConnection();
            gameConnection.ListenEndpoint = NetworkEndpoint.AnyIpv4;
            gameConnection.ConnectEndpoint = NetworkEndpoint.Parse(ConnectionSettings.Instance.IPAddress, port);
            gameConnection.SessionConnectionType = NetworkType.Direct;
            return Task.FromResult(gameConnection);
        }

        static async Task StartServicesAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }

            if (!AuthenticationService.Instance.IsAuthorized)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }

        static SessionOptions CreateSessionOptions(ConnectionType connectionType, string address, string port)
        {
            SessionOptions options = new SessionOptions { MaxPlayers = GameManager.MaxPlayer };
            switch (connectionType)
            {
                case ConnectionType.Relay:
                    options.WithRelayNetwork();
                    break;
                case ConnectionType.Direct:
                    options.WithDirectNetwork("0.0.0.0", address, ushort.Parse(port));
                    break;
            }
            return options;
        }
    }
}
