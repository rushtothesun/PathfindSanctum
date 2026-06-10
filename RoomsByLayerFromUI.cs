using System.Collections.Generic;
using System.Numerics;
using ExileCore2.Shared;
using ExileCore2.PoEMemory.Elements.Sanctum;

namespace PathfindSanctum;

/// <summary>
/// Handles the extraction and organization of Sanctum room data from the game's UI elements.
/// Uses memory-based parsing via SanctumRoomElement for both KB/M and Controller modes.
/// </summary>
public class RoomsByLayerFromUI
{
    #region Mappings

    /// <summary>
    /// Maps raw FightRoom.RoomType.Id values from game memory to the display names
    /// used by the pathfinder weight system.
    /// </summary>
    private static readonly Dictionary<string, string> MemoryRoomTypeMapping =
        new()
        {
            { "Explore", "Escape" },
            { "Lair", "Chalice" },
            { "PortalArena", "Ritual" },
            { "Gauntlet", "Gauntlet" },
            { "TimerArena", "Hourglass" },
            { "Boss", "Boss" }
        };

    /// <summary>
    /// Maps raw RewardRoom.RoomType.Id values from game memory to the display names
    /// used by the pathfinder weight system.
    /// </summary>
    private static readonly Dictionary<string, string> MemoryRewardMapping =
        new()
        {
            { "WaterMajor", "Large Fountain" },
            { "WaterMinor", "Fountain" },
            { "LegendPledge", "Pledge to Kochai" },
            { "LegendWater", "Honour Halani" },
            { "LegendCurse", "Honour Ahkeli" },
            { "LegendBoon", "Honour Orbala" },
            { "LegendRandom", "Honour Galai" },
            { "LegendHonor", "Honour Tabana" },
            { "Merchant", "Merchant" },
            { "BronzeKey", "Bronze Key" },
            { "SilverKey", "Silver Key" },
            { "GoldKey", "Gold Key" },
            { "BronzeKeyChest", "Bronze Cache" },
            { "SilverKeyChest", "Silver Cache" },
            { "GoldKeyChest", "Gold Cache" }
        };

    #endregion

    #region Models

    public class FakeSanctumRoomElement
    {
        public RoomData Data { get; private set; } = new();
        public Vector2 Position { get; set; }
        public RectangleF ClientRect { get; set; }

        public RectangleF GetClientRect() => ClientRect;

        public void Update(string roomType, string affliction, string reward)
        {
            if (roomType != null)
                Data.FightRoom = new FightRoom { RoomType = new RoomType { Id = roomType } };

            if (affliction != null)
                Data.RoomEffect = new RoomEffect { ReadableName = affliction };

            if (reward != null)
                Data.RewardRoom = new RewardRoom { RoomType = new RoomType { Id = reward } };
        }

        #region Nested Types

        public class RoomType
        {
            public string Id { get; set; }
        }

        public class FightRoom
        {
            public RoomType RoomType { get; set; }
        }

        public class RewardRoom
        {
            public RoomType RoomType { get; set; }
        }

        public class RoomEffect
        {
            public string ReadableName { get; set; }
        }

        public class RoomData
        {
            public FightRoom FightRoom { get; set; }
            public RewardRoom RewardRoom { get; set; }
            public RoomEffect RoomEffect { get; set; }
        }

        #endregion
    }

    #endregion

    // KB/M baseline: where the layers container sits relative to floorWindow at 1440p (scale 1.0).
    // At 1080p the scroll width is 1525.5 (scale 0.75).
    // In controller mode the container shifts horizontally but not vertically.
    // These are the KB/M design-time positions used to compute the correction delta.
    private const float BaselineDesignWidth = 2034.0f;
    private const float BaselineRelativeX = 369.7f;
    private const float BaselineRelativeY = 151.4f;

