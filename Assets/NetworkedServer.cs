//Phu Pham
//101250748
//
//T163 - Game Programming
//GAME3110

using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

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
        saveDataPath = Application.dataPath + Path.DirectorySeparatorChar + "playersdata.txt";
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
                    GameRoom gameRoom = GetGameRoomFromClientIDIncludeObservers(recConnectionID);
                    if (gameRoom != null)
                    {
                        //Check if a player disconnected
                        if ((gameRoom.playerID1 == recConnectionID || gameRoom.playerID2 == recConnectionID) && gameRoom.gameHasEnded == false)
                            ProcessRecievedMsg(ClientToServerSignifiers.EndGame + "," + "Opponent disconnected", recConnectionID);
                        RemoveClientFromGameRoom(gameRoom, recConnectionID);
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

        //Account creation
        if (signifier == ClientToServerSignifiers.CreateAccount) //A player is creating their account
        {
            string n = csv[1];
            string p = csv[2];
            bool nameIsInUse = searchAccountsByName(n, out PlayerAccount temp);

            if(nameIsInUse)
            {
                SendMessageToClient(ServerToClientSignifiers.AccountCreationFailed + ", name taken", id); //Check if name is already taken
            }
            else
            {
                SaveNewUser(new PlayerAccount(n, p));
                SendMessageToClient(ServerToClientSignifiers.AccountCreated + ", Account created successfully!", id);
            }
        }
        //Account Login
        else if(signifier == ClientToServerSignifiers.Login)
        {
            string name = csv[1];
            string pass = csv[2];
            PlayerAccount accountToCheck = null;
            searchAccountsByName(name, out accountToCheck);

            if(accountToCheck == null) //Check if name is wrong
                SendMessageToClient(ServerToClientSignifiers.LoginFailed + "," + " user not found!", id);
            else if(pass == accountToCheck.password) //Login if name & pass is correct
                
                SendMessageToClient(ServerToClientSignifiers.LoginComplete+ "," + " Logging in", id);
            else  //Check if password is wrong
                 SendMessageToClient(ServerToClientSignifiers.LoginFailed + "," + " password incorrect!", id);

        }
        //Joining a game room
        else if(signifier == ClientToServerSignifiers.JoinGameRoomQueue)
        {
            Debug.Log("Client is waiting to join game");
            if(playerWaitingForMatchWithId == -1)
            { 
                playerWaitingForMatchWithId = id;
            }
            else
            {
                //Create a new gameroom
                GameRoom gameRoom = new GameRoom(playerWaitingForMatchWithId, id );

                if(gameRooms.Count == 0)
                    gameRoom.gameRoomID = 0;
                else
                    gameRoom.gameRoomID = gameRooms.Last.Value.gameRoomID + 1;

                gameRooms.AddLast(gameRoom);
                SendMessageToClient(ServerToClientSignifiers.GameStart + "," + gameRoom.gameRoomID, gameRoom.playerID1);
                SendMessageToClient(ServerToClientSignifiers.GameStart + "," + gameRoom.gameRoomID, gameRoom.playerID2);
                playerWaitingForMatchWithId = -1;

                //Decide who gets the first turn
                //bool player1GoesFirst = Random.Range(0,2) == 0;
                //if(player1GoesFirst)


                //First player to join gets first turn
                    SendMessageToClient(ServerToClientSignifiers.ChosenAsPlayerOne + "", gameRoom.playerID1);


                //else
                //    SendMessageToClient(ServerToClientSignifiers.ChosenAsPlayerOne + "", gameRoom.playerID2);
            }
        }
        //Tic-Tac-Toe Gameplay
        else if(signifier == ClientToServerSignifiers.TTTSquareChosen)
        {
            string newMsg = ServerToClientSignifiers.OpponentAction + "," + csv[1];
            GameRoom gameRoom = GetGameRoomFromClientID(id);
            SendMessageToOthers(gameRoom, id, newMsg);
            gameRoom.savedSquareChoices.Add(csv[1]);
        }
        //Game Ended
        else if(signifier == ClientToServerSignifiers.EndGame)
        {
            string newMsg = ServerToClientSignifiers.GameOver + "," + csv[1];
            GameRoom gr = GetGameRoomFromClientID(id);
            SendMessageToOthers(gr, id, newMsg);
            gr.gameHasEnded = true;
        }
        //Player Messaging
        else if(signifier == ClientToServerSignifiers.ChatMessage)
        {
            string newMsg = ServerToClientSignifiers.ChatLogMessage + ","  + csv[1];
            GameRoom gameRoom = GetGameRoomFromClientIDIncludeObservers(id);
            SendMessageToOthers(gameRoom, id, newMsg);
        }
        //Observer join random room
        else if(signifier == ClientToServerSignifiers.JoinAnyRoomAsObserver)
        {
            if(gameRooms.Count > 0)
            { 
                EnterGameRoomAsObserver(gameRooms.First.Value, id);
            }
        }
        //Observer join specific room
        else if (signifier == ClientToServerSignifiers.JoinSpecificRoomAsObserver)
        {
            int requestedRoomNumber = int.Parse(csv[1]);

            GameRoom specifiedRoom = GetRoomFromRoomID(requestedRoomNumber);

            if(specifiedRoom !=null)
                EnterGameRoomAsObserver(specifiedRoom, id);
        }
        //Player leaving room
        else if(signifier == ClientToServerSignifiers.LeavingRoom)
        {
            if(id == playerWaitingForMatchWithId)
            {
                playerWaitingForMatchWithId = -1;
                return;
            }

            GameRoom gameRoom = GetGameRoomFromClientIDIncludeObservers(id);

            if(gameRoom != null)
                RemoveClientFromGameRoom(gameRoom, id);
        }
        //Replay request
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

    //Search through each account by name to check if in use
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

    //Save a new user to log
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

    //Load a user from log
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




    //Find a player IDs from all game rooms
    private GameRoom GetGameRoomFromClientID(int id)
    {
        foreach(GameRoom gr in gameRooms)
        {
            if(gr.playerID1 == id || gr.playerID2 == id)
                return gr;
        }
        return null;
    }

    //Check all IDs from a room
    private GameRoom GetGameRoomFromClientIDIncludeObservers(int id)
    {
        foreach (GameRoom gr in gameRooms)
        {
            foreach (int observerId in gr.observerIDs)
            {
                if (observerId == id)
                    return gr;
            }

        }
        return null;
    }

    //Find a room from an ID
    private GameRoom GetRoomFromRoomID(int id)
    {
        foreach (GameRoom gr in gameRooms)
        {
            if (gr.gameRoomID == id)
                return gr;
        }
        return null;
    }


    //Send a message to all but sender
    void SendMessageToOthers(GameRoom gr, int fromID, string msg)
    {
        foreach(int id in gr.observerIDs)
        {
            if(id != fromID)
                 SendMessageToClient(msg, id);
        }
    }


    //Enter a room as observer
    void EnterGameRoomAsObserver(GameRoom gr, int playerId)
    {
        gr.observerIDs.Add(playerId);
        string msg = ServerToClientSignifiers.EnteredGameRoomAsObserver + "," + gr.gameRoomID;
        foreach(string turnData in gr.savedSquareChoices)
        {
            msg += "," + turnData;
        }
        SendMessageToClient(msg, playerId);
    }

    //Remove a player from a room
    void RemoveClientFromGameRoom(GameRoom gr, int id)
    {
        int index = -1;
        for(int i = 0; i < gr.observerIDs.Count; i++)
        {
            if(gr.observerIDs[i] == id)
            { 
                index = i;
                break;
            }
        }
        if(index != -1)
            gr.observerIDs.RemoveAt(index);

        if(gr.observerIDs.Count == 0)
            gameRooms.Remove(gr);
    }

}


public static class ClientToServerSignifiers
{
    public const int CreateAccount = 1;
    public const int Login = 2;
    public const int JoinGameRoomQueue = 3;
    public const int TTTSquareChosen = 4;

    public const int ChatMessage = 8;

    public const int JoinAnyRoomAsObserver = 9;
    public const int JoinSpecificRoomAsObserver = 10;

    public const int EndGame = 11;
    public const int LeavingRoom = 12;

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
    public const int OpponentAction = 7;

    public const int ChatLogMessage = 11;

    public const int EnteredGameRoomAsObserver = 12;

    public const int GameOver = 13;
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
    public int playerID1, playerID2;
    public bool gameHasEnded = false;

    public List<int> observerIDs;
    public List<string> savedSquareChoices;

    public GameRoom(int id1, int id2)
    {
        //Player
        playerID1 = id1;
        playerID2 = id2;

        //Observer
        observerIDs = new List<int>();
        observerIDs.Add(playerID1);
        observerIDs.Add(playerID2);
        savedSquareChoices = new List<string>();
    }
}