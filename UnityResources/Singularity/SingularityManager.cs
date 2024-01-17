using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Android;

namespace Sngty
{
    public class SingularityManager : MonoBehaviour
    {
        public UnityEvent onBluetoothReady;
        public UnityEvent onConnected;
        public UnityEvent<string> onMessageRecieved;
        public UnityEvent<string> onError;

        private AndroidJavaClass BluetoothManager;
        private AndroidJavaObject bluetoothManager;

        private List<AndroidJavaObject> connectedDevices;


        internal void PermissionCallbacks_PermissionDeniedAndDontAskAgain(string permissionName)
        {
            Debug.Log($"{permissionName} PermissionDeniedAndDontAskAgain");
        }

        internal void PermissionCallbacks_PermissionGranted(string permissionName)
        {
            Debug.Log($"{permissionName} PermissionCallbacks_PermissionGranted");

            BluetoothPermissionsGranted();
        }

        internal void PermissionCallbacks_PermissionDenied(string permissionName)
        {
            Debug.Log($"{permissionName} PermissionCallbacks_PermissionDenied");
        }

        // Awake is called before any object's Start().
        // Set up bluetooth using Awake() so it's ready for other objects.
        // Trying to use the singularity manager in other script's Awake() methods may not work properly.
        void Awake()
        {
            // Check for necessary bluetooth permissions and request if necessary
            // You may need to restart the app on the headset after granting permissions.
            // Sometimes, you may need to restart the app twice for the permissions to fully work.
            // This is a quick and dirty solution for getting the permissions.
            // A better way is to use callbacks to let everything else know when the permissions have been granted.
            bool hasBtConnectPermission = Permission.HasUserAuthorizedPermission("android.permission.BLUETOOTH_CONNECT");
            bool hasBtPermission = Permission.HasUserAuthorizedPermission("android.permission.BLUETOOTH");
            bool hasBtAdminPermission = Permission.HasUserAuthorizedPermission("android.permission.BLUETOOTH_ADMIN");
            bool hasBtScanPermission = Permission.HasUserAuthorizedPermission("android.permission.BLUETOOTH_SCAN");

            List<string> permissionsNeeded = new List<string>();
            if (!hasBtConnectPermission)
            {
                permissionsNeeded.Add("android.permission.BLUETOOTH_CONNECT");
            }
            if (!hasBtPermission)
            {
                permissionsNeeded.Add("android.permission.BLUETOOTH");
            }
            if (!hasBtAdminPermission)
            {
                permissionsNeeded.Add("android.permission.BLUETOOTH_ADMIN");
            }
            if (!hasBtScanPermission)
            {
                permissionsNeeded.Add("android.permission.BLUETOOTH_SCAN");
            }
            Debug.LogWarning("May need to restart the app. Requesting permissions: " + string.Join(", ", permissionsNeeded));

            if (permissionsNeeded.Count == 0)
            {
                BluetoothPermissionsGranted();
            }
            else
            {
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionDenied += PermissionCallbacks_PermissionDenied;
                callbacks.PermissionGranted += PermissionCallbacks_PermissionGranted;
                callbacks.PermissionDeniedAndDontAskAgain += PermissionCallbacks_PermissionDeniedAndDontAskAgain;

                Permission.RequestUserPermissions(permissionsNeeded.ToArray(), callbacks);
            }
        }


        // Start is called before the first frame update
        void Start()
        {
        }

        // Update is called once per frame
        void Update()
        {
        }

        private void BluetoothPermissionsGranted()
        {
            BluetoothManager = new AndroidJavaClass("com.harrysoft.androidbluetoothserial.BluetoothManager");
            bluetoothManager = BluetoothManager.CallStatic<AndroidJavaObject>("getInstance");

            connectedDevices = new List<AndroidJavaObject>();

            onBluetoothReady.Invoke();
        }

        public void ConnectToDevice(DeviceSignature sig)
        {
            AndroidJavaClass Schedulers = new AndroidJavaClass("io.reactivex.schedulers.Schedulers");
            AndroidJavaClass AndroidSchedulers = new AndroidJavaClass("io.reactivex.android.schedulers.AndroidSchedulers");
            bluetoothManager.Call<AndroidJavaObject>("openSerialDevice", sig.mac)
                            .Call<AndroidJavaObject>("subscribeOn", Schedulers.CallStatic<AndroidJavaObject>("io"))
                            .Call<AndroidJavaObject>("observeOn", AndroidSchedulers.CallStatic<AndroidJavaObject>("mainThread"))
                            .Call("subscribe", new RxSingleObserver(onError, onConnected, onMessageRecieved, connectedDevices));

        }

