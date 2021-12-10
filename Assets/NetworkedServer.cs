using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;
using UnityEngine.UI;

public class NetworkedServer : MonoBehaviour
{
    int maxConnections = 1000;
    int reliableChannelID;
    int unreliableChannelID;
    int hostID;
    int socketPort = 5491;

    List<PlayerAccount> playerAccounts;
    string saveDataPath;

    int playerWaitingForMatchWithId = -1;

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

        playerAccounts = new List<PlayerAccount>();
        saveDataPath = Application.dataPath + Path.DirectorySeparatorChar + "playerAccountData.txt";
        LoadAccountData();

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

        NetworkEventType recNetworkEvent = NetworkTransport.Receive(out recHostID, out recConnectionID, out recChannelID, recBuffer, bufferSize, out dataSize, out error);

        switch (recNetworkEvent)
        {
            case NetworkEventType.Nothing:
                break;
            case NetworkEventType.ConnectEvent:
                Debug.Log("Connection, " + recConnectionID);
                break;
            case NetworkEventType.DataEvent:
                string msg = Encoding.Unicode.GetString(recBuffer, 0, dataSize);
                ProcessRecievedMsg(msg, recConnectionID);
                break;
            case NetworkEventType.DisconnectEvent:
                Debug.Log("Disconnection, " + recConnectionID);

                if(recConnectionID == playerWaitingForMatchWithId)
                    playerWaitingForMatchWithId = -1;
                else
                {
                    GameRoom gr = GetGameRoomFromClientIDIncludeObservers(recConnectionID);
                    if (gr != null)
                    {
                        if ((gr.playerId1 == recConnectionID || gr.playerId2 == recConnectionID) && gr.gameHasEnded == false)
                            ProcessRecievedMsg(ClientToServerSignifiers.EndingTheGame + "," + "Other player left early", recConnectionID);
                        RemoveClientFromGameRoom(gr, recConnectionID);
                    }
                }
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
        Debug.Log("msg received = " + msg + ".  connection id = " + id + " frame: " + Time.frameCount);

        string[] csv = msg.Split(',');

        int signifier = int.Parse(csv[0]);

        if (signifier == ClientToServerSignifiers.CreateAccount)
        {
            string n = csv[1];
            string p = csv[2];
            bool nameIsInUse = searchAccountsByName(n, out PlayerAccount temp);

            if(nameIsInUse)
            {
                SendMessageToClient(ServerToClientSignifiers.AccountCreationFailed + ", name already in use", id);
            }
            else
            {
                SaveNewUser(new PlayerAccount(n, p));
                SendMessageToClient(ServerToClientSignifiers.AccountCreated + ",Account created", id);
            }
        }
        else if(signifier == ClientToServerSignifiers.Login)
        {
            string n = csv[1];
            string p = csv[2];
            PlayerAccount accountToCheck = null;
            searchAccountsByName(n, out accountToCheck);

            if(accountToCheck == null)
                SendMessageToClient(ServerToClientSignifiers.LoginFailed + "," + "could not find user", id);
            else if(p == accountToCheck.password)
                //login
                SendMessageToClient(ServerToClientSignifiers.LoginComplete+ "," + "Logging in", id);
            else //incorrect password
                 SendMessageToClient(ServerToClientSignifiers.LoginFailed + "," + "incorrect password", id);

        }
        else if(signifier == ClientToServerSignifiers.JoinGameRoomQueue)
        {
            Debug.Log("client is waiting to join game");
            if(playerWaitingForMatchWithId == -1)
            { 
                playerWaitingForMatchWithId = id;
            }
            else
            {
                GameRoom gr = new GameRoom(playerWaitingForMatchWithId, id );

                if(gameRooms.Count == 0)
                    gr.gameRoomID = 0;
                else
                    gr.gameRoomID = gameRooms.Last.Value.gameRoomID + 1;

                gameRooms.AddLast(gr);
                SendMessageToClient(ServerToClientSignifiers.GameStart + "," + gr.gameRoomID, gr.playerId1);
                SendMessageToClient(ServerToClientSignifiers.GameStart + "," + gr.gameRoomID, gr.playerId2);
                playerWaitingForMatchWithId = -1;

                //decide who gets the first turn
                bool player1GoesFirst = Random.Range(0,2) == 0;
                if(player1GoesFirst)
                    SendMessageToClient(ServerToClientSignifiers.ChosenAsPlayerOne + "", gr.playerId1);
                else
                    SendMessageToClient(ServerToClientSignifiers.ChosenAsPlayerOne + "", gr.playerId2);
            }
        }
        else if(signifier == ClientToServerSignifiers.SelectedTicTacToeSquare)
        {
            string newMsg = ServerToClientSignifiers.OpponentChoseASquare + "," + csv[1];
            GameRoom gr = GetGameRoomFromClientID(id);
            SendMessegeToRestOfRoom(gr, id, newMsg);
            gr.savedSquareChoices.Add(csv[1]);
        }
        else if(signifier == ClientToServerSignifiers.EndingTheGame)
        {
            string newMsg = ServerToClientSignifiers.GameIsOver + "," + csv[1];
            GameRoom gr = GetGameRoomFromClientID(id);
            SendMessegeToRestOfRoom(gr, id, newMsg);
            gr.gameHasEnded = true;
        }
        else if(signifier == ClientToServerSignifiers.ChatLogMessage)
        {
            string newMsg = ServerToClientSignifiers.ChatLogMessage + ","  + csv[1];
            GameRoom gr = GetGameRoomFromClientIDIncludeObservers(id);
            SendMessegeToRestOfRoom(gr, id, newMsg);
        }
        else if(signifier == ClientToServerSignifiers.JoinAnyRoomAsObserver)
        {
            if(gameRooms.Count > 0)
            { 
                EnterGameRoomAsObserver(gameRooms.First.Value, id);
            }
        }
        else if (signifier == ClientToServerSignifiers.JoinSpecificRoomAsObserver)
        {
            int requestedRoomNumber = int.Parse(csv[1]);

            GameRoom specifiedRoom = GetRoomFromRoomID(requestedRoomNumber);

            if(specifiedRoom !=null)
                EnterGameRoomAsObserver(specifiedRoom, id);
        }
        else if(signifier == ClientToServerSignifiers.LeaveTheRoom)
        {
            if(id == playerWaitingForMatchWithId)
            {
                playerWaitingForMatchWithId = -1;
                return;
            }

            GameRoom gr = GetGameRoomFromClientIDIncludeObservers(id);

            if(gr != null)
                RemoveClientFromGameRoom(gr, id);
        }
        else if(signifier == ClientToServerSignifiers.RequestTurnData)
        {
            GameRoom gr = GetRoomFromRoomID(int.Parse(csv[1]));
            string newMsg = ServerToClientSignifiers.TurnData + "";
            foreach (string turnData in gr.savedSquareChoices)
            {
                newMsg += "," + turnData;
            }
            SendMessageToClient(newMsg, id);
        }

    }



