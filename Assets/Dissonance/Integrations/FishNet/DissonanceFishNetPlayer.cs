using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Connection;
using Dissonance.Integrations.FishNet.Utils;
using FishNet.Transporting;

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

        private static Log Log = Logs.Create(LogCategory.Network, "FishNet Player Component");

        private DissonanceComms _comms;

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

        public Vector3 Position => this.trackingTransform != null ? this.trackingTransform.position : this.transform.position;
        public Quaternion Rotation => this.trackingTransform != null ? this.trackingTransform.rotation : this.transform.rotation;
        public NetworkPlayerType Type
        {
            get
            {
                if (this._comms == null || this._playerId.Value == null)
                    return NetworkPlayerType.Unknown;
                return this._comms.LocalPlayerName.Equals(this._playerId.Value) ? NetworkPlayerType.Local : NetworkPlayerType.Remote;
            }
        }

        private void OnEnable()
        {
            if (DissonanceFishNetComms.Instance != null)
                this._comms = DissonanceFishNetComms.Instance.Comms;
        }

        private void OnDisable()
        {
            if (this.IsTracking)
                StopTracking();
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();

            this._playerId.OnChange += OnPlayerIdChanged;
            if (_comms != null)
                this._comms.LocalPlayerNameChanged += LocalPlayerNameChanged;
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();

            this._playerId.OnChange -= OnPlayerIdChanged;
            if (this._comms != null)
                this._comms.LocalPlayerNameChanged -= LocalPlayerNameChanged;

            // When an object is destroyed due to a network disconnection, 
            // the OnDisable method is called, but _playerId is already null at that stage.
            // Therefore, we stop tracking at this point while _playerId still holds a value.
            // Failing to do so may cause Dissonance to fail to properly stop tracking,
            // resulting in an error such as the following:
            // (PlayerTrackerManager.cs: RemoveTracker: _unlinkedPlayerTrackers.Remove(player.PlayerId) -> ArgumentNullException: Value cannot be null.)
            if (this.IsTracking)
                StopTracking();
        }

        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);

            if (this.IsOwner == false) return;

            if (this._comms == null)
            {
                LoggingHelper.Logger.Error(
                    "cannot find DissonanceFishNetComms component in scene\r\n" +
                    "not placing a DissonanceFishNetComms component on a game object in the scene");
            }

            Log.Debug("Tracking `OnOwnershipClient` Name={0}", this._comms.LocalPlayerName);

            if (this._comms.LocalPlayerName != null)
                SetLocalPlayerNameAsOwner(this._comms.LocalPlayerName);
        }

        private void LocalPlayerNameChanged(string playerName)
        {
            if (this.IsOwner)
            {
                // When LocalPlayerName changes, the Owner updates _playerId.
                SetLocalPlayerNameAsOwner(playerName);
            }
        }

        [Client(RequireOwnership = true)]
        private void SetLocalPlayerNameAsOwner(string playerName)
        {
            // At this stage, the value is only changed locally and is not synchronized. See: WritePermission.ClientUnsynchronized
            // To synchronize the value, use RpcSetPlayerName.
            this._playerId.Value = playerName;
            RestartTracking();

            // This method is called on the server. The owner sends a request to the server to update _playerId,
            // and when the server changes the value of _playerId, OnPlayerIdChanged is invoked.
            RpcSetPlayerName(playerName);
        }

        /// <summary>
        /// Invoking on client will cause it to run on the server 
        /// </summary>
        /// <param name="playerName">PlayerName</param>
        /// <param name="channel">The channel through which data is transmitted. Use Reliable to ensure no data loss occurs.</param>
        [ServerRpc(RequireOwnership = true)]
        private void RpcSetPlayerName(string playerName, Channel channel = Channel.Reliable)
        {
            // The server changes the value of _playerId, and since it is of the SyncVar type,
            // the OnPlayerIdChanged callback will be triggered on all clients
            this._playerId.Value = playerName;
        }

        /// <summary>
        /// When the server changes the value of _playerId, it is run on all clients
        /// </summary>
        /// <param name="prev">Previous _playerId value</param>
        /// <param name="next">Current _playerId value</param>
        /// <param name="asServer">Indicates if the callback is occurring on the server or on the client.</param>
        private void OnPlayerIdChanged(string prev, string next, bool asServer)
        {
            // To enable tracking, clients except the owner call RestartTracking.
            // (The owner has already called this in SetLocalPlayerNameAsOwner)
            if (this.IsOwner == false)
                RestartTracking();
        }

        private void RestartTracking()
        {
            // We need the player name to be set on all the clients and then tracking to be started (on each client).

            // We need to stop and restart tracking to handle the name change
            if (this.IsTracking)
                StopTracking();

            // Perform the actual work
            StartTracking();
        }

        private void StartTracking()
        {
            if (this.IsTracking)
                throw Log.CreatePossibleBugException("Attempting to start player tracking, but tracking is already started", "31971B1F-52FD-4FCF-89E9-67A17A917921");

            if (this._comms != null)
            {
                if (string.IsNullOrEmpty(this._playerId.Value))
                    Log.Error($"{nameof(StartTracking)} called but {nameof(_playerId)} is null or empty!");

                this._comms.TrackPlayerPosition(this);
                this.IsTracking = true;
            }
        }

        private void StopTracking()
        {
            if (!this.IsTracking)
                throw Log.CreatePossibleBugException("Attempting to stop player tracking, but tracking is not started", "C7CF0174-0667-4F07-88E3-800ED652142D");

            if (this._comms != null)
            {
                if (string.IsNullOrEmpty(this._playerId.Value))
                    Log.Error($"{nameof(StopTracking)} called but {nameof(_playerId)} is null or empty!");

                this._comms.StopTracking(this);
                this.IsTracking = false;
            }
        }
    }
}
