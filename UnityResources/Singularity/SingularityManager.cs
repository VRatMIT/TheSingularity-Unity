using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Android;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Net;
using System;

namespace Sngty
{
    public class SingularityManager : MonoBehaviour
    {
        public UnityEvent onConnected;
        public UnityEvent<string> onMessageRecieved;
        public UnityEvent<string> onError;

        private AndroidJavaClass BluetoothManager;
        private AndroidJavaObject bluetoothManager;

        private List<AndroidJavaObject> connectedDevices;

        public enum ConnectionType
        {
            Bluetooth,
            Wifi
        }
        
        public ConnectionType connectionType = ConnectionType.Wifi;
        
        [Header("Wifi Settings")]
        public string clientIP;
        public int clientPort = 80;


        private TcpClient tcpClient;
        private NetworkStream tcpStream;


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
            if (connectionType == ConnectionType.Bluetooth)
            {
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
                Permission.RequestUserPermissions(permissionsNeeded.ToArray());

                BluetoothManager = new AndroidJavaClass("com.harrysoft.androidbluetoothserial.BluetoothManager");
                bluetoothManager = BluetoothManager.CallStatic<AndroidJavaObject>("getInstance");

                connectedDevices = new List<AndroidJavaObject>();
            }
            Debug.Log("starting...");
            ConnectWifi();
        }

        private async void ConnectWifi()
        {

            try
            {
                Debug.Log("Starting connection to client: " + clientIP);
                tcpClient = new TcpClient();
                await tcpClient.ConnectAsync(clientIP, clientPort);
                if (tcpClient.Connected)
                {
                    tcpStream = tcpClient.GetStream();
                    onConnected.Invoke();
                    ReadWifiMessage();
                    Debug.Log("Connected to client: " + clientIP);
                }
            }
            catch (Exception e)
            {
                onError.Invoke("Failed to connect: " + e.Message);
            }
        }

        private async void ReadWifiMessage()
        {
            byte[] buffer = new byte[1024];
            int bytesRead;
            string accumulatedData = "";
            string lastMessage = "";

            while (tcpClient != null && tcpClient.Connected)
            {
                if (tcpStream.DataAvailable)
                {
                    bytesRead = await tcpStream.ReadAsync(buffer, 0, buffer.Length);
                    string message = System.Text.Encoding.ASCII.GetString(buffer, 0, bytesRead);
                    accumulatedData += message;

                    int endByte = accumulatedData.LastIndexOf("E");
                    int startByte = -1;
                    if (endByte != -1)
                        startByte = accumulatedData.LastIndexOf("S", endByte - 1);

                    if (startByte != -1 && endByte != -1)
                    {
                        string currentMessage = accumulatedData.Substring(startByte + 1, endByte - startByte - 1);

                        if (!string.Equals(lastMessage, currentMessage))
                        {
                            lastMessage = currentMessage;
                            onMessageRecieved.Invoke(currentMessage);
                        }
                        Debug.Log($"Message recieved! {currentMessage}");
                        accumulatedData = accumulatedData.Substring(startByte + 1);
                    }
                }
                await Task.Delay(50);
            }
            onError.Invoke("Connection lost");
        }

        public void ConnectToDevice(DeviceSignature sig)
        {
            if (connectionType == ConnectionType.Wifi)
            {
                ConnectWifi();
                return;
            }

            AndroidJavaClass Schedulers = new AndroidJavaClass("io.reactivex.schedulers.Schedulers");
            AndroidJavaClass AndroidSchedulers = new AndroidJavaClass("io.reactivex.android.schedulers.AndroidSchedulers");
            bluetoothManager.Call<AndroidJavaObject>("openSerialDevice", sig.mac)
                            .Call<AndroidJavaObject>("subscribeOn",Schedulers.CallStatic<AndroidJavaObject>("io"))
                            .Call<AndroidJavaObject>("observeOn", AndroidSchedulers.CallStatic<AndroidJavaObject>("mainThread"))
                            .Call("subscribe", new RxSingleObserver(onError, onConnected, onMessageRecieved, connectedDevices));

        }

        public void sendMessage(string message, DeviceSignature sig)
        {
            if (connectionType == ConnectionType.Wifi)
            {
                if (tcpClient != null && tcpClient.Connected)
                {
                    try
                    {
                        byte[] data = System.Text.Encoding.ASCII.GetBytes(message + "\n");
                        tcpStream.Write(data, 0, data.Length);
                    }
                    catch (Exception e)
                    {
                        onError.Invoke("Failed to send message: " + e.Message);
                    }
                }
                return;
            }
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
            if (connectionType == ConnectionType.Wifi)
            {
                if (tcpClient != null)
                {
                    tcpStream?.Close();
                    tcpClient.Close();
                    tcpClient = null;
                }
                return;
            }

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
            if(bluetoothManager != null)
            {
                bluetoothManager.Call("close");
            }

            if(connectedDevices != null)
            {
                connectedDevices.Clear();
            }
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

        void OnApplicationQuit()
        {
            if (connectionType == ConnectionType.Wifi && tcpClient != null)
            {
                tcpStream?.Close();
                tcpClient.Close();
            }
            DisconnectAll();
        }

    }

    public struct DeviceSignature
    {
        public string name;
        public string mac;
    }
}