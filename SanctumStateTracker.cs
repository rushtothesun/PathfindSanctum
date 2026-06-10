using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ExileCore2.PoEMemory.Elements.Sanctum;
using static PathfindSanctum.RoomsByLayerFromUI;

namespace PathfindSanctum;

public class SanctumStateTracker
{
    private uint? currentAreaHash;
    private Dictionary<(int Layer, int Room), RoomState> roomStates = new();

    public List<List<FakeSanctumRoomElement>> roomsByLayer;
    public byte[][][] roomLayout;

    public int PlayerLayerIndex = -1;
    public int PlayerRoomIndex = -1;

    public bool HasRoomData()
    {
        return roomStates.Count > 0;
    }

    public bool IsSameSanctum(uint newAreaHash)
    {
        if (currentAreaHash == null)
        {
            currentAreaHash = newAreaHash;
            return false;
        }
        return currentAreaHash == newAreaHash;
    }

    public void UpdateRoomStates(SanctumFloorWindow floorWindow, bool isController, System.Numerics.Vector2 manualOffset)
    {
        this.roomsByLayer = RoomsByLayerFromUI.GetRoomsByLayer(floorWindow, isController, manualOffset);
        if (roomsByLayer == null || roomsByLayer.Count == 0)
        {
            return;
        }

        // Update Layout Data (null-safe)
        var floorData = GetSanctumFloorData(floorWindow, isController);
        if (floorData == null)
        {
            return;
        }
        this.roomLayout = floorData.RoomLayout;

        // Update Player Data (null-safe)
        var roomChoices = floorData.RoomChoices;
        if (roomChoices != null && roomChoices.Count > 0)
        {
            PlayerLayerIndex = roomChoices.Count - 1;
            PlayerRoomIndex = roomChoices.Last();
        }
        else
        {
            PlayerLayerIndex = -1;
            PlayerRoomIndex = -1;
        }

        // Update Room Data
        for (var layer = 0; layer < roomsByLayer.Count; layer++)
        {
            for (var room = 0; room < roomsByLayer[layer].Count; room++)
            {
                var sanctumRoom = roomsByLayer[layer][room];
                if (sanctumRoom == null)
                {
                    continue;
                }

                var key = (layer, room);
                if (!roomStates.ContainsKey(key))
                {
                    int numConnections = 0;

                    // ✅ zabezpieczenie przed IndexOutOfRange
                    if (roomLayout != null
                        && layer < roomLayout.Length
                        && roomLayout[layer] != null
                        && room < roomLayout[layer].Length
                        && roomLayout[layer][room] != null)
                    {
                        numConnections = roomLayout[layer][room].Length;
                    }

                    roomStates[key] = new RoomState(sanctumRoom, numConnections);
                }
                else
                {
                    roomStates[key].UpdateRoom(sanctumRoom);
                }
            }
        }
    }

    /// <summary>
    /// In KB/M mode, FloorData is read natively from SanctumFloorWindow (offset 0x30).
    /// In Controller mode, 0x30 is invalid; the active selector lives at offset 0x7C8.
    /// This workaround is needed until ExileCore2 natively handles controller mode.
    /// </summary>
    private static SanctumFloorData GetSanctumFloorData(SanctumFloorWindow floorWindow, bool isController)
    {
        if (floorWindow == null) return null;

        if (!isController)
        {
            return floorWindow.FloorData;
        }

        var selectorAddress = floorWindow.M.Read<long>(floorWindow.Address + 0x7C8);
        if (selectorAddress == 0)
        {
            return null;
        }

        var selector = floorWindow.GetObject<SanctumFloorWindowDataSelector>(selectorAddress);
        return selector?.FloorData;
    }

    public void Reset(uint newAreaHash)
    {
        currentAreaHash = newAreaHash;
        roomStates.Clear();
    }

    public RoomState GetRoom(int layer, int room)
    {
        return roomStates.TryGetValue((layer, room), out var state) ? state : null;
    }
}

public class RoomState
{
    public string RoomType { get; private set; }
    public string Affliction { get; private set; }
    public string Reward { get; private set; }
    public int Connections { get; private set; }

    public Vector2 Position { get; internal set; }

    public RoomState(FakeSanctumRoomElement room, int numConnections)
    {
        Connections = numConnections;
        UpdateRoom(room);
    }

    public void UpdateRoom(FakeSanctumRoomElement newRoom)
    {
        var newRoomType = newRoom?.Data?.FightRoom?.RoomType?.Id;
        var newAffliction = newRoom?.Data?.RoomEffect?.ReadableName;
        var newReward = newRoom?.Data?.RewardRoom?.RoomType?.Id;

        // Only update each field if we're getting new information (not null/empty)
        if (!string.IsNullOrEmpty(newRoomType))
            RoomType = newRoomType;
        if (!string.IsNullOrEmpty(newAffliction))
            Affliction = newAffliction;
        if (!string.IsNullOrEmpty(newReward))
            Reward = newReward;
        Position = newRoom?.Position ?? Position;
    }

    public override string ToString()
    {
        return $"Type: {RoomType}, Affliction: {Affliction}, Reward: {Reward}, Connections: {Connections}";
    }
}