    /// <summary>
    /// Extracts room data organized by layer from the Sanctum floor window.
    /// In KB/M mode, the native RoomsByLayer property reads children at path 0/0/1.
    /// In Controller mode, 0/0/1 points to the Legend Display Panel instead of room layers,
    /// so we must manually traverse to 0/0/0/1 to reach the real grid container.
    /// </summary>
    public static List<List<FakeSanctumRoomElement>> GetRoomsByLayer(SanctumFloorWindow floorWindow, bool isController, Vector2 manualOffset)
    {
        var result = new List<List<FakeSanctumRoomElement>>();

        var layersContainer = isController
            ? floorWindow
                ?.GetChildAtIndex(0)
                ?.GetChildAtIndex(0)
                ?.GetChildAtIndex(0)
                ?.GetChildAtIndex(1)
            : floorWindow
                ?.GetChildAtIndex(0)
                ?.GetChildAtIndex(0)
                ?.GetChildAtIndex(1);

        if (layersContainer == null)
            return result;

        // Compute the rect correction offset once per call.
        // In controller mode, the UI tree shifts the layers container horizontally
        // relative to floorWindow compared to KB/M. We measure the delta so the
        // overlay frames align with the visual room icons.
        // In KB/M mode this naturally computes to (0, 0).
        var rectOffset = ComputeRectOffset(floorWindow, layersContainer, manualOffset);

        // For each layer
        for (int i = 0; i < layersContainer.Children.Count; i++)
        {
            var layer = new List<FakeSanctumRoomElement>();
            var layerElement = layersContainer.Children[i];

            // For each room in the layer
            foreach (var roomElement in layerElement.Children)
            {
                var room = ProcessRoomElement(floorWindow, roomElement, rectOffset);
                layer.Add(room);
            }

            result.Add(layer);
        }

        return result;
    }

    /// <summary>
    /// Computes the offset between the layers container's actual position relative to
    /// floorWindow and its expected KB/M baseline position (scaled for current resolution).
    /// This avoids window-centering math entirely — floorWindow is the anchor.
    /// </summary>
    private static Vector2 ComputeRectOffset(SanctumFloorWindow floorWindow, ExileCore2.PoEMemory.Element layersContainer, Vector2 manualOffset)
    {
        var scrollRect = floorWindow.GetClientRect();
        var layersRect = layersContainer.GetClientRect();
        float uiScale = scrollRect.Width / BaselineDesignWidth;

        // Where the layers container actually is, relative to the scroll
        float actualRelX = layersRect.Left - scrollRect.Left;
        float actualRelY = layersRect.Top - scrollRect.Top;

        // Where it should be based on KB/M design baseline
        float expectedRelX = BaselineRelativeX * uiScale;
        float expectedRelY = BaselineRelativeY * uiScale;

        return new Vector2(
            actualRelX - expectedRelX + manualOffset.X,
            actualRelY - expectedRelY + manualOffset.Y);
    }

    /// <summary>
    /// Wraps a UI room element as a SanctumRoomElement to read room type, reward,
    /// and affliction data directly from game memory instead of parsing tooltips.
    /// </summary>
    private static FakeSanctumRoomElement ProcessRoomElement(
        SanctumFloorWindow floorWindow,
        ExileCore2.PoEMemory.Element roomElement,
        Vector2 rectOffset)
    {
        var rect = roomElement.GetClientRect();

        // Apply the controller mode rect correction (zero in KB/M mode)
        if (rectOffset != Vector2.Zero)
        {
            rect = new RectangleF(rect.X - rectOffset.X, rect.Y - rectOffset.Y, rect.Width, rect.Height);
        }

        var sanctumRoom = new FakeSanctumRoomElement
        {
            ClientRect = rect,
            Position = rect.TopLeft
        };

        // Instantiate the UI element as a SanctumRoomElement to access memory-backed data
        var sanctumRoomElem = floorWindow.GetObject<SanctumRoomElement>(roomElement.Address);
        var memoryData = sanctumRoomElem?.Data;

        if (memoryData != null)
        {
            string roomType = null;
            string reward = null;
            string affliction = memoryData.RoomEffect?.ReadableName;

            var rawRoomType = memoryData.FightRoom?.RoomType?.Id;
            if (rawRoomType != null)
            {
                MemoryRoomTypeMapping.TryGetValue(rawRoomType, out roomType);
            }

            var rawReward = memoryData.RewardRoom?.RoomType?.Id;
            if (rawReward != null)
            {
                MemoryRewardMapping.TryGetValue(rawReward, out reward);
            }

            sanctumRoom.Update(roomType, affliction, reward);
        }
        else
        {
            sanctumRoom.Update(null, null, null);
        }

        return sanctumRoom;
    }
}
