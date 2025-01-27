using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Connection;
using Dissonance.Integrations.FishNet.Utils;

namespace Dissonance.Integrations.FishNet
{
    /// <summary>
    /// When added to the player prefab, allows Dissonance to automatically track
    /// the location of remote players for positional audio for games using the
    /// FishNet API.
    /// </summary>
    public class DissonanceFishNetPlayer
        : NetworkBehaviour, IDissonancePlayer
    {
        [Tooltip("This transform will be used in positional voice processing. If unset, then GameObject's transform will be used.")]
        [SerializeField] private Transform trackingTransform;

        private static readonly Log Log = Logs.Create(LogCategory.Network, "FishNet Player Component");

        private DissonanceFishNetComms _comms;

        public bool IsTracking { get; private set; }

        /// <summary>
        /// The name of the player
        /// </summary>
        /// <remarks>
        /// This is a syncvar, this means unity will handle setting this value.
        /// This is important for Join-In-Progress because new clients will join and instantly have the player name correctly set without any effort on our part.
        /// https://fish-networking.gitbook.io/docs/manual/guides/synchronizing/syncvar
        /// </remarks>
        private readonly SyncVar<string> _playerId = new(settings: new SyncTypeSettings(WritePermission.ClientUnsynchronized, ReadPermission.Observers));
        public string PlayerId { get { return _playerId.Value; } }

        public Vector3 Position
        {
            get { return trackingTransform != null ? trackingTransform.position : transform.position; }
        }

        public Quaternion Rotation
        {
            get { return trackingTransform != null ? trackingTransform.rotation : transform.rotation; }
        }

        public NetworkPlayerType Type
        {
            get
            {
                if (_comms == null || _playerId.Value == null)
                    return NetworkPlayerType.Unknown;
                return _comms.Comms.LocalPlayerName.Equals(_playerId.Value) ? NetworkPlayerType.Local : NetworkPlayerType.Remote;
            }
        }

        public void OnDestroy()
        {
            if (_comms != null)
                _comms.Comms.LocalPlayerNameChanged -= SetPlayerName;
        }

        public void OnEnable()
        {
            _comms = DissonanceFishNetComms.Instance;
        }

        public void OnDisable()
        {
            if (IsTracking)
                StopTracking();
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();

            _playerId.OnChange += OnPlayerIdChanged;
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();

            _playerId.OnChange -= OnPlayerIdChanged;
        }

        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);

            if (this.IsOwner == false) return;

            var comms = DissonanceFishNetComms.Instance;
            if (comms == null)
            {
                LoggingHelper.Logger.Error(
                    "cannot find DissonanceFishNetComms component in scene\r\n" +
                    "not placing a DissonanceFishNetComms component on a game object in the scene");
            }

            Log.Debug("Tracking `OnStartLocalPlayer` Name={0}", comms.Comms.LocalPlayerName);

            // This method is called on the client which has control authority over this object. This will be the local client of whichever player we are tracking.
            if (comms.Comms.LocalPlayerName != null)
                SetPlayerName(comms.Comms.LocalPlayerName);

            //Subscribe to future name changes (this is critical because we may not have run the initial set name yet and this will trigger that initial call)
            comms.Comms.LocalPlayerNameChanged += SetPlayerName;
        }

        private void SetPlayerName(string playerName)
        {
            //We need the player name to be set on all the clients and then tracking to be started (on each client).
            //To do this we send a command from this client, informing the server of our name. The server will pass this on to all the clients (with an RPC)
            // Client -> Server -> Client

            //We need to stop and restart tracking to handle the name change
            if (IsTracking)
                StopTracking();

            //Perform the actual work
            _playerId.Value = playerName;
            StartTracking();

            //Inform the server the name has changed
            if (IsOwner)
                RpcSetPlayerName(playerName);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            //A client is starting. Start tracking if the name has been properly initialised
            if (!string.IsNullOrEmpty(PlayerId))
                StartTracking();
        }

        /// <summary>
        /// Invoking on client will cause it to run on the server
        /// </summary>
        /// <param name="playerName"></param>
        [ServerRpc]
        private void RpcSetPlayerName(string playerName)
        {
            // The server changes the value of _playerId, and since it is of the SyncVar type, the OnPlayerIdChanged callback will be triggered on all clients
            _playerId.Value = playerName;
        }

        /// <summary>
        /// When the server changes the value of _playerId, it is run on all clients
        /// </summary>
        /// <param name="playerName"></param>
        private void OnPlayerIdChanged(string prev, string next, bool asServer)
        {
            if (!IsOwner)
                SetPlayerName(next);
        }

        private void StartTracking()
        {
            if (IsTracking)
                throw Log.CreatePossibleBugException("Attempting to start player tracking, but tracking is already started", "31971B1F-52FD-4FCF-89E9-67A17A917921");

            if (_comms != null)
            {
                _comms.Comms.TrackPlayerPosition(this);
                IsTracking = true;
            }
        }

        private void StopTracking()
        {
            if (!IsTracking)
                throw Log.CreatePossibleBugException("Attempting to stop player tracking, but tracking is not started", "C7CF0174-0667-4F07-88E3-800ED652142D");

            if (_comms != null)
            {
                _comms.Comms.StopTracking(this);
                IsTracking = false;
            }
        }
    }
}
