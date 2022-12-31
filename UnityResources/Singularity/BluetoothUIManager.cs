using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BluetoothUIManager : MonoBehaviour
{
    public GameObject connectPanel;
    public GameObject consolePanel;
    public GameObject connectedPanel;
    public GameObject connectingPanel;
    public GameObject errorPanel;
    //public TMPro.TMP_Text errorMessageText;
    public TMPro.TMP_Dropdown dropdown;
    public Sngty.SingularityManager singularity;

    private enum uistate { Disconnected, Connecting, Connected };
    private List<Sngty.DeviceSignature> devices;

    private uistate state = uistate.Disconnected;
    private bool displayErrorUI = false;

    private uistate laststate = uistate.Disconnected;
    private Sngty.DeviceSignature thisDevice;

    List<string> activeMessages = new List<string>();
    public int maxMessages = 7;
    // Start is called before the first frame update
    void Start()
    {
        updateDeviceOptions();
    }

    // Update is called once per fram
    void Update()
    {
        if (state != laststate)
        {
            handleUIChange();
            laststate = state;
        }

        //Debug.Log(dropdown.value);
        //updateDeviceOptions();
        if (displayErrorUI)
        {
            errorPanel.SetActive(true);
            displayErrorUI = false;
        }

    }

    public void onConnectButton()
    {
        state = uistate.Connecting;
        thisDevice = devices[dropdown.value];
        singularity.ConnectToDevice(thisDevice);
        connectingPanel.transform.Find("DeviceName").GetComponent<TMPro.TMP_Text>().text = thisDevice.name;
    }

    public void onDisconnectButton()
    {
        singularity.DisconnectAll();
        state = uistate.Disconnected;
        for (int i = 0; i < maxMessages; i++)
        {
            consolePanel.transform.Find("ConsoleContent" + i.ToString()).GetComponent<TMPro.TMP_Text>().text = "";
        }
    }

    public void onDropdownValueChanged()
    {
        //updateDeviceOptions();
    }

    private void handleUIChange()
    {
        if (state == uistate.Connecting)
        {
            connectPanel.SetActive(false);
            connectingPanel.SetActive(true);
            connectedPanel.SetActive(false);
            consolePanel.SetActive(false);
        }
        else if (state == uistate.Connected)
        {
            connectedPanel.SetActive(true);
            connectingPanel.SetActive(false);
            connectPanel.SetActive(false);
            consolePanel.SetActive(true);
        }
        else if (state == uistate.Disconnected)
        {
            connectedPanel.SetActive(false);
            connectingPanel.SetActive(false);
            connectPanel.SetActive(true);
            consolePanel.SetActive(false);
        }
    }

    private void updateDeviceOptions()
    {
        if (state == uistate.Disconnected)
        {
            devices = singularity.GetPairedDevices();
            dropdown.ClearOptions();
            List < TMPro.TMP_Dropdown.OptionData > options = new List<TMPro.TMP_Dropdown.OptionData>();
            devices.ForEach(device => options.Add(new TMPro.TMP_Dropdown.OptionData(device.name)));
            dropdown.AddOptions(options);
        }
    }

    public void onConnected()
    {
        state = uistate.Connected;
        connectedPanel.transform.Find("DeviceName").GetComponent<TMPro.TMP_Text>().text = thisDevice.name;
        connectedPanel.transform.Find("DeviceMac").GetComponent<TMPro.TMP_Text>().text = thisDevice.mac;
    }

    public void onError(string errorMessage)
    {
        errorPanel.transform.Find("ErrorMessage").GetComponent<TMPro.TMP_Text>().text = errorMessage;
        //errorPanel.SetActive(true);
        displayErrorUI = true;
        onDisconnectButton();
    }

    public void onMessaggeRecieved(string message)
    {
        activeMessages.Add(System.DateTime.Now.Hour + ":" + System.DateTime.Now.Minute + ":" + System.DateTime.Now.Second + "." + System.DateTime.Now.Millisecond.ToString("D3") + " " + message);
        if (activeMessages.Count > maxMessages)
        {
            activeMessages.RemoveAt(0);
        }
        for (int i = 0; i < activeMessages.Count; i++)
        {
            consolePanel.transform.Find("ConsoleContent" + i.ToString()).GetComponent<TMPro.TMP_Text>().text = activeMessages[i];
        }
        if (activeMessages.Count < maxMessages)
        {
            for (int i = activeMessages.Count; i < maxMessages; i++)
            {
                consolePanel.transform.Find("ConsoleContent" + i.ToString()).GetComponent<TMPro.TMP_Text>().text = "";
            }
        }
    }
}