    /// Helper Functions
    /// 
  
   //login/username functions

    private bool searchAccountsByName(string name, out PlayerAccount account)
    {
        bool nameIsInUse = false;
        account = null;
        foreach (PlayerAccount pa in playerAccounts)
        {
            if (name == pa.name)
            {
                nameIsInUse = true;
                account = pa;
                break;
            }
        }
       
        return nameIsInUse;
    }

    private void SaveNewUser(PlayerAccount newPlayerAccount)
    {
        playerAccounts.Add(newPlayerAccount);

        StreamWriter sw = new StreamWriter(saveDataPath, true);
        foreach(PlayerAccount pa in playerAccounts)
        { 
            sw.WriteLine(pa.name + "," + pa.password);
        }
        sw.Close();
    }

    private void LoadAccountData()
    {
        if(File.Exists(saveDataPath) == false)
            return;

        string line = "";
        StreamReader sr = new StreamReader(saveDataPath);
        while((line = sr.ReadLine()) != null)
        {
            string[] cvs = line.Split(',');
            playerAccounts.Add(new PlayerAccount(cvs[0], cvs[1]) );
        }
        sr.Close();
    }

    //finding game room functions
    //

    //checks each room's playerIDs if they match the given id
    private GameRoom GetGameRoomFromClientID(int id)
    {
        foreach(GameRoom gr in gameRooms)
        {
            if(gr.playerId1 == id || gr.playerId2 == id)
                return gr;
        }
        return null;
    }

