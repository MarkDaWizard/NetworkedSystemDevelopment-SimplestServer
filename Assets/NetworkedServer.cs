using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;
using UnityEngine.UI;
using System;

public class NetworkedServer : MonoBehaviour
{
    int maxConnections = 1000;
    int reliableChannelID;
    int unreliableChannelID;
    int hostID;
    int socketPort = 5491;
    LinkedList<PlayerAccount> playerAccounts;
    // Start is called before the first frame update
    void Start()
    {
        NetworkTransport.Init();
        ConnectionConfig config = new ConnectionConfig();
        reliableChannelID = config.AddChannel(QosType.Reliable);
        unreliableChannelID = config.AddChannel(QosType.Unreliable);
        HostTopology topology = new HostTopology(config, maxConnections);
        hostID = NetworkTransport.AddHost(topology, socketPort, null);
        playerAccounts = new LinkedList<PlayerAccount>();
        //read in player accounts
        LoadPlayerManagementFile();
    }

    // Update is called once per frame
    void Update()
    {

        int recHostID;
        int recConnectionID;
        int recChannelID;
        byte[] recBuffer = new byte[1024];
        int bufferSize = 1024;
        int dataSize;
        byte error = 0;

        NetworkEventType recNetworkEvent = NetworkTransport.Receive(out recHostID, out recConnectionID
            , out recChannelID, recBuffer, bufferSize, out dataSize, out error);

        switch (recNetworkEvent)
        {
            case NetworkEventType.Nothing:
                break;
            case NetworkEventType.ConnectEvent:
                Debug.Log("Connection, " + recConnectionID);
                //a
                break;
            case NetworkEventType.DataEvent:
                string msg = Encoding.Unicode.GetString(recBuffer, 0, dataSize);
                ProcessRecievedMsg(msg, recConnectionID);
                break;
            case NetworkEventType.DisconnectEvent:
                Debug.Log("Disconnection, " + recConnectionID);
                //a
                break;
        }

    }

    public void SendMessageToClient(string msg, int id)
    {
        byte error = 0;
        byte[] buffer = Encoding.Unicode.GetBytes(msg);
        NetworkTransport.Send(hostID, id, reliableChannelID, buffer, msg.Length * sizeof(char), out error);
    }

    private void ProcessRecievedMsg(string msg, int id)
    {
        Debug.Log("msg recieved = " + msg + ".  connection id = " + id);
        string[] csv = msg.Split(',');
        int signifier = int.Parse(csv[0]);
        string n = "";
        string p = "";
        if (csv.Length > 1)
            n = csv[1];
        if (csv.Length > 2)
        {

            p = csv[2];
        }
        bool nameIsInUse = false;
        bool validUser = false;
        try
        {
            if (signifier == ClientToServerSignifiers.CreateAccount)
            {
                Debug.Log("create account");
                //chk if player already exists
                foreach (PlayerAccount pa in playerAccounts)
                {
                    if (pa.name == n)
                    {
                        nameIsInUse = true;
                        break;
                    }
                }
                if (nameIsInUse)
                {
                    AppendLogFile(n + ":Account creation failed from connection " + id);
                    SendMessageToClient(ServerToClientSignifiers.AccountCreationFailed + "," + n, id);
                    // + "," + System.DateTime.Now.ToString("HH:mm:ss MM/dd/yyyy"));
                }
                else
                {
                    ///if not create new, add to list
                    PlayerAccount playerAccount = new PlayerAccount(id, n, p);
                    playerAccounts.AddLast(playerAccount);
                    //send to client suc or fail
                    Debug.Log("create success");
                    AppendLogFile(n + ":Account creation succeed from connection " + id);
                    SendMessageToClient(ServerToClientSignifiers.AccountCreationComplete + "," + n, id);
                    // save list to hd
                    SavePlayerManagementFile();
                }


            }
            else if (signifier == ClientToServerSignifiers.Login)
            {
                Debug.Log("login");

                //chk if player is already exists,
                foreach (PlayerAccount pa in playerAccounts)
                {
                    if (pa.name == n && pa.password == p)
                    {
                        validUser = true;
                        Debug.Log("login success");
                        break;
                    }
                }
                //send to client suc or fail
                if (validUser)
                {
                    AppendLogFile(n + ":Login succeed from connection " + id);
                    SendMessageToClient(ServerToClientSignifiers.LoginComplete + "," + n, id);
                }
                else
                {
                    AppendLogFile(n + ":Login failed from connection " + id);
                    SendMessageToClient(ServerToClientSignifiers.LoginFailed + "," + n, id);
                }
            }

        }
        catch (Exception ex)
        {
            Debug.Log("error" + ex.Message);
        }

    }

    public void SavePlayerManagementFile()
    {
        StreamWriter sw = new StreamWriter(Application.dataPath + Path.DirectorySeparatorChar + "PlayerManagementFile.txt");
        foreach (PlayerAccount pa in playerAccounts)
        {
            sw.WriteLine(PlayerAccount.PlayerIdSinifier + "," + pa.id + "," + pa.name + "," + pa.password);
        }
        sw.Close();
    }

    public void LoadPlayerManagementFile()
    {
        if (File.Exists(Application.dataPath + Path.DirectorySeparatorChar + "PlayerManagementFile.txt"))
        {
            StreamReader sr = new StreamReader(Application.dataPath + Path.DirectorySeparatorChar + "PlayerManagementFile.txt");
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                string[] csv = line.Split(',');

                int signifier = int.Parse(csv[0]);
                if (signifier == PlayerAccount.PlayerIdSinifier)
                {
                    playerAccounts.AddLast(new PlayerAccount(int.Parse(csv[1]), csv[2], csv[3]));
                }
            }
        }
    }
    public void AppendLogFile(string line)
    {
        StreamWriter sw = new StreamWriter(Application.dataPath + Path.DirectorySeparatorChar + "Log.txt", true);

        sw.WriteLine(System.DateTime.Now.ToString("yyyyMMdd HHmmss") + ": " + line);

        sw.Close();
    }


}

public class PlayerAccount
{
    public const int PlayerIdSinifier = 1;
    public string name, password;
    public int id;
    public PlayerAccount(int i, string n, string p)
    {
        id = i;
        name = n;
        password = p;
    }

}
public static class ClientToServerSignifiers
{
    public const int CreateAccount = 1;
    public const int Login = 2;
    public const int JoinGammeRoomQueue = 3;
    public const int PlayGame = 4;
    public const int SendMsg = 5;
    public const int SendPrefixMsg = 6;
    public const int JoinAsObserver = 7;
    public const int SendClientMsg = 8;
    public const int ReplayMsg = 9;
}
public static class ServerToClientSignifiers
{
    public const int LoginComplete = 1;
    public const int LoginFailed = 2;
    public const int AccountCreationComplete = 3;
    public const int AccountCreationFailed = 4;
    public const int OpponentPlay = 5;
    public const int GameStart = 6;
    public const int ReceiveMsg = 7;
    public const int someoneJoinedAsObserver = 8;
    public const int ListOfPlayer = 8;
    public const int JoinedPlay=9;
    public const int ReceiveCMsg = 10;
    public const int ReplayMsg = 11;
}
