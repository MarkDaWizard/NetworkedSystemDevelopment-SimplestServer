//SERVER

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
    int player1ID = -1;
    int player2ID = -1;
    string player1IDnum = "";
    string player2IDnum = "";
    LinkedList<GameRoom> gameRooms;
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

        LoadPlayerManagementFile();
        gameRooms = new LinkedList<GameRoom>();
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
                AppendLogFile("Start connection " + recConnectionID);
                break;
            case NetworkEventType.DataEvent:
                string msg = Encoding.Unicode.GetString(recBuffer, 0, dataSize);
                ProcessRecievedMsg(msg, recConnectionID);
                break;
            case NetworkEventType.DisconnectEvent:
                Debug.Log("Disconnection, " + recConnectionID);
                AppendLogFile("Disconnection " + recConnectionID);
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
        string pName = "";
        string pPass = "";
        if (csv.Length > 1)
            pName = csv[1];
        if (csv.Length > 2)
        {
            pPass = csv[2];
        }
        bool nameIsInUse = false;
        bool validUser = false;
        try
        {
            if (signifier == ClientToServerSignifiers.CreateAccount)
            {
                Debug.Log("create account");

                foreach (PlayerAccount playerAcc in playerAccounts)
                {
                    if (playerAcc.name == pName)
                    {
                        nameIsInUse = true;
                        break;
                    }
                }
                if (nameIsInUse)
                {
                    AppendLogFile(pName + ":Account creation failed from connection " + id);
                    SendMessageToClient(ServerToClientSignifiers.AccountCreationFailed + "," + pName, id);
                }
                else
                {
                    PlayerAccount playerAccount = new PlayerAccount(id, pName, pPass);
                    playerAccounts.AddLast(playerAccount);
                    Debug.Log("Account created");
                    AppendLogFile(pName + ":Account created from connection " + id);
                    SendMessageToClient(ServerToClientSignifiers.AccountCreationComplete + "," + pName, id);
                    SavePlayerManagementFile();
                }


            }
            else if (signifier == ClientToServerSignifiers.Login)
            {
                Debug.Log("Login");

                foreach (PlayerAccount playerAcc in playerAccounts)
                {
                    if (playerAcc.name == pName && playerAcc.password == pPass)
                    {
                        validUser = true;
                        Debug.Log("Login successful");
                        break;
                    }
                }
                if (validUser)
                {
                    AppendLogFile(pName + ":Login succeed from connection " + id);
                    SendMessageToClient(ServerToClientSignifiers.LoginComplete + "," + pName, id);
                }
                else
                {
                    AppendLogFile(pName + ":Login failed from connection " + id);
                    SendMessageToClient(ServerToClientSignifiers.LoginFailed + "," + pName, id);
                }
            }
            else if (signifier == ClientToServerSignifiers.JoinGammeRoomQueue)
            {
                if (player1ID == -1)
                {
                    player1ID = id;
                    if (csv.Length > 1)
                    {
                        player1IDnum = csv[1];
                        AppendLogFile(csv[1] + " joined, connection ID: " + id);
                        SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id + "," + csv[1], id);
                    }
                    else
                    {
                        AppendLogFile("A Player has joined, connection ID: " + id);
                        SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id, id);
                    }
                }
                else if (player2ID == -1)
                {
                    player2ID = id;
                    if (csv.Length > 1)
                    {
                        player2IDnum = csv[1];
                        AppendLogFile(csv[1] + " joined, connection ID: " + id);
                        SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id + "," + csv[1], id);
                        SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id + "," + csv[1], player1ID);
                    }
                    else
                    {
                        AppendLogFile("A player has joined, connection ID: " + id);
                        SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id, id);
                        SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id, player1ID);
                    }
                }

                else
                {
                    GameRoom gameRoom = new GameRoom();
                    gameRoom.Player1 = new PlayerAccount(player1ID, player1IDnum, "");
                    gameRoom.Player2 = new PlayerAccount(player2ID, player2IDnum, "");
                    gameRoom.Player3 = new PlayerAccount(id, csv[1], "");
                    gameRooms.AddLast(gameRoom);

                    AppendLogFile(csv[1] + " joined, connection ID: " + id);
                    AppendLogFile("Game started, current players: " + gameRoom.getPlayers());
                    SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id + "," + csv[1], id);
                    SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id + "," + csv[1], player1ID);
                    SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id + "," + csv[1], player2ID);
                    SendMessageToClient(ServerToClientSignifiers.GameStart + gameRoom.getPlayers(), gameRoom.Player1.id);
                    SendMessageToClient(ServerToClientSignifiers.GameStart + gameRoom.getPlayers(), gameRoom.Player2.id);
                    SendMessageToClient(ServerToClientSignifiers.GameStart + gameRoom.getPlayers(), gameRoom.Player3.id);

                    player1ID = -1;
                    player2ID = -1;
                    player1IDnum = "";
                    player2IDnum = "";
                }
            }
            else if (signifier == ClientToServerSignifiers.PlayGame)
            {
                GameRoom gameRoom = GetGameRoomClientId(id);
            }
            else if (signifier == ClientToServerSignifiers.SendMsg)
            {
                Debug.Log("send from s: " + msg);
                GameRoom gameRoom = GetGameRoomClientId(id);
                if (gameRoom != null)
                {
                    AppendLogFile(csv[2] + ":" + csv[1] + " from connection " + id);
                    SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + id + "," + csv[1] + "," + csv[2], gameRoom.Player1.id);
                    SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + id + "," + csv[1] + "," + csv[2], gameRoom.Player2.id);
                    SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + id + "," + csv[1] + "," + csv[2], gameRoom.Player3.id);
                    foreach (PlayerAccount playerAcc in gameRoom.ObserverList)
                    {
                        SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + id + "," + csv[1] + "," + csv[2], playerAcc.id);
                    }
                }
            }
            else if (signifier == ClientToServerSignifiers.SendPrefixMsg)
            {
                GameRoom gameRoom = GetGameRoomClientId(id);
                if (gameRoom != null)
                {
                    AppendLogFile(csv[2] + ":" + csv[1] + " from ID " + id);
                    SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + id + "," + csv[1] + "," + csv[2], gameRoom.Player1.id);
                    SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + id + "," + csv[1] + "," + csv[2], gameRoom.Player2.id);
                    SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + id + "," + csv[1] + "," + csv[2], gameRoom.Player3.id);
                    foreach (PlayerAccount playerAcc in gameRoom.ObserverList)
                    {
                        SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + id + "," + csv[1] + "," + csv[2], playerAcc.id);
                    }

                }
            }
            else if (signifier == ClientToServerSignifiers.SendClientMsg)
            {
                int receiverID = int.Parse(csv[1].Substring(0, csv[1].IndexOf(':')));
                if (csv.Length > 3)
                {
                    AppendLogFile(csv[3] + ":" + csv[2] + " to ID " + receiverID);
                    SendMessageToClient(ServerToClientSignifiers.ReceiveCMsg + "," + id + "," + csv[2] + "," + csv[3], receiverID);
                    SendMessageToClient(ServerToClientSignifiers.ReceiveCMsg + "," + id + "," + csv[2] + "," + csv[3], id);
                }

            }
            else if (signifier == ClientToServerSignifiers.JoinAsObserver)
            {
                Debug.Log("Someone joined as observer" + gameRooms.Count);
                foreach (GameRoom gameRoom in gameRooms)
                {
                    gameRoom.addObserver(id, csv[1]);

                    AppendLogFile(csv[1] + " joined as an observer, connection ID: " + id);
                    SendMessageToClient(ServerToClientSignifiers.someoneJoinedAsObserver + "," + id + "," + csv[1], gameRoom.Player1.id);
                    SendMessageToClient(ServerToClientSignifiers.someoneJoinedAsObserver + "," + id + "," + csv[1], gameRoom.Player2.id);
                    SendMessageToClient(ServerToClientSignifiers.someoneJoinedAsObserver + "," + id + "," + csv[1], gameRoom.Player3.id);

                }

            }
            else if (signifier == ClientToServerSignifiers.ReplayMsg)
            {
                Debug.Log("Replay Requested");
                string[] contain = ReadLogFile();
                foreach (var line in contain)
                {
                    SendMessageToClient(ServerToClientSignifiers.ReplayMsg + "," + line, id);
                }

            }
        }
        catch (Exception exception)
        {
            Debug.Log("Error! " + exception.Message);
        }

    }

    public void SavePlayerManagementFile()
    {
        StreamWriter sw = new StreamWriter(Application.dataPath + Path.DirectorySeparatorChar + "PlayerManagementFile.txt");
        foreach (PlayerAccount playerAcc in playerAccounts)
        {
            sw.WriteLine(PlayerAccount.PlayerIdSinifier + "," + playerAcc.id + "," + playerAcc.name + "," + playerAcc.password);
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

    public string[] ReadLogFile()
    {
        string[] contain = null;
        if (File.Exists(Application.dataPath + Path.DirectorySeparatorChar + "Log.txt"))
        {
            contain = File.ReadAllLines(Application.dataPath + Path.DirectorySeparatorChar + "Log.txt");
        }
        return contain;
    }
    public GameRoom GetGameRoomClientId(int playerId)
    {
        foreach (GameRoom gameRoom in gameRooms)
        {
            if (gameRoom.Player1.id == playerId || gameRoom.Player2.id == playerId || gameRoom.Player3.id == playerId)
            {
                return gameRoom;
            }
        }
        return null;
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
    public const int JoinedPlay = 9;
    public const int ReceiveCMsg = 10;
    public const int ReplayMsg = 11;
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
public class GameRoom
{
    public List<PlayerAccount> ObserverList;
    public PlayerAccount Player1, Player2, Player3;

    public GameRoom()
    {
        ObserverList = new List<PlayerAccount>();
    }

    public void addObserver(int id, string n)
    {
        if (!ObserverList.Contains(new PlayerAccount(id, n, "")))
            ObserverList.Add(new PlayerAccount(id, n, ""));
    }
    public string getPlayers()
    {
        string player = "";

        player += "," + Player1.id + ":" + Player1.name;
        player += "," + Player2.id + ":" + Player2.name;
        player += "," + Player3.id + ":" + Player3.name;
 
        return player;
    }
}