    //checks all ids in each room to find the given id
    private GameRoom GetGameRoomFromClientIDIncludeObservers(int id)
    {
        foreach (GameRoom gr in gameRooms)
        {
            foreach (int observerId in gr.observerIds)
            {
                if (observerId == id)
                    return gr;
            }

        }
        return null;
    }

    private GameRoom GetRoomFromRoomID(int id)
    {
        foreach (GameRoom gr in gameRooms)
        {
            if (gr.gameRoomID == id)
                return gr;
        }
        return null;
    }



    void SendMessegeToRestOfRoom(GameRoom gr, int fromID, string msg)
    {
        foreach(int id in gr.observerIds)
        {
            if(id != fromID)
                 SendMessageToClient(msg, id);
        }
    }



    void EnterGameRoomAsObserver(GameRoom gr, int playerId)
    {
        gr.observerIds.Add(playerId);
        string msg = ServerToClientSignifiers.EnteredGameRoomAsObserver + "," + gr.gameRoomID;
        foreach(string turnData in gr.savedSquareChoices)
        {
            msg += "," + turnData;
        }
        SendMessageToClient(msg, playerId);
    }
    void RemoveClientFromGameRoom(GameRoom gr, int id)
    {
        int index = -1;
        for(int i = 0; i < gr.observerIds.Count; i++)
        {
            if(gr.observerIds[i] == id)
            { 
                index = i;
                break;
            }
        }
        if(index != -1)
            gr.observerIds.RemoveAt(index);

        if(gr.observerIds.Count == 0)
            gameRooms.Remove(gr);
    }

}


public static class ClientToServerSignifiers
{
    public const int CreateAccount = 1;
    public const int Login = 2;
    public const int JoinGameRoomQueue = 3;
    public const int SelectedTicTacToeSquare = 4;

    public const int ChatLogMessage = 8;

    public const int JoinAnyRoomAsObserver = 9;
    public const int JoinSpecificRoomAsObserver = 10;

    public const int EndingTheGame = 11;
    public const int LeaveTheRoom = 12;

    public const int RequestTurnData = 14;
}

public static class ServerToClientSignifiers
{
    public const int LoginComplete = 1;
    public const int LoginFailed = 2;

    public const int AccountCreated = 3;
    public const int AccountCreationFailed = 4;

    public const int GameStart = 5;

    public const int ChosenAsPlayerOne = 6;
    public const int OpponentChoseASquare = 7;

    public const int ChatLogMessage = 11;

    public const int EnteredGameRoomAsObserver = 12;

    public const int GameIsOver = 13;
    public const int TurnData = 14;
}


public class PlayerAccount
{
    public string name, password;

    public PlayerAccount()
    {
    }
    public PlayerAccount(string name, string password)
    {
        this.name = name;
        this.password = password;
    }
}

public class GameRoom
{
    public int gameRoomID;
    public int playerId1, playerId2;
    public bool gameHasEnded = false;

    public List<int> observerIds;
    public List<string> savedSquareChoices;

    public GameRoom(int id1, int id2)
    {
        playerId1 = id1;
        playerId2 = id2;

        observerIds = new List<int>();
        observerIds.Add(playerId1);
        observerIds.Add(playerId2);
        savedSquareChoices = new List<string>();
    }
}