        public void sendMessage(string message, DeviceSignature sig)
        {
            for (int i = 0; i < connectedDevices.Count; i++)
            {
                if (connectedDevices[i].Call<string>("mac") == sig.mac)
                {
                    AndroidJavaObject connectedDevice = connectedDevices[i];
                    AndroidJavaObject deviceInterface = connectedDevice.Call<AndroidJavaObject>("toSimpleDeviceInterface");
                    deviceInterface.Call("sendMessage", message);
                    break;
                }
            }
        }

        public void DisconnectDevice(DeviceSignature sig)
        {
            bluetoothManager.Call("closeDevice", sig.mac);
            for (int i = 0; i < connectedDevices.Count; i++)
            {
                if (connectedDevices[i].Call<string>("mac") == sig.mac)
                {
                    connectedDevices.RemoveAt(i);
                    break;
                }
            }
        }

        public void DisconnectAll()
        {
            bluetoothManager.Call("close");
            connectedDevices.Clear();
        }

        public List<DeviceSignature> GetPairedDevices()
        {
            AndroidJavaObject pairedDevicesCollection = bluetoothManager.Call<AndroidJavaObject>("getPairedDevices");
            AndroidJavaObject pairedDevicesIterator = pairedDevicesCollection.Call<AndroidJavaObject>("iterator");
            int size = pairedDevicesCollection.Call<int>("size");
            List<DeviceSignature> pairedDevices = new List<DeviceSignature>();
            for (int i = 1; i <= size; i++)
            {
                AndroidJavaObject thisDevice = pairedDevicesIterator.Call<AndroidJavaObject>("next");
                DeviceSignature thisSignature;
                thisSignature.name = thisDevice.Call<string>("getName");
                thisSignature.mac = thisDevice.Call<string>("getAddress");
                pairedDevices.Add(thisSignature);
            }
            return pairedDevices;
        }

        class RxSingleObserver : AndroidJavaProxy
        {
            private UnityEvent<string> onErrorEvent;
            private UnityEvent onConnectedEvent;
            private UnityEvent<string> onMessageRecievedEvent;
            private List<AndroidJavaObject> connectedDevices;
            public RxSingleObserver(UnityEvent<string> onErrorEvent, UnityEvent onConnectedEvent, UnityEvent<string> onMessageRecievedEvent, List<AndroidJavaObject> connectedDevices) : base("io.reactivex.SingleObserver")
            {
                this.onErrorEvent = onErrorEvent;
                this.onConnectedEvent = onConnectedEvent;
                this.onMessageRecievedEvent = onMessageRecievedEvent;
                this.connectedDevices = connectedDevices;
            }

            void onError(AndroidJavaObject e) //e is type throwable in Java
            {
                Debug.LogWarning("BLUETOOTH ERROR");

                onErrorEvent.Invoke("Error from BTManager: " + e.Call<string>("getMessage"));
            }

            void onSuccess(AndroidJavaObject connectedDevice) //connectedDevice is type BluetoothSerialDevice in Java
            {
                onConnectedEvent.Invoke();

                AndroidJavaObject deviceInterface = connectedDevice.Call<AndroidJavaObject>("toSimpleDeviceInterface");
                deviceInterface.Call("setMessageReceivedListener", new messageRecievedListener(onMessageRecievedEvent));
                connectedDevices.Add(connectedDevice);
            }

            /*void onSubscribe()
            {
                //do nothing
            }*/
        }

        class messageRecievedListener : AndroidJavaProxy
        {
            private UnityEvent<string> onMessageRecievedEvent;
            public messageRecievedListener(UnityEvent<string> onMessageRecievedEvent) : base("com.harrysoft.androidbluetoothserial.SimpleBluetoothDeviceInterface$OnMessageReceivedListener")
            {
                this.onMessageRecievedEvent = onMessageRecievedEvent;
            }

            void onMessageReceived(string message)
            {
                onMessageRecievedEvent.Invoke(message);
            }
        }

    }
    public struct DeviceSignature
    {
        public string name;
        public string mac;
    }
}